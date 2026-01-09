<script setup lang="ts">
  import { useAuthStore } from '~/stores/authStore';
  import { type UserProfile, type UserAccomplishment, MediaType } from '~/types';
  import { useToast } from 'primevue/usetoast';
  import { getMediaTypeText } from '~/utils/mediaTypeMapper';

  const route = useRoute();
  const auth = useAuthStore();
  const toast = useToast();
  const { $api } = useNuxtApp();

  const targetUsername = computed(() => route.params.username as string);
  const isOwnProfile = computed(() => auth.isAuthenticated && auth.user?.userName?.toLowerCase() === targetUsername.value.toLowerCase());

  const profile = ref<UserProfile | null>(null);
  const accomplishments = ref<UserAccomplishment[]>([]);
  const isLoading = ref(true);
  const error = ref<string | null>(null);
  const profileNotFound = ref(false);
  const isUpdatingVisibility = ref(false);

  const selectedTab = ref<string>('global');

  const fetchProfileData = async (username: string) => {
    profile.value = null;
    accomplishments.value = [];
    profileNotFound.value = false;
    error.value = null;
    isLoading.value = true;
    selectedTab.value = 'global';

    try {
      const profileResult = await $api<UserProfile>(`user/profile/${username}`);
      profile.value = profileResult;

      try {
        const accomplishmentsResult = await $api<UserAccomplishment[]>(`user/profile/${username}/accomplishments`);
        accomplishments.value = accomplishmentsResult || [];
      } catch (accErr: any) {
        if (accErr?.response?.status !== 404) {
          error.value = 'Failed to load accomplishments';
        }
      }
    } catch (err: any) {
      if (err?.response?.status === 404) {
        profileNotFound.value = true;
      } else {
        error.value = 'Failed to load profile';
      }
    } finally {
      isLoading.value = false;
    }
  };

  await fetchProfileData(targetUsername.value);

  watch(
    () => route.params.username,
    async (newUsername) => {
      if (newUsername && typeof newUsername === 'string') {
        await fetchProfileData(newUsername);
      }
    }
  );

  const globalAccomplishment = computed(() => accomplishments.value.find((a) => a.mediaType === null));

  const mediaTypeAccomplishments = computed(() => accomplishments.value.filter((a) => a.mediaType !== null));

  const availableTabs = computed(() => {
    const tabs = [{ label: 'Global', value: 'global' }];
    for (const acc of mediaTypeAccomplishments.value) {
      if (acc.mediaType !== null) {
        tabs.push({
          label: getMediaTypeText(acc.mediaType),
          value: acc.mediaType.toString(),
        });
      }
    }
    return tabs;
  });

  const selectedAccomplishment = computed(() => {
    if (selectedTab.value === 'global') {
      return globalAccomplishment.value;
    }
    const mediaType = parseInt(selectedTab.value);
    return accomplishments.value.find((a) => a.mediaType === mediaType);
  });

  const displayUsername = computed(() => profile.value?.username ?? targetUsername.value);

  const profileIsPublic = computed({
    get: () => profile.value?.isPublic ?? false,
    set: (value: boolean) => {
      if (profile.value) {
        profile.value.isPublic = value;
      }
    },
  });

  const toggleVisibility = async () => {
    if (!isOwnProfile.value || isUpdatingVisibility.value || !profile.value) return;

    isUpdatingVisibility.value = true;
    try {
      const newIsPublic = profile.value.isPublic;
      const result = await $api<UserProfile>('user/profile', {
        method: 'PATCH',
        body: { isPublic: newIsPublic },
      });
      profile.value = result;
      toast.add({
        severity: 'success',
        summary: newIsPublic ? 'Your profile is now public and accessible to anyone with the link' : 'Your profile is now private and visible only to you',
        life: 3000,
      });
    } catch (err) {
      toast.add({
        severity: 'error',
        summary: 'Failed to update profile visibility',
        life: 5000,
      });
    } finally {
      isUpdatingVisibility.value = false;
    }
  };

  const formatNumber = (num: number | undefined) => {
    if (num === undefined) return '0';
    return num.toLocaleString();
  };

  useHead(() => ({
    title: `Profile - ${displayUsername.value}`,
    meta: [{ name: 'description', content: `User profile for ${displayUsername.value}` }],
  }));
</script>

