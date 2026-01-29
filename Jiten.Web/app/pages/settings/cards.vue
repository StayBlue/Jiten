<script setup lang="ts">
import { ref, computed } from 'vue';
import { useConfirm } from 'primevue/useconfirm';
import { useToast } from 'primevue/usetoast';
import type { FsrsCardWithWordDto } from '~/types/types';
import { FsrsState } from '~/types/enums';

definePageMeta({
  middleware: ['auth'],
});

useHead({ title: 'FSRS Cards - Settings - Jiten' });

const { $api } = useNuxtApp();
const toast = useToast();
const confirm = useConfirm();
const { subscriptions, fetchSubscriptions } = useWordSets();

// Data
const cards = ref<FsrsCardWithWordDto[]>([]);
const loading = ref(true);
const globalFilter = ref('');
const selectedState = ref<FsrsState | null>(null);

// Word set context
const wordSetTotalWords = computed(() => subscriptions.value.reduce((sum, s) => sum + s.wordCount, 0));
const hasWordSetSubscriptions = computed(() => subscriptions.value.length > 0);

// State options for dropdown
const stateOptions = ref([
  { label: 'All States', value: null },
  { label: 'New', value: FsrsState.New },
  { label: 'Learning', value: FsrsState.Learning },
  { label: 'Review', value: FsrsState.Review },
  { label: 'Relearning', value: FsrsState.Relearning },
  { label: 'Blacklisted', value: FsrsState.Blacklisted },
  { label: 'Mastered', value: FsrsState.Mastered },
]);

// Fetch cards and word set subscriptions on mount
onMounted(async () => {
  await fetchCards();
  fetchSubscriptions();
});

async function fetchCards() {
  loading.value = true;
  try {
    const fetchedCards = await $api<FsrsCardWithWordDto[]>('user/vocabulary/cards');
    // Add plain text version for filtering
    cards.value = fetchedCards.map(card => ({
      ...card,
      wordTextPlain: stripRuby(card.wordText)
    }));
  } catch (error) {
    console.error('Error fetching cards:', error);
    toast.add({
      severity: 'error',
      summary: 'Error',
      detail: 'Failed to load cards',
      life: 5000,
    });
  } finally {
    loading.value = false;
  }
}

// Filtered cards based on selected state and search query
const filteredCards = computed(() => {
  let result = cards.value;

  // Filter by state
  if (selectedState.value !== null) {
    result = result.filter(card => card.state === selectedState.value);
  }

  // Filter by search query
  if (globalFilter.value) {
    const query = globalFilter.value.toLowerCase();
    result = result.filter(card =>
      card.wordTextPlain?.toLowerCase().includes(query)
    );
  }

  return result;
});

// State display helpers
function getStateName(state: FsrsState): string {
  switch (state) {
    case FsrsState.New: return 'New';
    case FsrsState.Learning: return 'Learning';
    case FsrsState.Review: return 'Review';
    case FsrsState.Relearning: return 'Relearning';
    case FsrsState.Blacklisted: return 'Blacklisted';
    case FsrsState.Mastered: return 'Mastered';
    default: return 'Unknown';
  }
}

function getStateSeverity(state: FsrsState): string {
  switch (state) {
    case FsrsState.New: return 'info';
    case FsrsState.Learning: return 'warn';
    case FsrsState.Review: return 'success';
    case FsrsState.Relearning: return 'warn';
    case FsrsState.Blacklisted: return 'secondary';
    case FsrsState.Mastered: return 'success';
    default: return 'secondary';
  }
}

// Format dates
function formatDate(date: Date | undefined): string {
  if (!date) return 'Never';
  const d = new Date(date);
  return d.toLocaleDateString(undefined, {
    year: 'numeric',
    month: 'short',
    day: 'numeric'
  });
}

function formatDateTime(date: Date | undefined): string {
  if (!date) return 'Never';
  const d = new Date(date);
  return d.toLocaleString(undefined, {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit'
  });
}

// Format numbers
function formatNumber(value: number | undefined, decimals: number = 2): string {
  if (value === undefined || value === null) return '-';
  return value.toFixed(decimals);
}

