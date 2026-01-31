<script setup lang="ts">
  const emit = defineEmits<{ changed: [] }>();

  const { $api } = useNuxtApp();
  const toast = useToast();
  const { JpdbApiClient } = useJpdbApi();

  const isLoading = ref(false);
  const jpdbApiKey = ref('');
  const blacklistedAsKnown = ref(true);
  const dueAsKnown = ref(true);
  const suspendedAsKnown = ref(false);
  const importAdditionalReadings = ref(true);
  const frequencyThreshold = ref(15000);
  const jpdbProgress = ref('');

  async function importFromJpdbApi() {
    if (!jpdbApiKey.value) {
      toast.add({ severity: 'error', summary: 'Error', detail: 'Please enter your JPDB API key.', life: 5000 });
      return;
    }

    try {
      isLoading.value = true;
      jpdbProgress.value = 'Initialising JPDB API client...';
      toast.add({ severity: 'info', summary: 'Processing', detail: 'Importing from JPDB API...', life: 5000 });

      const client = new JpdbApiClient(jpdbApiKey.value);

      jpdbProgress.value = 'Fetching user decks...';
      await new Promise((resolve) => setTimeout(resolve, 100));

      const response = await client.getFilteredVocabularyIds(blacklistedAsKnown.value, dueAsKnown.value, suspendedAsKnown.value);

      if (response && response.length > 0) {
        jpdbProgress.value = 'Sending vocabulary to your account...';
        await new Promise((resolve) => setTimeout(resolve, 100));

        const result = await $api<{ added: number; skipped: number }>('user/vocabulary/import-from-ids', {
          method: 'POST',
          body: JSON.stringify({
            wordIds: response,
            frequencyThreshold: importAdditionalReadings.value ? frequencyThreshold.value : null,
          }),
          headers: { 'Content-Type': 'application/json' },
        });

        if (result) {
          emit('changed');
          toast.add({ severity: 'success', summary: 'Synced with account', detail: `Added ${result.added}, skipped ${result.skipped}.`, life: 6000 });
        } else {
          toast.add({ severity: 'info', summary: 'No changes', detail: 'No words were added to your account.', life: 5000 });
        }
      } else {
        toast.add({ severity: 'info', summary: 'No words found', detail: 'No words were found from JPDB.', life: 5000 });
      }
    } catch (error) {
      console.error('Error importing from JPDB API:', error);
      const errorMessage = error instanceof Error ? error.message : 'Failed to import from JPDB API. Please check your API key and try again.';
      toast.add({ severity: 'error', summary: 'Error', detail: errorMessage, life: 5000 });
    } finally {
      isLoading.value = false;
      jpdbProgress.value = '';
      jpdbApiKey.value = '';
    }
  }
</script>

<template>
  <Card>
    <template #title>
      <h3 class="text-lg font-semibold">Import from JPDB API</h3>
    </template>
    <template #content>
      <p class="mb-2">
        You can find your API key on the bottom of the settings page (<a
          href="https://jpdb.io/settings"
          target="_blank"
          rel="nofollow"
          class="text-primary-500 hover:underline"
          >https://jpdb.io/settings</a
        >)
      </p>
      <p class="mb-3 text-sm text-gray-600 dark:text-gray-400">
        Your API key will only be used for the import and won't be saved anywhere. Only the word list is sent to the server.
      </p>

      <div class="mb-3">
        <span class="p-float-label">
          <InputText id="jpdbApiKey" v-model="jpdbApiKey" class="w-full" type="password" />
          <label for="jpdbApiKey">JPDB API Key</label>
        </span>
      </div>

      <div class="mb-3 flex flex-col gap-2">
        <div class="flex items-center">
          <Checkbox id="blacklistedAsKnown" v-model="blacklistedAsKnown" :binary="true" />
          <label for="blacklistedAsKnown" class="ml-2">Consider <strong>blacklisted</strong> as known (please check your blacklisted settings on JPDB)</label>
        </div>
        <div class="flex items-center">
          <Checkbox id="dueAsKnown" v-model="dueAsKnown" :binary="true" />
          <label for="dueAsKnown" class="ml-2">Consider <strong>due</strong> as known</label>
        </div>
        <div class="flex items-center">
          <Checkbox id="suspendedAsKnown" v-model="suspendedAsKnown" :binary="true" />
          <label for="suspendedAsKnown" class="ml-2">Consider <strong>suspended</strong> as known</label>
        </div>
        <div class="flex items-center">
          <Checkbox id="importAdditionalReadings" v-model="importAdditionalReadings" :binary="true" />
          <label for="importAdditionalReadings" class="ml-2">Import additional readings within frequency range of the imported reading (only the most frequent reading by default)</label>
        </div>
        <div v-if="importAdditionalReadings" class="ml-6 flex items-center gap-2">
          <label for="frequencyThreshold" class="text-sm">Frequency range:</label>
          <InputNumber id="frequencyThreshold" v-model="frequencyThreshold" :min="1000" :max="100000" :step="1000" class="w-32" />
        </div>
      </div>

      <Button label="Import from JPDB" icon="pi pi-download" :disabled="!jpdbApiKey || isLoading" class="w-full md:w-auto" @click="importFromJpdbApi" />
    </template>
  </Card>

  <LoadingOverlay :visible="isLoading">
    <p v-if="jpdbProgress">{{ jpdbProgress }}</p>
    <p v-else>Processing your data...</p>
  </LoadingOverlay>
</template>