<template>
  <div class="container mx-auto p-4">
    <div v-if="isLoading" class="flex justify-center items-center min-h-[50vh]">
      <ProgressSpinner />
    </div>

    <div v-else-if="profileNotFound" class="text-center py-16">
      <Card>
        <template #content>
          <div class="flex flex-col items-center gap-4">
            <Icon name="material-symbols:person-off" size="4rem" class="text-gray-400" />
            <h2 class="text-xl font-semibold">Profile not found</h2>
            <p class="text-gray-500">This profile does not exist or is set to private.</p>
            <NuxtLink to="/">
              <Button label="Go Home" icon="pi pi-home" />
            </NuxtLink>
          </div>
        </template>
      </Card>
    </div>

    <div v-else-if="error" class="text-center py-8">
      <Message severity="error">{{ error }}</Message>
    </div>

    <div v-else-if="profile" class="flex flex-col gap-6">
      <Card>
        <template #content>
          <div class="flex justify-between items-center flex-wrap gap-4">
            <div>
              <h1 class="text-2xl font-bold">{{ displayUsername }}</h1>
              <p class="text-gray-500 text-sm">
                <span v-if="profile.isPublic">Public profile</span>
                <span v-else>Private profile</span>
              </p>
            </div>
            <div v-if="isOwnProfile" class="flex items-center gap-3">
              <label for="visibility-toggle" class="text-sm">Public profile</label>
              <ToggleSwitch v-model="profileIsPublic" input-id="visibility-toggle" :disabled="isUpdatingVisibility" @change="toggleVisibility" />
            </div>
          </div>
        </template>
      </Card>

      <div v-if="accomplishments.length === 0" class="text-center py-8">
        <Message severity="info">No accomplishments yet. Complete some media to see your stats!</Message>
      </div>

      <template v-else>
        <Tabs v-model:value="selectedTab">
          <TabList class="flex-wrap">
            <Tab v-for="tab in availableTabs" :key="tab.value" :value="tab.value">
              {{ tab.label }}
            </Tab>
          </TabList>
        </Tabs>

        <div v-if="selectedAccomplishment" class="grid grid-cols-2 md:grid-cols-3 gap-4">
          <Card class="text-center">
            <template #content>
              <div class="text-3xl font-bold tabular-nums text-purple-600 dark:text-purple-400">
                {{ formatNumber(selectedAccomplishment.completedDeckCount) }}
              </div>
              <div class="text-sm text-gray-500 mt-1">Completed</div>
            </template>
          </Card>

          <Card class="text-center">
            <template #content>
              <div class="text-3xl font-bold tabular-nums text-purple-600 dark:text-purple-400">
                {{ formatNumber(selectedAccomplishment.totalCharacterCount) }}
              </div>
              <div class="text-sm text-gray-500 mt-1">Characters</div>
            </template>
          </Card>

          <Card class="text-center">
            <template #content>
              <div class="text-3xl font-bold tabular-nums text-purple-600 dark:text-purple-400">
                {{ formatNumber(selectedAccomplishment.totalWordCount) }}
              </div>
              <div class="text-sm text-gray-500 mt-1">Words</div>
            </template>
          </Card>

          <Card class="text-center">
            <template #content>
              <div class="text-3xl font-bold tabular-nums text-purple-600 dark:text-purple-400">
                {{ formatNumber(selectedAccomplishment.uniqueWordCount) }}
              </div>
              <div class="text-sm text-gray-500 mt-1">Unique Words</div>
            </template>
          </Card>

          <Card class="text-center">
            <template #content>
              <div class="text-3xl font-bold tabular-nums text-purple-600 dark:text-purple-400">
                {{ formatNumber(selectedAccomplishment.uniqueWordUsedOnceCount) }}
              </div>
              <div class="text-sm text-gray-500 mt-1">1-Occurrence Words</div>
            </template>
          </Card>

          <Card class="text-center">
            <template #content>
              <div class="text-3xl font-bold tabular-nums text-purple-600 dark:text-purple-400">
                {{ formatNumber(selectedAccomplishment.uniqueKanjiCount) }}
              </div>
              <div class="text-sm text-gray-500 mt-1">Unique Kanji</div>
            </template>
          </Card>
        </div>

        <div v-else class="text-center py-4">
          <Message severity="info">No data for this category</Message>
        </div>

        <div v-if="isOwnProfile" class="flex justify-center">
          <NuxtLink :to="{ path: `/profile/${displayUsername}/vocabulary`, query: selectedTab !== 'global' ? { mediaType: selectedTab } : {} }">
            <Button label="View Vocabulary" icon="pi pi-list" />
          </NuxtLink>
        </div>

        <div v-if="selectedAccomplishment?.lastComputedAt" class="text-center text-sm text-gray-400">
          Last updated: {{ new Date(selectedAccomplishment.lastComputedAt).toLocaleDateString() }}
        </div>
      </template>
    </div>
  </div>
</template>
