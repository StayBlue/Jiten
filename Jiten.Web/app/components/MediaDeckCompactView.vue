<script setup lang="ts">
  import type { Deck } from '~/types';
  import { getMediaTypeText } from '~/utils/mediaTypeMapper';
  import { localiseTitle } from '~/utils/localiseTitle';
  import MediaDeckDownloadDialog from '~/components/MediaDeckDownloadDialog.vue';
  import Card from 'primevue/card';
  import { useAuthStore } from '~/stores/authStore';
  import { useJitenStore } from '~/stores/jitenStore';

  const authStore = useAuthStore();
  const store = useJitenStore();

  const props = defineProps<{
    deck: Deck;
  }>();

  const showDownloadDialog = ref(false);

  const borderColor = computed(() => {
    if (!authStore.isAuthenticated || store.hideCoverageBorders || (props.deck.coverage == 0 && props.deck.uniqueCoverage == 0))
      return 'none';
    return getCoverageBorder(props.deck.coverage, '4px');
  });
</script>

<template>
  <div class="relative group h-48 w-34">
    <div class="h-48 w-34 overflow-hidden rounded-md border hover:shadow-md transition-shadow duration-200"   :style="{ 'border': borderColor }">
      <div class="relative h-full">
        <!-- Cover image -->
        <img
          :src="deck.coverName == 'nocover.jpg' ? '/img/nocover.jpg' : deck.coverName"
          :alt="deck.originalTitle"
          class="w-full h-full object-cover"
        />

        <!-- Title overlay at bottom -->
        <div class="absolute flex justify-between items-center bottom-0 left-0 right-0 bg-black/75 0 p-1 text-white">
<!--          <div class="font-bold text-sm truncate">{{ localiseTitle(deck) }}</div>-->
          <div class="text-xs text-gray-300">{{ getMediaTypeText(deck.mediaType) }}</div>
          <div v-if="deck.selectedWordOccurrences != 0" class="bg-purple-500 dark:bg-purple-300 border-1 border-purple-200 dark:border-purple-800 text-white dark:text-black px-2 py-1 rounded-full text-xs font-bold">
            x{{ deck.selectedWordOccurrences.toLocaleString() }}
          </div>
        </div>

        <!-- Hover overlay with additional info -->
        <div
          class="absolute inset-0 bg-black bg-opacity-80 text-white p-2 flex flex-col opacity-0 group-hover:opacity-100 transition-opacity duration-200"
        >
          <div class="font-bold mb-2 truncate">{{ localiseTitle(deck) }}</div>
          <div class="text-xs mb-1">{{ getMediaTypeText(deck.mediaType) }}</div>

          <div class="text-xs space-y-1 mt-auto">
            <div class="flex justify-between">
              <span>Chars:</span>
              <span class="tabular-nums">{{ deck.characterCount.toLocaleString() }}</span>
            </div>
            <div class="flex justify-between">
              <span>Uniq words:</span>
              <span class="tabular-nums">{{ deck.uniqueWordCount.toLocaleString() }}</span>
            </div>
            <div v-if="deck.difficulty != -1" class="flex justify-between">
              <span>Difficulty:</span>
              <DifficultyStars :difficulty="deck.difficulty" />
            </div>
          </div>

          <div class="mt-2 flex gap-1">
            <Button
              v-tooltip="'View details'"
              as="router-link"
              :to="`/decks/media/${deck.deckId}/detail`"
              size="small"
              class="p-button-sm"
            >
              <Icon name="material-symbols:info-outline" size="1.5em" />
            </Button>
            <Button
              v-tooltip="'View vocabulary'"
              as="router-link"
              :to="`/decks/media/${deck.deckId}/vocabulary`"
              size="small"
              class="p-button-sm"
            >
              <Icon name="material-symbols:menu-book-outline" size="1.5em" />
            </Button>
            <Button v-tooltip="'Download deck'" size="small" class="p-button-sm" @click="showDownloadDialog = true">
              <Icon name="material-symbols:download" size="1.5em" />
            </Button>
          </div>
        </div>
      </div>
    </div>
  </div>

  <MediaDeckDownloadDialog :deck="deck" :visible="showDownloadDialog" @update:visible="showDownloadDialog = $event" />
</template>

<style scoped></style>
