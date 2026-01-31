<script setup lang="ts">
  const props = defineProps<{
    sortByOptions: { label: string; value: string }[];
    sortBy: string;
    sortDescending: boolean;
    displayFilter: string;
    showDisplayFilter?: boolean;
    sortByWidth?: string;
  }>();

  const emit = defineEmits<{
    'update:sortBy': [value: string];
    'update:sortDescending': [value: boolean];
    'update:displayFilter': [value: string];
  }>();

  const displayOptions = [
    { label: 'All', value: 'all' },
    { label: 'In My List', value: 'known' },
    { label: 'Only Young', value: 'young' },
    { label: 'Only Mature', value: 'mature' },
    { label: 'Only Mastered', value: 'mastered' },
    { label: 'Only Blacklisted', value: 'blacklisted' },
    { label: 'Only Unknown', value: 'unknown' },
  ];

  const sortByModel = computed({
    get: () => props.sortBy,
    set: (v) => emit('update:sortBy', v),
  });

  const displayModel = computed({
    get: () => props.displayFilter,
    set: (v) => emit('update:displayFilter', v),
  });

  const sortByWidthClass = computed(() => props.sortByWidth ?? 'md:w-56');
</script>

<template>
  <div class="flex flex-col md:flex-row gap-2 w-full">
    <div class="flex gap-2">
      <FloatLabel variant="on">
        <Select
          v-model="sortByModel"
          :options="sortByOptions"
          option-label="label"
          option-value="value"
          placeholder="Sort by"
          input-id="sortBy"
          :class="['w-full', sortByWidthClass]"
        />
        <label for="sortBy">Sort by</label>
      </FloatLabel>
      <Button @click="emit('update:sortDescending', !sortDescending)" class="min-w-12 w-12">
        <Icon v-if="sortDescending" name="mingcute:az-sort-descending-letters-line" size="1.25em" />
        <Icon v-else name="mingcute:az-sort-ascending-letters-line" size="1.25em" />
      </Button>
    </div>
    <slot />
    <div v-if="showDisplayFilter">
      <FloatLabel variant="on">
        <Select
          v-model="displayModel"
          :options="displayOptions"
          option-label="label"
          option-value="value"
          placeholder="display"
          input-id="display"
          class="w-full md:w-56"
          scroll-height="30vh"
        />
        <label for="display">Display</label>
      </FloatLabel>
    </div>
  </div>
</template>
