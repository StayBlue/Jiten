<script setup lang="ts">
  import type { Kanji, WordSummary } from '~/types';

  const route = useRoute();
  const convertToRuby = useConvertToRuby();

  const character = computed(() => {
    const c = route.params.character;
    return typeof c === 'string' ? c : c[0];
  });

  const { data: kanji, status } = useApiFetch<Kanji>(() => `kanji/${encodeURIComponent(character.value)}`);

  const jlptText = computed(() => {
    if (!kanji.value?.jlptLevel) return null;
    return `N${kanji.value.jlptLevel}`;
  });

  const gradeText = computed(() => {
    if (!kanji.value?.grade) return null;
    if (kanji.value.grade <= 6) return `Grade ${kanji.value.grade}`;
    if (kanji.value.grade === 8) return 'Secondary';
    return `Grade ${kanji.value.grade}`;
  });

  useHead(() => {
    return {
      title: `${character.value} - Kanji`,
      meta: [
        {
          name: 'description',
          content: kanji.value?.meanings.join(', ') || `Details for kanji ${character.value}`,
        },
      ],
    };
  });
</script>

<template>
  <div class="max-w-4xl mx-auto px-4 py-8">
    <div v-if="status === 'pending'" class="flex justify-center py-12">
      <ProgressSpinner />
    </div>

    <div v-else-if="status === 'error'" class="text-center py-12">
      <p class="text-surface-500">Kanji not found</p>
    </div>

    <div v-else-if="kanji" class="space-y-2">
      <!-- Main kanji display -->
      <div class="text-center">
        <div class="text-9xl font-bold mb-4" lang="ja">{{ kanji.character }}</div>

        <!-- Metadata badges -->
        <div class="flex flex-wrap justify-center gap-2 mb-4">
          <Tag v-if="kanji.frequencyRank" severity="primary"> Jiten frequency #{{ kanji.frequencyRank }} </Tag>
          <Tag v-if="jlptText" severity="info"> JLPT {{ jlptText }} </Tag>
          <Tag v-if="gradeText" severity="secondary">
            {{ gradeText }}
          </Tag>
          <Tag severity="contrast"> {{ kanji.strokeCount }} strokes </Tag>
        </div>
      </div>

      <!-- Meanings -->
      <div class="border-surface-200 dark:border-surface-700 border rounded-lg p-4">
        <h2 class="text-lg font-semibold mb-2">Meanings</h2>
        <p class="text-surface-700 dark:text-surface-300">
          {{ kanji.meanings.join(', ') }}
        </p>
      </div>

      <!-- Readings -->
      <div class="grid grid-cols-1 md:grid-cols-2 gap-4">
        <div v-if="kanji.onReadings.length > 0" class="border-surface-200 dark:border-surface-700 border rounded-lg p-4">
          <h2 class="text-lg font-semibold mb-2">On'yomi</h2>
          <div class="flex flex-wrap gap-2">
            <Tag v-for="reading in kanji.onReadings" :key="reading" severity="primary" :value="reading" />
          </div>
        </div>

        <div v-if="kanji.kunReadings.length > 0" class="border-surface-200 dark:border-surface-700 border rounded-lg p-4">
          <h2 class="text-lg font-semibold mb-2">Kun'yomi</h2>
          <div class="flex flex-wrap gap-2">
            <Tag v-for="reading in kanji.kunReadings" :key="reading" severity="secondary" :value="reading" />
          </div>
        </div>
      </div>

      <!-- Most common words -->
      <div v-if="kanji.topWords && kanji.topWords.length > 0" class="border-surface-200 dark:border-surface-700 border rounded-lg p-4">
        <h2 class="text-lg font-semibold mb-4">Most common words using this kanji</h2>
        <div class="space-y-1">
          <NuxtLink
            v-for="word in kanji.topWords"
            :key="`${word.wordId}-${word.readingIndex}`"
            :to="`/vocabulary/${word.wordId}/${word.readingIndex}`"
            class="grid grid-cols-[minmax(8rem,auto)_1fr_auto] items-baseline gap-x-4 p-2 rounded-lg hover:bg-surface-100 dark:hover:bg-surface-800 transition-colors"
          >
            <span class="text-xl font-medium" lang="ja" v-html="convertToRuby(word.readingFurigana)" />
            <span v-if="word.mainDefinition" class="text-surface-600 dark:text-surface-400 text-sm truncate">
              {{ word.mainDefinition }}
            </span>
            <span v-else />
            <Tag v-if="word.frequencyRank" severity="secondary" class="text-xs shrink-0"> #{{ word.frequencyRank }} </Tag>
          </NuxtLink>
        </div>
      </div>
    </div>
  </div>
</template>