// Set vocabulary state actions
async function setVocabularyState(card: FsrsCardWithWordDto, state: string) {
  try {
    await $api('srs/set-vocabulary-state', {
      method: 'POST',
      body: {
        wordId: card.wordId,
        readingIndex: card.readingIndex,
        state: state
      }
    });

    toast.add({
      severity: 'success',
      summary: 'Card Updated',
      detail: 'Card state changed successfully',
      life: 3000,
    });

    await fetchCards();
  } catch (error) {
    console.error('Error updating card:', error);
    toast.add({
      severity: 'error',
      summary: 'Error',
      detail: 'Failed to update card. Please try again.',
      life: 5000,
    });
  }
}

// Action handlers
function setToMastered(card: FsrsCardWithWordDto) {
  setVocabularyState(card, 'neverForget-add');
}

function removeMastered(card: FsrsCardWithWordDto) {
  setVocabularyState(card, 'neverForget-remove');
}

function blacklist(card: FsrsCardWithWordDto) {
  setVocabularyState(card, 'blacklist-add');
}

function unblacklist(card: FsrsCardWithWordDto) {
  setVocabularyState(card, 'blacklist-remove');
}

// Delete card
function confirmDeleteCard(card: FsrsCardWithWordDto) {
  const wordTextPlain = stripRuby(card.wordText);

  confirm.require({
    message: `Are you sure you want to set the card to new for "${wordTextPlain}"?`,
    header: 'Confirm Deletion',
    icon: 'pi pi-exclamation-triangle',
    acceptClass: 'p-button-danger',
    accept: () => deleteCard(card),
  });
}

async function deleteCard(card: FsrsCardWithWordDto) {
  try {
    await $api(`user/vocabulary/remove/${card.wordId}/${card.readingIndex}`, {
      method: 'POST'
    });

    toast.add({
      severity: 'success',
      summary: 'Card Deleted',
      detail: 'Card and review history removed successfully',
      life: 3000,
    });

    await fetchCards();
  } catch (error) {
    console.error('Error deleting card:', error);
    toast.add({
      severity: 'error',
      summary: 'Error',
      detail: 'Failed to delete card',
      life: 5000,
    });
  }
}

// Statistics
const stats = computed(() => {
  const total = cards.value.length;
  const byState = cards.value.reduce((acc, card) => {
    acc[card.state] = (acc[card.state] || 0) + 1;
    return acc;
  }, {} as Record<number, number>);

  return {
    total,
    new: byState[FsrsState.New] || 0,
    learning: byState[FsrsState.Learning] || 0,
    review: byState[FsrsState.Review] || 0,
    relearning: byState[FsrsState.Relearning] || 0,
    blacklisted: byState[FsrsState.Blacklisted] || 0,
    mastered: byState[FsrsState.Mastered] || 0,
  };
});

// Get available actions for a card based on its state
function getAvailableActions(card: FsrsCardWithWordDto) {
  const actions = [];

  if (card.state === FsrsState.Mastered) {
    actions.push({ label: 'Remove Mastered', command: () => removeMastered(card) });
  } else if (card.state === FsrsState.Blacklisted) {
    actions.push({ label: 'Unblacklist', command: () => unblacklist(card) });
  } else {
    actions.push({ label: 'Set to Mastered', command: () => setToMastered(card) });
    actions.push({ label: 'Blacklist', command: () => blacklist(card) });
  }

  return actions;
}
</script>

