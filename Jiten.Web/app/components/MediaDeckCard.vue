<script setup lang="ts">
  import { type Deck, MediaType, DeckStatus } from '~/types';
  import Card from 'primevue/card';
  import TieredMenu from 'primevue/tieredmenu';
  import { getChildrenCountText, getMediaTypeText } from '~/utils/mediaTypeMapper';
  import { getLinkTypeText } from '~/utils/linkTypeMapper';
  import { getDeckStatusText } from '~/utils/deckStatusMapper';
  import { useJitenStore } from '~/stores/jitenStore';
  import { formatDateAsYyyyMmDd } from '~/utils/formatDateAsYyyyMmDd';
  import { useAuthStore } from '~/stores/authStore';

  const props = defineProps<{
    deck: Deck;
    isCompact?: boolean;
    hideControl?: boolean;
  }>();

  const emit = defineEmits<{
    'update:deck': [deck: Deck];
  }>();

  const showDownloadDialog = ref(false);
  const showIssueDialog = ref(false);
  const isDescriptionExpanded = ref(false);
  const showIgnoreOverlay = ref(false);
  const menu = ref();

  const store = useJitenStore();
  const authStore = useAuthStore();

  const displayAdminFunctions = computed(() => store.displayAdminFunctions);
  const readingSpeed = computed(() => store.readingSpeed);
  const readingDuration = computed(() => Math.round(props.deck.characterCount / readingSpeed.value));

  const toggleMenu = (event: Event) => {
    menu.value.toggle(event);
  };

  const { toggleFavourite, toggleIgnore: _toggleIgnore, cancelIgnore: _cancelIgnore, setStatus } = useDeckPreference(
    () => props.deck,
    (updated) => emit('update:deck', updated)
  );

  const toggleIgnore = async () => {
    const newState = await _toggleIgnore();
    if (newState !== null) {
      showIgnoreOverlay.value = newState;
    }
  };

  const cancelIgnore = async () => {
    await _cancelIgnore();
    showIgnoreOverlay.value = false;
  };

  const menuItems = computed(() => [
    {
      label: props.deck.isFavourite ? 'Unfavourite' : 'Favourite',
      icon: props.deck.isFavourite ? 'pi pi-star-fill' : 'pi pi-star',
      command: toggleFavourite,
    },
    {
      label: props.deck.isIgnored ? 'Unignore' : 'Ignore',
      icon: props.deck.isIgnored ? 'pi pi-eye' : 'pi pi-eye-slash',
      command: toggleIgnore,
    },
    {
      label: 'Set status',
      icon: 'pi pi-flag',
      items: [
        {
          label: 'None',
          command: () => setStatus(DeckStatus.None),
        },
        {
          label: 'Planning',
          command: () => setStatus(DeckStatus.Planning),
        },
        {
          label: 'Ongoing',
          command: () => setStatus(DeckStatus.Ongoing),
        },
        {
          label: 'Completed',
          command: () => setStatus(DeckStatus.Completed),
        },
        {
          label: 'Dropped',
          command: () => setStatus(DeckStatus.Dropped),
        },
      ],
    },
  ]);

  const statusColor = computed(() => {
    if (!props.deck.status || props.deck.status === DeckStatus.None) return '';

    switch (props.deck.status) {
      case DeckStatus.Planning:
        return 'text-gray-500';
      case DeckStatus.Ongoing:
        return 'text-yellow-500';
      case DeckStatus.Completed:
        return 'text-green-500';
      case DeckStatus.Dropped:
        return 'text-red-500';
      default:
        return '';
    }
  });

  const sortedLinks = computed(() => {
    if (!props.deck.links || props.deck.links.length === 0) return [];

    return [...props.deck.links].sort((a, b) => {
      const textA = getLinkTypeText(a.linkType);
      const textB = getLinkTypeText(b.linkType);
      return textA.localeCompare(textB);
    });
  });

  const toggleDescription = () => {
    isDescriptionExpanded.value = !isDescriptionExpanded.value;
  };

  const borderColor = computed(() => {
    if (!authStore.isAuthenticated || store.hideCoverageBorders || (props.deck.coverage == 0 && props.deck.uniqueCoverage == 0)) return 'none';
    return getCoverageBorder(props.deck.coverage);
  });

