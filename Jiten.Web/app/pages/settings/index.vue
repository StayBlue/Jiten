<script setup lang="ts">
  import SettingsCoverage from '~/components/SettingsCoverage.vue';
  import SettingsApiKey from '~/components/SettingsApiKey.vue';
  import SettingsWordSets from '~/components/SettingsWordSets.vue';

  definePageMeta({
    middleware: ['auth'],
  });

  const { vocabStatsLoading, totalWordsAmount, fetchKnownWordsAmount } = useVocabularyStats();

  onMounted(() => {
    fetchKnownWordsAmount();
  });
</script>

<template>
  <div class="container mx-auto p-2 md:p-4">
    <SettingsCoverage class="mb-4" />

    <Card class="mb-4">
      <template #title>
        <h3 class="text-lg font-semibold">Vocabulary</h3>
      </template>
      <template #content>
        <p class="text-gray-600 dark:text-gray-300 mb-3">
          View your current known vocabulary. Import known words from AnkiConnect, JPDB, Anki text exports, or by frequency range. Export your word list, or back up your complete vocabulary
          including review history.
        </p>
        <p v-if="!vocabStatsLoading && totalWordsAmount > 0" class="mb-3 text-muted-color">
          You have <span class="font-extrabold text-primary-600 dark:text-primary-300">{{ totalWordsAmount }}</span> tracked word{{ totalWordsAmount === 1 ? '' : 's' }}.
        </p>
        <NuxtLink to="/settings/vocabulary">
          <Button icon="pi pi-cog" label="Manage Vocabulary" class="w-full md:w-auto" />
        </NuxtLink>
      </template>
    </Card>

    <SettingsWordSets class="mb-4" />

    <SettingsApiKey />
  </div>
</template>

<style scoped></style>
