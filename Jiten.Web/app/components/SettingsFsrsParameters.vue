<script setup lang="ts">
  import { useToast } from 'primevue/usetoast';
  import { useConfirm } from 'primevue/useconfirm';
  import { type FsrsParametersResponse, type SrsRecomputeBatchResponse } from '~/types';

  const { $api } = useNuxtApp();
  const toast = useToast();
  const confirm = useConfirm();

  const expectedCount = 21;
  const defaultDesiredRetention = 0.9;
  const parametersCsv = ref('');
  const desiredRetention = ref(defaultDesiredRetention);
  const isDefault = ref(false);
  const isLoading = ref(true);
  const isSaving = ref(false);
  const isRecomputing = ref(false);
  const isResetting = ref(false);
  const recomputeProcessed = ref(0);
  const recomputeTotal = ref(0);
  const recomputeProgress = ref(0);
  const hasUserEdited = ref(false);

  const parsedState = computed(() => {
    const raw = parametersCsv.value.trim();
    if (!raw) {
      return { count: 0, error: 'Parameters are required.' };
    }

    const parts = raw
      .split(',')
      .map((part) => part.trim())
      .filter((part) => part.length > 0);
    const count = parts.length;
    if (count !== expectedCount) {
      return { count, error: `Expected ${expectedCount} values, got ${count}.` };
    }

    const numbers = parts.map((part) => Number(part));
    if (numbers.some((value) => Number.isNaN(value) || !Number.isFinite(value))) {
      return { count, error: 'All values must be valid numbers.' };
    }

    return { count, error: null };
  });

  const valueCount = computed(() => parsedState.value.count);
  const validationError = computed(() => parsedState.value.error);
  const retentionError = computed(() => {
    const value = desiredRetention.value;
    if (value === null || Number.isNaN(value) || !Number.isFinite(value)) {
      return 'Desired retention must be a valid number.';
    }

    if (value <= 0 || value >= 1) {
      return 'Desired retention must be between 0 and 1.';
    }

    return null;
  });
  const formError = computed(() => retentionError.value ?? validationError.value);

  const loadParameters = async (force = false) => {
    try {
      isLoading.value = true;
      const result = await $api<FsrsParametersResponse>('srs/settings');
      if (force || !hasUserEdited.value || !parametersCsv.value.trim()) {
        parametersCsv.value = result.parameters;
        hasUserEdited.value = false;
      }
      isDefault.value = result.isDefault;
      desiredRetention.value = result.desiredRetention ?? defaultDesiredRetention;
    } catch {
      toast.add({
        severity: 'error',
        summary: 'Error',
        detail: 'Failed to load FSRS parameters.',
        life: 5000,
      });
    } finally {
      isLoading.value = false;
    }
  };

  onMounted(() => {
    void loadParameters();
  });

  const saveParameters = async () => {
    if (formError.value) {
      toast.add({
        severity: 'error',
        summary: 'Invalid settings',
        detail: formError.value,
        life: 5000,
      });
      return;
    }

    try {
      isSaving.value = true;
      const result = await $api<FsrsParametersResponse>('srs/settings', {
        method: 'PUT',
        body: { parameters: parametersCsv.value, desiredRetention: desiredRetention.value },
      });
      parametersCsv.value = result.parameters;
      isDefault.value = result.isDefault;
      desiredRetention.value = result.desiredRetention ?? defaultDesiredRetention;
      hasUserEdited.value = false;
      toast.add({
        severity: 'success',
        summary: 'Saved',
        detail: 'FSRS parameters updated.',
        life: 4000,
      });
    } catch (error: unknown) {
      const message = error instanceof Error ? error.message : 'Failed to update FSRS parameters.';
      toast.add({
        severity: 'error',
        summary: 'Error',
        detail: message,
        life: 5000,
      });
    } finally {
      isSaving.value = false;
    }
  };

  const confirmResetParameters = () => {
    confirm.require({
      message: 'This will reset your FSRS parameters and desired retention to the defaults. You will need to reschedule your cards for the changes to take effect.',
      header: 'Reset to default',
      icon: 'pi pi-exclamation-triangle',
      rejectProps: {
        label: 'Cancel',
        severity: 'secondary',
        outlined: true,
      },
      acceptProps: {
        label: 'Reset',
      },
      accept: async () => {
        await resetParameters();
      },
    });
  };

  const resetParameters = async () => {
    try {
      isResetting.value = true;
      const result = await $api<FsrsParametersResponse>('srs/settings', {
        method: 'PUT',
        body: { parameters: '', desiredRetention: defaultDesiredRetention },
      });
      parametersCsv.value = result.parameters;
      isDefault.value = result.isDefault;
      desiredRetention.value = result.desiredRetention ?? defaultDesiredRetention;
      hasUserEdited.value = false;
      toast.add({
        severity: 'success',
        summary: 'Reset to default',
        detail: 'FSRS parameters reset to defaults.',
        life: 4000,
      });
    } catch (error: unknown) {
      const message = error instanceof Error ? error.message : 'Failed to reset parameters.';
      toast.add({
        severity: 'error',
        summary: 'Error',
        detail: message,
        life: 5000,
      });
    } finally {
      isResetting.value = false;
    }
  };

  const recomputeSchedule = async () => {
    try {
      isRecomputing.value = true;
      recomputeProcessed.value = 0;
      recomputeTotal.value = 0;
      recomputeProgress.value = 0;
      let lastCardId = 0;

      while (true) {
        const result = await $api<SrsRecomputeBatchResponse>('srs/settings/recompute-batch', {
          method: 'POST',
          query: {
            lastCardId,
            batchSize: 500,
          },
        });

        if (!recomputeTotal.value) {
          recomputeTotal.value = result.total;
        }

        recomputeProcessed.value += result.processed;
        if (recomputeTotal.value > 0) {
          recomputeProgress.value = Math.min(100, Math.round((recomputeProcessed.value / recomputeTotal.value) * 100));
        }

        lastCardId = result.lastCardId;

        if (result.done || result.processed === 0) {
          break;
        }
      }

      toast.add({
        severity: 'success',
        summary: 'Reschedule complete',
        detail: 'Your SRS schedule has been recomputed.',
        life: 4000,
      });
    } catch (error: unknown) {
      const message = error instanceof Error ? error.message : 'Failed to queue rescheduling.';
      toast.add({
        severity: 'error',
        summary: 'Error',
        detail: message,
        life: 5000,
      });
    } finally {
      isRecomputing.value = false;
    }
  };
