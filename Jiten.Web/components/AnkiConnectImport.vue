<script setup lang="ts">
  import { ref, computed } from 'vue';
  import { YankiConnect } from 'yanki-connect';
  import { useToast } from 'primevue/usetoast';

  const { $api } = useNuxtApp();
  const toast = useToast();

  let currentStep = ref(0);

  let client: YankiConnect;
  let decks: Record<string, number> = {};
  let deckEntries: Array<[string, number]> = [];
  let cantConnect = ref(false);
  let reviewsForKnown = ref(10);
  let cardsIds: number[] = [];

  const isLoading = ref(false);

  // Store only the minimal data we need for each card to reduce memory usage
  const cards = ref<Array<{ value: string; reps: number }>>([]);
  const cardsToKeep = computed(() => {
    return cards.value.filter((c) => c.reps >= reviewsForKnown.value);
  });

  const selectedDeck = ref<number>(0);
  const selectedField = ref<number>(0);
  const fields = ref<Array<[string, { order: number; value: string }]>>([]);
  const overwriteExisting = ref(false);
  const forceImportCardsWithNoReviews = ref(false);
  const parseWords = ref(false);

  const fieldsOptions = computed(() =>
    (fields.value || []).map((entry, idx) => ({
      label: entry[0] + (entry[1].value ? ` (${entry[1].value.substring(0, 20)})` : ''),
      value: idx,
    }))
  );

  const Connect = async () => {
    try {
      client = new YankiConnect();
      decks = await client.deck.deckNamesAndIds();
      deckEntries = Object.entries(decks);
      cantConnect.value = false;
      await NextStep();
    } catch (e) {
      cantConnect.value = true;
      console.log(e);
    }
  };

  const PreviousStep = () => {
    currentStep.value -= 2;
    NextStep();
  };

  const NextStep = async () => {
    currentStep.value++;

    if (currentStep.value == 2) {
      if (selectedDeck.value == null) {
        currentStep.value--;
        return;
      }

      isLoading.value = true;
      cardsIds = await client.card.findCards({ query: `did:${selectedDeck.value}` });
      const previewCards = await client.card.cardsInfo({ cards: [cardsIds[0]] });
      selectedField.value = 0;
      if (previewCards && previewCards.length > 0) {
        fields.value = Object.entries(previewCards[0].fields || {});
      } else {
        fields.value = [];
      }
      cards.value = [];
      isLoading.value = false;
    }

    if (currentStep.value == 3) {
      isLoading.value = true;

      const fieldEntry = fields.value[selectedField.value];
      const fieldName = fieldEntry ? fieldEntry[0] : undefined;
      if (!fieldName) {
        console.warn('No field selected for mapping');
        return;
      }

      // Retrieve cards info in batches
      const batchSize = 200;
      const allCardsInfo: any[] = [];

      for (let i = 0; i < cardsIds.length; i += batchSize) {
        const batch = cardsIds.slice(i, i + batchSize);
        const cardsBatch = await client.card.cardsInfo({ cards: batch });
        allCardsInfo.push(...(cardsBatch || []));
      }

      // Calculate collection creation time from earliest card
      const collectionCreated = Math.min(...allCardsInfo.map((c) => c.mod)) * 1000;

      // Retrieve all reviews
      const deckName = deckEntries.find(([_, id]) => id === selectedDeck.value)?.[0];
      if (!deckName) {
        console.warn('Deck not found');
        return;
      }

      const reviews = await client.statistic.cardReviews({
        deck: deckName,
        startID: 1,
      });

      // Build review map (limit to 10 most recent per card)
      const reviewByCard = new Map<number, any[]>();
      for (const [reviewTime, cardID, _usn, buttonPressed, _newInterval, _previousInterval, _newFactor, reviewDuration, _reviewType] of reviews) {
        const existing = reviewByCard.get(cardID) ?? [];
        existing.push({
          Rating: buttonPressed,
          ReviewDateTime: new Date(reviewTime),
          ReviewDuration: reviewDuration,
        });
        reviewByCard.set(cardID, existing);
      }

      // Limit to 10 most recent reviews
      for (const [key, arr] of reviewByCard.entries()) {
        arr.sort((a, b) => b.ReviewDateTime.getTime() - a.ReviewDateTime.getTime());
        reviewByCard.set(key, arr.slice(0, 10));
      }

      // Build cards payload
      const enrichedCards: any[] = [];
      for (const card of allCardsInfo) {
        // Skip suspended cards (queue = -1)
        if (card.queue === -1) continue;

        const field = card.fields[fieldName];
        const word = field?.value?.trim() || '';
        if (!word) continue;

        const reviews = reviewByCard.get(card.cardId) ?? [];

        // Convert Anki state to FSRS state
        let state: number;
        if (card.queue === 0)
          state = 0;
        else if (card.queue === 1 || card.queue === 3)
          state = 1; // Learning
        else if (card.queue === 2)
          state = 2; // Review
        else state = 2; // Default to Review

        // Convert interval to stability
        const stability = card.interval > 0 ? card.interval : 0;

        // Convert ease factor to difficulty (1300-2500 → 10-1)
        const difficulty = Math.max(1, Math.min(10, 10 - (card.factor - 1300) / 170.0));

        // Last review timestamp
        const cardReviews = reviewByCard.get(card.cardId) ?? [];
        const lastReview = cardReviews.length > 0 ? cardReviews[0].ReviewDateTime : null;

        let due: Date;
        if (card.queue === 0) {
          due = new Date(); // New cards due now
        } else if (card.queue === 1 || card.queue === 3) {
          // Learning cards: due is timestamp in seconds
          due = new Date(card.due * 1000);
          // console.log("learning : " + due);
        } else {
          // Review cards: days since collection creation
          if (lastReview) {
            // If we have history, this is the most accurate method
            // Add interval (days) to last review
            due = new Date(lastReview.getTime() + card.interval * 86400000);
          } else {
            // Fallback if no reviews found (rare, or if review log was cleared)
            // We use 'mod' (Last Modified Date) as a proxy for Last Review Date
            // Anki stores 'mod' in SECONDS
            due = new Date(card.mod * 1000 + card.interval * 86400000);
          }

          // console.log("review : " + due)
        }

        enrichedCards.push({
          Card: {
            Word: word,
            Stability: stability,
            Difficulty: difficulty,
            Reps: card.reps,
            Lapses: card.lapses,
            Due: due.toISOString(),
            State: state,
            LastReview: lastReview?.toISOString(),
          },
          ReviewLogs: reviews.map((r) => ({
            Rating: r.Rating,
            ReviewDateTime: r.ReviewDateTime.toISOString(),
            ReviewDuration: r.ReviewDuration,
          })),
        });
      }

      cards.value = enrichedCards;
      isLoading.value = false;
    }

    if (currentStep.value == 4) {
      isLoading.value = true;

      try {
        const payload = {
          cards: cards.value,
          overwrite: overwriteExisting.value,
          forceImportCardsWithNoReviews: forceImportCardsWithNoReviews.value,
          parseWords: parseWords.value
        };

        const result = await $api<{
          imported: number;
          updated: number;
          skipped: number;
          reviewLogs: number;
          skippedWords: string[];
          skippedCountNoReviews: number;
          skippedWordsNoReviews: string[];
        }>('user/vocabulary/import-from-anki', {
          method: 'POST',
          body: JSON.stringify(payload),
          headers: { 'Content-Type': 'application/json' },
        });

        if (result) {
          let message = '';
          if (result.imported > 0) {
            message += `Imported ${result.imported} new card${result.imported === 1 ? '' : 's'}`;
          }
          if (result.updated > 0) {
            if (message) message += ', ';
            message += `updated ${result.updated} existing card${result.updated === 1 ? '' : 's'}`;
          }
          if (result.reviewLogs) {
            message += ` with ${result.reviewLogs} review log${result.reviewLogs === 1 ? '' : 's'}`;
          }
          if (result.skipped > 0) {
            if (message) message += '. ';
            message += `${result.skipped} card${result.skipped === 1 ? '' : 's'} skipped`;
          }
          if (result.skippedCountNoReviews > 0) {
            if (message) message += '. ';
            message += `${result.skippedCountNoReviews} card${result.skippedCountNoReviews === 1 ? '' : 's'} skipped (no reviews)`;
          }
          if (!message) {
            message = 'No cards were imported';
          } else {
            message += '.';
          }

          toast.add({
            severity: 'success',
            summary: 'Anki Data Imported',
            detail: message,
            life: 6000,
          });

          // Show skipped words if any
          if (result.skippedWords && result.skippedWords.length > 0) {
            console.log('Skipped words (not parsed):', result.skippedWords);
            toast.add({
              severity: 'warn',
              summary: 'Some words not found',
              detail: `${result.skippedWords.length} words that could not be parsed correctly or were not in the dictionary. Check console for list.`,
              life: 10000,
            });
          }
        }
      } catch (error) {
        console.error('Error importing Anki data:', error);
        toast.add({ severity: 'error', detail: 'Failed to import data.', life: 5000 });
      } finally {
        isLoading.value = false;
        currentStep.value = 1;
      }
    }
  };
