<script setup lang="ts">
  import { useRoute, useRouter } from 'vue-router';
  import { stripRuby } from '~/utils/stripRuby';
  import type { Word } from '~/types/types';

  const route = useRoute();
  const router = useRouter();

  const wordId = ref(Number(route.params.wordId) || 0);
  const readingIndex = ref(Number(route.params.readingIndex) || 0);

  const url = computed(() => `vocabulary/${wordId.value}/${readingIndex.value}`);
  const { data: wordData } = await useApiFetch<Word>(url);

  const title = computed(() => {
    if (wordData.value?.mainReading?.text) {
      return stripRuby(wordData.value.mainReading.text);
    }
    return 'Word';
  });

  const onReadingSelected = (newIndex: number) => {
    readingIndex.value = newIndex;
    router.replace(
      {
        path: `/vocabulary/${wordId.value}/${readingIndex.value}`,
      },
      false
    );
  };

  useHead(() => {
    return {
      title: `${title.value} - Jiten`,
      meta: [
        {
          name: 'description',
          content: `Detail for the word ${title.value}`
        }]
    };
  });

  if (import.meta.server) {
    defineOgImageComponent('VocabularyWordOgImage', {
      wordId: String(wordId.value),
      readingIndex: String(readingIndex.value),
    });
  }
</script>

<template>
  <VocabularyDetail
    :word-id="wordId"
    :reading-index="readingIndex"
    @reading-selected="onReadingSelected"
  />
</template>
