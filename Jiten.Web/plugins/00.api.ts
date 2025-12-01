export default defineNuxtPlugin((nuxtApp) => {
  const config = useRuntimeConfig();

  const proxyHeaders = import.meta.server ? useRequestHeaders(['x-forwarded-for', 'cf-connecting-ip', 'user-agent']) : {};

  const api = $fetch.create({
    baseURL: config.public.baseURL,
    async onRequest({ request, options }) {
      const authStore = useAuthStore();

      options.headers = new Headers(options.headers);

      if (import.meta.server) {
        // Pass the Client IP chain
        if (proxyHeaders['x-forwarded-for']) {
          options.headers.set('X-Forwarded-For', proxyHeaders['x-forwarded-for']);
        }

        // Pass Cloudflare specific IP if it exists
        if (proxyHeaders['cf-connecting-ip']) {
          options.headers.set('CF-Connecting-IP', proxyHeaders['cf-connecting-ip']);
        }

        // Pass User-Agent (otherwise API sees "node-fetch" or similar)
        if (proxyHeaders['user-agent']) {
          options.headers.set('User-Agent', proxyHeaders['user-agent']);
        }
      }

      // Extract URL to check if this is an auth endpoint
      const url = request.toString();
      const isAuthEndpoint = url.includes('/auth/');

      // Determine if this specific auth endpoint needs Authorization header
      // Most auth endpoints don't need it (login, register, refresh, etc.)
      // Only /auth/me and /auth/revoke-token require authentication
      const needsAuthHeader = url.includes('/auth/me') || url.includes('/auth/revoke-token');

      // Use token from authStore (reactive, always current) instead of re-reading cookies
      // This prevents stale reads during SSR when cookies have just been updated
      if (authStore.accessToken && (!isAuthEndpoint || needsAuthHeader)) {
        options.headers = options.headers || {};
        options.headers.set('Authorization', `Bearer ${authStore.accessToken}`);
      }
    },
    onResponse({ response }) {
      // Process response if needed
    },
    async onResponseError({ request, options, response }) {
      // Handle 401 errors with automatic token refresh
      if (response.status === 401) {
        const authStore = useAuthStore();

        // Don't try to refresh on auth endpoints to avoid infinite loops
        const url = request.toString();
        const isAuthEndpoint = url.includes('/auth/');

        if (!isAuthEndpoint && !authStore.isRefreshing) {
          console.log('Received 401, attempting token refresh...');

          // Try to refresh the token
          const refreshSuccess = await authStore.refreshAccessToken();

          if (refreshSuccess) {
            console.log('Token refreshed, retrying original request...');

            // Use token from authStore instead of re-reading cookie
            // This prevents context errors and stale reads
            if (authStore.accessToken) {
              options.headers = options.headers || {};
              options.headers.set('Authorization', `Bearer ${authStore.accessToken}`);
            }

            // Retry the original request with the new token
            try {
              return await $fetch(request, options);
            } catch (retryError) {
              console.error('Retry after token refresh failed:', retryError);
              // If retry fails, proceed with original 401 handling
            }
          }
        }

        // If we reach here, token refresh failed or this is an auth endpoint
        // Navigate to login page
        await nuxtApp.runWithContext(() => {
          const router = useRouter(); // Now safe because we are in context
          const currentRoute = router.currentRoute.value.path;

          if (currentRoute !== '/login') {
            return navigateTo({
              path: '/login',
              query: { redirect: currentRoute },
            });
          }
        });
      }
    },
  });

  return {
    provide: {
      api,
    },
  };
});
