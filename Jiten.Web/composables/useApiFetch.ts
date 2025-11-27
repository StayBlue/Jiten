import { PaginatedResponse } from '~/types/types';
import type { AsyncDataRequestStatus, UseFetchOptions } from '#app';

export function useApiFetch<T>(
  request: string | (() => string),
  opts?: any
): {
  data: Ref<T | null>;
  status: Ref<AsyncDataRequestStatus>;
  error: Ref<Error | null>;
  refresh: (opts?: any) => Promise<void>;
  execute: (opts?: any) => Promise<void>;
} {
  const authStore = useAuthStore();
  const isHandling401 = ref(false);

  // Proactively ensure token is valid before making request (client-side only)
  const tokenCheckPromise = import.meta.client && authStore.isAuthenticated
    ? authStore.ensureValidToken()
    : Promise.resolve(true);

  // Create a unique key for this request to prevent duplicates
  const key = generateRequestKey(request);
  const uniqueKey = `api-${key}-${safeStringifyQuery(opts?.query)}`;

  // Set default headers
  const headers = new Headers(opts?.headers || {});

  if (import.meta.server) {
    const proxyHeaders = useRequestHeaders(['x-forwarded-for', 'cf-connecting-ip', 'user-agent']);

    // Forward the IP chain
    if (proxyHeaders['x-forwarded-for']) {
      headers.set('X-Forwarded-For', proxyHeaders['x-forwarded-for']);
    }
    // Forward Cloudflare real IP (if present)
    if (proxyHeaders['cf-connecting-ip']) {
      headers.set('CF-Connecting-IP', proxyHeaders['cf-connecting-ip']);
    }
    // Forward User-Agent (optional, but good for logs)
    if (proxyHeaders['user-agent']) {
      headers.set('User-Agent', proxyHeaders['user-agent']);
    }
  }

  // Add authorization header if token exists
  // Use authStore.accessToken instead of cookie to get fresh token during SSR
  if (authStore.accessToken) {
    headers.set('Authorization', `Bearer ${authStore.accessToken}`);
  }

  // Merge options with headers
  const options = {
    ...opts,
    headers,
    key: uniqueKey,
    server: opts?.server ?? true,
    lazy: opts?.lazy ?? false,
    // Await token validation before making request
    async onRequest({ options }) {
      await tokenCheckPromise;
      // Update Authorization header with fresh token after refresh
      if (authStore.accessToken) {
        options.headers.set('Authorization', `Bearer ${authStore.accessToken}`);
      }
    }
  };

  const { data, status, error, refresh, execute } = useFetch<T>(request, {
    baseURL: useRuntimeConfig().public.baseURL,
    ...options
  });

  // Client-side only: watch for 401 errors and retry after token refresh
  if (import.meta.client) {
    watch(error, async (newError) => {
      if (!newError || isHandling401.value) return;

      const fetchError = newError as any;
      const is401 = fetchError.status === 401 || fetchError.statusCode === 401;

      if (!is401) return;

      isHandling401.value = true;

      try {
        const url = typeof request === 'function' ? request() : request;
        const isAuthEndpoint = url.includes('/auth/');

        if (isAuthEndpoint) {
          console.log('401 on auth endpoint, not retrying');
          return;
        }

        if (!authStore.isRefreshing) {
          console.log('Received 401 in useApiFetch, attempting token refresh...');
          const refreshSuccess = await authStore.refreshAccessToken();

          if (!refreshSuccess) {
            console.log('Token refresh failed');
            return;
          }
          console.log('Token refreshed successfully');
        } else {
          // Wait for concurrent refresh to complete
          while (authStore.isRefreshing) {
            await new Promise(resolve => setTimeout(resolve, 100));
          }
        }

        // Clear the error and retry
        error.value = null;
        await execute();

      } finally {
        isHandling401.value = false;
      }
    });
  }

  return { data, status, error, refresh, execute };
}

