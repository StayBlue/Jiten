<script setup lang="ts">
  import MediaList from '~/components/MediaList.vue';

  useHead(() => {
    return {
      title: 'Media decks',
    };
  });
</script>

<template>
  <MediaList />
</template>

<style scoped></style>