</script>

<template>
  <div class="relative">
    <div
      v-if="showIgnoreOverlay"
      class="absolute inset-0 z-50 flex items-center justify-center backdrop-blur-lg bg-black/50 rounded-lg ignore-overlay"
      @click.stop
    >
      <div class="bg-white dark:bg-gray-800 rounded-lg p-6 max-w-md mx-4 shadow-xl">
        <p class="text-center text-gray-800 dark:text-gray-200 mb-4">This media will be ignored and no longer appear in search results.</p>
        <div class="text-center">
          <a
            href="#"
            class="text-primary-500 hover:text-primary-700 dark:hover:text-primary-400 font-semibold underline-offset-2 hover:underline"
            @click.prevent="cancelIgnore"
          >
            Cancel
          </a>
        </div>
      </div>
    </div>

    <Card class="p-2" :style="{ outline: borderColor }">
      <template #title>
        <div class="flex justify-between items-start">
          <span>{{ localiseTitle(deck) }}</span>
          <div class="flex flex-row items-center gap-1 h-6">
            <div v-if="authStore.isAuthenticated" class="flex items-center gap-2">
              <i v-if="deck.isFavourite" class="pi pi-star-fill text-yellow-500 text-lg" />
              <i v-if="deck.isIgnored" class="pi pi-eye-slash text-gray-800 dark:text-gray-300 text-lg" />
              <span v-if="deck.status && deck.status !== DeckStatus.None" :class="['text-sm font-bold', statusColor]">
                {{ getDeckStatusText(deck.status) }}
              </span>
            </div>
            <Tooltip content="View stats">
              <router-link
                :to="`/decks/media/${deck.deckId}/stats`"
                class="inline-block p-1.5 rounded hover:bg-gray-200 dark:hover:bg-gray-700 transition-colors cursor-pointer"
              >
                <i class="pi pi-chart-bar text-primary-500" />
              </router-link>
            </Tooltip>
            <Tooltip v-if="authStore.isAuthenticated" content="More options">
              <button type="button" class="p-1.5 rounded hover:bg-gray-200 dark:hover:bg-gray-700 transition-colors cursor-pointer" @click="toggleMenu">
                <i class="pi pi-ellipsis-v text-primary-500" />
              </button>
            </Tooltip>
          </div>
        </div>
      </template>
      <template v-if="!isCompact" #subtitle>{{ getMediaTypeText(deck.mediaType) }}</template>
      <template #content>
        <div class="flex-gap-6">
          <div class="flex-1 max-w-full overflow-hidden">
            <div class="flex flex-col md:flex-row gap-x-4 gap-y-2 w-full">
              <div v-if="!isCompact" class="text-left text-sm md:text-center">
                <img
                  :src="deck.coverName == 'nocover.jpg' ? '/img/nocover.jpg' : deck.coverName"
                  :alt="deck.originalTitle"
                  class="h-48 w-34 min-w-34 object-cover"
                />
                <div>{{ formatDateAsYyyyMmDd(new Date(deck.releaseDate)).replace(/-/g, '/') }}</div>
                <template v-if="authStore.isAuthenticated && (deck.coverage != 0 || deck.uniqueCoverage != 0)">
                  <div>
                    <div class="text-gray-600 dark:text-gray-300 truncate pr-2 font-medium">Coverage</div>
                    <div
                      v-tooltip="`${((deck.wordCount * deck.coverage) / 100).toFixed(0)} / ${deck.wordCount}`"
                      class="relative w-full bg-gray-400 dark:bg-gray-700 rounded-lg h-6 overflow-hidden"
                    >
                      <div class="bg-purple-500 h-6 rounded-lg transition-all duration-700" :style="{ width: deck.coverage.toFixed(1) + '%' }"></div>
                      <span class="absolute inset-0 flex items-center pl-2 text-xs font-bold text-white dark:text-white">
                        {{ deck.coverage.toFixed(1) }}%
                      </span>
                    </div>
                  </div>
                  <div>
                    <div class="text-gray-600 dark:text-gray-300 truncate pr-2 font-medium">Unique coverage</div>
                    <div
                      v-tooltip="`${((deck.uniqueWordCount * deck.uniqueCoverage) / 100).toFixed(0)} / ${deck.uniqueWordCount}`"
                      class="relative w-full bg-gray-400 dark:bg-gray-700 rounded-lg h-6 overflow-hidden"
                    >
                      <div class="bg-purple-500 h-6 rounded-lg transition-all duration-700" :style="{ width: deck.uniqueCoverage.toFixed(1) + '%' }"></div>
                      <span class="absolute inset-0 flex items-center pl-2 text-xs font-bold text-white dark:text-white">
                        {{ deck.uniqueCoverage.toFixed(1) }}%
                      </span>
                    </div>
                  </div>
                </template>
              </div>
              <div>
                <div class="flex flex-col gap-x-6 gap-y-2" :class="isCompact ? '' : 'md:flex-row md:flex-wrap'">
                  <div class="w-full md:w-64">
                    <div class="flex justify-between flex-wrap stat-row">
                      <span class="text-gray-600 dark:text-gray-300 truncate pr-2 font-medium">Character count</span>
                      <span class="tabular-nums font-semibold">{{ deck.characterCount.toLocaleString() }}</span>
                    </div>
                    <div class="flex justify-between flex-wrap stat-row">
                      <span class="text-gray-600 dark:text-gray-300 truncate pr-2 font-medium">Word count</span>
                      <span class="tabular-nums font-semibold">{{ deck.wordCount.toLocaleString() }}</span>
                    </div>
                    <div class="flex justify-between flex-wrap stat-row">
                      <span class="text-gray-600 dark:text-gray-300 truncate pr-2 font-medium">Unique words</span>
                      <span class="tabular-nums font-semibold">{{ deck.uniqueWordCount.toLocaleString() }}</span>
                    </div>
                    <div class="flex justify-between flex-wrap stat-row">
                      <span class="text-gray-600 dark:text-gray-300 truncate pr-2 font-medium">Words (1-occurrence)</span>
                      <span class="tabular-nums font-semibold">{{ deck.uniqueWordUsedOnceCount.toLocaleString() }}</span>
                    </div>
                  </div>

                  <div class="w-full md:w-64">
                    <div class="flex justify-between flex-wrap stat-row">
                      <span class="text-gray-600 dark:text-gray-300 truncate pr-2 font-medium">Unique kanji</span>
                      <span class="tabular-nums font-semibold">{{ deck.uniqueKanjiCount.toLocaleString() }}</span>
                    </div>
                    <div class="flex justify-between flex-wrap stat-row">
                      <span class="text-gray-600 dark:text-gray-300 truncate pr-2 font-medium">Kanji (1-occurrence)</span>
                      <span class="tabular-nums font-semibold">{{ deck.uniqueKanjiUsedOnceCount.toLocaleString() }}</span>
                    </div>
                    <div v-if="deck.averageSentenceLength !== 0" class="flex justify-between flex-wrap stat-row">
                      <span class="text-gray-600 dark:text-gray-300 truncate pr-2 font-medium">Average sentence length</span>
                      <span class="tabular-nums font-semibold">{{ deck.averageSentenceLength.toFixed(1) }}</span>
                    </div>
                    <div v-if="deck.difficulty != -1" class="flex justify-between flex-wrap stat-row">
                      <Tooltip
                        :content="'This is a work in progress.\nIf you find scores that are way higher or lower than they should be, please report them so the algorithm can be refined further.'"
                      >
                        <span class="text-gray-600 dark:text-gray-300 truncate pr-2 font-medium">
                          Difficulty
                          <span class="text-purple-500 text-xs align-super"> beta </span>
                        </span>
                      </Tooltip>
                      <DifficultyDisplay :difficulty="deck.difficulty" :difficulty-raw="deck.difficultyRaw" />
                    </div>
                  </div>

                  <div class="w-full md:w-64">
                    <div
                      v-if="!deck.hideDialoguePercentage && deck.dialoguePercentage != 0 && deck.dialoguePercentage != 100"
                      class="flex justify-between flex-wrap stat-row"
                    >
                      <span class="text-gray-600 dark:text-gray-300 truncate pr-2 font-medium">Dialogue</span>
                      <span class="tabular-nums font-semibold">{{ deck.dialoguePercentage.toFixed(1) }}%</span>
                    </div>

                    <div v-if="deck.childrenDeckCount != 0" class="flex justify-between flex-wrap stat-row">
                      <span class="text-gray-600 dark:text-gray-300 truncate pr-2 font-medium">{{ getChildrenCountText(deck.mediaType) }}</span>
                      <span class="tabular-nums font-semibold">{{ deck.childrenDeckCount.toLocaleString() }}</span>
                    </div>

                    <div
                      v-if="
                        deck.mediaType == MediaType.Novel ||
                        deck.mediaType == MediaType.NonFiction ||
                        deck.mediaType == MediaType.VisualNovel ||
                        deck.mediaType == MediaType.WebNovel
                      "
                      class="flex justify-between flex-wrap stat-row"
                    >
                      <Tooltip
                        :content="
                          'Based on your reading speed of:\n ' +
                          '<strong>' +
                          readingSpeed +
                          '</strong>' +
                          ' characters per hour.\n<i>You can adjust it in the quick settings cog at the top right.</i>'
                        "
                      >
                        <span class="text-gray-600 dark:text-gray-300 truncate pr-2 font-medium">
                          Duration
                          <i class="pi pi-info-circle cursor-pointer text-primary-500" />
                        </span>
                      </Tooltip>

                      <span class="tabular-nums font-semibold">{{ readingDuration > 0 ? readingDuration : '<1' }} h</span>
                    </div>

                    <div v-if="deck.externalRating != 0" class="flex justify-between flex-wrap stat-row">
                      <Tooltip content="Score based on user ratings from 3rd party websites, such as AniList, TMDB, VNDB or IGDB.">
                        <span class="text-gray-600 dark:text-gray-300 truncate pr-2 font-medium">External Rating</span>
                      </Tooltip>
                      <span class="tabular-nums font-semibold">{{ deck.externalRating }} %</span>
                    </div>

                    <div v-if="deck.selectedWordOccurrences != 0" class="flex justify-between flex-wrap stat-row">
                      <span class="text-gray-600 dark:text-gray-300 truncate pr-2 font-medium">Appears (times)</span>
                      <span class="tabular-nums font-bold">{{ deck.selectedWordOccurrences.toLocaleString() }}</span>
                    </div>
                  </div>
                </div>

                <div class="mt-2">
                  <div v-if="deck.description" class="description-container" :class="{ expanded: isDescriptionExpanded }">
                    <p class="whitespace-pre-line mb-0 text-sm">{{ deck.description }}</p>
                    <a v-if="deck.description.length > 50" href="#" class="text-primary-500 hover:text-primary-700 text-sm" @click.prevent="toggleDescription">
                      {{ isDescriptionExpanded ? 'View less' : 'View more' }}
                    </a>
                  </div>
                </div>

                <ExampleSentenceEntry v-if="deck.exampleSentence != undefined" :example-sentence="deck.exampleSentence" />

                <div v-if="deck.genres?.length || deck.tags?.length || deck.relationships?.length" class="mt-4 space-y-2">
                  <GenreTagDisplay v-if="!store.hideGenres && deck.genres?.length" :genres="deck.genres" label="Genres" />
                  <GenreTagDisplay v-if="!store.hideTags && deck.tags?.length" :tags="deck.tags" label="Tags" />
                  <RelatedMediaDisplay v-if="!store.hideRelations && deck.relationships?.length" :relationships="deck.relationships" />
                </div>

                <div v-if="sortedLinks.length" class="mt-4 flex flex-col md:flex-row gap-4">
                  <a v-for="link in sortedLinks" :key="link.url" :href="link.url" target="_blank">{{ getLinkTypeText(link.linkType) }}</a>
                </div>
                <div v-if="!hideControl" class="mt-4">
                  <div class="flex flex-col md:flex-row gap-2" :class="{ 'justify-center': isCompact }">
                    <Tooltip content="Details">
                      <Button
                        as="router-link"
                        :to="`/decks/media/${deck.deckId}/detail`"
                        :label="isCompact ? undefined : 'Details'"
                        class="text-center"
                        icon="pi pi-eye"
                      />
                    </Tooltip>
                    <Tooltip content="Vocabulary">
                      <Button
                        as="router-link"
                        :to="`/decks/media/${deck.deckId}/vocabulary`"
                        :label="isCompact ? undefined : 'Vocabulary'"
                        class="text-center"
                        icon="pi pi-book"
                      />
                    </Tooltip>
                    <Tooltip content="Download deck">
                      <Button
                        :label="isCompact ? undefined : 'Download deck'"
                        class="text-center"
                        icon="pi pi-download"
                        @click="showDownloadDialog = true"
                      />
                    </Tooltip>
                    <Button
                      v-if="!isCompact && displayAdminFunctions"
                      as="router-link"
                      :to="`/dashboard/media/${deck.deckId}`"
                      label="Edit"
                      icon="pi pi-pencil"
                      class="text-center"
                    />
                    <Button
                      v-if="!isCompact && authStore.isAuthenticated"
                      label="Report an issue"
                      icon="pi pi-exclamation-triangle"
                      class="text-center"
                      @click="showIssueDialog = true"
                    />
                  </div>
                </div>
              </div>
            </div>
          </div>
        </div>
      </template>
    </Card>

    <MediaDeckDownloadDialog :deck="deck" :visible="showDownloadDialog" @update:visible="showDownloadDialog = $event" />
    <ReportIssueDialog :visible="showIssueDialog" @update:visible="showIssueDialog = $event" :deck="deck" />

    <TieredMenu ref="menu" :model="menuItems" popup />
  </div>
