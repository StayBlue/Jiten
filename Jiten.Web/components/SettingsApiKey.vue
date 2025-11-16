<script async setup lang="ts">
  import { useToast } from 'primevue/usetoast';
  import { useConfirm } from 'primevue/useconfirm';
  import { type ApiKeyInfo, type CreateApiKeyResponse } from '~/types/types';

  const { $api } = useNuxtApp();
  const toast = useToast();
  const confirm = useConfirm();

  const apiKeyInfo = ref<ApiKeyInfo | null>(null);
  const newlyCreatedKey = ref<string | null>(null);
  const isLoading = ref(false);
  const isCopied = ref(false);

  const fetchApiKeyInfo = async () => {
    try {
      const result = await $api<{ apiKey: ApiKeyInfo | null }>('api-key/info');
      apiKeyInfo.value = result.apiKey;
    } catch {
      apiKeyInfo.value = null;
    }
  };

  await fetchApiKeyInfo();

  const createApiKey = async () => {
    try {
      isLoading.value = true;
      const result = await $api<CreateApiKeyResponse>('api-key/create', { method: 'POST' });
      newlyCreatedKey.value = result.apiKey;
      await fetchApiKeyInfo();
      toast.add({
        severity: 'success',
        summary: 'API key created',
        detail: 'Your API key has been created. Make sure to copy it now!',
        life: 10000,
      });
    } catch (error: unknown) {
      const errorMessage = error instanceof Error ? error.message : 'Failed to create API key';
      toast.add({
        severity: 'error',
        summary: 'Error',
        detail: errorMessage,
        life: 5000,
      });
    } finally {
      isLoading.value = false;
    }
  };

  const revokeAndRegenerate = async () => {
    if (!apiKeyInfo.value) return;

    try {
      isLoading.value = true;
      await $api(`api-key/${apiKeyInfo.value.id}/revoke`, { method: 'POST' });
      const result = await $api<CreateApiKeyResponse>('api-key/create', { method: 'POST' });
      newlyCreatedKey.value = result.apiKey;
      await fetchApiKeyInfo();
      toast.add({
        severity: 'success',
        summary: 'API key regenerated',
        detail: 'Your old API key has been revoked and a new one created.',
        life: 10000,
      });
    } catch (error: unknown) {
      const errorMessage = error instanceof Error ? error.message : 'Failed to regenerate API key';
      toast.add({
        severity: 'error',
        summary: 'Error',
        detail: errorMessage,
        life: 5000,
      });
    } finally {
      isLoading.value = false;
    }
  };

  const confirmGenerate = () => {
    confirm.require({
      message: 'Your API key will only be shown once after creation. Make sure to copy it immediately.',
      header: 'Generate API Key',
      icon: 'pi pi-exclamation-triangle',
      rejectProps: {
        label: 'Cancel',
        severity: 'secondary',
        outlined: true,
      },
      acceptProps: {
        label: 'Generate',
      },
      accept: () => {
        createApiKey();
      },
    });
  };

  const confirmRegenerate = () => {
    confirm.require({
      message: 'This will revoke your current API key immediately. Any applications using the old key will stop working. The new key will only be shown once.',
      header: 'Regenerate API Key',
      icon: 'pi pi-exclamation-triangle',
      rejectProps: {
        label: 'Cancel',
        severity: 'secondary',
        outlined: true,
      },
      acceptProps: {
        label: 'Regenerate',
        severity: 'danger',
      },
      accept: () => {
        revokeAndRegenerate();
      },
    });
  };

  const copyToClipboard = async () => {
    if (!newlyCreatedKey.value) return;
    try {
      await navigator.clipboard.writeText(newlyCreatedKey.value);
      isCopied.value = true;
      toast.add({
        severity: 'success',
        summary: 'Copied',
        detail: 'API key copied to clipboard',
        life: 3000,
      });
      setTimeout(() => {
        isCopied.value = false;
      }, 2000);
    } catch {
      toast.add({
        severity: 'error',
        summary: 'Error',
        detail: 'Failed to copy to clipboard',
        life: 3000,
      });
    }
  };

  const formatDate = (dateString: string) => {
    return new Date(dateString).toLocaleString();
  };
</script>

<template>
  <div>
    <Card>
      <template #title>
        <h3 class="text-lg font-semibold">API Key</h3>
      </template>
      <template #content>
        <div v-if="newlyCreatedKey" class="mb-4">
          <Message severity="warn" :closable="false">
            <p class="font-semibold mb-2">Your new API key (only shown once):</p>
            <div class="flex items-center gap-2">
              <code class="bg-surface-100 dark:bg-surface-800 p-2 rounded text-sm break-all flex-1">
                {{ newlyCreatedKey }}
              </code>
              <Button :icon="isCopied ? 'pi pi-check' : 'pi pi-copy'" :severity="isCopied ? 'success' : 'secondary'" @click="copyToClipboard" />
            </div>
          </Message>
        </div>

        <div v-if="!apiKeyInfo && !newlyCreatedKey">
          <p class="mb-4">
            API keys allow you to authenticate with the Jiten API programmatically. You can use them to build integrations or access your data from external applications.
          </p>
          <Message severity="info" :closable="false" class="mb-4">
            <p>Your API key will only be shown once after creation. Make sure to store it securely.</p>
          </Message>
          <Button icon="pi pi-key" label="Generate API Key" :loading="isLoading" @click="confirmGenerate" />
        </div>

        <div v-else-if="apiKeyInfo">
          <div class="space-y-2 mb-4">
            <p>
              <span class="font-semibold">Key preview:</span>
              <code class="bg-surface-100 dark:bg-surface-800 px-2 py-1 rounded ml-2">{{ apiKeyInfo.keyPreview }}</code>
            </p>
            <p>
              <span class="font-semibold">Created:</span>
              {{ formatDate(apiKeyInfo.createdAt) }}
            </p>
            <p>
              <span class="font-semibold">Last used:</span>
              {{ apiKeyInfo.lastUsedAt ? formatDate(apiKeyInfo.lastUsedAt) : 'Never' }}
            </p>
          </div>
          <Button icon="pi pi-refresh" label="Regenerate API Key" severity="warn" :loading="isLoading" @click="confirmRegenerate" />
        </div>
      </template>
    </Card>
    <BlockUI :blocked="isLoading" full-screen />
  </div>
</template>

<style scoped></style>
