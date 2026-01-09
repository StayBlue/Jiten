<script setup lang="ts">
  import { type AccomplishmentVocabularyDto, type UserProfile, MediaType, SortOrder } from '~/types';
  import { useAuthStore } from '~/stores/authStore';
  import { getMediaTypeText } from '~/utils/mediaTypeMapper';

  const route = useRoute();
  const router = useRouter();
  const auth = useAuthStore();

  const targetUsername = computed(() => route.params.username as string);
  const isOwnProfile = computed(() => auth.isAuthenticated && auth.user?.userName?.toLowerCase() === targetUsername.value.toLowerCase());

  const offset = computed(() => (route.query.offset ? Number(route.query.offset) : 0));

  const sortByOptions = ref([
    { label: 'Occurrences', value: 'occurrences' },
    { label: 'Global Frequency', value: 'globalFreq' },
  ]);

  const displayOptions = ref([
    { label: 'All', value: 'all' },
    { label: 'In My List', value: 'known' },
    { label: 'Only Young', value: 'young' },
    { label: 'Only Mature', value: 'mature' },
    { label: 'Only Mastered', value: 'mastered' },
    { label: 'Only Blacklisted', value: 'blacklisted' },
    { label: 'Only Unknown', value: 'unknown' },
  ]);

  const mediaTypeOptions = computed(() => {
    const options = [{ label: 'All Media Types', value: '' }];
    for (const type of Object.values(MediaType).filter((v) => typeof v === 'number')) {
      options.push({
        label: getMediaTypeText(type as MediaType),
        value: (type as MediaType).toString(),
      });
    }
    return options;
  });

  const sortOrder = ref(route.query.sortOrder === 'true' || route.query.sortOrder === '1');
  const sortBy = ref(route.query.sortBy?.toString() || sortByOptions.value[0].value);
  const mediaTypeFilter = ref(route.query.mediaType?.toString() || '');
  const display = ref(route.query.display?.toString() || displayOptions.value[0].value);

  watch(sortOrder, (newValue) => {
    router.replace({
      query: { ...route.query, sortOrder: newValue ? '1' : '0', offset: 0 },
    });
  });

  watch(sortBy, (newValue) => {
    router.replace({
      query: { ...route.query, sortBy: newValue, offset: 0 },
    });
  });

  watch(mediaTypeFilter, (newValue) => {
    router.replace({
      query: { ...route.query, mediaType: newValue || undefined, offset: 0 },
    });
  });

  watch(display, (newValue) => {
    router.replace({
      query: { ...route.query, display: newValue, offset: 0 },
    });
  });

  const userNotFound = ref(false);

  const { data: profileData, error: profileError } = await useApiFetch<UserProfile>(`user/profile/${targetUsername.value}`);

  if (profileError.value?.statusCode === 404) {
    userNotFound.value = true;
  }

  const profile = profileData.value;
  const canViewStats = computed(() => isOwnProfile.value || profile?.isPublic);
  const displayUsername = computed(() => profile?.username ?? targetUsername.value);

  const queryParams = computed(() => {
    const params: Record<string, any> = {
      offset: offset.value,
      sortBy: sortBy.value,
      descending: sortOrder.value,
      displayFilter: display.value,
    };
    if (mediaTypeFilter.value) {
      params.mediaType = parseInt(mediaTypeFilter.value);
    }
    return params;
  });

  const {
    data: response,
    status,
    error,
  } = await useApiFetchPaginated<AccomplishmentVocabularyDto>(`user/profile/${targetUsername.value}/accomplishments/vocabulary`, {
    query: queryParams,
    watch: [offset, sortBy, sortOrder, mediaTypeFilter, display],
  });

  const currentPage = computed(() => response.value?.currentPage ?? 1);
  const pageSize = computed(() => response.value?.pageSize ?? 100);
  const totalItems = computed(() => response.value?.totalItems ?? 0);

  const start = computed(() => (currentPage.value - 1) * pageSize.value + 1);
  const end = computed(() => Math.min(currentPage.value * pageSize.value, totalItems.value));

  const previousLink = computed(() => {
    return response.value?.hasPreviousPage ? { query: { ...route.query, offset: response.value.previousOffset } } : null;
  });

  const nextLink = computed(() => {
    return response.value?.hasNextPage ? { query: { ...route.query, offset: response.value.nextOffset } } : null;
  });

  const scrollToTop = () => {
    nextTick(() => {
      window.scrollTo({ top: 0, behavior: 'instant' });
    });
  };

  useHead(() => ({
    title: `${displayUsername.value} - Vocabulary`,
    meta: [{ name: 'description', content: `Vocabulary list for ${displayUsername.value}` }],
  }));
</script>

