<script setup lang="ts">
  import { useApiFetchPaginated } from '~/composables/useApiFetch';
  import { type Deck, MediaType, SortOrder, type Word, DisplayStyle } from '~/types';
  import Skeleton from 'primevue/skeleton';
  import Card from 'primevue/card';
  import InputText from 'primevue/inputtext';
  import { debounce } from 'perfect-debounce';
  import { useDisplayStyleStore } from '~/stores/displayStyleStore';
  import MediaDeckCompactView from '~/components/MediaDeckCompactView.vue';
  import MediaDeckTableView from '~/components/MediaDeckTableView.vue';
  import { useAuthStore } from '~/stores/authStore';

  // Helpers for numeric parsing
  const toNumOrNull = (v: unknown) => {
    if (v === undefined || v === null || v === '' || (Array.isArray(v) && v.length === 0)) return null;
    const s = Array.isArray(v) ? v[0] : v;
    const n = Number(s);
    return Number.isFinite(n) ? n : null;
  };

  const props = defineProps<{
    word?: Word;
    defaultMediaType?: MediaType | null;
  }>();

  const route = useRoute();
  const router = useRouter();

  watch(
    () => props.defaultMediaType,
    (newVal) => {
      if (newVal !== null && newVal !== undefined) {
        const curr = route.query.mediaType ? Number(Array.isArray(route.query.mediaType) ? route.query.mediaType[0] : route.query.mediaType) : null;
        if (curr !== Number(newVal)) {
          router.replace({
            query: { ...route.query, mediaType: Number(newVal) as any, offset: 0 as any },
          });
        }
      } else if (newVal === null) {
        if (route.query.mediaType) {
          router.replace({
            query: { ...route.query, mediaType: undefined, offset: 0 as any },
          });
        }
      }
    },
    { immediate: true }
  );

  const filters = ref();

  const offset = computed(() => (route.query.offset ? Number(route.query.offset) : 0));
  const mediaType = computed(() => (route.query.mediaType ? route.query.mediaType : null));

  const titleFilter = ref(route.query.title ? (Array.isArray(route.query.title) ? route.query.title[0] : route.query.title) : null);
  const debouncedTitleFilter = ref(titleFilter.value);

  const sortByOptions = ref([
    { label: 'Title', value: 'title' },
    { label: 'Difficulty', value: 'difficulty' },
    { label: 'Character Count', value: 'charCount' },
    { label: 'Subdeck Count', value: 'subdeckCount' },
    { label: 'External Rating', value: 'extRating' },
    { label: 'Unique Kanji', value: 'uKanji' },
    { label: 'Unique Word Count', value: 'uWordCount' },
    { label: 'Word Count', value: 'wordCount' },
    { label: 'Unique Kanji Used Once', value: 'uKanjiOnce' },
    { label: 'Release Date', value: 'releaseDate' },
    { label: 'Added Date', value: 'addedDate' },
  ]);

  // Array used to reorder the sortByOptions when some options are added or removed to keep the order consistent'
  const sortByOrdering = [
    'title',
    'difficulty',
    'charCount',
    'coverage',
    'uCoverage',
    'extRating',
    'dialoguePercentage',
    'sentenceLength',
    'uKanji',
    'uWordCount',
    'wordCount',
    'subdeckCount',
    'uKanjiOnce',
    'releaseDate',
    'addedDate',
  ];

  const statusFilterOptions = ref([
    { label: 'Show All', value: 'none' },
    { label: 'Only Favourited', value: 'fav' },
    { label: 'Only Ignored', value: 'ignore' },
    { label: 'Only Planning', value: 'planning' },
    { label: 'Only Ongoing', value: 'ongoing' },
    { label: 'Only Completed', value: 'completed' },
    { label: 'Only Dropped', value: 'dropped' },
  ]);

  const statusFilter = ref('none');

  const authStore = useAuthStore();
  const isConnected = computed(() => authStore.isAuthenticated);

  const sortOrder = ref(route.query.sortOrder ? route.query.sortOrder : SortOrder.Ascending);
  const sortBy = ref(route.query.sortBy ? route.query.sortBy : sortByOptions.value[0].value);
  const wordIdRef = ref(props.word?.wordId);
  const readingIndexRef = ref(props.word?.mainReading?.readingIndex);

  if (isConnected.value) {
    if (!sortByOptions.value.some((o) => o.value === 'uCoverage')) {
      sortByOptions.value.push({ label: 'Unique Coverage', value: 'uCoverage' });
    }
    if (!sortByOptions.value.some((o) => o.value === 'coverage')) {
      sortByOptions.value.push({ label: 'Coverage', value: 'coverage' });
    }
  }

  // Advanced filter state
  const currentYear = new Date().getFullYear();

  const charCountMin = ref<number | null>(toNumOrNull(route.query.charCountMin));
  const charCountMax = ref<number | null>(toNumOrNull(route.query.charCountMax));
  const releaseYearMin = ref<number | null>(toNumOrNull(route.query.releaseYearMin));
  const releaseYearMax = ref<number | null>(toNumOrNull(route.query.releaseYearMax));
  const uniqueKanjiMin = ref<number | null>(toNumOrNull(route.query.uniqueKanjiMin));
  const uniqueKanjiMax = ref<number | null>(toNumOrNull(route.query.uniqueKanjiMax));
  const subdeckCountMin = ref<number | null>(toNumOrNull(route.query.subdeckCountMin));
  const subdeckCountMax = ref<number | null>(toNumOrNull(route.query.subdeckCountMax));
  const extRatingMin = ref<number | null>(toNumOrNull(route.query.extRatingMin));
  const extRatingMax = ref<number | null>(toNumOrNull(route.query.extRatingMax));

  const charCountRange = computed<[number, number]>({
    get: () => [charCountMin.value ?? 0, charCountMax.value ?? 20000000],
    set: (val) => {
      charCountMin.value = val[0];
      charCountMax.value = val[1];
    },
  });

  const releaseYearRange = computed<[number, number]>({
    get: () => [releaseYearMin.value ?? 1900, releaseYearMax.value ?? currentYear],
    set: (val) => {
      releaseYearMin.value = val[0];
      releaseYearMax.value = val[1];
    },
  });

  const uniqueKanjiRange = computed<[number, number]>({
    get: () => [uniqueKanjiMin.value ?? 0, uniqueKanjiMax.value ?? 5000],
    set: (val) => {
      uniqueKanjiMin.value = val[0];
      uniqueKanjiMax.value = val[1];
    },
  });

  const subdeckCountRange = computed<[number, number]>({
    get: () => [subdeckCountMin.value ?? 0, subdeckCountMax.value ?? 2000],
    set: (val) => {
      subdeckCountMin.value = val[0];
      subdeckCountMax.value = val[1];
    },
  });

  const extRatingRange = computed<[number, number]>({
    get: () => [extRatingMin.value ?? 0, extRatingMax.value ?? 100],
    set: (val) => {
      extRatingMin.value = val[0];
      extRatingMax.value = val[1];
    },
  });

  // Ensure min is not higher than max and values stay within bounds
  const clamp = (val: number, min: number, max: number) => Math.min(max, Math.max(min, val));
  const normalizePair = (minRef: any, maxRef: any, floor: number, ceil: number) => {
    if (minRef.value != null) minRef.value = clamp(minRef.value, floor, ceil);
    if (maxRef.value != null) maxRef.value = clamp(maxRef.value, floor, ceil);
    if (minRef.value != null && maxRef.value != null && minRef.value > maxRef.value) {
      // keep ranges valid: when min surpasses max, move max up to min
      maxRef.value = minRef.value;
    }
  };

  // Normalize ranges when user edits inputs
  watch([charCountMin, charCountMax], () => normalizePair(charCountMin, charCountMax, 0, 20000000));
  watch([releaseYearMin, releaseYearMax], () => normalizePair(releaseYearMin, releaseYearMax, 1900, currentYear));
  watch([uniqueKanjiMin, uniqueKanjiMax], () => normalizePair(uniqueKanjiMin, uniqueKanjiMax, 0, 5000));
  watch([subdeckCountMin, subdeckCountMax], () => normalizePair(subdeckCountMin, subdeckCountMax, 0, 2000));
  watch([extRatingMin, extRatingMax], () => normalizePair(extRatingMin, extRatingMax, 0, 2000));

  const debouncedFilters = ref({
    charCountMin: charCountMin.value,
    charCountMax: charCountMax.value,
    releaseYearMin: releaseYearMin.value,
    releaseYearMax: releaseYearMax.value,
    uniqueKanjiMin: uniqueKanjiMin.value,
    uniqueKanjiMax: uniqueKanjiMax.value,
    subdeckCountMin: subdeckCountMin.value,
    subdeckCountMax: subdeckCountMax.value,
    extRatingMin: extRatingMin.value,
    extRatingMax: extRatingMax.value,
  });

  const updateFiltersDebounced = debounce(
    () => {
      debouncedFilters.value = {
        charCountMin: charCountMin.value,
        charCountMax: charCountMax.value,
        releaseYearMin: releaseYearMin.value,
        releaseYearMax: releaseYearMax.value,
        uniqueKanjiMin: uniqueKanjiMin.value,
        uniqueKanjiMax: uniqueKanjiMax.value,
        subdeckCountMin: subdeckCountMin.value,
        subdeckCountMax: subdeckCountMax.value,
        extRatingMin: extRatingMin.value,
        extRatingMax: extRatingMax.value,
      };

      const toUndef = (v: number | null) => (v === null ? undefined : v);
      router.replace({
        query: {
          ...route.query,
          charCountMin: toUndef(charCountMin.value) as any,
          charCountMax: toUndef(charCountMax.value) as any,
          releaseYearMin: toUndef(releaseYearMin.value) as any,
          releaseYearMax: toUndef(releaseYearMax.value) as any,
          uniqueKanjiMin: toUndef(uniqueKanjiMin.value) as any,
          uniqueKanjiMax: toUndef(uniqueKanjiMax.value) as any,
          subdeckCountMin: toUndef(subdeckCountMin.value) as any,
          subdeckCountMax: toUndef(subdeckCountMax.value) as any,
          extRatingMin: toUndef(extRatingMin.value) as any,
          extRatingMax: toUndef(extRatingMax.value) as any,
          offset: 0 as any,
        },
      });
    },
    500,
    { leading: false }
  );

  watch(
    [charCountMin, charCountMax, releaseYearMin, releaseYearMax, uniqueKanjiMin, uniqueKanjiMax, subdeckCountMin, subdeckCountMax, extRatingMin, extRatingMax],
    () => {
      updateFiltersDebounced();
    }
  );

  watch(
    () => mediaType.value,
    (newMediaType) => {
      updateOptions();
    }
  );

  const updateOptions = () => {
    const showDialogueOptionMediaTypes = [MediaType.Novel, MediaType.VisualNovel, MediaType.WebNovel, MediaType.NonFiction];

    if (mediaType.value == null || showDialogueOptionMediaTypes.includes(Number(mediaType.value))) {
      if (!sortByOptions.value.some((o) => o.value === 'dialoguePercentage')) {
        sortByOptions.value.push({ label: 'Dialogue Percentage', value: 'dialoguePercentage' });
      }
    } else {
      if (sortByOptions.value.some((o) => o.value === 'dialoguePercentage')) {
        sortByOptions.value = sortByOptions.value.filter((o) => o.value !== 'dialoguePercentage');
      }
      if (sortBy.value === 'dialoguePercentage') {
        sortBy.value = 'title';
      }
    }

    const showAvgSentenceLengthOptionMediaTypes = [MediaType.Novel, MediaType.VisualNovel, MediaType.WebNovel, MediaType.NonFiction, MediaType.VideoGame];

    if (mediaType.value == null || showAvgSentenceLengthOptionMediaTypes.includes(Number(mediaType.value))) {
      if (!sortByOptions.value.some((o) => o.value === 'sentenceLength')) {
        sortByOptions.value.push({ label: 'Average Sentence Length', value: 'sentenceLength' });
      }
    } else {
      if (sortByOptions.value.some((o) => o.value === 'sentenceLength')) {
        sortByOptions.value = sortByOptions.value.filter((o) => o.value !== 'sentenceLength');
      }
      if (sortBy.value === 'sentenceLength') {
        sortBy.value = 'title';
      }
    }

    // reorder the options by sortByOrdering
    sortByOptions.value.sort((a, b) => {
      const indexA = sortByOrdering.indexOf(a.value);
      const indexB = sortByOrdering.indexOf(b.value);
      return indexA - indexB;
    });
  };

  updateOptions();

  watch(
    () => props.word,
    (newWord) => {
      if (newWord) {
        wordIdRef.value = newWord.wordId;
        readingIndexRef.value = newWord.mainReading?.readingIndex;

        // Reset sorting when word changes
        if (!sortByOptions.value.some((opt) => opt.value === 'occurrences')) {
          sortByOptions.value.unshift({ label: 'Occurrences', value: 'occurrences' });
        }
        sortBy.value = 'occurrences';
        sortOrder.value = SortOrder.Descending;
      }
    },
    { immediate: true, deep: true }
  );

  if (props.word != null) {
    sortByOptions.value.unshift({ label: 'Occurrences', value: 'occurrences' });
    sortBy.value = 'occurrences';
    sortOrder.value = SortOrder.Descending;
  }

  const updateDebounced = debounce(async (newValue: string | null) => {
    debouncedTitleFilter.value = newValue;
    await router.replace({
      query: {
        ...route.query,
        title: newValue || undefined,
        sortBy: 'filter',
        offset: 0,
      },
    });
    sortBy.value = 'filter';
  }, 500);

  watch(titleFilter, (newValue) => {
    updateDebounced(newValue);
  });

  watch(sortOrder, (newValue) => {
    router.replace({
      query: {
        ...route.query,
        sortOrder: newValue,
      },
    });
  });

  watch(sortBy, (newValue) => {
    router.replace({
      query: {
        ...route.query,
        sortBy: newValue,
      },
    });
  });

  const url = computed(() => `media-deck/get-media-decks`);

  const {
    data: response,
    status,
    error,
  } = await useApiFetchPaginated<Deck[]>(url, {
    query: {
      offset: offset,
      mediaType: mediaType,
      wordId: wordIdRef,
      readingIndex: readingIndexRef,
      titleFilter: debouncedTitleFilter,
      sortBy: sortBy,
      sortOrder: sortOrder,
      status: statusFilter,
      charCountMin: computed(() => debouncedFilters.value.charCountMin),
      charCountMax: computed(() => debouncedFilters.value.charCountMax),
      releaseYearMin: computed(() => debouncedFilters.value.releaseYearMin),
      releaseYearMax: computed(() => debouncedFilters.value.releaseYearMax),
      uniqueKanjiMin: computed(() => debouncedFilters.value.uniqueKanjiMin),
      uniqueKanjiMax: computed(() => debouncedFilters.value.uniqueKanjiMax),
      subdeckCountMin: computed(() => debouncedFilters.value.subdeckCountMin),
      subdeckCountMax: computed(() => debouncedFilters.value.subdeckCountMax),
      extRatingMin: computed(() => debouncedFilters.value.extRatingMin),
      extRatingMax: computed(() => debouncedFilters.value.extRatingMax),
    },
    watch: [offset, mediaType],
  });

  const currentPage = computed(() => response.value?.currentPage);
  const pageSize = computed(() => response.value?.pageSize);
  const totalItems = computed(() => response.value?.totalItems);

  const start = computed(() => (currentPage.value - 1) * pageSize.value + 1);
  const end = computed(() => Math.min(currentPage.value * pageSize.value, totalItems.value));

  const previousLink = computed(() => {
    return response.value?.hasPreviousPage ? { query: { ...route.query, offset: response.value.previousOffset } } : null;
  });

  const nextLink = computed(() => {
    return response.value?.hasNextPage ? { query: { ...route.query, offset: response.value.nextOffset } } : null;
  });

  const scrollToTop = () => {
    nextTick(() => {
      window.scrollTo({ top: 0, behavior: 'instant' });
    });
  };

  const updateDeckInList = (updatedDeck: Deck) => {
    if (response.value?.data) {
      const index = response.value.data.findIndex(d => d.deckId === updatedDeck.deckId);
      if (index !== -1) {
        response.value.data[index] = updatedDeck;
      }
    }
  };

  const displayStyleStore = useDisplayStyleStore();
  const displayStyle = computed(() => displayStyleStore.displayStyle);

  const mediaTypeOptions = [
    { type: null, label: 'All' },
    { type: MediaType.Anime, label: 'Anime' },
    { type: MediaType.Drama, label: 'Dramas' },
    { type: MediaType.Manga, label: 'Manga' },
    { type: MediaType.Movie, label: 'Movies' },
    { type: MediaType.Novel, label: 'Novels' },
    { type: MediaType.NonFiction, label: 'Non-Fiction' },
    { type: MediaType.VideoGame, label: 'Video Games' },
    { type: MediaType.VisualNovel, label: 'Visual Novels' },
    { type: MediaType.WebNovel, label: 'Web Novels' },
  ];

  const isActive = (type: MediaType | null) => {
    if (type === null) {
      return !mediaType.value || mediaType.value === '0';
    }
    return Number(mediaType.value) === type;
  };
