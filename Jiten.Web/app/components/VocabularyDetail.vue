<script setup lang="ts">
  import type { ExampleSentence, MediaType, Word } from '~/types';
  import { formatPercentageApprox } from '~/utils/formatPercentageApprox';
  import { getMediaTypeText } from '~/utils/mediaTypeMapper';
  import ExampleSentenceEntry from '~/components/ExampleSentenceEntry.vue';
  import Button from 'primevue/button';
  import { useJitenStore } from '~/stores/jitenStore';

  const props = defineProps({
    wordId: {
      type: Number,
      required: true,
    },
    readingIndex: {
      type: Number,
      required: true,
    },
    showRedirect: {
      type: Boolean,
      required: false,
    },
    conjugations: {
      type: Array as PropType<string[]>,
      default: () => [],
      required: false,
    },
  });

  const emit = defineEmits(['mainReadingTextChanged', 'readingSelected']);
  const { $api } = useNuxtApp();

  const store = useJitenStore();
  const convertToRuby = useConvertToRuby();

  const currentReadingIndex = ref(props.readingIndex);
  const url = computed(() => `vocabulary/${props.wordId}/${currentReadingIndex.value}`);

  // Just use useApiFetch directly - no wrapping needed!
  const { data: response } = useApiFetch<Word>(url);

  const { resolvedGroups } = useDictionaryDefinitions(
    computed(() => response.value?.mainReading?.text),
    computed(() => response.value?.definitions),
  );

  const getSortedReadings = () => {
    return response.value?.alternativeReadings.sort((a, b) => b.frequencyPercentage - a.frequencyPercentage) || [];
  };

  const sortedReadings = computed(() => getSortedReadings());

  const mediaAmountUrl = 'media-deck/decks-count';
  const { data: mediaAmountResponse } = useApiFetch<Record<MediaType, number>>(mediaAmountUrl);

  const totalMediaCount = computed(() => {
    if (!mediaAmountResponse.value) return 0;
    return Object.values(mediaAmountResponse.value).reduce((sum, count) => sum + count, 0);
  });

  const selectedMediaType = ref<MediaType | null>(null);
  const mediaAccordionValue = ref('0');
  const selectMediaType = (type: number | string | null) => {
    if (type != null) {
      selectedMediaType.value = Number(type) as MediaType;
    } else {
      selectedMediaType.value = null;
    }
    mediaAccordionValue.value = '1';
    exampleSentences.value = [];
    canLoadExampleSentences.value = true;
    getRandomExampleSentences();
  };

  const switchReadingOrWord = async () => {
    exampleSentences.value = [];
    canLoadExampleSentences.value = true;
    await getRandomExampleSentences();
    const result = await $api<Word>(url.value);
    response.value = result;
  };

  const selectReading = async (index: number) => {
    emit('readingSelected', index);
    currentReadingIndex.value = index;
    await switchReadingOrWord();
  };

  watch(
    [() => props.wordId, () => props.readingIndex],
    async ([newWordId, newReadingIndex]) => {
      currentReadingIndex.value = newReadingIndex;
      await switchReadingOrWord();
    },
    { immediate: false }
  );

  watch(
    () => response.value?.mainReading.text,
    (newText) => {
      emit('mainReadingTextChanged', newText);
    },
    { immediate: true }
  );

  const conjugationString = computed(() => {
    let conjugations = [...props.conjugations];
    conjugations = conjugations.filter((conj) => !conj.startsWith('(')).filter((conj) => conj != '');
    conjugations.reverse();

    if (conjugations.length == 0) return null;

    return conjugations.join(' ; ');
  });

  const exampleSentences = ref<ExampleSentence[]>([]);
  const canLoadExampleSentences = ref(true);
  onMounted(() => {
    getRandomExampleSentences();
  });

  async function getRandomExampleSentences() {
    let url = `vocabulary/${props.wordId}/${currentReadingIndex.value}/random-example-sentences`;

    if (selectedMediaType.value != null) {
      url += '/' + selectedMediaType.value;
    }

    const alreadyLoaded = exampleSentences.value.map((sentence) => sentence.sourceDeck.deckId);
    const results = await $api<ExampleSentence[]>(url, {
      method: 'POST',
      body: alreadyLoaded,
    });

    if (results.length == 0) {
      canLoadExampleSentences.value = false;
      return;
    }

    exampleSentences.value.push(...results);
  }
</script>

