<script setup lang="ts">
  import type { DictionaryEntry } from '~/types';

  defineProps<{
    entry: DictionaryEntry;
  }>();

  const convertToRuby = useConvertToRuby();
  const expanded = ref(false);
  const hasBeenExpanded = ref(false);
  const animateOpen = ref(false);

  watch(expanded, (val) => {
    if (val && !hasBeenExpanded.value) {
      hasBeenExpanded.value = true;
      nextTick(() => {
        requestAnimationFrame(() => {
          animateOpen.value = true;
        });
      });
    } else {
      animateOpen.value = val;
    }
  });
</script>

<template>
  <div
    class="rounded-lg overflow-hidden transition-all duration-200 bg-white dark:bg-gray-800/60 border"
    :class="expanded
      ? 'shadow-md ring-1 ring-purple-200 dark:ring-purple-900/50 border-purple-200 dark:border-purple-900/50'
      : 'shadow-sm hover:shadow-md border-gray-200 dark:border-gray-700'"
  >
    <div class="cursor-pointer select-none" @click="expanded = !expanded">
      <div class="flex items-center gap-3 px-4 pt-2.5">
        <div class="flex-1 min-w-0 flex items-center gap-3 flex-wrap">
          <span v-if="entry.primaryKanjiText" class="text-lg font-noto-sans font-medium" v-html="convertToRuby(entry.primaryKanjiText)" />
          <span
            v-if="entry.primaryKanjiText"
            class="text-sm text-gray-500 dark:text-gray-400 font-noto-sans"
          >{{ entry.text }}</span>
          <span v-else class="text-lg font-noto-sans font-medium" v-html="convertToRuby(entry.text)" />
          <span
            v-for="pos in entry.partsOfSpeech.slice(0, 2)"
            :key="pos"
            class="inline-block rounded-full px-2 py-0.5 text-xs bg-purple-100 text-purple-700 dark:bg-purple-900/30 dark:text-purple-300"
          >
            {{ pos }}
          </span>
        </div>
        <div class="text-xs text-gray-400 dark:text-gray-500 shrink-0">
          <span v-if="entry.frequencyRank < 2147483647">#{{ entry.frequencyRank.toLocaleString() }}</span>
        </div>
      </div>

      <div v-if="!animateOpen" class="px-4 mt-0.5">
        <span class="text-sm text-gray-600 dark:text-gray-400 line-clamp-1">
          {{ entry.meanings.join('; ') }}
        </span>
      </div>

      <div class="flex justify-center pt-0.5 pb-1">
        <Icon
          name="material-symbols:keyboard-arrow-down"
          class="text-gray-400 dark:text-gray-600 text-xl transition-transform duration-200"
          :class="expanded ? 'rotate-180' : ''"
        />
      </div>
    </div>

    <div class="expand-grid" :class="{ 'expand-grid--open': animateOpen }">
      <div class="expand-grid__inner">
        <div v-if="hasBeenExpanded" class="border-t border-gray-200 dark:border-gray-700">
          <VocabularyDetail
            :word-id="entry.wordId"
            :reading-index="entry.readingIndex"
            :show-redirect="true"
          />
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.expand-grid {
  display: grid;
  grid-template-rows: 0fr;
  transition: grid-template-rows 0.3s ease-in-out;
}

.expand-grid--open {
  grid-template-rows: 1fr;
}

.expand-grid__inner {
  overflow: hidden;
  min-height: 0;
}
</style>
