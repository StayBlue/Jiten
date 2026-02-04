export default defineNuxtPlugin(async (nuxtApp) => {
  const authStore = useAuthStore();

  if (import.meta.server) {
    // On SSR: sync tokens from cookies but don't attempt refresh.
    // Client will handle token refresh after hydration.
    authStore.syncTokensFromCookies();

    if (authStore.isAuthenticated && !authStore.isTokenExpired(authStore.accessToken!)) {
      try {
        await authStore.fetchCurrentUser();
      } catch (error) {
        console.error('Error fetching user during SSR:', error);
      }
    }
  } else if (!authStore.user) {
    try {
      await authStore.ensureValidToken();

      if (authStore.isAuthenticated) {
        await authStore.fetchCurrentUser();
      }
    } catch (error) {
      console.error('Error initialising auth in plugin:', error);
    }
  }
});
