<script setup lang="ts">
  import { DisplayStyle } from '~/types';
  import { useDisplayStyleStore } from '~/stores/displayStyleStore';

  const displayStyleStore = useDisplayStyleStore();
  const displayStyle = computed(() => displayStyleStore.displayStyle);

  const setDisplayStyle = (style: DisplayStyle) => {
    displayStyleStore.displayStyle = style;
  };
</script>

<template>
  <div class="flex gap-2">
    <Button
      v-tooltip="'Card View'"
      :class="{ 'p-button-outlined': displayStyle !== DisplayStyle.Card }"
      @click="setDisplayStyle(DisplayStyle.Card)"
    >
      <Icon name="material-symbols:view-agenda-outline" size="1.25em" />
    </Button>
    <Button
      v-tooltip="'Compact View'"
      :class="{ 'p-button-outlined': displayStyle !== DisplayStyle.Compact }"
      @click="setDisplayStyle(DisplayStyle.Compact)"
    >
      <Icon name="material-symbols:grid-view" size="1.25em" />
    </Button>
    <Button
      v-tooltip="'Table View'"
      :class="{ 'p-button-outlined': displayStyle !== DisplayStyle.Table }"
      @click="setDisplayStyle(DisplayStyle.Table)"
    >
      <Icon name="material-symbols:table-rows" size="1.25em" />
    </Button>
  </div>
</template>

<style scoped></style>
