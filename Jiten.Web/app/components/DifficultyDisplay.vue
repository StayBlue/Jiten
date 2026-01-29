<script setup lang="ts">
  import { computed } from 'vue';
  import { useJitenStore } from '~/stores/jitenStore';
  import { DifficultyDisplayStyle, DifficultyValueDisplayStyle } from '~/types';
  import { formatDifficultyValue } from '~/utils/difficultyColours';

  const props = defineProps<{
    difficulty: number;
    difficultyRaw: number;
  }>();

  const store = useJitenStore();

  const nameValues = ['Beginner', 'Easy', 'Average', 'Hard', 'Expert', 'Insane'];

  // Map difficulty to color classes (light and dark)
  const colorClasses = [
    'text-green-700 dark:text-green-300',
    'text-green-500 dark:text-green-200',
    'text-yellow-600 dark:text-yellow-300',
    'text-amber-600 dark:text-amber-300',
    'text-red-600 dark:text-red-300',
    'text-red-600 dark:text-red-300',
  ] as const;

  const usePercentage = computed(() => store.difficultyValueDisplayStyle === DifficultyValueDisplayStyle.Percentage);

  const difficultyValue = computed(() => formatDifficultyValue(props.difficultyRaw, usePercentage.value, 1));

  const difficultyText = computed(() => {
    switch (store.difficultyDisplayStyle) {
      case DifficultyDisplayStyle.Name:
        return nameValues[props.difficulty];

      case DifficultyDisplayStyle.Value:
        return difficultyValue.value;

      case DifficultyDisplayStyle.NameAndValue:
        return `${nameValues[props.difficulty]} (${difficultyValue.value})`;

      default:
        return '';
    }
  });

  const tooltip = computed(() => {
    switch (store.difficultyDisplayStyle) {
      case DifficultyDisplayStyle.Name:
        return difficultyValue.value;

      case DifficultyDisplayStyle.Value:
        return nameValues[props.difficulty];

      case DifficultyDisplayStyle.NameAndValue:
        return '';

      default:
        return '';
    }
  });

  // Compute class for the current difficulty (clamped to valid range)
  const difficultyClass = computed(() => {
    const index = Math.min(Math.max(props.difficulty, 0), colorClasses.length - 1);
    return colorClasses[index];
  });
</script>

<template>
  <Tooltip :content="tooltip">
    <span :class="['tabular-nums font-bold', difficultyClass]">
      {{ difficultyText }}
    </span>
  </Tooltip>
</template>

<style scoped></style>