<template>
  <div class="container mx-auto p-2 md:p-4">
    <Card class="mb-4">
      <template #title>
        <div class="flex justify-between items-center">
          <h2 class="text-xl font-bold">FSRS Cards Management</h2>
          <Button
            icon="pi pi-refresh"
            label="Refresh"
            :loading="loading"
            @click="fetchCards"
          />
        </div>
      </template>
      <template #subtitle>
        <div class="mt-2">
          <p class="mb-2">
            Total cards: <strong>{{ stats.total }}</strong>
          </p>
          <div class="grid grid-cols-2 md:grid-cols-3 lg:grid-cols-6 gap-2 text-sm">
            <div>New: <Tag :value="stats.new" severity="info" /></div>
            <div>Learning: <Tag :value="stats.learning" severity="warn" /></div>
            <div>Review: <Tag :value="stats.review" severity="success" /></div>
            <div>Relearning: <Tag :value="stats.relearning" severity="warn" /></div>
            <div>Blacklisted: <Tag :value="stats.blacklisted" severity="secondary" /></div>
            <div>Mastered: <Tag :value="stats.mastered" severity="success" /></div>
          </div>
        </div>
      </template>
      <template #content>
        <div class="mb-4 flex flex-col md:flex-row gap-3">
          <IconField icon-position="left" class="flex-1">
            <InputIcon class="pi pi-search" />
            <InputText
              v-model="globalFilter"
              placeholder="Search by word text..."
              class="w-full"
            />
          </IconField>
          <Select
            v-model="selectedState"
            :options="stateOptions"
            option-label="label"
            option-value="value"
            placeholder="Filter by state"
            class="w-full md:w-auto md:min-w-48"
          />
        </div>

        <Message v-if="hasWordSetSubscriptions && !loading" severity="info" :closable="true" class="mb-4">
          Your word set subscriptions (~{{ wordSetTotalWords }} words from {{ subscriptions.length }} {{ subscriptions.length === 1 ? 'set' : 'sets' }}) are not shown here. They are managed separately.
          <NuxtLink to="/settings/word-sets" class="font-semibold underline ml-1">Manage Word Sets</NuxtLink>
        </Message>

        <DataTable
          :value="filteredCards"
          :loading="loading"
          sort-field="due"
          :sort-order="1"
          paginator
          :rows="50"
          :rows-per-page-options="[25, 50, 100, 200]"
          striped-rows
          responsive-layout="scroll"
          class="text-sm"
        >
          <Column field="wordText" header="Word" sortable>
            <template #body="{ data }">
              <NuxtLink
                :to="`/vocabulary/${data.wordId}/${data.readingIndex}`"
                class="text-primary-600 hover:underline font-medium dark:text-primary-400"
                v-html="data.wordText"
              />
            </template>
          </Column>

          <Column field="state" header="State" sortable>
            <template #body="{ data }">
              <Tag
                :value="getStateName(data.state)"
                :severity="getStateSeverity(data.state)"
              />
            </template>
          </Column>

          <Column field="step" header="Step" sortable>
            <template #body="{ data }">
              {{ data.step ?? '-' }}
            </template>
          </Column>

          <Column field="stability" header="Stability" sortable>
            <template #body="{ data }">
              {{ formatNumber(data.stability, 1) }}
            </template>
          </Column>

          <Column field="difficulty" header="Difficulty" sortable>
            <template #body="{ data }">
              {{ formatNumber(data.difficulty, 1) }}
            </template>
          </Column>

          <Column field="due" header="Due" sortable>
            <template #body="{ data }">
              <span :class="{ 'text-red-600 font-bold dark:text-red-400': new Date(data.due) <= new Date() }">
                {{ formatDate(data.due) }}
              </span>
            </template>
          </Column>

          <Column field="lastReview" header="Last Review" sortable>
            <template #body="{ data }">
              {{ formatDateTime(data.lastReview) }}
            </template>
          </Column>

          <Column header="Actions" frozen align-frozen="right">
            <template #body="{ data }">
              <div class="flex gap-2">
                <SplitButton
                  :label="data.state === FsrsState.Mastered ? 'Remove Mastered' : data.state === FsrsState.Blacklisted ? 'Unblacklist' : 'Set to Mastered'"
                  :model="getAvailableActions(data)"
                  severity="secondary"
                  size="small"
                  text
                  @click="getAvailableActions(data)[0].command()"
                />

                <Button
                  icon="pi pi-trash"
                  severity="danger"
                  text
                  size="small"
                  @click="confirmDeleteCard(data)"
                  aria-label="Set to new"
                />
              </div>
            </template>
          </Column>

          <template #empty>
            <div class="text-center py-4 text-gray-500 dark:text-gray-400">
              <template v-if="hasWordSetSubscriptions">
                <p>You have no individual cards.</p>
                <p class="mt-1">
                  Your vocabulary includes ~{{ wordSetTotalWords }} words from {{ subscriptions.length }} word set {{ subscriptions.length === 1 ? 'subscription' : 'subscriptions' }}, which are managed separately.
                </p>
                <NuxtLink to="/settings/word-sets">
                  <Button icon="pi pi-cog" label="Manage Word Sets" severity="secondary" size="small" class="mt-2" />
                </NuxtLink>
              </template>
              <template v-else>
                No cards found. Import vocabulary to get started.
              </template>
            </div>
          </template>
        </DataTable>
      </template>
    </Card>
  </div>
</template>
