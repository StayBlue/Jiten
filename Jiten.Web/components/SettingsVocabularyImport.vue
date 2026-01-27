<script setup lang="ts">
  import { useConfirm } from 'primevue/useconfirm';
  import ConfirmDialog from 'primevue/confirmdialog';
  import Card from 'primevue/card';
  import InputText from 'primevue/inputtext';
  import Checkbox from 'primevue/checkbox';

  import { useJitenStore } from '~/stores/jitenStore';
  import { useToast } from 'primevue/usetoast';
  import AnkiConnectImport from '~/components/AnkiConnectImport.vue';
  import SettingsFsrsParameters from '~/components/SettingsFsrsParameters.vue';
  import type { FsrsExportDto, FsrsImportResultDto } from '~/types/types';

  const toast = useToast();

  const frequencyRange = defineModel<number[]>('frequencyRange');
  frequencyRange.value = [0, 100];

  const store = useJitenStore();
  const { $api } = useNuxtApp();
  const confirm = useConfirm();
  const { JpdbApiClient } = useJpdbApi();

  const youngWordsAmount = ref(0);
  const matureWordsAmount = ref(0);
  const masteredWordsAmount = ref(0);
  const blacklistedWordsAmount = ref(0);
  const youngFormsAmount = ref(0);
  const matureFormsAmount = ref(0);
  const masteredFormsAmount = ref(0);
  const blacklistedFormsAmount = ref(0);

  const totalWordsAmount = computed(() => youngWordsAmount.value + matureWordsAmount.value + masteredWordsAmount.value + blacklistedWordsAmount.value);
  const totalFormsAmount = computed(() => youngFormsAmount.value + matureFormsAmount.value + masteredFormsAmount.value + blacklistedFormsAmount.value);

  onMounted(async () => {
    await fetchKnownWordsAmount();
  });

  async function fetchKnownWordsAmount() {
    try {
      const result = await $api<{
        young: number;
        mature: number;
        mastered: number;
        blacklisted: number;
        youngForm: number;
        matureForm: number;
        masteredForm: number;
        blacklistedForm: number;
      }>('user/vocabulary/known-ids/amount');
      youngWordsAmount.value = result.young;
      matureWordsAmount.value = result.mature;
      masteredWordsAmount.value = result.mastered;
      blacklistedWordsAmount.value = result.blacklisted;
      youngFormsAmount.value = result.youngForm;
      matureFormsAmount.value = result.matureForm;
      masteredFormsAmount.value = result.masteredForm;
      blacklistedFormsAmount.value = result.blacklistedForm;
    } catch {}
  }

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

          youngWordsAmount.value = 0;
          matureWordsAmount.value = 0;
          masteredWordsAmount.value = 0;
          blacklistedWordsAmount.value = 0;
          youngFormsAmount.value = 0;
          matureFormsAmount.value = 0;
          masteredFormsAmount.value = 0;
          blacklistedFormsAmount.value = 0;
        } catch (e) {
          console.error(e);
          toast.add({ severity: 'error', summary: 'Error', detail: 'Failed to clear known words on server.', life: 5000 });
        }
      },
      reject: () => {},
    });
  }

  const isLoading = ref(false);
  const jpdbApiKey = ref('');
  const blacklistedAsKnown = ref(true);
  const dueAsKnown = ref(true);
  const suspendedAsKnown = ref(false);
  const jpdbProgress = ref('');

  const uploadedCount = ref<number | null>(null);
  const addedCount = ref<number | null>(null);
  const skippedCount = ref<number | null>(null);
  const parseWordsAnkiTxt = ref(false);

  async function importFromJpdbApi() {
    if (!jpdbApiKey.value) {
      toast.add({ severity: 'error', summary: 'Error', detail: 'Please enter your JPDB API key.', life: 5000 });
      return;
    }

    try {
      isLoading.value = true;
      jpdbProgress.value = 'Initializing JPDB API client...';
      toast.add({ severity: 'info', summary: 'Processing', detail: 'Importing from JPDB API...', life: 5000 });

      const client = new JpdbApiClient(jpdbApiKey.value);

      jpdbProgress.value = 'Fetching user decks...';
      await new Promise((resolve) => setTimeout(resolve, 100)); // Allow UI to update

      const response = await client.getFilteredVocabularyIds(blacklistedAsKnown.value, dueAsKnown.value, suspendedAsKnown.value);

      if (response && response.length > 0) {
        jpdbProgress.value = 'Sending vocabulary to your account...';
        await new Promise((resolve) => setTimeout(resolve, 100)); // Allow UI to update

        // Send IDs to server to save into the user's account
        const result = await $api<{ added: number; skipped: number }>('user/vocabulary/import-from-ids', {
          method: 'POST',
          body: JSON.stringify(response),
          headers: { 'Content-Type': 'application/json' },
        });

        if (result) {
          await fetchKnownWordsAmount();
          toast.add({ severity: 'success', summary: 'Synced with account', detail: `Added ${result.added}, skipped ${result.skipped}.`, life: 6000 });
        } else {
          toast.add({ severity: 'info', summary: 'No changes', detail: 'No words were added to your account.', life: 5000 });
        }
        console.log(`JPDB IDs sent to server: ${response.length}`, response);
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
      // Clear the API key for security
      jpdbApiKey.value = '';
    }
  }

  async function handleAnkiFileSelect(event) {
    const file = event.files?.[0];
    if (!file) {
      toast.add({ severity: 'error', summary: 'Error', detail: 'No file selected.', life: 5000 });
      return;
    }

    // Validate file extension
    const fileName = file.name.toLowerCase();
    if (!fileName.endsWith('.txt') && !fileName.endsWith('.csv')) {
      toast.add({ severity: 'error', summary: 'Error', detail: 'Please upload a .txt or .csv file.', life: 5000 });
      return;
    }

    try {
      // Show loading indicator
      isLoading.value = true;
      toast.add({ severity: 'info', summary: 'Processing Anki file...', detail: 'Please wait...', life: 5000 });

      // Create FormData to send the file
      const formData = new FormData();
      formData.append('file', file);

      // Send the file to the API (server parses and saves to user account)
      const result = await $api<{ parsed: number; added: number }>(`user/vocabulary/import-from-anki-txt?parseWords=${parseWordsAnkiTxt.value}`, {
        method: 'POST',
        body: formData,
      });

      if (result) {
        uploadedCount.value = result.parsed;
        addedCount.value = result.added;
        await fetchKnownWordsAmount();
        toast.add({
          severity: 'success',
          summary: 'Known words updated',
          detail: `Parsed ${result.parsed} words, added ${result.added} forms.`,
          life: 6000,
        });
      }
    } catch (error) {
      console.error('Error processing Anki file:', error);
      const errorMessage = error instanceof Error ? error.message : 'Failed to process Anki file.';
      toast.add({ severity: 'error', summary: 'Error', detail: errorMessage, life: 5000 });
    } finally {
      isLoading.value = false;
    }
  }

  const updateMinFrequency = (value: number) => {
    if (frequencyRange.value) {
      frequencyRange.value = [value, frequencyRange.value[1]];
    }
  };

  const updateMaxFrequency = (value: number) => {
    if (frequencyRange.value) {
      frequencyRange.value = [frequencyRange.value[0], value];
    }
  };

  async function getVocabularyByFrequency() {
    const data = await $api<{ words: number; forms: number; skipped: number }>(
      `user/vocabulary/import-from-frequency/${frequencyRange.value[0]}/${frequencyRange.value[1]}`,
      {
        method: 'POST',
      }
    );
    toast.add({ severity: 'success', detail: `Added ${data.words} words, ${data.forms} forms by frequency range.`, life: 5000 });
    await nextTick();
    await fetchKnownWordsAmount();
  }

  async function downloadKnownWordIds() {
    try {
      const wordIds = await $api<number[]>('user/vocabulary/known-ids');
      const content = wordIds.join('\n');
      const blob = new Blob([content], { type: 'text/plain' });
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = 'jiten-known-word-ids.txt';
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      URL.revokeObjectURL(url);
      toast.add({ severity: 'success', detail: `Exported ${wordIds.length} word IDs to text file.`, life: 5000 });
    } catch (e) {
      console.error(e);
      toast.add({ severity: 'error', summary: 'Error', detail: 'Failed to export from server.', life: 5000 });
    }
  }

  async function sendLocalKnownIds() {
    try {
      const ids = store.getKnownWordIds();
      if (!ids || ids.length === 0) {
        toast.add({ severity: 'info', summary: 'No data', detail: 'No known word IDs found in local storage.', life: 4000 });
        return;
      }
      isLoading.value = true;

      const bodyPayload = ids || [];

      const result = await $api<{ added: number; skipped: number }>('user/vocabulary/import-from-ids', {
        method: 'POST',
        body: JSON.stringify(bodyPayload),
        headers: {
          'Content-Type': 'application/json',
        },
      });
      if (result) {
        addedCount.value = result.added;
        await fetchKnownWordsAmount();
        toast.add({ severity: 'success', summary: 'Known words saved', detail: `Added ${result.added} forms.`, life: 6000 });
      }
    } catch (e) {
      console.error(e);
      toast.add({ severity: 'error', summary: 'Error', detail: 'Failed to send known word IDs.', life: 5000 });
    } finally {
      isLoading.value = false;
    }
  }

  function handleWordIdsFileSelect(event) {
    const file = event.files?.[0];
    if (!file) {
      toast.add({ severity: 'error', summary: 'Error', detail: 'No file selected.', life: 5000 });
      return;
    }

    // Ensure it's a TXT file
    if (file.type !== 'text/plain') {
      toast.add({ severity: 'error', summary: 'Error', detail: 'Please upload a TXT file.', life: 5000 });
      return;
    }

    const reader = new FileReader();

    reader.onload = async (e) => {
      try {
        const text = e.target?.result as string;

        // Split the text by newlines and convert each line to a number
        const wordIds = text
          .split('\n')
          .map((line) => line.trim())
          .filter((line) => line !== '')
          .map((line) => parseInt(line, 10))
          .filter((id) => !isNaN(id));

        if (wordIds.length > 0) {
          const bodyPayload = wordIds || [];

          try {
            const result = await $api<{ added: number; skipped: number }>('user/vocabulary/import-from-ids', {
              method: 'POST',
              body: JSON.stringify(bodyPayload),
              headers: {
                'Content-Type': 'application/json',
              },
            });
            toast.add({ severity: 'success', detail: `Imported ${wordIds.length} word IDs.`, life: 5000 });
          } catch {
          } finally {
            await nextTick();
            await fetchKnownWordsAmount();
          }
        } else {
          toast.add({ severity: 'info', detail: 'No valid word IDs found in the file.', life: 5000 });
        }
      } catch (error) {
        console.error('Error processing file:', error);
        toast.add({ severity: 'error', detail: 'Failed to process file. Invalid data format.', life: 5000 });
      }
    };

    reader.onerror = () => {
      toast.add({ severity: 'error', detail: 'Error reading file.', life: 5000 });
    };

    reader.readAsText(file);
  }

  // FSRS Export/Import
  const fsrsOverwriteExisting = ref(false);
  const fsrsImportResult = ref<FsrsImportResultDto | null>(null);
  const fsrsIsLoading = ref(false);

  // Export Words
  const exportWordsLoading = ref(false);
  const exportKanaOnly = ref(true);
  const exportMastered = ref(true);
  const exportMature = ref(true);
  const exportYoung = ref(true);
  const exportBlacklisted = ref(true);

  async function downloadFsrsVocabulary() {
    try {
      fsrsIsLoading.value = true;
      toast.add({ severity: 'info', summary: 'Exporting...', detail: 'Fetching your vocabulary data...', life: 3000 });

      const data = await $api<FsrsExportDto>('user/vocabulary/export');

      // Create JSON blob
      const jsonContent = JSON.stringify(data, null, 2);
      const blob = new Blob([jsonContent], { type: 'application/json' });
      const url = URL.createObjectURL(blob);

      // Format date for filename
      const exportDate = new Date(data.exportDate);
      const dateStr = exportDate.toISOString().split('T')[0];

      // Download file
      const a = document.createElement('a');
      a.href = url;
      a.download = `jiten-vocabulary-export-${dateStr}.json`;
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      URL.revokeObjectURL(url);

      toast.add({
        severity: 'success',
        summary: 'Export Successful',
        detail: `Exported ${data.totalCards} cards with ${data.totalReviews} review logs.`,
        life: 5000,
      });
    } catch (error) {
      console.error('Error exporting FSRS vocabulary:', error);
      toast.add({ severity: 'error', summary: 'Error', detail: 'Failed to export vocabulary data.', life: 5000 });
    } finally {
      fsrsIsLoading.value = false;
    }
  }

  async function exportWords() {
    try {
      exportWordsLoading.value = true;
      toast.add({ severity: 'info', summary: 'Exporting...', detail: 'Generating your vocabulary export...', life: 3000 });

      const config = useRuntimeConfig();
      const authStore = useAuthStore();

      const params = new URLSearchParams({
        exportKanaOnly: exportKanaOnly.value.toString(),
        exportMastered: exportMastered.value.toString(),
        exportMature: exportMature.value.toString(),
        exportYoung: exportYoung.value.toString(),
        exportBlacklisted: exportBlacklisted.value.toString(),
      });

      const response = await fetch(`${config.public.baseURL}user/vocabulary/export-words?${params}`, {
        method: 'GET',
        headers: { Authorization: `Bearer ${authStore.accessToken}` },
      });

      if (!response.ok) throw new Error('Failed to export vocabulary');

      const blob = await response.blob();
      const url = URL.createObjectURL(blob);
      const dateStr = new Date().toISOString().split('T')[0];
      const a = document.createElement('a');
      a.href = url;
      a.download = `jiten-vocabulary-export-${dateStr}.txt`;
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      URL.revokeObjectURL(url);

      toast.add({ severity: 'success', summary: 'Export Successful', detail: 'Your vocabulary has been exported.', life: 5000 });
    } catch (error) {
      console.error('Error exporting words:', error);
      toast.add({ severity: 'error', summary: 'Error', detail: 'Failed to export vocabulary.', life: 5000 });
    } finally {
      exportWordsLoading.value = false;
    }
  }

  function handleFsrsFileSelect(event) {
    const file = event.files?.[0];
    if (!file) {
      toast.add({ severity: 'error', summary: 'Error', detail: 'No file selected.', life: 5000 });
      return;
    }

    // Ensure it's a JSON file
    if (file.type !== 'application/json') {
      toast.add({ severity: 'error', summary: 'Error', detail: 'Please upload a JSON file.', life: 5000 });
      return;
    }

    const reader = new FileReader();

    reader.onload = async (e) => {
      try {
        fsrsIsLoading.value = true;
        fsrsImportResult.value = null;
        toast.add({ severity: 'info', summary: 'Importing...', detail: 'Processing vocabulary data...', life: 3000 });

        const text = e.target?.result as string;
        const importData: FsrsExportDto = JSON.parse(text);

        // Validate basic structure
        if (!importData.cards || !Array.isArray(importData.cards)) {
          throw new Error('Invalid file format: missing or invalid cards array');
        }

        // Send to API
        const result = await $api<FsrsImportResultDto>(`user/vocabulary/import?overwrite=${fsrsOverwriteExisting.value}`, {
          method: 'POST',
          body: JSON.stringify(importData),
          headers: {
            'Content-Type': 'application/json',
          },
        });

        fsrsImportResult.value = result;

        if (result.validationErrors && result.validationErrors.length > 0) {
          toast.add({
            severity: 'warn',
            summary: 'Import Completed with Errors',
            detail: `Imported ${result.cardsImported} cards, but ${result.validationErrors.length} errors occurred.`,
            life: 6000,
          });
        } else {
          const totalProcessed = result.cardsImported + result.cardsUpdated + result.cardsSkipped;
          toast.add({
            severity: 'success',
            summary: 'Import Successful',
            detail: `Processed ${totalProcessed} cards: ${result.cardsImported} imported, ${result.cardsUpdated} updated, ${result.cardsSkipped} skipped.`,
            life: 6000,
          });
        }

        await fetchKnownWordsAmount();
      } catch (error) {
        console.error('Error importing FSRS vocabulary:', error);
        const errorMsg = error instanceof Error ? error.message : 'Failed to import vocabulary data.';
        toast.add({ severity: 'error', summary: 'Import Failed', detail: errorMsg, life: 5000 });
      } finally {
        fsrsIsLoading.value = false;
      }
    };

    reader.onerror = () => {
      toast.add({ severity: 'error', summary: 'Error', detail: 'Error reading file.', life: 5000 });
      fsrsIsLoading.value = false;
    };

    reader.readAsText(file);
  }
</script>

<template>
  <div class="">
    <Card class="mb-4">
      <template #title>
        <div class="flex justify-between items-center">
          <h2 class="text-xl font-bold">Vocabulary Management</h2>
        </div>
      </template>
      <template #subtitle>
        <p class="text-gray-600 dark:text-gray-300">
          You're currently tracking <span class="font-extrabold text-primary-600 dark:text-primary-300">{{ totalWordsAmount }}</span> words under
          <b>{{ totalFormsAmount }}</b> forms. Of them,
        </p>
        <ul class="text-gray-600 dark:text-gray-300 space-y-1 ml-3">
          <li>
            <span class="font-extrabold text-yellow-600 dark:text-yellow-300">{{ youngWordsAmount }}</span> are young (<b>{{ youngFormsAmount }}</b> forms).
          </li>
          <li>
            <span class="font-extrabold text-green-600 dark:text-green-300">{{ matureWordsAmount }}</span> are mature (<b>{{ matureFormsAmount }}</b> forms).
          </li>
          <li>
            <span class="font-extrabold text-green-600 dark:text-green-300">{{ masteredWordsAmount }}</span> are mastered (<b>{{ masteredFormsAmount }}</b>
            forms).
          </li>
          <li>
            <span class="font-extrabold text-gray-600 dark:text-gray-300">{{ blacklistedWordsAmount }}</span> are blacklisted (<b>{{
              blacklistedFormsAmount
            }}</b>
            forms).
          </li>
        </ul>
      </template>
      <template #content>
        <p class="mb-3">You can upload a list of known words to calculate coverage and exclude them from downloads using one of the options below.</p>
        <div class="mt-3">
          <NuxtLink to="/settings/cards">
            <Button icon="pi pi-table" label="View All Words" severity="info" outlined class="w-full md:w-auto" />
          </NuxtLink>
        </div>
      </template>
    </Card>

    <SettingsFsrsParameters class="mb-4" />

    <AnkiConnectImport class="mb-4" @import-complete="fetchKnownWordsAmount" />

    <Card class="mb-4">
      <template #title>
        <h3 class="text-lg font-semibold">Export/Import Complete Vocabulary (Backup)</h3>
      </template>
      <template #content>
        <p class="mb-3">
          Export and import your complete vocabulary with full data. This includes card states, review history, stability, difficulty, and due dates. Use this
          for complete backups or transferring data between accounts.
        </p>

        <div class="mb-4">
          <h4 class="text-md font-semibold mb-2">Export</h4>
          <Button icon="pi pi-download" label="Export Complete Vocabulary" :loading="fsrsIsLoading" class="w-full md:w-auto" @click="downloadFsrsVocabulary" />
        </div>

        <div class="mb-3">
          <h4 class="text-md font-semibold mb-2">Import</h4>
          <div class="mb-3 flex items-center">
            <Checkbox id="fsrsOverwrite" v-model="fsrsOverwriteExisting" :binary="true" />
            <label for="fsrsOverwrite" class="ml-2">
              <span>Overwrite existing cards</span>
              <span class="text-sm text-gray-600 dark:text-gray-400 block">
                If unchecked, only new cards will be added. If checked, existing cards will be replaced with imported data.
              </span>
            </label>
          </div>

          <FileUpload
            mode="basic"
            name="fsrsFile"
            accept=".json"
            :custom-upload="true"
            :auto="true"
            :choose-label="'Select JSON File'"
            :disabled="fsrsIsLoading"
            class="mb-3"
            @select="handleFsrsFileSelect"
          />

          <div v-if="fsrsImportResult" class="bg-gray-100 dark:bg-gray-800 p-3 rounded">
            <h5 class="font-semibold mb-2">Import Results</h5>
            <div class="text-sm space-y-1">
              <div>
                Cards imported: <strong class="text-green-600">{{ fsrsImportResult.cardsImported }}</strong>
              </div>
              <div>
                Cards updated: <strong class="text-blue-600">{{ fsrsImportResult.cardsUpdated }}</strong>
              </div>
              <div>
                Cards skipped: <strong class="text-amber-600">{{ fsrsImportResult.cardsSkipped }}</strong>
              </div>
              <div>
                Review logs imported: <strong>{{ fsrsImportResult.reviewLogsImported }}</strong>
              </div>
              <div v-if="fsrsImportResult.validationErrors && fsrsImportResult.validationErrors.length > 0">
                <details class="mt-2">
                  <summary class="cursor-pointer text-red-600 font-semibold">Validation Errors ({{ fsrsImportResult.validationErrors.length }})</summary>
                  <ul class="list-disc list-inside mt-2 text-red-600 space-y-1">
                    <li v-for="(error, index) in fsrsImportResult.validationErrors" :key="index">{{ error }}</li>
                  </ul>
                </details>
              </div>
            </div>
          </div>
        </div>
      </template>
    </Card>

    <Card class="mb-4">
      <template #title>
        <h3 class="text-lg font-semibold">Export Word List</h3>
      </template>
      <template #content>
        <p class="mb-3">
          Export your vocabulary as a text file organised by learning state. <br />
          Each line contain a single word.
        </p>

        <div class="mb-3 flex flex-col gap-3">
          <h4 class="text-md font-semibold mb-2">Export Options</h4>

          <div class="mb-3 flex flex-col gap-2">
            <div class="flex items-center">
              <Checkbox id="exportKanaOnly" v-model="exportKanaOnly" :binary="true" />
              <label for="exportKanaOnly" class="ml-2">
                <span>Export <strong>kana only</strong> words</span>
                <span class="text-sm text-gray-600 dark:text-gray-400 block"> Include words that are written entirely in hiragana or katakana </span>
              </label>
            </div>

            <div class="flex items-center">
              <Checkbox id="exportMastered" v-model="exportMastered" :binary="true" />
              <label for="exportMastered" class="ml-2">
                <span>Export <strong>mastered</strong> words</span>
              </label>
            </div>

            <div class="flex items-center">
              <Checkbox id="exportMature" v-model="exportMature" :binary="true" />
              <label for="exportMature" class="ml-2">
                <span>Export <strong>mature</strong> words</span>
                <span class="text-sm text-gray-600 dark:text-gray-400 block"> Cards in review with interval ≥ 21 days </span>
              </label>
            </div>

            <div class="flex items-center">
              <Checkbox id="exportYoung" v-model="exportYoung" :binary="true" />
              <label for="exportYoung" class="ml-2">
                <span>Export <strong>young</strong> words</span>
                <span class="text-sm text-gray-600 dark:text-gray-400 block"> New, learning, relearning, or review with interval &lt; 21 days </span>
              </label>
            </div>

            <div class="flex items-center">
              <Checkbox id="exportBlacklisted" v-model="exportBlacklisted" :binary="true" />
              <label for="exportBlacklisted" class="ml-2">
                <span>Export <strong>blacklisted</strong> words</span>
              </label>
            </div>
          </div>

          <Button icon="pi pi-download" label="Export Words" :loading="exportWordsLoading" class="w-full md:w-auto" @click="exportWords" />
        </div>
      </template>
    </Card>

    <Card class="mb-4">
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
        </div>

        <Button label="Import from JPDB" icon="pi pi-download" :disabled="!jpdbApiKey || isLoading" @click="importFromJpdbApi" class="w-full md:w-auto" />
      </template>
    </Card>

    <Card class="mb-4">
      <template #title>
        <h3 class="text-lg font-semibold">Import from Anki Deck or List of Words</h3>
      </template>
      <template #content>
        <p class="mb-2">Anki: export your deck as Export format: Notes in Plain Text (.txt) and untick all the boxes.</p>
        <p class="mb-2">This can also import a list of words, one per line. The word can be ended by a comma or a tab as long as there's only one per line.</p>
        <p class="mb-3 text-sm text-amber-600 dark:text-amber-400">
          Warning: This will mark ALL words contained in the deck as known. You will have to remove the lines you don't want manually before uploading your
          file. The words to add need to be the first word on each line. Limited to 50000 words.
        </p>

        <div class="mb-3 flex items-center">
          <Checkbox id="parseWordsAnkiTxt" v-model="parseWordsAnkiTxt" :binary="true" />
          <label for="parseWordsAnkiTxt" class="ml-2">
            <span>Parse words instead of importing directly</span>
            <span class="text-sm text-gray-600 dark:text-gray-400 block">
              Only use if you have conjugated verbs instead of the dictionary form (less accurate)
            </span>
          </label>
        </div>

        <FileUpload
          mode="basic"
          name="ankiFile"
          accept=".txt, .csv"
          :custom-upload="true"
          :auto="true"
          :choose-label="'Select .txt or .csv File'"
          class="mb-3"
          @select="handleAnkiFileSelect"
        />

        <div v-if="addedCount !== null || skippedCount !== null || uploadedCount !== null" class="text-sm text-gray-700 dark:text-gray-300">
          <div v-if="uploadedCount !== null">
            Parsed from file: <strong>{{ uploadedCount }}</strong>
          </div>
          <div v-if="addedCount !== null">
            Added: <strong class="text-green-600">{{ addedCount }}</strong>
          </div>
          <!--          <div v-if="skippedCount !== null">Already present: <strong class="text-amber-600">{{ skippedCount }}</strong></div>-->
        </div>
      </template>
    </Card>

    <Card class="mb-4">
      <template #title>
        <h3 class="text-lg font-semibold">Add Words by Frequency Range</h3>
      </template>
      <template #content>
        <div class="flex flex-col gap-4">
          <div class="flex flex-row flex-wrap gap-2 items-center">
            <InputNumber
              :model-value="frequencyRange?.[0] ?? 0"
              show-buttons
              fluid
              size="small"
              class="max-w-30 flex-shrink-0"
              @update:model-value="updateMinFrequency"
            />
            <Slider v-model="frequencyRange" range :min="0" :max="10000" class="flex-grow mx-2 flex-basis-auto" />
            <InputNumber
              :model-value="frequencyRange?.[1] ?? 0"
              show-buttons
              fluid
              size="small"
              class="max-w-30 flex-shrink-0"
              @update:model-value="updateMaxFrequency"
            />
          </div>
          <Button icon="pi pi-plus" label="Add Words by Frequency" class="w-full md:w-auto" @click="getVocabularyByFrequency" />
        </div>
      </template>
    </Card>

    <Card class="mb-4">
      <template #title>
        <div class="flex justify-between items-center">
          <h2 class="text-xl font-bold">Danger Zone</h2>
        </div>
      </template>
      <template #subtitle> </template>
      <template #content>
        <p class="mb-3">
          Clicking this button will <b>delete ALL your known words</b>. This action cannot be undone. Please make a backup before using it, and use it at your
          own risk.
        </p>

        <div class="flex">
          <Button severity="danger" icon="pi pi-trash" label="Clear All Known Words" @click="clearKnownWords" />
        </div>
      </template>
    </Card>

    <!-- Loading overlay -->
    <div v-if="isLoading" class="loading-overlay">
      <i class="pi pi-spin pi-spinner" style="font-size: 2rem" />
      <p v-if="jpdbProgress">{{ jpdbProgress }}</p>
      <p v-else>Processing your data...</p>
    </div>
  </div>
</template>

<style scoped>
  .loading-overlay {
    position: fixed;
    top: 0;
    left: 0;
    width: 100%;
    height: 100%;
    background-color: rgba(0, 0, 0, 0.8);
    display: flex;
    flex-direction: column;
    justify-content: center;
    align-items: center;
    z-index: 9999;
    color: white;
  }

  .loading-overlay i {
    margin-bottom: 1rem;
  }
</style>
