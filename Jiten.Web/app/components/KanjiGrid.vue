<script setup lang="ts">
  import type { KanjiGridResponse } from '~/types';

  const props = defineProps<{
    username: string;
  }>();

  const { $api } = useNuxtApp();

  const onlySeen = ref(false);
  const isLoading = ref(false);
  const error = ref<string | null>(null);
  const gridData = shallowRef<KanjiGridResponse | null>(null);
  const gridRef = ref<HTMLElement | null>(null);
  const manuallyLoaded = ref(false);

  // Single tooltip state (event delegation)
  const tooltip = ref({ visible: false, text: '', x: 0, y: 0 });

  const fetchKanjiGrid = async () => {
    isLoading.value = true;
    error.value = null;

    try {
      const data = await $api<KanjiGridResponse>(
        `user/profile/${props.username}/kanji-grid`,
        { query: { onlySeen: onlySeen.value } }
      );
      gridData.value = data;
    } catch (err: unknown) {
      const fetchError = err as { response?: { status?: number } };
      error.value = fetchError?.response?.status === 404
        ? 'Kanji data not available'
        : 'Failed to load kanji grid';
    } finally {
      isLoading.value = false;
    }
  };

  // Colour interpolation: gray (0) -> red (1) -> green (maxThreshold)
  const getKanjiColour = (score: number, maxThreshold: number): string => {
    if (score <= 0) return 'rgb(128,128,128)';

    if (score <= 1) {
      const t = score;
      const r = Math.round(128 + (220 - 128) * t);
      const g = Math.round(128 + (38 - 128) * t);
      const b = Math.round(128 + (38 - 128) * t);
      return `rgb(${r},${g},${b})`;
    }

    const t = Math.min((score - 1) / (maxThreshold - 1), 1);
    const r = Math.round(220 + (34 - 220) * t);
    const g = Math.round(38 + (197 - 38) * t);
    const b = Math.round(38 + (94 - 38) * t);
    return `rgb(${r},${g},${b})`;
  };

  // Render grid using raw DOM (bypasses Vue's VDOM for 12k elements)
  const renderGrid = () => {
    if (!gridRef.value || !gridData.value?.kanji) return;

    const maxThreshold = gridData.value.maxScoreThreshold;
    const html = gridData.value.kanji.map(k => {
      const bg = getKanjiColour(k.score, maxThreshold);
      const fg = k.score > 0 ? 'white' : 'inherit';
      return `<a href="/kanji/${k.character}" class="kanji-cell" style="background:${bg};color:${fg}" data-tooltip="${k.character}: Score ${k.score.toFixed(1)}, ${k.wordCount} words" lang="ja">${k.character}</a>`;
    }).join('');

    gridRef.value.innerHTML = html;
  };

  const formatDate = (dateString: string | null): string => {
    if (!dateString) return 'Never';
    return new Date(dateString).toLocaleDateString();
  };

  // Computed stats
  const seenPercentage = computed(() => {
    if (!gridData.value) return 0;
    return ((gridData.value.seenKanjiCount / gridData.value.totalKanjiCount) * 100).toFixed(1);
  });

  const masteredCount = computed(() => {
    if (!gridData.value?.kanji) return 0;
    return gridData.value.kanji.filter(k => k.score >= 10).length;
  });

  const masteredPercentage = computed(() => {
    if (!gridData.value) return 0;
    return ((masteredCount.value / gridData.value.totalKanjiCount) * 100).toFixed(1);
  });

  // Event delegation for tooltips
  const showTooltip = (e: MouseEvent) => {
    const target = e.target as HTMLElement;
    if (target.classList.contains('kanji-cell')) {
      tooltip.value = {
        visible: true,
        text: target.dataset.tooltip || '',
        x: e.clientX + 10,
        y: e.clientY + 10
      };
    }
  };

  const moveTooltip = (e: MouseEvent) => {
    if (tooltip.value.visible) {
      tooltip.value.x = e.clientX + 10;
      tooltip.value.y = e.clientY + 10;
    }
  };

  const hideTooltip = () => {
    tooltip.value.visible = false;
  };

  const loadGrid = () => {
    manuallyLoaded.value = true;
    fetchKanjiGrid();
  };

  watch(onlySeen, () => {
    if (manuallyLoaded.value) fetchKanjiGrid();
  });
  watch(gridData, () => nextTick(renderGrid));
