<script setup lang="ts">
  import { useRoute } from '#vue-router';
  import type { Deck } from '~/types';
  import { getMediaTypeText } from '~/utils/mediaTypeMapper';

  const route = useRoute();
  const mediaType = Number(route.params.mediaType);
  const url = computed(() => `media-deck/get-media-decks-by-type/${mediaType}`);

  const localiseTitle = useLocaliseTitle();

  const { data: response, status, error } = await useApiFetch<Deck>(url.value, {});
</script>

<template>
  <Card v-if="response">
    <template #title> List of decks for {{ getMediaTypeText(mediaType) }}</template>
    <template #content>
      <ul>
        <li v-for="deck in response" :key="deck.deckId">
          <NuxtLink :to="`/decks/media/${deck.deckId}/detail`" target="_blank">{{ localiseTitle(deck) }}</NuxtLink>
        </li>
      </ul>
    </template>
  </Card>
</template>

<style scoped></style>
