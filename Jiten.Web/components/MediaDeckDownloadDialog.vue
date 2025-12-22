<script setup lang="ts">
  import { type Deck, DeckDownloadType, DeckFormat, DeckOrder } from '~/types';
  import { SelectButton, Select, Slider, InputNumber, Checkbox, Dialog, Button, BlockUI, ProgressSpinner } from 'primevue';
  import { debounce } from 'perfect-debounce';
  import { useApiFetch } from '~/composables/useApiFetch';
  import { useAuthStore } from '~/stores/authStore';
  import { computed, onMounted, ref, watch } from 'vue';

  const props = defineProps<{
    deck: Deck;
    visible: boolean;
  }>();

  const emit = defineEmits(['update:visible']);
  const { $api } = useNuxtApp();
  const authStore = useAuthStore();

  const localVisible = ref(props.visible);
  const downloading = ref(false);

  type Mode = 'manual' | 'target';
  const downloadMode = ref<Mode>('manual');
  const targetPercentage = ref(80);
  const minTargetPercentage = computed(() => {
    // Can't target below current coverage
    const coverage = props.deck.coverage ?? 0;
    return Math.ceil(coverage * 10) / 10;
  });

  // --- Options ---
  const deckOrders = getEnumOptions(DeckOrder, getDeckOrderText);
  let downloadTypes = getEnumOptions(DeckDownloadType, getDownloadTypeText);
  downloadTypes = downloadTypes.filter((d) => d.value != DeckDownloadType.TargetCoverage);

  const formatOptions = [
    {
      value: DeckFormat.Anki,
      label: 'Anki',
      desc: 'Anki deck (.apkg)',
      icon: 'pi pi-clone',
      longDesc: `Generates an .apkg file using the <a href="https://github.com/donkuri/lapis/tree/main" target="_blank" class="text-primary hover:underline font-medium">Lapis template</a>, for use with Anki.`,
    },
    {
      value: DeckFormat.Txt,
      label: 'Text',
      desc: 'Vocabulary List (.txt)',
      icon: 'pi pi-file',
      longDesc: `Plain text file, one word per line, vocabulary only.`,
    },
    {
      value: DeckFormat.TxtRepeated,
      label: 'Text (Rep)',
      desc: 'Repeated vocab (.txt)',
      icon: 'pi pi-copy',
      longDesc: `Plain text format, one word per line, vocabulary only. The vocabulary is repeated for each occurrence to handle frequency on some websites.`,
    },
    {
      value: DeckFormat.Csv,
      label: 'CSV',
      desc: 'Spreadsheet',
      icon: 'pi pi-table',
      longDesc: `The vocabulary list with more data, such as the furigana, pitch, definitions, example sentence, etc.`,
    },
    {
      value: DeckFormat.Yomitan,
      label: 'Yomitan',
      desc: 'Occurrences dic (.zip)',
      icon: 'pi pi-book',
      longDesc: `A zip file importable as a Yomitan dictionary. It displays the specific number of occurrences of each word within this media source.`,
    },
  ];

  const modeOptions = computed(() => [
    { label: 'Manual Control', value: 'manual', icon: 'pi pi-sliders-h' },
    {
      label: authStore.isAuthenticated ? 'Target Coverage %' : 'Target Coverage % (Login required)',
      value: 'target',
      icon: 'pi pi-chart-pie',
      disabled: !authStore.isAuthenticated,
    },
  ]);

  // Models
  const format = defineModel<DeckFormat>('deckFormat', { default: DeckFormat.Anki });
  const downloadType = defineModel<DeckDownloadType>('downloadType', { default: DeckDownloadType.TopDeckFrequency });
  const deckOrder = defineModel<DeckOrder>('deckOrder', { default: DeckOrder.DeckFrequency });
  const frequencyRange = defineModel<number[]>('frequencyRange');

  // Exclusions
  const excludeKana = ref(false);
  const excludeMatureMasteredBlacklisted = ref(false);
  const excludeAllTrackedWords = ref(false);
  const excludeExampleSentences = ref(false);

  // Stats
  const currentSliderMax = ref(props.deck.uniqueWordCount);
  const debouncedCurrentCardAmount = ref(0);

  // Computed for current selection details
  const currentFormatDetails = computed(() => {
    return formatOptions.find((f) => f.value === format.value) || formatOptions[0];
  });

  const targetPercentageCardCount = computed(() => {
    return Math.floor(props.deck.uniqueWordCount * (targetPercentage.value / 100));
  });

  const currentCardAmount = computed(() => {
    if (downloadMode.value === 'target') return targetPercentageCardCount.value;

    if (downloadType.value == DeckDownloadType.Full) {
      return props.deck.uniqueWordCount;
    } else if (downloadType.value == DeckDownloadType.TopDeckFrequency || downloadType.value == DeckDownloadType.TopChronological) {
      return (frequencyRange.value?.[1] ?? 0) - (frequencyRange.value?.[0] ?? 0);
    } else if (downloadType.value == DeckDownloadType.TopGlobalFrequency) {
      return debouncedCurrentCardAmount.value;
    }
    return 0;
  });

  // --- Lifecycle & Watches ---
  onMounted(() => {
    if (!frequencyRange.value) {
      frequencyRange.value = [0, Math.min(props.deck.uniqueWordCount, 5000)];
    }
  });

  watch(
    () => props.visible,
    (newVal) => (localVisible.value = newVal)
  );
  watch(localVisible, (newVal) => emit('update:visible', newVal));

  watch(
    () => downloadType.value,
    (newVal) => {
      if (newVal == DeckDownloadType.Full) {
        frequencyRange.value = [0, props.deck.uniqueWordCount];
        currentSliderMax.value = props.deck.uniqueWordCount;
      } else if (newVal == DeckDownloadType.TopDeckFrequency || newVal == DeckDownloadType.TopChronological) {
        frequencyRange.value = [0, Math.min(props.deck.uniqueWordCount, 5000)];
        currentSliderMax.value = props.deck.uniqueWordCount;
      } else if (newVal == DeckDownloadType.TopGlobalFrequency) {
        frequencyRange.value = [1, Math.min(200000, 30000)];
        currentSliderMax.value = 200000;
        updateDebounced();
      }
    }
  );

  watch(
    () => frequencyRange.value,
    () => {
      if (downloadType.value == DeckDownloadType.TopGlobalFrequency) updateDebounced();
    }
  );

  watch(
    () => authStore.isAuthenticated,
    (isAuth) => {
      if (!isAuth && downloadMode.value === 'target') {
        downloadMode.value = 'manual';
      }
    }
  );

  watch(
    minTargetPercentage,
    (minVal) => {
      if (targetPercentage.value < minVal) {
        targetPercentage.value = minVal;
      }
    },
    { immediate: true }
  );

  const updateDebounced = debounce(async () => {
    const { data: response } = await useApiFetch<number>(`media-deck/${props.deck.deckId}/vocabulary-count-frequency`, {
      query: { minFrequency: frequencyRange.value![0], maxFrequency: frequencyRange.value![1] },
    });
    debouncedCurrentCardAmount.value = response.value ?? 0;
  }, 500);

  // --- Actions ---
  const downloadFile = async () => {
    try {
      downloading.value = true;
      const url = `media-deck/${props.deck.deckId}/download`;

      let payload: any = {
        format: format.value,
        excludeKana: excludeKana.value,
        excludeMatureMasteredBlacklisted: excludeMatureMasteredBlacklisted.value,
        excludeAllTrackedWords: excludeAllTrackedWords.value,
        excludeExampleSentences: excludeExampleSentences.value,
      };

      if (downloadMode.value === 'target') {
        payload = {
          ...payload,
          downloadType: DeckDownloadType.TargetCoverage,
          targetPercentage: targetPercentage.value,
        };
      } else {
        payload = {
          ...payload,
          downloadType: downloadType.value,
          order: deckOrder.value,
          minFrequency: frequencyRange.value![0],
          maxFrequency: frequencyRange.value![1],
        };
      }

      const response = await $api<File>(url, {
        method: 'POST',
        body: payload,
        headers: { 'Content-Type': 'application/json' },
        responseType: 'blob',
      });

      if (response) {
        localVisible.value = false;
        const blobUrl = window.URL.createObjectURL(response);
        const link = document.createElement('a');
        link.href = blobUrl;

        const extMap: Record<string, string> = {
          [DeckFormat.Anki]: 'apkg',
          [DeckFormat.Csv]: 'csv',
          [DeckFormat.Yomitan]: 'zip',
          [DeckFormat.Txt]: 'txt',
          [DeckFormat.TxtRepeated]: 'txt',
        };

        link.setAttribute('download', `${localiseTitle(props.deck).substring(0, 30)}.${extMap[format.value]}`);
        document.body.appendChild(link);
        link.click();
        link.remove();
      }
    } catch (err) {
      console.error('Error:', err);
    } finally {
      downloading.value = false;
    }
  };
