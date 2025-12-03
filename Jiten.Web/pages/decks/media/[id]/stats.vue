<script setup lang="ts">
  import type { DeckCoverageStats, CurveDatum, DeckDetail } from '~/types';
  import { useApiFetch, useApiFetchPaginated } from '~/composables/useApiFetch';

  const route = useRoute();
  const deckId = computed(() => route.params.id as string);

  const { data: deckResponse, status: deckStatus, error: deckError } = await useApiFetchPaginated<DeckDetail>(`media-deck/${deckId.value}/detail`);

  const { data: stats, status: statsStatus, error: statsError } = await useApiFetch<DeckCoverageStats>(`media-deck/${deckId.value}/stats`);

  const { data: curveData, status: curveStatus, error: curveError } = await useApiFetch<CurveDatum[]>(`media-deck/${deckId.value}/coverage-curve?points=100`);

  const isLoading = computed(() => deckStatus.value === 'pending' || statsStatus.value === 'pending' || curveStatus.value === 'pending');

  const hasError = computed(() => deckError.value || statsError.value || curveError.value);

  const title = computed(() => {
    if (!deckResponse.value?.data) {
      return '';
    }

    let title = '';
    if (deckResponse.value.data.parentDeck != null) {
      title += localiseTitle(deckResponse.value.data.parentDeck) + ' - ';
    }

    title += localiseTitle(deckResponse.value.data.mainDeck);

    return title;
  });

  useHead(() => ({
    title: `${title.value} - Coverage Statistics`,
    meta: [
      {
        name: 'description',
        content: `Advanced statistics for ${title.value}`,
      },
    ],
  }));

  const sortedMilestones = computed(() => {
    if (!stats.value?.milestones) return [];

    return Object.entries(stats.value.milestones)
      .map(([percentage, words]) => ({
        percentage,
        words,
        sortKey: parseInt(percentage.replace('%', '')),
      }))
      .sort((a, b) => a.sortKey - b.sortKey);
  });
</script>

<template>
  <div class="flex flex-col gap-4">
    <div>
      Coverage Statistics for
      <NuxtLink :to="`/decks/media/${deckId}/detail`">
        {{ title }}
      </NuxtLink>
    </div>

    <div v-if="isLoading" class="flex flex-col gap-4">
      <Card v-for="i in 3" :key="i" class="p-2">
        <template #content>
          <Skeleton width="100%" :height="i === 2 ? '400px' : '150px'" />
        </template>
      </Card>
    </div>

    <div v-else-if="hasError" class="flex flex-col gap-4">
      <Card>
        <template #content>
          <p class="text-red-600 dark:text-red-400">Error loading coverage statistics</p>
          <p class="text-sm text-gray-500 dark:text-gray-400 mt-2">Statistics may not have been computed yet for this deck.</p>
          <div v-if="statsError" class="text-sm text-gray-600 dark:text-gray-300 mt-2">
            {{ statsError }}
          </div>
        </template>
      </Card>
    </div>

    <div v-else class="flex flex-col gap-4">
      <Card>
        <template #header>
          <h2 class="text-xl font-bold px-6 pt-6">Coverage</h2>
          <h3 class="italic text-sm px-6">This shows how much text coverage you'll have if you know the most X frequent words in that work.</h3>
        </template>
        <template #content>
          <CoverageChart class="hidden sm:block" v-if="curveData" :curve-data="curveData" />
          <div v-else class="text-center text-gray-500 py-8">No curve data available</div>

          <h2 class="text-xl font-bold pt-6">Milestones</h2>
          <div class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4 pt-2">
            <div v-for="milestone in sortedMilestones" :key="milestone.percentage">
              <div class="text-sm text-gray-500 dark:text-gray-400">{{ milestone.percentage }} Coverage</div>
              <div class="text-xl font-bold">{{ milestone.words.toLocaleString() }} words</div>
            </div>
            <div>
              <div class="text-sm text-gray-500 dark:text-gray-400">Total Words</div>
              <div class="text-2xl font-bold">{{ stats?.totalUniqueWords.toLocaleString() }}</div>
            </div>
          </div>
        </template>
      </Card>
    </div>
  </div>
</template>

<style scoped></style>
