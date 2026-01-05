<script setup lang="ts">
import InputText from 'primevue/inputtext';
import ProgressSpinner from 'primevue/progressspinner';
import { getMediaTypeText } from '~/utils/mediaTypeMapper';
import type { MediaSuggestion } from '~/types/types';
import { TitleLanguage } from '~/types';

const props = defineProps<{
  placeholder?: string;
  autofocus?: boolean;
}>();

const route = useRoute();
const store = useJitenStore();

const searchText = ref<string>(
  Array.isArray(route.query.text) ? route.query.text[0] || '' : (route.query.text as string) || ''
);
const isDropdownOpen = ref(false);
const highlightedIndex = ref(0);

const { suggestions, totalCount, isLoading, fetchSuggestions, clearSuggestions } = useMediaSuggestions();

const showMediaSection = computed(() => searchText.value.length >= 2);

watch(searchText, (newValue) => {
  if (newValue && newValue.length >= 1) {
    isDropdownOpen.value = true;
    if (newValue.length >= 2) {
      fetchSuggestions(newValue);
    } else {
      clearSuggestions();
    }
  } else {
    isDropdownOpen.value = false;
    clearSuggestions();
  }
  highlightedIndex.value = 0;
});

const navigateToParse = async () => {
  if (!searchText.value.trim()) return;

  isDropdownOpen.value = false;
  await navigateTo({
    path: '/parse',
    query: { text: searchText.value.trim() },
  });
};

const navigateToMediaSearch = async () => {
  if (!searchText.value.trim()) return;

  isDropdownOpen.value = false;
  await navigateTo({
    path: '/decks/media',
    query: { title: searchText.value.trim() },
  });
};

const navigateToDeck = async (deckId: number) => {
  isDropdownOpen.value = false;
  await navigateTo(`/decks/media/${deckId}/detail`);
};

const totalOptions = computed(() => {
  if (!showMediaSection.value) {
    return 1;
  }
  return 2 + suggestions.value.length;
});

const handleKeyDown = (event: KeyboardEvent) => {
  if (!isDropdownOpen.value && searchText.value.length >= 1) {
    if (event.key === 'ArrowDown') {
      isDropdownOpen.value = true;
      return;
    }
  }

  switch (event.key) {
    case 'ArrowDown':
      event.preventDefault();
      highlightedIndex.value = (highlightedIndex.value + 1) % totalOptions.value;
      break;
    case 'ArrowUp':
      event.preventDefault();
      highlightedIndex.value = highlightedIndex.value === 0 ? totalOptions.value - 1 : highlightedIndex.value - 1;
      break;
    case 'Enter':
      event.preventDefault();
      handleSelection();
      break;
    case 'Escape':
      isDropdownOpen.value = false;
      break;
  }
};

const handleSelection = () => {
  if (highlightedIndex.value === 0) {
    navigateToParse();
  } else if (showMediaSection.value && highlightedIndex.value === 1) {
    navigateToMediaSearch();
  } else if (showMediaSection.value) {
    const suggestionIndex = highlightedIndex.value - 2;
    if (suggestions.value[suggestionIndex]) {
      navigateToDeck(suggestions.value[suggestionIndex].deckId);
    }
  }
};

const dropdownRef = ref<HTMLElement | null>(null);
const inputRef = ref<HTMLElement | null>(null);

onMounted(() => {
  document.addEventListener('click', handleClickOutside);
});

onUnmounted(() => {
  document.removeEventListener('click', handleClickOutside);
});

const handleClickOutside = (event: MouseEvent) => {
  const target = event.target as Node;
  if (dropdownRef.value && !dropdownRef.value.contains(target) && inputRef.value && !inputRef.value.contains(target)) {
    isDropdownOpen.value = false;
  }
};

const getTitle = (suggestion: MediaSuggestion): string => {
  if (store.titleLanguage === TitleLanguage.Original) {
    return suggestion.originalTitle;
  }

  if (store.titleLanguage === TitleLanguage.Romaji) {
    return suggestion.romajiTitle || suggestion.originalTitle;
  }

  if (store.titleLanguage === TitleLanguage.English) {
    return suggestion.englishTitle || suggestion.romajiTitle || suggestion.originalTitle;
  }

  return suggestion.originalTitle;
};

const getCoverUrl = (coverName: string): string => {
  return coverName === 'nocover.jpg' ? '/img/nocover.jpg' : coverName;
};

const remainingCount = computed(() => {
  return Math.max(0, totalCount.value - suggestions.value.length);
});
</script>

