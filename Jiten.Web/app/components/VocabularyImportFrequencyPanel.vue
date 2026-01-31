<script setup lang="ts">
  const emit = defineEmits<{ changed: [] }>();

  const { $api } = useNuxtApp();
  const toast = useToast();

  const frequencyRange = ref([0, 100]);

  const updateMinFrequency = (value: number) => {
    frequencyRange.value = [value, frequencyRange.value[1]];
  };

  const updateMaxFrequency = (value: number) => {
    frequencyRange.value = [frequencyRange.value[0], value];
  };

  async function getVocabularyByFrequency() {
    const data = await $api<{ words: number; forms: number; skipped: number }>(
      `user/vocabulary/import-from-frequency/${frequencyRange.value[0]}/${frequencyRange.value[1]}`,
      { method: 'POST' },
    );
    toast.add({ severity: 'success', detail: `Added ${data.words} words, ${data.forms} forms by frequency range.`, life: 5000 });
    await nextTick();
    emit('changed');
  }
</script>

<template>
  <Card>
    <template #title>
      <h3 class="text-lg font-semibold">Add Words by Frequency Range</h3>
    </template>
    <template #content>
      <div class="flex flex-col gap-4">
        <div class="flex flex-row flex-wrap gap-2 items-center">
          <InputNumber
            :model-value="frequencyRange[0]"
            show-buttons
            fluid
            size="small"
            class="max-w-30 flex-shrink-0"
            @update:model-value="updateMinFrequency"
          />
          <Slider v-model="frequencyRange" range :min="0" :max="10000" class="flex-grow mx-2 flex-basis-auto" />
          <InputNumber
            :model-value="frequencyRange[1]"
            show-buttons
            fluid
            size="small"
            class="max-w-30 flex-shrink-0"
            @update:model-value="updateMaxFrequency"
          />
        </div>
        <Button icon="pi pi-plus" label="Add Words by Frequency" class="w-full md:w-auto" @click="getVocabularyByFrequency" />
      </div>
    </template>
  </Card>
</template>