</script>

<template>
  <Card>
    <template #title>AnkiConnect</template>
    <template #content>
      <div v-if="cantConnect" class="text-red-800 dark:text-red-400">
        <p>Couldn't connect to Anki.</p>
        <p>
          Make sure you have the <a href="https://ankiweb.net/shared/info/2055492159" rel="nofollow" target="_blank">Anki Connect plugin</a> installed and
          enabled.
        </p>
        <p>Make sure Anki is running</p>
        <p>
          Go to Anki > Tools > Add-ons > AnkiConnect > Config and add the following line to webCorsOriginList, "https://jiten.moe" so it looks like the
          following screenshot:
        </p>
        <img src="/assets/img/ankiconnect.jpg" alt="Anki Connect Config" class="w-full" />
      </div>
      <div v-if="currentStep == 0">
        <p>
          Add words directly from Anki using the <a href="https://ankiweb.net/shared/info/2055492159" rel="nofollow" target="_blank">Anki Connect plugin</a>.
        </p>
        <div class="p-4">
          <Button label="Connect to Anki" @click="Connect()" />
        </div>
      </div>

      <div v-if="currentStep == 1 && deckEntries.length > 0">
        <p>Select a deck to add words from.</p>
        <Select v-model="selectedDeck" :options="deckEntries" optionLabel="0" optionValue="1" placeholder="Select a deck" class="w-full" />
        <div class="flex flex-row gap-2 p-4">
          <Button label="Next" :disabled="!selectedDeck" @click="NextStep()" />
        </div>
      </div>
      <div v-if="currentStep == 2">
        <p>
          Selected deck: <b>{{ deckEntries.find((d) => d[1] === selectedDeck)?.[0] || '—' }}</b>
        </p>
        <div v-if="isLoading">
          <ProgressSpinner style="width: 50px; height: 50px" stroke-width="8px" animation-duration=".5s" />
          <p>Loading your deck...</p>
        </div>
        <div v-else>
          <p>Select the correct field containing the words WITHOUT furigana</p>
          <Select v-model="selectedField" :options="fieldsOptions" optionLabel="label" optionValue="value" placeholder="Select a field" class="w-full" />
          <div class="flex flex-row gap-2 p-4">
            <Button label="Back" :disabled="!selectedDeck" @click="PreviousStep()" />
            <Button label="Next" @click="NextStep()" />
          </div>
        </div>
      </div>
      <div v-if="currentStep == 3">
        <div v-if="isLoading">
          <ProgressSpinner style="width: 50px; height: 50px" stroke-width="8px" animation-duration=".5s" />
          <p>Loading cards: {{ cards.length }}/{{ cardsIds.length }} ({{ ((cards.length / cardsIds.length) * 100).toFixed(0) }}%)...</p>
        </div>
        <div v-else>
          <p>
            This will import <b>{{ cards.length }} words</b>.
          </p>
          <div class="flex flex-col gap-3 p-4">
            <div class="flex items-center gap-2">
              <Checkbox v-model="overwriteExisting" inputId="overwrite" :binary="true" />
              <label for="overwrite" class="cursor-pointer">
                Overwrite existing cards (replace cards you already have with Anki versions, even if they are more recent)
              </label>
            </div>
            <div class="flex items-center gap-2">
              <Checkbox v-model="forceImportCardsWithNoReviews" inputId="forceImportCardsWithNoReviews" :binary="true" />
              <label for="forceImportCardsWithNoReviews" class="cursor-pointer">
                Force import cards with no reviews (import cards even if they have no reviews in Anki, not recommended)
              </label>
            </div>
            <div class="flex items-center gap-2">
              <Checkbox v-model="parseWords" inputId="parseWords" :binary="true" />
              <label for="parseWords" class="cursor-pointer">
                Parse words instead of importing them directly (only use if you have conjugated verbs instead of the dictionary form, less accurate)
              </label>
            </div>
            <div class="flex flex-row gap-2">
              <Button label="Back" :disabled="!selectedDeck" @click="PreviousStep()" />
              <Button label="Import" :disabled="!selectedDeck" @click="NextStep()" />
            </div>
          </div>
        </div>
      </div>
      <div v-if="currentStep == 4">
        <ProgressSpinner style="width: 50px; height: 50px" stroke-width="8px" animation-duration=".5s" />
        <p>Adding to your known words...</p>
      </div>
    </template>
  </Card>
</template>

<style scoped></style>