</script>

<template>
  <div class="flex flex-col gap-4">
    <Card>
      <template #content>
        <div class="flex flex-row flex-wrap justify-around gap-2">
          <NuxtLink
            v-for="option in mediaTypeOptions"
            :key="option.label"
            :to="{ query: option.type ? { ...route.query, mediaType: option.type, offset: 0 } : { ...route.query, mediaType: undefined, offset: 0 } }"
            :class="{ 'font-bold !text-purple-500': isActive(option.type) }"
          >
            {{ option.label }}
          </NuxtLink>
        </div>
      </template>
    </Card>
    <div class="flex flex-col md:flex-row gap-2">
      <div class="flex flex-row gap-2">
        <FloatLabel variant="on" class="w-full">
          <Select
            v-model="sortBy"
            :options="sortByOptions"
            option-label="label"
            option-value="value"
            placeholder="Sort by"
            input-id="sortBy"
            class="w-full md:w-56"
            scroll-height="30vh"
          />
          <label for="sortBy">Sort by</label>
        </FloatLabel>
        <Button class="w-12" @click="sortOrder = sortOrder === SortOrder.Ascending ? SortOrder.Descending : SortOrder.Ascending">
          <Icon v-if="sortOrder == SortOrder.Descending" name="mingcute:az-sort-descending-letters-line" size="1.25em" />
          <Icon v-if="sortOrder == SortOrder.Ascending" name="mingcute:az-sort-ascending-letters-line" size="1.25em" />
        </Button>
      </div>

      <IconField class="w-full">
        <InputIcon>
          <Icon name="material-symbols:search-rounded" />
        </InputIcon>
        <InputText v-model="titleFilter" type="text" placeholder="Search by title" class="w-full" />
        <InputIcon v-if="titleFilter" class="cursor-pointer" @click="titleFilter = null">
          <Icon name="material-symbols:close" />
        </InputIcon>
      </IconField>

      <!-- Advanced Filters -->
      <Button class="w-32" @click="filters.toggle($event)"> Filters </Button>

      <Popover ref="filters" class="w-full max-w-3xl">
        <div class="flex flex-col gap-4 p-3 min-w-[280px]">
          <FloatLabel v-if="isConnected" variant="on" class="w-full">
            <Select
              v-model="statusFilter"
              :options="statusFilterOptions"
              option-label="label"
              option-value="value"
              placeholder="Status"
              input-id="preferenceFilter"
              class="w-full md:w-56"
              scroll-height="30vh"
            />
            <label for="sortBy">Status</label>
          </FloatLabel>

          <div class="flex flex-col gap-2">
            <div class="text-sm font-medium text-gray-600 dark:text-gray-300">Character count</div>
            <div class="flex items-center gap-3">
              <InputNumber
                v-model="charCountMin"
                :min="0"
                :max="20000000"
                :use-grouping="true"
                fluid
                class="max-w-34 flex-shrink-0"
                show-buttons
                size="small"
                placeholder="Min"
                :step="10000"
              />
              <Slider v-model="charCountRange" range :min="0" :max="20000000" class="flex-1" />
              <InputNumber
                v-model="charCountMax"
                :min="0"
                :max="20000000"
                :use-grouping="true"
                fluid
                class="max-w-34 flex-shrink-0"
                show-buttons
                size="small"
                placeholder="Max"
                :step="10000"
              />
            </div>
          </div>

          <div class="flex flex-col gap-2">
            <div class="text-sm font-medium text-gray-600 dark:text-gray-300">Release year</div>
            <div class="flex items-center gap-3">
              <InputNumber
                v-model="releaseYearMin"
                :min="1900"
                :max="currentYear"
                :use-grouping="false"
                fluid
                class="max-w-28 flex-shrink-0"
                show-buttons
                size="small"
                placeholder="Min"
              />
              <Slider v-model="releaseYearRange" range :min="1900" :max="currentYear" class="flex-1" />
              <InputNumber
                v-model="releaseYearMax"
                :min="1900"
                :max="currentYear"
                :use-grouping="false"
                fluid
                class="max-w-28 flex-shrink-0"
                show-buttons
                size="small"
                placeholder="Max"
              />
            </div>
          </div>

          <div class="flex flex-col gap-2">
            <div class="text-sm font-medium text-gray-600 dark:text-gray-300">Unique kanji</div>
            <div class="flex items-center gap-3">
              <InputNumber
                v-model="uniqueKanjiMin"
                :min="0"
                :max="5000"
                :use-grouping="false"
                fluid
                class="max-w-28 flex-shrink-0"
                show-buttons
                size="small"
                placeholder="Min"
                :step="10"
              />
              <Slider v-model="uniqueKanjiRange" range :min="0" :max="5000" class="flex-1" />
              <InputNumber
                v-model="uniqueKanjiMax"
                :min="0"
                :max="5000"
                :use-grouping="false"
                fluid
                class="max-w-28 flex-shrink-0"
                show-buttons
                size="small"
                placeholder="Max"
                :step="10"
              />
            </div>
          </div>

          <div class="flex flex-col gap-2">
            <div class="text-sm font-medium text-gray-600 dark:text-gray-300">Subdecks</div>
            <div class="flex items-center gap-3">
              <InputNumber
                v-model="subdeckCountMin"
                :min="0"
                :max="2000"
                :use-grouping="false"
                fluid
                class="max-w-28 flex-shrink-0"
                show-buttons
                size="small"
                placeholder="Min"
              />
              <Slider v-model="subdeckCountRange" range :min="0" :max="2000" class="flex-1" />
              <InputNumber
                v-model="subdeckCountMax"
                :min="0"
                :max="2000"
                :use-grouping="false"
                fluid
                class="max-w-28 flex-shrink-0"
                show-buttons
                size="small"
                placeholder="Max"
              />
            </div>
          </div>

          <div class="flex flex-col gap-2">
            <div class="text-sm font-medium text-gray-600 dark:text-gray-300">External Rating (0 = unknown rating)</div>
            <div class="flex items-center gap-3">
              <InputNumber
                v-model="extRatingMin"
                :min="0"
                :max="100"
                :use-grouping="false"
                fluid
                class="max-w-28 flex-shrink-0"
                show-buttons
                size="small"
                placeholder="Min"
              />
              <Slider v-model="extRatingRange" range :min="0" :max="100" class="flex-1" />
              <InputNumber
                v-model="extRatingMax"
                :min="0"
                :max="100"
                :use-grouping="false"
                fluid
                class="max-w-28 flex-shrink-0"
                show-buttons
                size="small"
                placeholder="Max"
              />
            </div>
          </div>
        </div>
      </Popover>

      <div class="flex flex-row gap-2 items-center">
        <DisplayStyleSelector />
      </div>
    </div>
    <div>
      <div class="flex flex-col gap-1">
        <div class="flex flex-col md:flex-row justify-between">
          <div class="flex gap-8 pl-2 pb-2">
            <NuxtLink :to="previousLink" :class="previousLink == null ? '!text-gray-500 pointer-events-none' : ''" no-rel> Previous </NuxtLink>
            <NuxtLink :to="nextLink" :class="nextLink == null ? '!text-gray-500 pointer-events-none' : ''" no-rel> Next </NuxtLink>
          </div>
          <div class="pr-2 text-gray-500 dark:text-gray-300">
            viewing decks {{ start }}-{{ end }} from {{ totalItems }}
            total
          </div>
        </div>

        <div v-if="status === 'pending'" class="flex flex-col gap-4">
          <Card v-for="i in 5" :key="i" class="p-2">
            <template #content>
              <Skeleton width="100%" height="250px" />
            </template>
          </Card>
        </div>

        <div v-else-if="error">Error: {{ error }}</div>

        <!-- Card View -->
        <div v-else-if="displayStyle === DisplayStyle.Card" class="flex flex-col gap-2">
          <MediaDeckCard v-for="deck in response.data" :key="deck.deckId" :deck="deck" @update:deck="updateDeckInList" />
        </div>

        <!-- Compact View -->
        <div v-else-if="displayStyle === DisplayStyle.Compact" class="flex flex-wrap gap-4 justify-center">
          <MediaDeckCompactView v-for="deck in response.data" :key="deck.id" :deck="deck" />
        </div>

        <!-- Table View -->
        <div v-else-if="displayStyle === DisplayStyle.Table" class="flex flex-col gap-0.5">
          <MediaDeckTableView v-for="deck in response.data" :key="deck.id" :deck="deck" />
        </div>
      </div>
      <div class="flex gap-8 pl-2">
        <NuxtLink :to="previousLink" :class="previousLink == null ? '!text-gray-500 pointer-events-none' : ''" no-rel @click="scrollToTop"> Previous </NuxtLink>
        <NuxtLink :to="nextLink" :class="nextLink == null ? '!text-gray-500 pointer-events-none' : ''" no-rel @click="scrollToTop"> Next </NuxtLink>
      </div>
    </div>
  </div>
</template>

<style scoped></style>