<template>
  <div class="relative w-full">
    <div ref="inputRef" class="flex flex-row">
      <IconField class="w-full">
        <InputIcon>
          <Icon name="material-symbols:search-rounded" />
        </InputIcon>
        <InputText
          v-model="searchText"
          type="text"
          :placeholder="placeholder || 'Search words, sentences, or media'"
          class="w-full"
          maxlength="500"
          :autofocus="autofocus"
          role="combobox"
          aria-autocomplete="list"
          :aria-expanded="isDropdownOpen"
          aria-controls="omni-search-dropdown"
          @keydown="handleKeyDown"
          @focus="searchText.length >= 1 && (isDropdownOpen = true)"
        />
      </IconField>
      <Button label="Parse" class="ml-2" :disabled="!searchText.trim()" @click="navigateToParse">
        <Icon name="material-symbols:search-rounded" />
      </Button>
    </div>

    <Transition name="fade">
      <div
        v-if="isDropdownOpen && searchText.length >= 1"
        id="omni-search-dropdown"
        ref="dropdownRef"
        class="absolute z-50 w-full mt-1 bg-white dark:bg-gray-800 border border-gray-200 dark:border-gray-700 rounded-lg shadow-lg overflow-hidden"
        role="listbox"
      >
        <div
          class="px-4 py-3 cursor-pointer flex items-center gap-3 transition-colors"
          :class="
            highlightedIndex === 0 ? 'bg-purple-100 dark:bg-purple-900/30' : 'hover:bg-gray-100 dark:hover:bg-gray-700'
          "
          role="option"
          :aria-selected="highlightedIndex === 0"
          @click="navigateToParse"
          @mouseenter="highlightedIndex = 0"
        >
          <Icon name="material-symbols:translate" class="text-xl text-purple-500" />
          <div class="min-w-0 flex-1">
            <div class="font-medium">Parse: "{{ searchText }}"</div>
            <div class="text-sm text-gray-500 dark:text-gray-400">Look up words and definitions</div>
          </div>
          <div class="text-xs text-gray-400 dark:text-gray-500">
            <kbd class="px-1.5 py-0.5 bg-gray-200 dark:bg-gray-700 rounded font-mono text-xs">Enter</kbd>
          </div>
        </div>

        <template v-if="showMediaSection">
          <div
            class="px-4 py-2.5 cursor-pointer flex items-center gap-3 border-t border-gray-100 dark:border-gray-700 transition-colors"
            :class="
              highlightedIndex === 1 ? 'bg-purple-100 dark:bg-purple-900/30' : 'hover:bg-gray-100 dark:hover:bg-gray-700'
            "
            role="option"
            :aria-selected="highlightedIndex === 1"
            @click="navigateToMediaSearch"
            @mouseenter="highlightedIndex = 1"
          >
            <Icon name="material-symbols:video-library-outline" class="text-lg text-gray-500" />
            <span class="flex-1">
              View more media for "{{ searchText }}"
              <span v-if="remainingCount > 0" class="text-purple-500 font-medium">(+{{ remainingCount }})</span>
            </span>
            <Icon name="material-symbols:arrow-forward" class="text-gray-400" />
          </div>

          <div
            v-if="isLoading"
            class="px-4 py-3 flex items-center justify-center border-t border-gray-100 dark:border-gray-700"
          >
            <ProgressSpinner style="width: 20px; height: 20px" stroke-width="4" />
            <span class="ml-2 text-sm text-gray-500">Searching media...</span>
          </div>

          <template v-else-if="suggestions.length > 0">
            <div class="border-t border-gray-100 dark:border-gray-700">
              <div class="px-4 py-1.5 text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wide">
                Media
              </div>
              <div
                v-for="(suggestion, index) in suggestions"
                :key="suggestion.deckId"
                class="px-4 py-2 cursor-pointer flex items-center gap-3 transition-colors"
                :class="
                  highlightedIndex === index + 2
                    ? 'bg-purple-100 dark:bg-purple-900/30'
                    : 'hover:bg-gray-100 dark:hover:bg-gray-700'
                "
                role="option"
                :aria-selected="highlightedIndex === index + 2"
                @click="navigateToDeck(suggestion.deckId)"
                @mouseenter="highlightedIndex = index + 2"
              >
                <img
                  :src="getCoverUrl(suggestion.coverName)"
                  :alt="getTitle(suggestion)"
                  class="w-10 h-14 object-cover rounded flex-shrink-0"
                />
                <div class="min-w-0 flex-1">
                  <div class="font-medium truncate">{{ getTitle(suggestion) }}</div>
                  <div class="text-sm text-gray-500 dark:text-gray-400">
                    {{ getMediaTypeText(suggestion.mediaType) }}
                  </div>
                </div>
              </div>
            </div>
          </template>

          <div
            v-else-if="!isLoading"
            class="px-4 py-3 text-sm text-gray-500 dark:text-gray-400 border-t border-gray-100 dark:border-gray-700"
          >
            No media found matching "{{ searchText }}"
          </div>
        </template>
      </div>
    </Transition>
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
