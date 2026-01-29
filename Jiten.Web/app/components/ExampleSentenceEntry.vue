<script setup lang="ts">
  import type { ExampleSentence } from '~/types';
  import { computed } from 'vue';

  const props = defineProps<{
    exampleSentence: ExampleSentence;
    showSource?: boolean;
  }>();

  const store = useJitenStore();
  const isNsfw = isTextNsfw(props.exampleSentence.text);
  const isRevealed = computed({
    get: () => store.displayAllNsfw,
    set: (value) => {
      store.displayAllNsfw = value;
    },
  });

  const formattedText = computed(() => {
    const { text, wordPosition, wordLength } = props.exampleSentence;
    if (wordPosition < 0 || wordLength <= 0 || wordPosition >= text.length) {
      return text;
    }

    const before = text.substring(0, wordPosition).trim();
    const bold = text.substring(wordPosition, wordPosition + wordLength);
    const after = text.substring(wordPosition + wordLength).trim();

    return before + '<span class="text-primary-500 dark:text-primary-500 font-bold">' + bold + '</span>' + after;
  });

  const handleReveal = () => {
    if (isNsfw && !isRevealed.value) {
      isRevealed.value = true;
    }
  };
</script>

<template>
  <div class="flex flex-col">
    <blockquote class="relative inline-block border-l-4 border-primary-500 pl-5 pr-3 py-3 bg-gray-50 dark:bg-gray-900 rounded-r shadow-sm overflow-hidden">
      <div v-html="formattedText" class="md:text-lg text-sm transition-filter duration-200" :class="{ 'blur-sm': isNsfw && !isRevealed }" @click="handleReveal"></div>
      <div v-if="isNsfw && !isRevealed" class="absolute top-0 left-0 w-full h-full flex items-center justify-center cursor-pointer z-10" @click="handleReveal">
        <div class="text-center px-3 py-2 bg-white/80 backdrop-blur-md border border-red-300 text-red-600 text-sm font-semibold rounded shadow">
          This text is potentially not safe for work. Click to reveal.
        </div>
      </div>
    </blockquote>
    <div v-if="showSource" class="flex items-center mb-2">
      <span class="text-xs italic mr-2 ml-4">Source:</span>
      <div class="inline-flex items-center text-xs flex-wrap">
        <NuxtLink
          v-if="exampleSentence.sourceDeckParent != null"
          :to="`/decks/media/${exampleSentence.sourceDeckParent.deckId}/detail`"
          target="_blank"
          class="hover:underline text-primary-600"
        >
          {{ localiseTitle(exampleSentence.sourceDeckParent) }}
        </NuxtLink>
        <span v-if="exampleSentence.sourceDeckParent != null" class="mx-1">-</span>
        <NuxtLink
          v-if="exampleSentence.sourceDeck != null"
          :to="`/decks/media/${exampleSentence.sourceDeck.deckId}/detail`"
          target="_blank"
          class="hover:underline text-primary-600"
        >
          {{ localiseTitle(exampleSentence.sourceDeck) }}
        </NuxtLink>
        &nbsp;
        ({{getMediaTypeText(exampleSentence.sourceDeck.mediaType)}})
      </div>
    </div>
  </div>
</template>

<style scoped></style>
