export const useJpdbApi = () => {
  const JpdbRateLimiter = {
    lastRequestTime: 0,
    minInterval: 500,

    async executeWithRateLimit<T>(fn: () => Promise<T>): Promise<T> {
      const now = Date.now();
      const elapsed = now - this.lastRequestTime;

      if (elapsed < this.minInterval) {
        await new Promise((resolve) => setTimeout(resolve, this.minInterval - elapsed));
      }

      this.lastRequestTime = Date.now();
      return await fn();
    },
  };

  interface VocabularyIdPair {
    id1: number;
    id2: number;
  }

  class JpdbApiClient {
    private apiKey: string;

    constructor(apiKey: string) {
      if (!apiKey) {
        throw new Error('API key is required');
      }
      this.apiKey = apiKey;
    }

    /**
     * Gets vocabulary IDs with specific states from all user decks
     */
    async getFilteredVocabularyIds(blacklistedAsKnown = false, dueAsKnown = false, suspendedAsKnown = false): Promise<number[]> {
      try {
        // Step 1: Get list of decks
        const deckIds = await this.getUserDecks();
        deckIds.push('never-forget');
        deckIds.push('blacklist');

        // Step 2: Get vocabulary from all decks
        const allVocabulary: VocabularyIdPair[] = [];
        for (const deckId of deckIds) {
          const deckVocabulary = await this.getDeckVocabulary(deckId);
          allVocabulary.push(...deckVocabulary);
        }

        // Remove duplicates by id1
        const uniqueVocab = allVocabulary.reduce((acc, vocab) => {
          if (!acc.find((v) => v.id1 === vocab.id1)) {
            acc.push(vocab);
          }
          return acc;
        }, [] as VocabularyIdPair[]);

        // Step 3: Lookup vocabulary info and filter by states
        const filteredIds = await this.lookupAndFilterVocabulary(uniqueVocab, blacklistedAsKnown, dueAsKnown, suspendedAsKnown);

        return filteredIds;
      } catch (error) {
        throw new Error(`Error getting filtered vocabulary IDs: ${error}`);
      }
    }

    private async getUserDecks(): Promise<any[]> {
      const requestBody = { fields: ['id'] };

      const response = await this.makeApiRequest('https://jpdb.io/api/v1/list-user-decks', requestBody);

      const deckIds: any[] = [];
      if (response.decks && Array.isArray(response.decks)) {
        for (const deck of response.decks) {
          if (Array.isArray(deck) && deck.length > 0) {
            deckIds.push(deck[0]);
          }
        }
      }

      return deckIds;
    }

    private async getDeckVocabulary(deckId: number): Promise<VocabularyIdPair[]> {
      const requestBody = { id: deckId, fetch_occurences: false };

      const response = await this.makeApiRequest('https://jpdb.io/api/v1/deck/list-vocabulary', requestBody);

      const vocabularyPairs: VocabularyIdPair[] = [];
      if (response.vocabulary && Array.isArray(response.vocabulary)) {
        for (const vocabItem of response.vocabulary) {
          if (Array.isArray(vocabItem) && vocabItem.length >= 2) {
            vocabularyPairs.push({
              id1: vocabItem[0],
              id2: vocabItem[1],
            });
          }
        }
      }

      return vocabularyPairs;
    }

    private async lookupAndFilterVocabulary(
      vocabularyPairs: VocabularyIdPair[],
      blacklistedAsKnown = false,
      dueAsKnown = false,
      suspendedAsKnown = false
    ): Promise<number[]> {
      const chunkSize = 2500;
      const filteredIds: number[] = [];
      const targetStates = new Set(['never-forget', 'known']);

      // Add optional states based on parameters
      if (blacklistedAsKnown) targetStates.add('blacklisted');
      if (dueAsKnown) targetStates.add('due');
      if (suspendedAsKnown) targetStates.add('suspended');

      // Process vocabulary in chunks
      for (let i = 0; i < vocabularyPairs.length; i += chunkSize) {
        const chunk = vocabularyPairs.slice(i, i + chunkSize);
        const lookupList = chunk.map((vp) => [vp.id1, vp.id2]);
        const requestBody = {
          list: lookupList,
          fields: ['vid', 'card_level', 'card_state'],
        };

        const response = await this.makeApiRequest('https://jpdb.io/api/v1/lookup-vocabulary', requestBody);

        if (response.vocabulary_info && Array.isArray(response.vocabulary_info)) {
          for (const vocabInfo of response.vocabulary_info) {
            if (!Array.isArray(vocabInfo) || vocabInfo.length < 3) continue;

            const id = vocabInfo[0];
            const states = vocabInfo[2];

            if (!Array.isArray(states)) continue;

            // Check if any state matches target states
            const matchesTargetState = states.some((state: string) => targetStates.has(state));

            if (matchesTargetState) {
              filteredIds.push(id);
            }
          }
        }
      }

      return filteredIds;
    }

    private async makeApiRequest(url: string, requestBody: any): Promise<any> {
      return await JpdbRateLimiter.executeWithRateLimit(async () => {
        const response = await $fetch(url, {
          method: 'POST',
          headers: {
            Authorization: `Bearer ${this.apiKey}`,
            'Content-Type': 'application/json',
          },
          body: requestBody,
        });

        return response;
      });
    }
  }

  return {
    JpdbApiClient,
  };
};