</script>

<template>
  <Dialog v-model:visible="localVisible" modal header="Download Deck" class="w-[95vw] sm:w-[90vw] md:w-[42rem]" :pt="{ content: { class: 'p-0' } }">
    <div class="flex flex-col h-full">
      <!-- SCROLLABLE CONTENT AREA -->
      <div class="p-5 overflow-y-auto max-h-[70vh] flex flex-col gap-6">
        <!-- 1. FORMAT SELECTION -->
        <section>
          <div class="text-xs font-bold text-gray-500 dark:text-gray-400 uppercase tracking-widest mb-3">File Format</div>

          <!-- Grid -->
          <div class="grid grid-cols-2 sm:grid-cols-3 gap-3">
            <div
              v-for="opt in formatOptions"
              :key="opt.value"
              @click="format = opt.value"
              class="border rounded-lg p-3 cursor-pointer transition-all duration-200 flex flex-col gap-1 items-start relative hover:border-gray-400 hover:dark:border-gray-500 hover:shadow-sm"
              :class="format === opt.value ? 'bg-primary-50 dark:bg-gray-600 border-primary dark:border-gray-700 ring-1 ring-primary' : 'bg-white dark:bg-gray-800 border-gray-200 dark:border-gray-700'"
            >
              <div class="flex items-center gap-2 w-full">
                <i :class="[opt.icon, format === opt.value ? 'text-primary' : 'text-gray-400 dark:text-gray-500']" class="text-lg"></i>
                <span class="font-semibold text-sm" :class="format === opt.value ? 'text-primary-900 dark:text-primary-300' : 'text-gray-700 dark:text-gray-300'">{{ opt.label }}</span>
              </div>
              <span class="text-[10px] leading-tight text-gray-500 dark:text-gray-400">{{ opt.desc }}</span>
              <!-- Active Badge -->
              <i v-if="format === opt.value" class="pi pi-check-circle text-primary absolute top-2 right-2 text-sm"></i>
            </div>
          </div>

          <!-- Description Box (Fixed Min-Height to prevent shift) -->
          <div class="mt-3 bg-gray-50 dark:bg-gray-900 border border-gray-200 dark:border-gray-700 rounded-md p-3 text-sm text-gray-600 dark:text-gray-300 min-h-[4.5rem] flex items-center">
            <!-- Using v-html to allow links (e.g. Lapis) -->
            <p v-html="currentFormatDetails.longDesc" class="leading-relaxed"></p>
          </div>
        </section>

        <template v-if="format != DeckFormat.Yomitan">
          <!-- 2. STRATEGY -->
          <section>
            <div class="text-xs font-bold text-gray-500 dark:text-gray-400 uppercase tracking-widest mb-3">Download Strategy</div>
            <SelectButton
              v-model="downloadMode"
              :options="modeOptions"
              option-value="value"
              option-label="label"
              option-disabled="disabled"
              class="w-full"
              :pt="{ button: { class: 'flex-1 text-sm py-2' } }"
            />

            <!-- MODE A: TARGET PERCENTAGE -->
            <div v-if="downloadMode === 'target'" class="mt-4 bg-gray-50 dark:bg-gray-900 rounded-xl p-5 border border-dashed border-gray-300 dark:border-gray-600">
              <div class="flex justify-between items-end mb-4">
                <div class="flex flex-col">
                  <span class="font-bold text-gray-800 dark:text-gray-200 text-lg">Deck Coverage</span>
                  <span class="text-xs text-gray-500 dark:text-gray-400">Select the least amount of words to reach the desired coverage.</span>
                </div>
                <div class="text-2xl font-black text-primary">{{ targetPercentage }}%</div>
              </div>
              <Slider v-model="targetPercentage" :step="0.1" :min="minTargetPercentage" :max="100" class="w-full" />
            </div>

            <!-- MODE B: MANUAL CONTROL -->
            <div v-else class="mt-4 flex flex-col gap-4">
              <div class="grid grid-cols-1 sm:grid-cols-2 gap-4">
                <div class="flex flex-col gap-1">
                  <label class="text-xs text-gray-500 dark:text-gray-400 font-medium">Filter By</label>
                  <Select v-model="downloadType" :options="downloadTypes" option-value="value" option-label="label" class="w-full text-sm" size="small" />
                </div>
                <div class="flex flex-col gap-1">
                  <label class="text-xs text-gray-500 dark:text-gray-400 font-medium">Then Sort By</label>
                  <Select v-model="deckOrder" :options="deckOrders" option-value="value" option-label="label" class="w-full text-sm" size="small" />
                </div>
              </div>

              <div v-if="downloadType != DeckDownloadType.Full" class="bg-gray-50 dark:bg-gray-900 rounded-lg p-4 border border-gray-200 dark:border-gray-700">
                <div class="flex justify-between items-center mb-3">
                  <div class="flex flex-col">
                    <span class="text-xs font-bold text-gray-500 dark:text-gray-400 uppercase">Frequency Range</span>
                    <span class="text-[10px] text-gray-400 dark:text-gray-500">Select start and end points</span>
                  </div>
                  <div class="flex items-center gap-2">
                    <InputNumber
                      :model-value="frequencyRange?.[0] ?? 0"
                      @update:model-value="(v) => (frequencyRange[0] = v)"
                      inputClass="text-center p-1 text-xs w-16 h-7"
                      :min="0"
                      :max="currentSliderMax"
                      :useGrouping="false"
                    />
                    <span class="text-gray-400 dark:text-gray-500">-</span>
                    <InputNumber
                      :model-value="frequencyRange?.[1] ?? 0"
                      @update:model-value="(v) => (frequencyRange[1] = v)"
                      inputClass="text-center p-1 text-xs w-16 h-7"
                      :min="0"
                      :max="currentSliderMax"
                      :useGrouping="false"
                    />
                  </div>
                </div>
                <Slider v-model="frequencyRange" range :min="0" :max="currentSliderMax" class="w-full" />
              </div>
            </div>
          </section>

          <!-- 3. OPTIONS -->
          <section>
            <div class="text-xs font-bold text-gray-500 dark:text-gray-400 uppercase tracking-widest mb-3">Options</div>
            <div class="flex flex-col gap-0">
              <div
                v-if="downloadMode !== 'target'"
                class="flex items-start gap-3 p-3 rounded-lg border border-transparent hover:bg-gray-50 hover:dark:bg-gray-800 hover:border-gray-200 hover:dark:border-gray-700 transition-colors cursor-pointer"
                @click="excludeKana = !excludeKana"
              >
                <Checkbox v-model="excludeKana" binary class="mt-1" @click.stop />
                <div>
                  <div class="text-sm font-medium text-gray-800 dark:text-gray-200">Exclude Kana-only Words</div>
                  <div class="text-xs text-gray-500 dark:text-gray-400">Removes words that have no Kanji (e.g., こころ, それでも, ...).</div>
                </div>
              </div>

              <div
                class="flex items-start gap-3 p-3 rounded-lg border border-transparent hover:bg-gray-50 hover:dark:bg-gray-800 hover:border-gray-200 hover:dark:border-gray-700 transition-colors cursor-pointer"
                @click="excludeExampleSentences = !excludeExampleSentences"
              >
                <Checkbox v-model="excludeExampleSentences" binary class="mt-1" @click.stop />
                <div>
                  <div class="text-sm font-medium text-gray-800 dark:text-gray-200">Exclude Example Sentences</div>
                  <div class="text-xs text-gray-500 dark:text-gray-400">Remove example sentences.</div>
                </div>
              </div>

              <div
                v-if="downloadMode !== 'target' && authStore.isAuthenticated"
                class="flex items-start gap-3 p-3 rounded-lg border border-transparent hover:bg-gray-50 hover:dark:bg-gray-800 hover:border-gray-200 hover:dark:border-gray-700 transition-colors cursor-pointer"
                @click="excludeMatureMasteredBlacklisted = !excludeMatureMasteredBlacklisted"
              >
                <Checkbox v-model="excludeMatureMasteredBlacklisted" binary class="mt-1" @click.stop />
                <div>
                  <div class="text-sm font-medium text-gray-800 dark:text-gray-200">Exclude Mature, Mastered & Blacklisted Vocabulary</div>
                  <div class="text-xs text-gray-500 dark:text-gray-400">Removes words that are mature (21+ day review interval), mastered, or blacklisted.</div>
                </div>
              </div>

              <div
                v-if="downloadMode !== 'target' && authStore.isAuthenticated"
                class="flex items-start gap-3 p-3 rounded-lg border border-transparent hover:bg-gray-50 hover:dark:bg-gray-800 hover:border-gray-200 hover:dark:border-gray-700 transition-colors cursor-pointer"
                @click="excludeAllTrackedWords = !excludeAllTrackedWords"
              >
                <Checkbox v-model="excludeAllTrackedWords" binary class="mt-1" @click.stop />
                <div>
                  <div class="text-sm font-medium text-gray-800 dark:text-gray-200">Exclude All Tracked Vocabulary</div>
                  <div class="text-xs text-gray-500 dark:text-gray-400">Removes all words in your vocabulary list, regardless of their status.</div>
                </div>
              </div>
            </div>
          </section>
        </template>
      </div>

      <!-- FOOTER -->
      <div class="bg-gray-50 dark:bg-gray-900 border-t border-gray-200 dark:border-gray-700 p-4 flex flex-col sm:flex-row justify-between items-center gap-4 shrink-0">
        <div class="text-sm text-gray-600 dark:text-gray-300">
          <span v-if="downloadMode !== 'target'">
            Result: approx <span class="font-bold text-gray-900 dark:text-gray-100">{{ currentCardAmount }}</span> cards
          </span>
        </div>
        <Button label="Download Deck" icon="pi pi-download" @click="downloadFile()" :loading="downloading" class="w-full sm:w-auto" />
      </div>
    </div>
  </Dialog>

  <BlockUI :blocked="downloading" full-screen />
  <div v-if="downloading" class="fixed inset-0 z-[9999] flex flex-col items-center justify-center bg-black/60 backdrop-blur-sm text-white">
    <ProgressSpinner style="width: 50px; height: 50px" stroke-width="6" />
    <div class="mt-4 font-medium text-lg">Preparing download...</div>
  </div>
</template>

<style scoped>
  :deep(.p-inputnumber-input) {
    padding: 0.25rem 0.5rem;
    font-size: 0.875rem;
  }
</style>
