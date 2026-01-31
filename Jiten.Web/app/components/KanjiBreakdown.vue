<script setup lang="ts">
  import type { KanjiList } from '~/types';

  const props = defineProps({
    wordId: {
      type: Number,
      required: true,
    },
    readingIndex: {
      type: Number,
      required: true,
    },
  });

  const { data: kanjis, status } = useApiFetch<KanjiList[]>(
    () => `vocabulary/${props.wordId}/${props.readingIndex}/kanji`
  );

  const jlptText = (level: number | null) => {
    if (!level) return null;
    return `N${level}`;
  };
</script>

<template>
  <div v-if="status === 'success' && kanjis && kanjis.length > 0" class="mt-4">
    <h3 class="text-gray-500 dark:text-gray-300 font-noto-sans text-sm mb-2">Kanji breakdown</h3>
    <div class="flex flex-wrap gap-2">
      <NuxtLink
        v-for="kanji in kanjis"
        :key="kanji.character"
        :to="`/kanji/${kanji.character}`"
        class="group relative inline-flex items-center gap-2 px-3 py-2 rounded-lg border border-surface-200 dark:border-surface-700 hover:border-primary-500 dark:hover:border-primary-400 hover:bg-surface-50 dark:hover:bg-surface-800 transition-all"
      >
        <span class="text-2xl font-medium" lang="ja">{{ kanji.character }}</span>
        <div class="flex flex-col text-xs">
          <span class="text-surface-600 dark:text-surface-400">
            {{ kanji.strokeCount }} strokes
          </span>
          <span v-if="kanji.jlptLevel" class="text-primary-600 dark:text-primary-400">
           JLPT {{ jlptText(kanji.jlptLevel) }}
          </span>
        </div>

        <!-- Tooltip with meanings on hover -->
        <div class="absolute bottom-full left-1/2 -translate-x-1/2 mb-2 px-3 py-2 bg-surface-800 dark:bg-surface-100 text-white dark:text-surface-900 text-xs rounded-lg opacity-0 group-hover:opacity-100 transition-opacity whitespace-nowrap pointer-events-none z-10">
          {{ kanji.meanings.slice(0, 3).join(', ') }}
          <div class="absolute top-full left-1/2 -translate-x-1/2 border-4 border-transparent border-t-surface-800 dark:border-t-surface-100"></div>
        </div>
      </NuxtLink>
    </div>
  </div>
</template>
