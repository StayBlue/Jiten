<script setup lang="ts">
  import { useApiFetch } from '~/composables/useApiFetch';
  import type { DeckWord, DictionaryEntry, DictionarySearchResult, ParseNormalisedResult } from '~/types';
  import OmniSearch from '~/components/OmniSearch.vue';

  const route = useRoute();
  const { $api } = useNuxtApp();

  const searchContent = ref(route.query.text || '');

  useHead(() => {
    return {
      title: 'Search ' + searchContent.value,
    };
  });

  const {
    data: response,
    status,
  } = await useApiFetch<ParseNormalisedResult>('vocabulary/parse-normalised', {
    query: { text: searchContent },
    watch: [searchContent],
  });

  const words = computed<DeckWord[]>(() => response.value?.words || []);
  const hasMeaningfulParseResults = computed(() => words.value.some(w => w.wordId !== 0));

  const isLikelyEnglish = computed(() => {
    const text = String(searchContent.value).trim();
    if (text.includes('*')) return true;
    const hasJapanese = /[\u3040-\u309F\u30A0-\u30FF\u4E00-\u9FFF\u3400-\u4DBF]/.test(text);
    if (hasJapanese) return false;
    return text.includes(' ');
  });

  const selectedWord = ref<DeckWord | undefined>();

  const activeSearchQuery = computed(() => {
    if (!isLikelyEnglish.value && hasMeaningfulParseResults.value && selectedWord.value) {
      return selectedWord.value.originalText;
    }
    return String(searchContent.value);
  });

  const {
    data: searchResponse,
    status: searchStatus,
  } = await useApiFetch<DictionarySearchResult>('vocabulary/search', {
    query: { query: activeSearchQuery },
    watch: [activeSearchQuery],
  });

  watch(
    () => route.query.text,
    (newText) => {
      if (newText) {
        selectedWord.value = undefined;
        response.value = null;
        searchResponse.value = null;
        searchContent.value = newText;
      }
    }
  );

  const showParseResults = computed(() => {
    if (!hasMeaningfulParseResults.value) return false;
    if (isLikelyEnglish.value) return false;
    return true;
  });

  // Pagination state
  const extraResults = ref<DictionaryEntry[]>([]);
  const extraDictResults = ref<DictionaryEntry[]>([]);
  const canLoadMoreResults = ref(false);
  const canLoadMoreDictResults = ref(false);
  const isLoadingMoreResults = ref(false);
  const isLoadingMoreDictResults = ref(false);

  watch(searchResponse, (resp) => {
    extraResults.value = [];
    extraDictResults.value = [];
    canLoadMoreResults.value = resp?.hasMore ?? false;
    canLoadMoreDictResults.value = (resp?.dictionaryResults?.length ?? 0) >= 50;
  });

  const allResults = computed(() => [
    ...(searchResponse.value?.results || []),
    ...extraResults.value,
  ]);

  const allDictResults = computed(() => [
    ...(searchResponse.value?.dictionaryResults || []),
    ...extraDictResults.value,
  ]);

  const directMatches = computed(() => {
    if (!selectedWord.value) return allResults.value;
    return allResults.value.filter(
      r => !(r.wordId === selectedWord.value!.wordId && r.readingIndex === selectedWord.value!.readingIndex)
    );
  });

  const dictionaryMatches = computed(() => allDictResults.value);

  const searchQueryType = computed(() => searchResponse.value?.queryType || '');
  const isSearchLoading = computed(() => searchStatus.value === 'pending');

  const directMatchesLabel = computed(() => {
    const type = searchQueryType.value;
    if (type === 'english') return 'Dictionary results';
    if (type === 'wildcard') return 'Wildcard results';
    return 'Direct matches';
  });

  const resultsTotalLabel = computed(() => {
    const count = directMatches.value.length;
    const more = canLoadMoreResults.value ? '+' : '';
    return `${count}${more}`;
  });

  const dictResultsTotalLabel = computed(() => {
    const count = dictionaryMatches.value.length;
    const more = canLoadMoreDictResults.value ? '+' : '';
    return `${count}${more}`;
  });

  async function loadMoreResults() {
    if (isLoadingMoreResults.value || !canLoadMoreResults.value) return;
    isLoadingMoreResults.value = true;
    try {
      const offset = allResults.value.length;
      const data = await $api<DictionarySearchResult>('vocabulary/search', {
        query: { query: activeSearchQuery.value, offset, limit: 50 },
      });
      if (data.results.length > 0) {
        extraResults.value.push(...data.results);
      }
      canLoadMoreResults.value = data.hasMore;
    } catch { canLoadMoreResults.value = false; }
    finally { isLoadingMoreResults.value = false; }
  }

  async function loadMoreDictResults() {
    if (isLoadingMoreDictResults.value || !canLoadMoreDictResults.value) return;
    isLoadingMoreDictResults.value = true;
    try {
      const offset = allDictResults.value.length;
      const data = await $api<DictionarySearchResult>('vocabulary/search', {
        query: { query: activeSearchQuery.value, offset, limit: 50 },
      });
      if (data.dictionaryResults.length > 0) {
        extraDictResults.value.push(...data.dictionaryResults);
      }
      canLoadMoreDictResults.value = data.dictionaryResults.length >= 50;
    } catch { canLoadMoreDictResults.value = false; }
    finally { isLoadingMoreDictResults.value = false; }
  }

  // IntersectionObserver for infinite scroll
  const resultsSentinel = ref<HTMLElement | null>(null);
  const dictSentinel = ref<HTMLElement | null>(null);
  let observer: IntersectionObserver | undefined;

  onMounted(() => {
    observer = new IntersectionObserver((entries) => {
      for (const entry of entries) {
        if (!entry.isIntersecting) continue;
        if (entry.target === resultsSentinel.value) loadMoreResults();
        if (entry.target === dictSentinel.value) loadMoreDictResults();
      }
    }, { rootMargin: '300px' });

    if (resultsSentinel.value) observer.observe(resultsSentinel.value);
    if (dictSentinel.value) observer.observe(dictSentinel.value);
  });

  watch(resultsSentinel, (newEl, oldEl) => {
    if (oldEl && observer) observer.unobserve(oldEl);
    if (newEl && observer) observer.observe(newEl);
  });

  watch(dictSentinel, (newEl, oldEl) => {
    if (oldEl && observer) observer.unobserve(oldEl);
    if (newEl && observer) observer.observe(newEl);
  });

  onUnmounted(() => observer?.disconnect());

  watch(
    () => status.value,
    (newStatus) => {
      if (!selectedWord.value) {
        selectedWord.value = words.value.find((word) => word.wordId != 0);
      } else if (newStatus === 'error' || newStatus === 'idle') {
        selectedWord.value = undefined;
      }
    },
    { immediate: true }
  );

  const handleWordClick = (word: DeckWord) => {
    if (word.wordId !== 0) {
      if (selectedWord.value?.wordId === word.wordId && selectedWord.value?.readingIndex === word.readingIndex) {
        selectedWord.value = undefined;
      } else {
        selectedWord.value = word;
      }
    }
  };

  const directMatchesExpanded = ref(true);
  const dictionaryMatchesExpanded = ref(true);
