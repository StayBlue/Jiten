<script async setup lang="ts">
  import { type UserMetadata } from '~/types';
  import { useToast } from 'primevue/usetoast';

  const { $api } = useNuxtApp();
  const toast = useToast();

  let lastRefresh = ref<Date>();

  try {
    const result = await $api<UserMetadata>('user/metadata');
    lastRefresh.value = result.coverageRefreshedAt ? new Date(result.coverageRefreshedAt) : undefined;
  } catch {}

  const isRefreshing = ref(false);

  const refreshCoverage = async () => {
    try {
      if (isRefreshing.value) return;
      isRefreshing.value = true;
      const result = await $api<UserMetadata>('user/metadata');
      await $api('user/coverage/refresh', { method: 'POST' });
      const currentRefreshDate = result.coverageRefreshedAt;

      const startTime = Date.now();
      const interval = setInterval(async () => {
        try {
          const result = await $api<UserMetadata>('user/metadata');
          if (result.coverageRefreshedAt !== currentRefreshDate) {
            lastRefresh.value = result.coverageRefreshedAt ? new Date(result.coverageRefreshedAt) : undefined;
            clearInterval(interval);
            isRefreshing.value = false;
            showSuccessToast(toast, 'Coverage successfully refreshed!');
          } else if (Date.now() - startTime >= 60000) {
            clearInterval(interval);
            isRefreshing.value = false;
            showErrorToast(toast, 'Timeout', 'Refreshing your coverage is taking longer than usual, please wait a few minutes and try refreshing the page.');
          }
        } catch {
          clearInterval(interval);
          isRefreshing.value = false;
        }
      }, 6500);
    } catch {
      isRefreshing.value = false;
      showErrorToast(toast, 'Error refreshing coverage', 'There was an error refreshing your coverage, please try again.');
    }
  };
</script>

<template>
  <div>
    <Card>
      <template #title>
        <h3 class="text-lg font-semibold">Coverage</h3>
      </template>
      <template #content>
        <p>
          Your coverage was last refreshed: <b>{{ lastRefresh?.toLocaleString() ?? 'Never' }}</b>
        </p>
        <div class="p-2">
          <Button icon="pi pi-refresh" label="Refresh now" class="w-full md:w-auto" @click="refreshCoverage" />
        </div>
      </template>
    </Card>
    <LoadingOverlay :visible="isRefreshing" message="Refreshing your coverage, please wait a few seconds…" />
  </div>
</template>

<style scoped></style>
