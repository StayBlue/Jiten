export default defineNuxtRouteMiddleware(async (to, from) => {
  const authStore = useAuthStore();
  const tokenCookie = useCookie('token');
  const refreshTokenCookie = useCookie('refreshToken');

  // If no tokens at all, redirect to login
  if (!tokenCookie.value && !refreshTokenCookie.value) {
    return navigateTo({
      path: '/login',
      query: { redirect: to.fullPath !== '/login' ? to.fullPath : undefined },
    });
  }

  // If we have tokens, try to ensure they're valid
  if (tokenCookie.value || refreshTokenCookie.value) {
    try {
      // This will check if the token is expired and refresh if needed
      const hasValidToken = await authStore.ensureValidToken();

      if (!hasValidToken) {
        // Token refresh failed, clear everything and redirect
        authStore.clearAuthData();
        return navigateTo({
          path: '/login',
          query: { redirect: to.fullPath !== '/login' ? to.fullPath : undefined },
        });
      }

      // Ensure the authenticated user is an admin
      if (!authStore.isAdmin) {
        return navigateTo({
          path: '/',
          query: { redirect: to.fullPath !== '/' ? to.fullPath : undefined },
        });
      }

      // We have a valid token and the user is an admin, allow the navigation
      return;
    } catch (error) {
      console.error('Auth middleware error:', error);
      // On error, clear auth data and redirect
      authStore.clearAuthData();
      return navigateTo({
        path: '/login',
        query: { redirect: to.fullPath !== '/login' ? to.fullPath : undefined },
      });
    }
  }

  // Fallback - if we somehow get here without valid authentication
  return navigateTo({
    path: '/login',
    query: { redirect: to.fullPath !== '/login' ? to.fullPath : undefined },
  });
});
