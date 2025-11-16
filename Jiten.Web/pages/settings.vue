<script setup lang="ts">
  import SettingsVocabularyImport from '~/components/SettingsVocabularyImport.vue';
  import SettingsCoverage from '~/components/SettingsCoverage.vue';
  import SettingsApiKey from '~/components/SettingsApiKey.vue';

  definePageMeta({
    middleware: ['auth'],
  });
</script>

<template>
  <div class="container mx-auto p-2 md:p-4">
    <SettingsCoverage class="mb-4" />
    <SettingsVocabularyImport class="mb-4" />
    <SettingsApiKey />
  </div>
</template>

<style scoped></style>
