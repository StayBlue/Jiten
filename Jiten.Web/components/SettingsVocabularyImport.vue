<script setup lang="ts">
  import { useConfirm } from 'primevue/useconfirm';
  import ConfirmDialog from 'primevue/confirmdialog';
  import Card from 'primevue/card';
  import InputText from 'primevue/inputtext';
  import Checkbox from 'primevue/checkbox';

  import { useJitenStore } from '~/stores/jitenStore';
  import { useToast } from 'primevue/usetoast';
  import AnkiConnectImport from '~/components/AnkiConnectImport.vue';

  const toast = useToast();

  const frequencyRange = defineModel<number[]>('frequencyRange');
  frequencyRange.value = [0, 100];

  const store = useJitenStore();
  const { $api } = useNuxtApp();
  const confirm = useConfirm();
  const { JpdbApiClient } = useJpdbApi();

  const knownWordsAmount = ref(0);
  const knownFormsAmount = ref(0);
  onMounted(async () => {
    await fetchKnownWordsAmount();
  });

  async function fetchKnownWordsAmount() {
    try {
      const result = await $api<{ words: number; forms: number }>('user/vocabulary/known-ids/amount');
      knownWordsAmount.value = result.words;
      knownFormsAmount.value = result.forms;
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
          knownWordsAmount.value = 0;
          knownFormsAmount.value = 0;
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

    // Ensure it's a TXT file
    if (file.type !== 'text/plain') {
      toast.add({ severity: 'error', summary: 'Error', detail: 'Please upload a TXT file.', life: 5000 });
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
      const result = await $api<{ parsed: number; added: number }>('user/vocabulary/import-from-anki-txt', {
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
      toast.add({ severity: 'error', detail: 'Failed to process Anki file.', life: 5000 });
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
    const data = await $api<{words:number,forms:number,skipped:number}>(`user/vocabulary/import-from-frequency/${frequencyRange.value[0]}/${frequencyRange.value[1]}`, {
      method: 'POST',
    });
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
          You currently have <span class="font-extrabold text-primary-500">{{ knownWordsAmount }}</span> known words and
          <span class="font-extrabold text-primary-500">{{ knownFormsAmount }}</span> known forms.
        </p>
      </template>
      <template #content>
        <p class="mb-3">You can upload a list of known words to calculate coverage and exclude them from downloads using one of the options below.</p>
      </template>
    </Card>

    <Card class="mb-4">
      <template #title>
        <h3 class="text-lg font-semibold">Export/Import Known Word IDs</h3>
      </template>
      <template #content>
        <p class="mb-3">
          You can export your known word IDs to a text file and import them later. This is useful for backing up your data or transferring it to another
          account.
        </p>

        <div class="flex flex-col md:flex-row gap-3 mb-3">
          <Button icon="pi pi-download" label="Export Word IDs" class="w-full md:w-auto" @click="downloadKnownWordIds" />

          <FileUpload
            mode="basic"
            name="wordIdsFile"
            accept=".txt"
            :custom-upload="true"
            :auto="true"
            :choose-label="'Import Word IDs'"
            class="w-full md:w-auto"
            @select="handleWordIdsFileSelect"
          />

          <div class="flex items-center gap-2">
            <Button icon="pi pi-upload" label="Send Local Known IDs" class="w-full md:w-auto" :loading="isLoading" @click="sendLocalKnownIds" />
            <span class="text-sm text-gray-600"
              >Local IDs: <strong>{{ store.getKnownWordIds().length }}</strong></span
            >
          </div>
        </div>
      </template>
    </Card>

    <AnkiConnectImport class="mb-4" />

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