</template>

<style scoped>
  .description-container:not(.expanded) p {
    display: -webkit-box;
    line-clamp: 2;
    -webkit-line-clamp: 2;
    -webkit-box-orient: vertical;
    overflow: hidden;
    text-overflow: ellipsis;
  }

  /* Ensure text wraps properly on small screens */
  .flex-1 {
    min-width: 0;
  }

  @media (max-width: 768px) {
    .description-container:not(.expanded) p {
      line-clamp: 4;
      -webkit-line-clamp: 4;
    }
  }

  .description-container.expanded p {
    white-space: pre-line;
  }

  /* Add additional responsive behavior for small screens */
  @media (max-width: 640px) {
    .flex-1 > div > div {
      width: 100%;
    }
  }

  /* Style for stat rows */
  .stat-row {
    padding: 0.2rem;
    border-radius: 3px;
    transition: background-color 0.2s;
  }

  .stat-row:hover {
    background-color: rgba(183, 135, 243, 0.21);
  }

  :deep(.dark) .stat-row:hover {
    background-color: rgba(255, 255, 255, 0.05);
  }

  .ignore-overlay {
    animation: fadeIn 0.3s ease-in-out;
  }

  @keyframes fadeIn {
    from {
      opacity: 0;
    }
    to {
      opacity: 1;
    }
  }
</style>