</script>

<template>
  <div class="kanji-grid-container">
    <div v-if="!manuallyLoaded" class="flex flex-col items-center gap-2 py-4">
      <Button label="Load kanji grid" icon="pi pi-th-large" @click="loadGrid" />
      <span class="text-xs text-surface-400">Hidden by default for performance</span>
    </div>

    <div v-else-if="isLoading" class="flex justify-center py-8">
      <ProgressSpinner />
    </div>

    <div v-else-if="error" class="text-center py-4">
      <Message severity="warn">{{ error }}</Message>
    </div>

    <div v-else-if="gridData" class="flex flex-col gap-4">
      <!-- Header with stats and filter -->
      <div class="flex justify-between items-center flex-wrap gap-4">
        <div class="flex gap-4 text-sm text-surface-500 dark:text-surface-400">
          <span>{{ gridData.seenKanjiCount }} / {{ gridData.totalKanjiCount }} kanji tracked ({{ seenPercentage }}%)</span>
          <span>{{ masteredCount }} mastered ({{ masteredPercentage }}%)</span>
        </div>

        <div class="flex items-center gap-2">
          <Checkbox v-model="onlySeen" input-id="only-seen" :binary="true" />
          <label for="only-seen" class="text-sm cursor-pointer">Only show kanji in tracked words</label>
        </div>
      </div>

      <!-- Colour legend -->
      <div class="flex items-center gap-2 text-sm text-surface-600 dark:text-surface-400">
        <span class="flex items-center gap-1">
          Score:
          <Icon
            v-tooltip="'Score is based on how many words you know containing this kanji. Mature/mastered words count as 1 point, young words as 0.5 points.'"
            name="material-symbols:info-outline"
            class="text-primary-700 dark:text-primary-300 cursor-help"
            style="font-size: 1rem"
          />
        </span>
        <div class="flex items-center gap-1">
          <div class="w-4 h-4 rounded" style="background: rgb(128, 128, 128)"></div>
          <span>0</span>
        </div>
        <span class="mx-1">→</span>
        <div class="flex items-center gap-1">
          <div class="w-4 h-4 rounded" style="background: rgb(220, 38, 38)"></div>
          <span>1</span>
        </div>
        <span class="mx-1">→</span>
        <div class="flex items-center gap-1">
          <div class="w-4 h-4 rounded" style="background: rgb(34, 197, 94)"></div>
          <span>{{ gridData.maxScoreThreshold }}+</span>
        </div>
      </div>

      <!-- Single floating tooltip -->
      <Teleport to="body">
        <div
          v-show="tooltip.visible"
          class="kanji-tooltip"
          :style="{ left: tooltip.x + 'px', top: tooltip.y + 'px' }"
        >
          {{ tooltip.text }}
        </div>
      </Teleport>

      <!-- Kanji grid rendered via innerHTML (bypasses Vue's VDOM) -->
      <div
        ref="gridRef"
        class="kanji-grid"
        @mouseover="showTooltip"
        @mousemove="moveTooltip"
        @mouseleave="hideTooltip"
      ></div>
    </div>
  </div>
</template>

<style scoped>
.kanji-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(2rem, 1fr));
  gap: 2px;
  content-visibility: auto;
  contain-intrinsic-size: auto 500px;
}
</style>

<style>
.kanji-cell {
  display: flex;
  align-items: center;
  justify-content: center;
  aspect-ratio: 1;
  font-size: 1.25rem;
  font-weight: 500;
  border-radius: 4px;
  transition: transform 0.1s, box-shadow 0.1s;
  text-decoration: none;
}

.kanji-cell:hover {
  transform: scale(1.1);
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.2);
  z-index: 1;
}

.kanji-tooltip {
  position: fixed;
  z-index: 9999;
  padding: 0.5rem 0.75rem;
  background: var(--p-surface-800);
  color: var(--p-surface-0);
  border-radius: 6px;
  font-size: 0.875rem;
  pointer-events: none;
  white-space: nowrap;
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.3);
}
</style>
