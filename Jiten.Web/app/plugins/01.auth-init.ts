export default defineNuxtPlugin(async (nuxtApp) => {
  const authStore = useAuthStore();

  // Initialize auth during both SSR and client-side
  // This ensures tokens are refreshed before any component renders
  if (import.meta.server || !authStore.user) {
    try {
      await authStore.ensureValidToken();

      // Fetch user if we have valid token
      if (authStore.isAuthenticated) {
        await authStore.fetchCurrentUser();
      }
    } catch (error) {
      console.error('Error initialising auth in plugin:', error);
      // Gracefully continue - auth will be retried by components/middleware
    }
  }
});
