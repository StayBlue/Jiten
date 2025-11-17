<script async setup lang="ts">
  import Button from 'primevue/button';
  import { KnownState, type Word } from '~/types';
  import { useAuthStore } from '~/stores/authStore';

  const { $api } = useNuxtApp();
  const auth = useAuthStore();

  const props = defineProps<{
    word: Word;
  }>();

  const toggleWordKnown = async () => {
    if (props.word.knownState == KnownState.Mature || props.word.knownState == KnownState.Young) {
      await $api<boolean>(`user/vocabulary/remove/${props.word.wordId}/${props.word.mainReading.readingIndex}`, {
        method: 'POST',
      });

      props.word.knownState = KnownState.Unknown;
    } else {
      await $api<boolean>(`user/vocabulary/add/${props.word.wordId}/${props.word.mainReading.readingIndex}`, {
        method: 'POST',
      });

      props.word.knownState = KnownState.Mature;
    }
  };
</script>

<template>
  <ClientOnly>
    <span class="inline-flex items-center gap-1">
      <template v-if="auth.isAuthenticated">
        <template v-if="word.knownState == KnownState.Mature">
          <span class="text-green-600 dark:text-green-300">Mature</span>
          <Button icon="pi pi-minus" size="small" text severity="danger" @click="toggleWordKnown" />
          <span aria-hidden="true">|</span>
        </template>
        <template v-else-if="word.knownState == KnownState.Young">
          <span class="text-yellow-600 dark:text-yellow-300">Young</span>
          <Button icon="pi pi-minus" size="small" text severity="danger" @click="toggleWordKnown" />
          <span aria-hidden="true">|</span>
        </template>
        <template v-else-if="word.knownState == KnownState.Blacklisted">
          <span class="text-gray-600 dark:text-gray-300">Blacklisted</span>
          <span aria-hidden="true">|</span>
        </template>
        <template v-else>
          <Button icon="pi pi-plus" size="small" text severity="success" @click="toggleWordKnown" />
          <span aria-hidden="true">|</span>
        </template>
      </template>
    </span>
    <template #fallback>
      <span class="inline-flex items-center gap-1" aria-hidden="true"></span>
    </template>
  </ClientOnly>
</template>
