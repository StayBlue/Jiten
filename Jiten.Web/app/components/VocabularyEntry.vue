<script setup lang="ts">
  import type { Word } from '~/types';
  import Card from 'primevue/card';
  import Button from 'primevue/button';
  import VocabularyDefinitions from '~/components/VocabularyDefinitions.vue';
  import { useJitenStore } from '~/stores/jitenStore';
  import VocabularyStatus from '~/components/VocabularyStatus.vue';

  const props = defineProps<{
    word: Word;
    isCompact: boolean;
  }>();

  const convertToRuby = useConvertToRuby();
  const isCompact = ref(props.isCompact);

  const toggleCompact = () => {
    isCompact.value = !isCompact.value;
  };
</script>

<template>
  <Card>
    <template #title>
      <div class="flex justify-between">
        <div class="flex flex-row md:gap-4 flex-wrap">
          <router-link class="text-2xl" :to="`/vocabulary/${word.wordId}/${word.mainReading.readingIndex}`" v-html="convertToRuby(word.mainReading.text)" />
          <Button text @click="toggleCompact">{{ isCompact ? 'Expand' : 'Compact' }}</Button>
        </div>
        <div class="text-gray-500 dark:text-gray-300 text-sm text-right">
          <VocabularyStatus :word="word" />
          x{{ word.occurrences }} | Rank #{{ word.mainReading.frequencyRank.toLocaleString() }}
        </div>
      </div>
    </template>
    <template #subtitle />
    <template #content>
      <VocabularyDefinitions :definitions="word.definitions" :is-compact="isCompact" />
    </template>
  </Card>
</template>

<style scoped></style>
