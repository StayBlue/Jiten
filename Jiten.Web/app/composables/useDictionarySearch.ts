import { debounce } from 'perfect-debounce';
import type { DictionaryEntry, DictionarySearchResult } from '~/types/types';

export function useDictionarySearch() {
  const { $api } = useNuxtApp();

  const results = ref<DictionaryEntry[]>([]);
  const queryType = ref('');
  const hasMore = ref(false);
  const isLoading = ref(false);
  const error = ref<Error | null>(null);

  const searchInternal = async (query: string, limit = 50, offset = 0) => {
    if (!query || query.trim().length === 0) {
      results.value = [];
      queryType.value = '';
      hasMore.value = false;
      isLoading.value = false;
      return;
    }

    isLoading.value = true;
    error.value = null;

    try {
      const result = await $api<DictionarySearchResult>('vocabulary/search', {
        query: { query: query.trim(), limit, offset },
      });
      results.value = result?.results || [];
      queryType.value = result?.queryType || '';
      hasMore.value = result?.hasMore || false;
    } catch (e) {
      error.value = e as Error;
      results.value = [];
      queryType.value = '';
      hasMore.value = false;
    } finally {
      isLoading.value = false;
    }
  };

  const search = debounce(searchInternal, 300);

  const clearResults = () => {
    results.value = [];
    queryType.value = '';
    hasMore.value = false;
    error.value = null;
  };

  return {
    results,
    queryType,
    hasMore,
    isLoading,
    error,
    search,
    clearResults,
  };
}
