<script setup lang="ts">
import { useApiFetch } from '~/composables/useApiFetch';
import { type Tag } from '~/types';
import { getAllGenres } from '~/utils/genreMapper';
import type { TagState } from '~/components/TriStateTag.vue';
import ScrollPanel from 'primevue/scrollpanel';

const props = defineProps<{
  isConnected: boolean;
}>();

const emit = defineEmits<{
  reset: [];
}>();

const statusFilter = defineModel<string>('statusFilter', { required: true });
const charCountMin = defineModel<number | null>('charCountMin', { required: true });
const charCountMax = defineModel<number | null>('charCountMax', { required: true });
const releaseYearMin = defineModel<number | null>('releaseYearMin', { required: true });
const releaseYearMax = defineModel<number | null>('releaseYearMax', { required: true });
const uniqueKanjiMin = defineModel<number | null>('uniqueKanjiMin', { required: true });
const uniqueKanjiMax = defineModel<number | null>('uniqueKanjiMax', { required: true });
const subdeckCountMin = defineModel<number | null>('subdeckCountMin', { required: true });
const subdeckCountMax = defineModel<number | null>('subdeckCountMax', { required: true });
const extRatingMin = defineModel<number | null>('extRatingMin', { required: true });
const extRatingMax = defineModel<number | null>('extRatingMax', { required: true });
const includeGenres = defineModel<number[]>('includeGenres', { required: true });
const excludeGenres = defineModel<number[]>('excludeGenres', { required: true });
const includeTags = defineModel<number[]>('includeTags', { required: true });
const excludeTags = defineModel<number[]>('excludeTags', { required: true });

const popover = ref();

const currentYear = new Date().getFullYear();

const statusFilterOptions = [
  { label: 'Show All', value: 'none' },
  { label: 'Only Favourited', value: 'fav' },
  { label: 'Only Ignored', value: 'ignore' },
  { label: 'Only Planning', value: 'planning' },
  { label: 'Only Ongoing', value: 'ongoing' },
  { label: 'Only Completed', value: 'completed' },
  { label: 'Only Dropped', value: 'dropped' },
];

const { data: availableTags } = useApiFetch<Tag[]>('media-deck/tags', {
  server: true,
  lazy: false,
});

const tags = computed(() => availableTags.value || []);
const genres = computed(() => getAllGenres());

const genreSearchQuery = ref('');
const tagSearchQuery = ref('');

const filteredGenres = computed(() => {
  if (!genreSearchQuery.value) return genres.value;
  const query = genreSearchQuery.value.toLowerCase();
  return genres.value.filter((genre) => genre.label.toLowerCase().includes(query));
});

const filteredTags = computed(() => {
  if (!tagSearchQuery.value) return tags.value;
  const query = tagSearchQuery.value.toLowerCase();
  return tags.value.filter((tag) => tag.name.toLowerCase().includes(query));
});

const genreFilteredCount = computed(() => filteredGenres.value.length);
const genreTotalCount = computed(() => genres.value.length);
const tagFilteredCount = computed(() => filteredTags.value.length);
const tagTotalCount = computed(() => tags.value.length);

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

const updateGenreState = (genreId: number, state: TagState) => {
  if (state === 'include') {
    if (!includeGenres.value.includes(genreId)) {
      includeGenres.value.push(genreId);
    }
    excludeGenres.value = excludeGenres.value.filter((id) => id !== genreId);
  } else if (state === 'exclude') {
    includeGenres.value = includeGenres.value.filter((id) => id !== genreId);
    if (!excludeGenres.value.includes(genreId)) {
      excludeGenres.value.push(genreId);
    }
  } else {
    includeGenres.value = includeGenres.value.filter((id) => id !== genreId);
    excludeGenres.value = excludeGenres.value.filter((id) => id !== genreId);
  }
};

const updateTagState = (tagId: number, state: TagState) => {
  if (state === 'include') {
    if (!includeTags.value.includes(tagId)) {
      includeTags.value.push(tagId);
    }
    excludeTags.value = excludeTags.value.filter((id) => id !== tagId);
  } else if (state === 'exclude') {
    includeTags.value = includeTags.value.filter((id) => id !== tagId);
    if (!excludeTags.value.includes(tagId)) {
      excludeTags.value.push(tagId);
    }
  } else {
    includeTags.value = includeTags.value.filter((id) => id !== tagId);
    excludeTags.value = excludeTags.value.filter((id) => id !== tagId);
  }
};

