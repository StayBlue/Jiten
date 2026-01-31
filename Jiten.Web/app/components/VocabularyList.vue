<script setup lang="ts">
  import type { Word } from '~/types/types';
  import type { AsyncDataRequestStatus } from '#app';

  defineProps<{
    words: Word[];
    status: AsyncDataRequestStatus;
    error?: Error | string | null;
    emptyMessage?: string;
    skeletonCount?: number;
  }>();
</script>

<template>
  <div v-if="status === 'pending'" class="flex flex-col gap-2">
    <Card v-for="i in (skeletonCount ?? 10)" :key="i" class="p-2">
      <template #content>
        <Skeleton width="100%" height="50px" />
      </template>
    </Card>
  </div>

  <div v-else-if="error">
    <slot name="error" :error="error">
      <Message severity="error">Failed to load vocabulary</Message>
    </slot>
  </div>

  <div v-else-if="words.length === 0 && emptyMessage" class="text-center py-8">
    <Message severity="info">{{ emptyMessage }}</Message>
  </div>

  <div v-else class="flex flex-col gap-2">
    <VocabularyEntry
      v-for="word in words"
      :key="`${word.wordId}-${word.mainReading.readingIndex}`"
      :word="word"
      :is-compact="true"
    />
  </div>
</template>