</script>

<template>
  <div>
    <OmniSearch />

    <template v-if="showParseResults">
      <span v-for="(word, index) in words" :key="index" class="pr-1.5 font-noto-sans">
        <span
          v-if="word.wordId != 0"
          class="text-purple-600 dark:text-purple-400 text-lg underline underline-offset-4 cursor-pointer hover:font-bold"
          @click="handleWordClick(word)"
        >
          {{ word.originalText }}
        </span>
        <span v-else>{{ word.originalText }}</span>
      </span>

      <div v-if="selectedWord">
        <Transition name="fade" mode="out-in">
          <VocabularyDetail
            :key="`${selectedWord.wordId}-${selectedWord.readingIndex}`"
            :word-id="selectedWord.wordId"
            :reading-index="selectedWord.readingIndex"
            :show-redirect="true"
            :conjugations="selectedWord.conjugations"
          />
        </Transition>
      </div>
    </template>

    <div v-if="isSearchLoading && !showParseResults" class="flex justify-center py-8">
      <ProgressSpinner style="width: 40px; height: 40px" stroke-width="4" />
    </div>

    <div v-if="directMatches.length > 0" class="mt-4">
      <div
        class="flex items-center gap-2 cursor-pointer select-none mb-2"
        @click="directMatchesExpanded = !directMatchesExpanded"
      >
        <Icon
          :name="directMatchesExpanded ? 'material-symbols:expand-more' : 'material-symbols:chevron-right'"
          class="text-gray-500"
        />
        <h2 class="text-sm font-medium text-gray-500 dark:text-gray-400">
          {{ directMatchesLabel }}
          <span class="text-xs">({{ resultsTotalLabel }})</span>
        </h2>
      </div>

      <div v-if="directMatchesExpanded" class="flex flex-col gap-2">
        <DictionaryResultEntry
          v-for="entry in directMatches"
          :key="`${entry.wordId}-${entry.readingIndex}`"
          :entry="entry"
        />
        <div v-if="canLoadMoreResults" ref="resultsSentinel" class="flex justify-center py-4">
          <ProgressSpinner v-if="isLoadingMoreResults" style="width: 30px; height: 30px" stroke-width="4" />
        </div>
      </div>
    </div>

    <div v-if="dictionaryMatches.length > 0" class="mt-4">
      <div
        class="flex items-center gap-2 cursor-pointer select-none mb-2"
        @click="dictionaryMatchesExpanded = !dictionaryMatchesExpanded"
      >
        <Icon
          :name="dictionaryMatchesExpanded ? 'material-symbols:expand-more' : 'material-symbols:chevron-right'"
          class="text-gray-500"
        />
        <h2 class="text-sm font-medium text-gray-500 dark:text-gray-400">
          Dictionary results
          <span class="text-xs">({{ dictResultsTotalLabel }})</span>
        </h2>
      </div>

      <div v-if="dictionaryMatchesExpanded" class="flex flex-col gap-2">
        <DictionaryResultEntry
          v-for="entry in dictionaryMatches"
          :key="`dict-${entry.wordId}-${entry.readingIndex}`"
          :entry="entry"
        />
        <div v-if="canLoadMoreDictResults" ref="dictSentinel" class="flex justify-center py-4">
          <ProgressSpinner v-if="isLoadingMoreDictResults" style="width: 30px; height: 30px" stroke-width="4" />
        </div>
      </div>
    </div>

    <div
      v-if="searchStatus === 'success' && status !== 'pending' && !showParseResults && directMatches.length === 0 && dictionaryMatches.length === 0 && String(searchContent).trim().length > 0"
      class="text-center py-8 text-gray-500 dark:text-gray-400"
    >
      No results found for "{{ activeSearchQuery }}"
    </div>
  </div>
</template>

<style scoped>
.fade-enter-active,
.fade-leave-active {
  transition: opacity 0.15s ease;
}

.fade-enter-from,
.fade-leave-to {
  opacity: 0;
}
</style>
