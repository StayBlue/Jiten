<script setup lang="ts">
  import type { Definition } from '~/types';

  const props = defineProps<{
    definitions: Definition[];
    isCompact: boolean;
  }>();

  const definitions = unref(props.definitions);
  const store = useJitenStore();
  const hideDefinition = computed({
    get: () => store.hideVocabularyDefinitions,
    set: (value) => {
      store.hideVocabularyDefinitions = value;
    },
  });

  const definitionsWithPartsOfSpeech = computed(() => {
    if (!Array.isArray(definitions)) {
      return [];
    }
    let previousPartOfSpeech = null;

    return definitions.map((definition) => {
      const isDifferentPartOfSpeech = JSON.stringify(previousPartOfSpeech) !== JSON.stringify(definition.partsOfSpeech);
      previousPartOfSpeech = definition.partsOfSpeech;
      return {
        ...definition,
        isDifferentPartOfSpeech,
      };
    });
  });
</script>

<template>
  <div v-if="!isCompact">
    <ul>
      <li v-for="definition in definitionsWithPartsOfSpeech" :key="definition.index">
        <div v-if="definition.isDifferentPartOfSpeech" class="font-bold">{{ definition.partsOfSpeech.join(', ') }}</div>
        <span class="text-gray-400">{{ definition.index }}.</span> {{ definition.meanings.join('; ') }}
      </li>
    </ul>
  </div>

  <div v-if="isCompact && !hideDefinition">
    <span v-for="definition in definitionsWithPartsOfSpeech.slice(0, 10)" :key="definition.index">
      {{ definition.meanings.join('; ') }}
      <span v-if="definition.index !== Math.min(definitionsWithPartsOfSpeech.length, 10)">; </span>
    </span>
  </div>
</template>

<style scoped></style>