</script>

<template>
  <Card>
    <template #title>
      <h3 class="text-lg font-semibold">FSRS Settings</h3>
    </template>
    <template #content>
      <div class="mb-4">
        <h4 class="text-md font-semibold mb-1">Desired retention</h4>
        <p class="text-sm text-gray-600 dark:text-gray-300 mb-2">Target recall rate (0–1). For example, 0.9 = 90% retention.</p>
        <InputNumber v-model="desiredRetention" class="w-full md:w-40" :min="0.01" :max="0.99" :step="0.01" :min-fraction-digits="2" :max-fraction-digits="4" />
      </div>

      <h4 class="text-md font-semibold mb-1">FSRS Parameters</h4>
      <p class="text-sm text-gray-600 dark:text-gray-300 mb-1">Enter 21 comma-separated numbers to customise FSRS scheduling.</p>
      <p class="text-sm text-gray-600 dark:text-gray-300 mb-3">
        You can retrieve your optimised parameters from Anki: go to
        <span class="font-semibold">Deck Options → FSRS → Optimise</span>, then copy and paste the parameters here.
        At least 1,000 reviews are recommended before optimising.
      </p>
      <Textarea v-model="parametersCsv" class="w-full" rows="3" placeholder="0.2172, 1.1771, 3.2602, ..." @update:modelValue="hasUserEdited = true" />
      <div class="mt-2 text-sm text-surface-600">
        Values: <b>{{ valueCount }}</b> / {{ expectedCount }}
      </div>
      <Message v-if="formError" key="fsrs-params-error" severity="error" :closable="false" class="mt-2">
        {{ formError }}
      </Message>
      <Message v-else-if="isDefault" key="fsrs-params-default" severity="info" :closable="false" class="mt-2"> Using default FSRS settings. </Message>
      <div class="mt-4 flex flex-wrap gap-2">
        <Button label="Save" :loading="isSaving" :disabled="!!formError || isLoading || isRecomputing || isResetting" @click="saveParameters" />
        <Button label="Reload" severity="secondary" outlined :disabled="isLoading || isRecomputing || isResetting" @click="loadParameters(true)" />
        <Button
          label="Reset to default"
          severity="secondary"
          outlined
          :loading="isResetting"
          :disabled="isLoading || isRecomputing || isSaving"
          @click="confirmResetParameters"
        />
      </div>
      <p class="mt-3 text-sm text-amber-600 dark:text-amber-400">
        Warning: Be careful with this setting, as it could result in an overwhelming number of immediate reviews.
      </p>
      <div class="mt-2">
        <Button label="Reschedule all cards" :loading="isRecomputing" :disabled="isLoading || isSaving" @click="recomputeSchedule" />
      </div>
      <div v-if="isRecomputing" class="mt-4 flex items-center gap-3">
        <ProgressSpinner style="width: 28px; height: 28px" stroke-width="6" animation-duration=".5s" />
        <div>
          <p class="font-semibold">Rescheduling cards... {{ recomputeProgress }}%</p>
          <p class="text-sm text-surface-500">Processed: {{ recomputeProcessed }} / {{ recomputeTotal || '—' }}</p>
        </div>
      </div>
    </template>
  </Card>
</template>

<style scoped></style>
