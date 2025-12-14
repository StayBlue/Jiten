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
            toast.add({
              severity: 'success',
              summary: 'Coverage successfully refreshed!',
              life: 5000,
            });
          } else if (Date.now() - startTime >= 60000) {
            clearInterval(interval);
            isRefreshing.value = false;
            toast.add({
              severity: 'error',
              summary: 'Timeout',
              detail: `Refreshing your coverage is taking longer than usual, please wait a few minutes and try refreshing the page.`,
              life: 5000,
            });
          }
        } catch {
          clearInterval(interval);
          isRefreshing.value = false;
        }
      }, 6500);
    } catch {
      isRefreshing.value = false;
      toast.add({
        severity: 'error',
        summary: 'Error refreshing coverage',
        detail: `There was an error refreshing your coverage, please try again.`,
        life: 5000,
      });
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
    <div v-if="isRefreshing" class="!fixed top-1/3 left-1/3 text-center" style="z-index: 9999">
      <div class="text-white font-bold text-lg">Refreshing your coverage, please wait a few seconds…</div>
      <ProgressSpinner style="width: 50px; height: 50px" stroke-width="8" fill="transparent" animation-duration=".5s" aria-label="Creating your deck" />
    </div>
<!--    <BlockUI :blocked="isRefreshing" full-screen />-->
  </div>
</template>

<style scoped></style>
