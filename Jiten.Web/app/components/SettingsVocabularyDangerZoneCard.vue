<script setup lang="ts">
  import { useConfirm } from 'primevue/useconfirm';

  const emit = defineEmits<{ changed: [] }>();

  const { $api } = useNuxtApp();
  const toast = useToast();
  const confirm = useConfirm();

  async function clearKnownWords() {
    confirm.require({
      message: 'Are you sure you want to clear all known words? This action cannot be undone.',
      header: 'Clear Known Words',
      icon: 'pi pi-exclamation-triangle',
      acceptClass: 'p-button-danger',
      rejectClass: 'p-button-secondary',
      accept: async () => {
        try {
          const result = await $api<{ removed: number }>('user/vocabulary/known-ids/clear', { method: 'DELETE' });
          toast.add({
            severity: 'success',
            summary: 'Known words cleared',
            detail: `Removed ${result?.removed ?? 0} known words from your account.`,
            life: 5000,
          });
          emit('changed');
        } catch (e) {
          console.error(e);
          toast.add({ severity: 'error', summary: 'Error', detail: 'Failed to clear known words on server.', life: 5000 });
        }
      },
      reject: () => {},
    });
  }
</script>

<template>
  <Card>
    <template #title>
      <h2 class="text-xl font-bold">Danger Zone</h2>
    </template>
    <template #content>
      <p class="mb-3">
        Clicking this button will <b>delete ALL your known words</b>. This action cannot be undone. Please make a backup before using it, and use it at your own
        risk.
      </p>
      <div class="flex">
        <Button severity="danger" icon="pi pi-trash" label="Clear All Known Words" @click="clearKnownWords" />
      </div>
    </template>
  </Card>
</template>
