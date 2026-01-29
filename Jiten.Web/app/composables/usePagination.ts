import type { PaginatedResponse } from '~/types/types';

export function usePagination<T>(response: Ref<PaginatedResponse<T> | null>) {
  const route = useRoute();

  const currentPage = computed(() => response.value?.currentPage ?? 1);
  const pageSize = computed(() => response.value?.pageSize ?? 100);
  const totalItems = computed(() => response.value?.totalItems ?? 0);

  const start = computed(() => (currentPage.value - 1) * pageSize.value + 1);
  const end = computed(() => Math.min(currentPage.value * pageSize.value, totalItems.value));

  const previousLink = computed(() => {
    return response.value?.hasPreviousPage
      ? { query: { ...route.query, offset: response.value.previousOffset } }
      : null;
  });

  const nextLink = computed(() => {
    return response.value?.hasNextPage
      ? { query: { ...route.query, offset: response.value.nextOffset } }
      : null;
  });

  return { currentPage, pageSize, totalItems, start, end, previousLink, nextLink };
}
