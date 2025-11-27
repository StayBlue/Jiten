<script setup lang="ts">
  import { type Deck, DeckDownloadType, DeckFormat, DeckOrder } from '~/types';
  import { Accordion, AccordionPanel, AccordionHeader, AccordionContent, SelectButton } from 'primevue';
  import { debounce } from 'perfect-debounce';
  import { useApiFetch, useApiFetchPaginated } from '~/composables/useApiFetch';
  import { useJitenStore } from '~/stores/jitenStore';

  const props = defineProps<{
    deck: Deck;
    visible: boolean;
  }>();

  const store = useJitenStore();

  const emit = defineEmits(['update:visible']);

  const localVisible = ref(props.visible);
  const downloading = ref(false);

  const deckOrders = getEnumOptions(DeckOrder, getDeckOrderText);
  const downloadTypes = getEnumOptions(DeckDownloadType, getDownloadTypeText);
  const deckFormats = getEnumOptions(DeckFormat, getDeckFormatText);

  const format = defineModel<DeckFormat>('deckFormat', { default: DeckFormat.Anki });
  const downloadType = defineModel<DeckDownloadType>('downloadType', { default: DeckDownloadType.TopDeckFrequency });
  const deckOrder = defineModel<DeckOrder>('deckOrder', { default: DeckOrder.DeckFrequency });
  const frequencyRange = defineModel<number[]>('frequencyRange');

  onMounted(() => {
    if (!frequencyRange.value) {
      frequencyRange.value = [0, Math.min(props.deck.uniqueWordCount, 5000)];
    }
  });

  watch(
    () => props.visible,
    (newVal) => {
      localVisible.value = newVal;
    }
  );

  watch(
    () => downloadType.value,
    (newVal) => {
      if (newVal == DeckDownloadType.Full) {
        frequencyRange.value = [0, Math.min(props.deck.uniqueWordCount, 5000)];
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
      if (downloadType.value == DeckDownloadType.TopGlobalFrequency) {
        updateDebounced();
      }
    }
  );

  watch(localVisible, (newVal) => {
    emit('update:visible', newVal);
  });

  const updateDebounced = debounce(async () => {
    const { data: response } = useApiFetch<number>(`media-deck/${props.deck.deckId}/vocabulary-count-frequency`, {
      query: {
        minFrequency: frequencyRange.value![0],
        maxFrequency: frequencyRange.value![1],
      },
    });
    debouncedCurrentCardAmount.value = response;
  }, 500);

  const debouncedCurrentCardAmount = ref(0);

  const url = `media-deck/${props.deck.deckId}/download`;
  const { $api } = useNuxtApp();

  const currentSliderMax = ref(props.deck.uniqueWordCount);

  const currentCardAmount = computed(() => {
    if (downloadType.value == DeckDownloadType.Full) {
      return props.deck.uniqueWordCount;
    } else if (downloadType.value == DeckDownloadType.TopDeckFrequency || downloadType.value == DeckDownloadType.TopChronological) {
      return frequencyRange.value![1] - frequencyRange.value![0];
    } else if (downloadType.value == DeckDownloadType.TopGlobalFrequency) {
      return debouncedCurrentCardAmount;
    }

    return 0;
  });

  const excludeFullWidthDigits = ref(true);
  const excludeKana = ref(false);
  const excludeKnownWords = ref(false);
  const excludeExampleSentences = ref(false);

  const downloadFile = async () => {
    try {
      downloading.value = true;
      localVisible.value = false;

      const response = await $api<File>(url, {
        method: 'POST',
        body: {
          format: format.value,
          downloadType: downloadType.value,
          order: deckOrder.value,
          minFrequency: frequencyRange.value![0],
          maxFrequency: frequencyRange.value![1],
          excludeKana: excludeKana.value,
          excludeKnownWords: excludeKnownWords.value,
          excludeExampleSentences: excludeExampleSentences.value,
        },
        headers: {
          'Content-Type': 'application/json',
        },
        responseType: 'blob',
      });

      if (response) {
        const blobUrl = window.URL.createObjectURL(response);
        const link = document.createElement('a');
        link.href = blobUrl;
        if (format.value === DeckFormat.Anki) {
          link.setAttribute('download', `${localiseTitle(props.deck).substring(0, 30)}.apkg`);
        } else if (format.value === DeckFormat.Csv) {
          link.setAttribute('download', `${localiseTitle(props.deck).substring(0, 30)}.csv`);
        } else if (format.value === DeckFormat.Txt || format.value === DeckFormat.TxtRepeated) {
          link.setAttribute('download', `${localiseTitle(props.deck).substring(0, 30)}.txt`);
        } else if (format.value === DeckFormat.Yomitan) {
          link.setAttribute('download', `${localiseTitle(props.deck).substring(0, 30)}.zip`);
        }

        document.body.appendChild(link);
        link.click();
        link.remove();
        downloading.value = false;
      } else {
        downloading.value = false;
        console.error('Error downloading file.');
      }
    } catch (err) {
      downloading.value = false;
      console.error('Error:', err);
    }
  };

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
</script>

<template>
  <Dialog v-model:visible="localVisible" modal :header="`Download deck ${localiseTitle(deck)}`" class="w-[95vw] sm:w-[90vw] md:w-[35rem]" :pt="{ root: { class: 'max-w-full' }, content: { class: 'p-3 sm:p-6' } }">
    <div class="flex flex-col gap-2">
      <div>
        <div class="text-gray-500 text-sm">Format</div>
        <SelectButton v-model="format" :options="deckFormats" option-value="value" option-label="label" size="small" />
      </div>
      <span v-if="format == DeckFormat.Anki" class="text-xs sm:text-sm">
        Uses the Lapis template from <a href="https://github.com/donkuri/lapis/tree/main">Lapis</a>
      </span>
      <span v-if="format == DeckFormat.Txt" class="text-xs sm:text-sm">
        Plain text format, one word per line, vocabulary only. <br />
        Perfect for importing in other websites.
      </span>
      <span v-if="format == DeckFormat.TxtRepeated" class="text-xs sm:text-sm">
        Plain text format, one word per line, vocabulary only. <br />
        The vocabulary is repeated for each occurrence to handle frequency on some websites.
      </span>
      <span v-if="format == DeckFormat.Yomitan" class="text-xs sm:text-sm">
        A zip file that can be imported as a yomitan dictionary. <br />
        It will show the number of occurrences of a word in the selected media.
      </span>
      <template v-if="format != DeckFormat.Yomitan">
      <div>
        <div class="text-gray-500 text-sm">Filter type</div>
        <Select v-model="downloadType" :options="downloadTypes" option-value="value" option-label="label" size="small" />
      </div>
      <div>
        <div class="text-gray-500 text-sm">Sort order</div>
        <Select v-model="deckOrder" :options="deckOrders" option-value="value" option-label="label" size="small" />
      </div>
      <div v-if="downloadType != DeckDownloadType.Full">
        <div class="text-gray-500 text-sm">Range</div>
        <div class="flex flex-col sm:flex-row gap-2 sm:items-center">
          <InputNumber
            :model-value="frequencyRange?.[0] ?? 0"
            show-buttons
            fluid
            size="small"
            class="w-full sm:max-w-28 sm:flex-shrink-0"
            @update:model-value="updateMinFrequency"
          />
          <Slider v-model="frequencyRange" range :min="0" :max="currentSliderMax" class="flex-grow sm:mx-2 my-2 sm:my-0 min-w-0" />
          <InputNumber
            :model-value="frequencyRange?.[1] ?? 0"
            show-buttons
            fluid
            size="small"
            class="w-full sm:max-w-28 sm:flex-shrink-0"
            @update:model-value="updateMaxFrequency"
          />
        </div>
      </div>
      <Accordion>
        <AccordionPanel value="0">
          <AccordionHeader> Advanced</AccordionHeader>
          <AccordionContent>
            <div class="flex flex-col gap-2">
              <div class="text-xs sm:text-sm text-gray-500">These options might not be reflected in the card count below.</div>
              <div class="flex items-center gap-2">
                <Checkbox v-model="excludeKana" input-id="excludeKana" name="kanaOnly" :binary="true" />
                <label for="excludeKana" class="text-sm">Exclude kana-only vocabulary</label>
              </div>
              <div class="flex items-center gap-2">
                <Checkbox v-model="excludeExampleSentences" input-id="excludeExampleSentences" name="noExampleSentences" :binary="true" />
                <label for="excludeExampleSentences" class="text-sm">Don't include example sentences</label>
              </div>
              <div class="flex items-center gap-2">
                <Checkbox v-model="excludeKnownWords" input-id="excludeKnownWords" name="excludeKnownWords" :binary="true" />
                <label for="excludeKnownWords" class="text-sm">Don't download known words</label>
              </div>
            </div>
          </AccordionContent>
        </AccordionPanel>
      </Accordion>

      <div>
        This will download <span class="font-bold">{{ currentCardAmount }} cards</span>.
      </div>
      </template>
      <div class="flex justify-center">
        <Button type="button" label="Download" @click="downloadFile()" />
      </div>
    </div>
  </Dialog>

  <div v-if="downloading" class="!fixed top-1/2 left-1/2 -translate-x-1/2 -translate-y-1/2 text-center px-4" style="z-index: 9999">
    <div class="text-white font-bold text-base sm:text-lg">Preparing your deck, please wait a few seconds…</div>
    <ProgressSpinner style="width: 50px; height: 50px" stroke-width="8" fill="transparent" animation-duration=".5s" aria-label="Creating your deck" />
  </div>
  <BlockUI :blocked="downloading" full-screen />
</template>

<style scoped></style>