<template>
  <div class="flex flex-col gap-2">
    <div v-if="userNotFound" class="text-center py-16">
      <Card>
        <template #content>
          <div class="flex flex-col items-center gap-4">
            <Icon name="material-symbols:person-off" size="4rem" class="text-gray-400" />
            <h2 class="text-xl font-semibold">User not found</h2>
            <p class="text-gray-500">This user does not exist or has been removed.</p>
            <NuxtLink to="/">
              <Button label="Go Home" icon="pi pi-home" />
            </NuxtLink>
          </div>
        </template>
      </Card>
    </div>

    <div v-else-if="!canViewStats" class="text-center py-16">
      <Card>
        <template #content>
          <div class="flex flex-col items-center gap-4">
            <Icon name="material-symbols:lock" size="4rem" class="text-gray-400" />
            <h2 class="text-xl font-semibold">This profile is private</h2>
            <p class="text-gray-500">This user has chosen to keep their profile private.</p>
            <NuxtLink :to="`/profile/${targetUsername}`">
              <Button label="Back to Profile" icon="pi pi-arrow-left" />
            </NuxtLink>
          </div>
        </template>
      </Card>
    </div>

    <div v-else class="flex flex-col gap-2">
      <div class="flex items-center gap-2">
        <NuxtLink :to="`/profile/${displayUsername}`" class="flex items-center gap-1 text-purple-600 hover:text-purple-800">
          <Icon name="material-symbols:arrow-back" />
          Back to Profile
        </NuxtLink>
      </div>

      <h1 class="text-xl font-semibold">Vocabulary from completed media</h1>

      <div class="flex flex-col md:flex-row gap-2 w-full">
        <div class="flex gap-2">
          <FloatLabel variant="on">
            <Select
              v-model="sortBy"
              :options="sortByOptions"
              option-label="label"
              option-value="value"
              placeholder="Sort by"
              input-id="sortBy"
              class="w-full md:w-44"
            />
            <label for="sortBy">Sort by</label>
          </FloatLabel>
          <Button @click="sortOrder = !sortOrder" class="min-w-12 w-12">
            <Icon v-if="sortOrder" name="mingcute:az-sort-descending-letters-line" size="1.25em" />
            <Icon v-else name="mingcute:az-sort-ascending-letters-line" size="1.25em" />
          </Button>
        </div>
        <FloatLabel variant="on">
          <Select
            v-model="mediaTypeFilter"
            :options="mediaTypeOptions"
            option-label="label"
            option-value="value"
            placeholder="Media Type"
            input-id="mediaType"
            class="w-full md:w-44"
            scroll-height="30vh"
          />
          <label for="mediaType">Media Type</label>
        </FloatLabel>
        <div v-if="auth.isAuthenticated">
          <FloatLabel variant="on">
            <Select
              v-model="display"
              :options="displayOptions"
              option-label="label"
              option-value="value"
              placeholder="display"
              input-id="display"
              class="w-full md:w-56"
              scroll-height="30vh"
            />
            <label for="display">Display</label>
          </FloatLabel>
        </div>
      </div>

      <div class="flex justify-between flex-col md:flex-row">
        <div class="flex gap-8 pl-2">
          <NuxtLink :to="previousLink" :class="previousLink == null ? '!text-gray-500 pointer-events-none' : ''" no-rel>Previous</NuxtLink>
          <NuxtLink :to="nextLink" :class="nextLink == null ? '!text-gray-500 pointer-events-none' : ''" no-rel>Next</NuxtLink>
        </div>
        <div class="text-gray-500 dark:text-gray-300">viewing words {{ start }}-{{ end }} from {{ totalItems.toLocaleString() }} total</div>
      </div>

      <div v-if="status === 'pending'" class="flex flex-col gap-2">
        <Card v-for="i in 10" :key="i" class="p-2">
          <template #content>
            <Skeleton width="100%" height="50px" />
          </template>
        </Card>
      </div>

      <div v-else-if="error" class="text-center py-8">
        <Message severity="error">Failed to load vocabulary</Message>
      </div>

      <div v-else-if="response?.data?.words?.length === 0" class="text-center py-8">
        <Message severity="info">No vocabulary found. Complete some media to see your vocabulary!</Message>
      </div>

      <div v-else class="flex flex-col gap-2">
        <VocabularyEntry v-for="word in response?.data?.words" :key="`${word.wordId}-${word.mainReading.readingIndex}`" :word="word" :is-compact="true" />
      </div>

      <div class="flex gap-8 pl-2">
        <NuxtLink :to="previousLink" :class="previousLink == null ? '!text-gray-500 pointer-events-none' : ''" no-rel>Previous</NuxtLink>
        <NuxtLink :to="nextLink" :class="nextLink == null ? '!text-gray-500 pointer-events-none' : ''" @click="scrollToTop" no-rel>Next</NuxtLink>
      </div>
    </div>
  </div>
</template>