<template>
  <Card class="p-4">
    <template v-if="response" #content>
      <div class="flex flex-col justify-between md:flex-row">
        <div class="flex flex-col gap-4 max-w-2xl">
          <div class="flex justify-between">
            <div>
              <div v-if="conjugationString != null" class="text-gray-500 text-xs font-noto-sans">(Conjugation: {{ conjugationString }})</div>
              <NuxtLink v-if="showRedirect" :to="`/vocabulary/${wordId}/${currentReadingIndex}`">
                <div class="text-3xl font-noto-sans" v-html="convertToRuby(response.mainReading.text)" />
              </NuxtLink>
              <div v-if="!showRedirect" class="text-3xl font-noto-sans" v-html="convertToRuby(response.mainReading.text)" />
            </div>
            <div class="flex flex-col md:flex-row items-end md:hidden">
              <div class="text-gray-500 dark:text-gray-300 text-right">Rank #{{ response.mainReading.frequencyRank.toLocaleString() }}</div>
              <VocabularyStatus :word="response" />
            </div>
          </div>

          <div>
            <h1 class="text-gray-500 dark:text-gray-300 text-sm">Meanings</h1>
            <div class="pl-2">
              <ClientOnly>
                <VocabularyDictionaryDefinitions :resolved-groups="resolvedGroups" :is-compact="false" :current-reading-index="currentReadingIndex" :readings="response.alternativeReadings" />
                <template #fallback>
                  <VocabularyDefinitions :definitions="response.definitions" :is-compact="false" :current-reading-index="currentReadingIndex" :readings="response.alternativeReadings" />
                </template>
              </ClientOnly>
            </div>
          </div>

          <div>
            <h1 class="text-gray-500 dark:text-gray-300 font-noto-sans text-sm">Forms</h1>
            <div class="pl-2 flex flex-row flex-wrap gap-8">
              <span v-for="reading in sortedReadings" :key="reading.readingIndex">
                <div :class="reading.readingIndex === currentReadingIndex ? 'font-bold !text-purple-500' : ' text-blue-500'">
                  <div class="text-center font-noto-sans cursor-pointer hover:underline" @click="selectReading(reading.readingIndex)">
                    {{ reading.text }}
                    <div class="text-xs">({{ formatPercentageApprox(reading.frequencyPercentage) }})</div>
                  </div>
                </div>
              </span>
            </div>
          </div>

          <ClientOnly>
            <div v-if="response.pitchAccents && response.pitchAccents.length > 0">
              <h1 class="text-gray-500 dark:text-gray-300 font-noto-sans text-sm">Pitch accents</h1>
              <div class="pl-2 flex flex-row flex-wrap gap-8">
                <span v-for="pitchAccent in response.pitchAccents" :key="pitchAccent">
                  <div>
                    <PitchDiagram :reading="response.mainReading.text" :pitch-accent="pitchAccent" />
                  </div>
                </span>
              </div>
            </div>
          </ClientOnly>

          <KanjiBreakdown :word-id="wordId" :reading-index="currentReadingIndex" />
        </div>

        <div class="md:min-w-64">
          <div class="text-gray-500 dark:text-gray-300 text-right hidden md:block">
            <VocabularyStatus :word="response" />
            Rank #{{ response.mainReading.frequencyRank }}
          </div>
          <div class="md:text-right pt-4 cursor-pointer" @click="selectMediaType(null)">
            Appears in <b>{{ response.mainReading.usedInMediaAmount }} media</b>
            ({{ totalMediaCount > 0 ? ((response.mainReading.usedInMediaAmount / totalMediaCount) * 100).toFixed(0) : '0' }}%)
          </div>
          <ClientOnly>
            <table v-if="response.mainReading.usedInMediaAmount > 0">
              <thead>
                <tr>
                  <th />
                  <th class="text-gray-500 dark:text-gray-300 text-sm pl-4">Amount</th>
                  <th class="text-gray-500 dark:text-gray-300 text-sm pl-4">% of total</th>
                </tr>
              </thead>
              <tr
                v-for="(amount, mediaType) in response.mainReading.usedInMediaAmountByType"
                :key="mediaType"
                class="cursor-pointer hover:bg-primary-100 dark:hover:bg-primary-900/30 transition-colors"
                @click="selectMediaType(mediaType)"
              >
                <th class="text-right p-0.5 !font-bold">{{ getMediaTypeText(Number(mediaType)) }}</th>
                <th class="text-right p-0.5">{{ amount }}</th>
                <th class="text-right p-0.5">{{ mediaAmountResponse ? ((amount / mediaAmountResponse[mediaType as MediaType]) * 100).toFixed(0) : '0' }}%</th>
              </tr>
            </table>
          </ClientOnly>
        </div>
      </div>

      <ClientOnly>
        <div v-if="exampleSentences != null && exampleSentences.length > 0">
          <Accordion value="1" lazy>
            <AccordionPanel value="1">
              <AccordionHeader>
                <div class="cursor-pointer">Example sentences</div>
              </AccordionHeader>
              <AccordionContent>
                <div class="text-xs pb-2">
                  Quotations belong to their original creators and are presented here for educational purposes only, as per the
                  <NuxtLink :to="`/terms`" target="_blank" class="hover:underline text-primary-600"> terms of service.</NuxtLink>
                </div>
                <ExampleSentenceEntry v-for="(exampleSentence, index) in exampleSentences" :key="index" :example-sentence="exampleSentence" :show-source="true" />
                <Button @click="getRandomExampleSentences()" :disabled="!canLoadExampleSentences">Load more</Button>
              </AccordionContent>
            </AccordionPanel>
          </Accordion>
        </div>
      </ClientOnly>

      <Accordion v-if="response.mainReading.usedInMediaAmount > 0" :value="mediaAccordionValue" lazy>
        <AccordionPanel value="1">
          <AccordionHeader>
            <div class="cursor-pointer">
              View the <b>{{ response.mainReading.usedInMediaAmount }}</b> media it appears in
            </div>
          </AccordionHeader>
          <AccordionContent>
            <MediaList :word="response" :default-media-type="selectedMediaType" />
          </AccordionContent>
        </AccordionPanel>
      </Accordion>
    </template>
  </Card>
</template>

<style scoped>
  th {
    font-weight: normal;
  }
</style>