const handleReset = () => {
  genreSearchQuery.value = '';
  tagSearchQuery.value = '';
  emit('reset');
};

const toggle = (event: Event) => {
  popover.value.toggle(event);
};

defineExpose({ toggle });
</script>

<template>
  <Button class="w-32" @click="toggle($event)">Filters</Button>

  <Popover ref="popover" class="w-full max-w-3xl">
    <div class="flex flex-col gap-4 p-3 min-w-[280px]">
      <Tabs value="filters">
        <TabList>
          <Tab value="filters">Filters</Tab>
          <Tab value="genres">Genres</Tab>
          <Tab value="tags">Tags</Tab>
        </TabList>

        <TabPanels>
          <TabPanel value="filters">
            <div class="flex flex-col gap-4 pt-4">
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
                <label for="preferenceFilter">Status</label>
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
          </TabPanel>

          <TabPanel value="genres">
            <div class="flex flex-col gap-3 pt-4">
              <div class="flex items-center justify-between gap-2">
                <div class="text-sm font-medium text-gray-600 dark:text-gray-300">{{ genreFilteredCount }}/{{ genreTotalCount }}</div>
                <IconField class="flex-1">
                  <InputIcon>
                    <Icon name="material-symbols:search-rounded" />
                  </InputIcon>
                  <InputText v-model="genreSearchQuery" type="text" placeholder="Search genres..." class="w-full" />
                  <InputIcon v-if="genreSearchQuery" class="cursor-pointer" @click="genreSearchQuery = ''">
                    <Icon name="material-symbols:close" />
                  </InputIcon>
                </IconField>
              </div>
              <ScrollPanel class="w-full" style="height: 350px">
                <div class="flex flex-wrap gap-2 p-2">
                  <TriStateTag
                    v-for="genre in filteredGenres"
                    :key="genre.value"
                    :label="genre.label"
                    :state="includeGenres.includes(genre.value) ? 'include' : excludeGenres.includes(genre.value) ? 'exclude' : 'neutral'"
                    @update:state="(state) => updateGenreState(genre.value, state)"
                  />
                </div>
              </ScrollPanel>
            </div>
          </TabPanel>

          <TabPanel value="tags">
            <div class="flex flex-col gap-3 pt-4">
              <div class="flex items-center justify-between gap-2">
                <div class="text-sm font-medium text-gray-600 dark:text-gray-300">{{ tagFilteredCount }}/{{ tagTotalCount }}</div>
                <IconField class="flex-1">
                  <InputIcon>
                    <Icon name="material-symbols:search-rounded" />
                  </InputIcon>
                  <InputText v-model="tagSearchQuery" type="text" placeholder="Search tags..." class="w-full" />
                  <InputIcon v-if="tagSearchQuery" class="cursor-pointer" @click="tagSearchQuery = ''">
                    <Icon name="material-symbols:close" />
                  </InputIcon>
                </IconField>
              </div>
              <ScrollPanel class="w-full" style="height: 350px">
                <div class="flex flex-wrap gap-2 p-2">
                  <TriStateTag
                    v-for="tag in filteredTags"
                    :key="tag.tagId"
                    :label="tag.name"
                    :state="includeTags.includes(tag.tagId) ? 'include' : excludeTags.includes(tag.tagId) ? 'exclude' : 'neutral'"
                    @update:state="(state) => updateTagState(tag.tagId, state)"
                  />
                </div>
              </ScrollPanel>
            </div>
          </TabPanel>
        </TabPanels>
      </Tabs>

      <!-- Reset Button -->
      <div class="flex justify-end pt-3 border-t border-gray-200 dark:border-gray-700">
        <Button severity="info" @click="handleReset">
          <Icon name="material-symbols:refresh" class="mr-2" />
          Reset All Filters
        </Button>
      </div>
    </div>
  </Popover>
</template>
