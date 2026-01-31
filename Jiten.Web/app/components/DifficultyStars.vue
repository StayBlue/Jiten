<script setup lang="ts">
  defineProps<{
    difficulty: number;
  }>();
</script>

<template>
  <span v-if="difficulty == 0" class="tabular-nums text-green-700 dark:text-green-300">★☆☆☆☆</span>
  <span v-else-if="difficulty == 1" class="tabular-nums text-green-500">★★☆☆☆</span>
  <span v-else-if="difficulty == 2" class="tabular-nums text-cyan-500">★★★☆☆</span>
  <span v-else-if="difficulty == 3" class="tabular-nums text-amber-600">★★★★☆</span>
  <span v-else-if="difficulty == 4" class="tabular-nums text-orange-600">★★★★★</span>
  <span v-else-if="difficulty == 5" class="tabular-nums text-red-600">★★★★★</span>
</template>