export  function useApiFetchPaginated<T>(
  request: string | (() => string),
  opts?: any
): {
  data: Ref<PaginatedResponse<T> | null>;
  status: Ref<AsyncDataRequestStatus>;
  error: Ref<Error | null>;
  refresh: (opts?: any) => Promise<void>;
  execute: (opts?: any) => Promise<void>;
} {
  const config = useRuntimeConfig();
  const authStore = useAuthStore();
  const isHandling401 = ref(false);

  // Proactively ensure token is valid before making request (client-side only)
  const tokenCheckPromise = import.meta.client && authStore.isAuthenticated
    ? authStore.ensureValidToken()
    : Promise.resolve(true);

  // Create a unique key for this request to prevent duplicates
  const key = generateRequestKey(request);
  const uniqueKey = `api-${key}-${safeStringifyQuery(opts?.query)}`;

  // Set default headers
  const headers = new Headers(opts?.headers || {});

  if (import.meta.server) {
    const proxyHeaders = useRequestHeaders(['x-forwarded-for', 'cf-connecting-ip', 'user-agent']);

    if (proxyHeaders['x-forwarded-for']) {
      headers.set('X-Forwarded-For', proxyHeaders['x-forwarded-for']);
    }
    if (proxyHeaders['cf-connecting-ip']) {
      headers.set('CF-Connecting-IP', proxyHeaders['cf-connecting-ip']);
    }
    if (proxyHeaders['user-agent']) {
      headers.set('User-Agent', proxyHeaders['user-agent']);
    }
  }

  // Add authorization header if token exists
  // Use authStore.accessToken instead of cookie to get fresh token during SSR
  if (authStore.accessToken) {
    headers.set('Authorization', `Bearer ${authStore.accessToken}`);
  }

  // Merge options with headers
  const options = {
    ...opts,
    headers,
    key: uniqueKey, // This prevents duplicate requests
    server: opts?.server ?? true,
    lazy: opts?.lazy ?? false,
    // Await token validation before making request
    async onRequest({ options }) {
      await tokenCheckPromise;
      // Update Authorization header with fresh token after refresh
      if (authStore.accessToken) {
        options.headers.set('Authorization', `Bearer ${authStore.accessToken}`);
      }
    }
  };

  // Use useFetch without await, keeping the data reactive
  const { data, status, error, refresh, execute } = useFetch<PaginatedResponse<T>>(request, {
    baseURL: config.public.baseURL,
    ...options,
  });

  // Client-side only: watch for 401 errors and retry after token refresh
  if (import.meta.client) {
    watch(error, async (newError) => {
      if (!newError || isHandling401.value) return;

      const fetchError = newError as any;
      const is401 = fetchError.status === 401 || fetchError.statusCode === 401;

      if (!is401) return;

      isHandling401.value = true;

      try {
        const url = typeof request === 'function' ? request() : request;
        const isAuthEndpoint = url.includes('/auth/');

        if (isAuthEndpoint) {
          console.log('401 on auth endpoint, not retrying');
          return;
        }

        if (!authStore.isRefreshing) {
          console.log('Received 401 in useApiFetchPaginated, attempting token refresh...');
          const refreshSuccess = await authStore.refreshAccessToken();

          if (!refreshSuccess) {
            console.log('Token refresh failed');
            return;
          }
          console.log('Token refreshed successfully');
        } else {
          // Wait for concurrent refresh to complete
          while (authStore.isRefreshing) {
            await new Promise(resolve => setTimeout(resolve, 100));
          }
        }

        // Clear the error and retry
        error.value = null;
        await execute();

      } finally {
        isHandling401.value = false;
      }
    });
  }

  const paginatedData = computed(() => {
    if (data.value) {
      return new PaginatedResponse<T>(
        data.value.data,
        data.value.totalItems,
        data.value.pageSize,
        data.value.currentOffset
      );
    }
    return null;
  });

  return {
    data: paginatedData,
    status,
    error,
    refresh,
    execute,
  };
}

// Helper function to generate a safe key from request parameter
const generateRequestKey = (request: string | (() => string)) => {
  if (typeof request === 'string') {
    return request;
  } else if (typeof request === 'function') {
    try {
      return request();
    } catch (e) {
      return 'dynamic-request';
    }
  } else {
    return String(request);
  }
};

// Helper function to safely stringify query parameters
const safeStringifyQuery = (query: any) => {
  if (!query || typeof query !== 'object') return '{}';

  try {
    // Convert reactive values to their actual values
    const plainQuery: Record<string, any> = {};
    for (const [key, value] of Object.entries(query)) {
      // Handle Vue refs and computed values
      if (value && typeof value === 'object' && 'value' in value) {
        plainQuery[key] = value.value;
      } else {
        plainQuery[key] = value;
      }
    }
    return JSON.stringify(plainQuery);
  } catch (e) {
    // Fallback if JSON.stringify still fails
    return Object.keys(query).sort().join('-');
  }
};
