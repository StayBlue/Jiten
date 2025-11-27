<script setup lang="ts">
  import Button from 'primevue/button';

  import { useJitenStore } from '~/stores/jitenStore';
  import { useAuthStore } from '~/stores/authStore';

  const store = useJitenStore();
  const auth = useAuthStore();
  const tokenCookie = useCookie('token');

  // Mobile menu state
  const mobileMenuOpen = ref(false);
  const toggleMobileMenu = () => (mobileMenuOpen.value = !mobileMenuOpen.value);

  // Close mobile menu on route change
  const route = useRoute();
  watch(
    () => route.fullPath,
    () => {
      mobileMenuOpen.value = false;
    }
  );

  onMounted(() => {
    if (store.darkMode) {
      document.documentElement.classList.add('dark-mode');
    }
  });

  function toggleDarkMode() {
    document.documentElement.classList.toggle('dark-mode');
    store.darkMode = !store.darkMode;
  }

  const settings = ref();

  const titleLanguageOptions = ref([
    { label: 'Japanese', value: 0 },
    { label: 'Romaji', value: 1 },
    { label: 'English', value: 2 },
  ]);

  const difficultyDisplayStyleOptions = ref([
    { label: 'Name only', value: 0 },
    { label: 'Name and value', value: 1 },
    { label: 'Value only', value: 2 },
  ]);

  const difficultyValueDisplayStyleOptions = ref([
    { label: '1 to 6', value: 0 },
    { label: '0 to 5', value: 1 },
    { label: 'Percentage', value: 2 },
  ]);

  const titleLanguage = computed({
    get: () => store.titleLanguage,
    set: (value) => (store.titleLanguage = value),
  });

  const displayFurigana = computed({
    get: () => store.displayFurigana,
    set: (value) => (store.displayFurigana = value),
  });

  const displayAllNsfw = computed({
    get: () => store.displayAllNsfw,
    set: (value) => (store.displayAllNsfw = value),
  });

  const hideVocabularyDefinitions = computed({
    get: () => store.hideVocabularyDefinitions,
    set: (value) => (store.hideVocabularyDefinitions = value),
  });

  const hideCoverageBorders = computed({
    get: () => store.hideCoverageBorders,
    set: (value) => (store.hideCoverageBorders = value),
  });

  const hideGenres = computed({
    get: () => store.hideGenres,
    set: (value) => (store.hideGenres = value),
  });

  const hideTags = computed({
    get: () => store.hideTags,
    set: (value) => (store.hideTags = value),
  });

  const displayAdminFunctions = computed({
    get: () => store.displayAdminFunctions,
    set: (value) => (store.displayAdminFunctions = value),
  });

  const readingSpeed = computed({
    get: () => store.readingSpeed,
    set: (value) => (store.readingSpeed = value),
  });

  const difficultyDisplayStyle = computed({
    get: () => store.difficultyDisplayStyle,
    set: (value) => (store.difficultyDisplayStyle = value),
  });

  const difficultyValueDisplayStyle = computed({
    get: () => store.difficultyValueDisplayStyle,
    set: (value) => (store.difficultyValueDisplayStyle = value),
  });

  const isOverSettings = ref(false);
  const isSettingsInteracted = ref(false);

  const onSettingsMouseEnter = () => {
    isOverSettings.value = true;
  };

  const onSettingsMouseLeave = () => {
    isOverSettings.value = false;
    setTimeout(() => {
      if (!isOverSettings.value && !isSettingsInteracted.value) {
        settings.value.hide();
      }
    }, 750);
  };

  const toggleSettings = (event: boolean) => {
    settings.value.toggle(event);
  };

  const showSettings = (event: boolean) => {
    settings.value.show(event);
  };
</script>

<template>
  <header>
    <div class="bg-indigo-900">
      <div class="flex justify-between items-center mb-6 mx-auto p-4 max-w-6xl">
        <NuxtLink to="/" class="!no-underline">
          <h1 class="text-2xl font-bold text-white">Jiten <span class="text-red-600 text-xs align-super">beta</span></h1>
        </NuxtLink>

        <!-- Desktop nav -->
        <nav class="hidden md:flex items-center space-x-4">
          <nuxt-link to="/" class="!text-white">Home</nuxt-link>
          <nuxt-link to="/decks/media" class="!text-white">Media</nuxt-link>
          <nuxt-link v-if="auth.isAuthenticated" to="/settings" class="!text-white">Settings</nuxt-link>
          <nuxt-link to="/other" class="!text-white">Tools</nuxt-link>
          <nuxt-link to="/faq" class="!text-white">FAQ</nuxt-link>
          <nuxt-link v-if="auth.isAuthenticated && auth.isAdmin && store.displayAdminFunctions" to="/Dashboard" class="!text-white">Dashboard</nuxt-link>
          <nuxt-link v-if="auth.isAuthenticated" to="/" class="!text-white" @click="auth.logout()"> Logout </nuxt-link>
          <nuxt-link v-else to="/login" class="!text-white">Login</nuxt-link>
          <Button
            type="button"
            label="Settings"
            severity="secondary"
            @mouseover="showSettings($event)"
            @mouseleave="onSettingsMouseLeave"
            @click="toggleSettings($event)"
          >
            <Icon name="material-symbols-light:settings" />
          </Button>

          <Button label="Toggle Dark Mode" severity="secondary" @click="toggleDarkMode()">
            <Icon name="line-md:light-dark" />
          </Button>
        </nav>

        <!-- Mobile hamburger button -->
        <button
          class="md:hidden inline-flex items-center justify-center p-2 rounded text-white hover:bg-indigo-800 focus:outline-none focus:ring-2 focus:ring-white"
          @click="toggleMobileMenu"
          aria-label="Toggle navigation menu"
          :aria-expanded="mobileMenuOpen.toString()"
        >
          <Icon :name="mobileMenuOpen ? 'material-symbols:close' : 'material-symbols:menu'" size="28" />
        </button>
      </div>

      <!-- Mobile menu panel -->
      <div v-if="mobileMenuOpen" class="md:hidden mx-auto max-w-6xl px-4 pb-4">
        <div class="bg-indigo-800 rounded-lg shadow-lg divide-y divide-indigo-700">
          <div class="flex flex-col py-2">
            <nuxt-link to="/" class="py-2 px-3 !text-white" @click="mobileMenuOpen = false">Home</nuxt-link>
            <nuxt-link to="/decks/media" class="py-2 px-3 !text-white" @click="mobileMenuOpen = false">Media</nuxt-link>
            <nuxt-link v-if="auth.isAuthenticated" to="/settings" class="py-2 px-3 !text-white" @click="mobileMenuOpen = false">Settings</nuxt-link>
            <nuxt-link to="/other" class="py-2 px-3 !text-white" @click="mobileMenuOpen = false">Other</nuxt-link>
            <nuxt-link to="/faq" class="py-2 px-3 !text-white" @click="mobileMenuOpen = false">FAQ</nuxt-link>
            <nuxt-link
              v-if="auth.isAuthenticated && auth.isAdmin && store.displayAdminFunctions"
              to="/Dashboard"
              class="py-2 px-3 !text-white"
              @click="mobileMenuOpen = false"
              >Dashboard</nuxt-link
            >
            <nuxt-link
              v-if="auth.isAuthenticated"
              to="/"
              class="py-2 px-3 !text-white"
              @click="auth.logout(); mobileMenuOpen = false"
              >Logout</nuxt-link
            >
            <nuxt-link v-else to="/login" class="py-2 px-3 !text-white" @click="mobileMenuOpen = false">Login</nuxt-link>
          </div>
          <div class="flex items-center gap-3 py-3 px-3">
            <Button
              type="button"
              label="Settings"
              severity="secondary"
              class="w-full justify-center"
              @click="toggleSettings($event)"
            >
              <Icon name="material-symbols-light:settings" />
            </Button>
            <Button label="Dark Mode" severity="secondary" class="w-full justify-center" @click="toggleDarkMode()">
              <Icon name="line-md:light-dark" />
            </Button>
          </div>
        </div>
      </div>
    </div>
  </header>

  <Popover ref="settings" @mouseenter="onSettingsMouseEnter" @mouseleave="onSettingsMouseLeave" :pt="{ root: { class: 'w-[90vw] max-w-sm md:w-auto' }, content: { class: 'p-3 md:p-4 max-h-[80vh] overflow-y-auto' } }">
    <div class="flex flex-col gap-2">
      <div class="flex justify-between items-center mb-2 md:hidden">
        <span class="font-semibold text-base">Settings</span>
        <Button icon="pi pi-times" text rounded size="small" @click="settings.hide()" aria-label="Close settings" />
      </div>
      <FloatLabel variant="on" class="">
        <Select
          v-model="titleLanguage"
          :options="titleLanguageOptions"
          option-label="label"
          option-value="value"
          placeholder="Titles Language"
          input-id="titleLanguage"
          @show="isSettingsInteracted = true"
          @hide="isSettingsInteracted = false"
        />
        <label for="titleLanguage">Titles Language</label>
      </FloatLabel>

      <Divider class="!my-1 md:!my-2 !mx-2" />

      <div class="flex flex-col gap-2 md:gap-4">
        <label for="readingSpeed" class="text-sm font-medium">Reading Speed (chars/hour)</label>
        <div class="w-full">
          <InputNumber v-model="readingSpeed" show-buttons :min="100" :max="100000" :step="100" size="small" class="w-full" fluid />
        </div>
        <div class="w-full px-1">
          <Slider v-model="readingSpeed" :min="100" :max="100000" :step="100" class="w-full" />
        </div>
      </div>

      <Divider class="!my-1 md:!my-2 !mx-2" />

      <div class="flex items-center gap-2 py-1">
        <Checkbox v-model="displayFurigana" input-id="displayFurigana" name="furigana" :binary="true" />
        <label for="displayFurigana" class="text-sm cursor-pointer">Display Furigana</label>
      </div>

      <div class="flex items-center gap-2 py-1">
        <Checkbox v-model="hideVocabularyDefinitions" input-id="hideVocabularyDefinitions" name="hideVocabularyDefinitions" :binary="true" />
        <label for="hideVocabularyDefinitions" class="text-sm cursor-pointer">Hide Vocabulary Definitions</label>
      </div>

      <div class="flex items-center gap-2 py-1">
        <Checkbox v-model="displayAllNsfw" input-id="displayAllNsfw" name="nsfw" :binary="true" />
        <label for="displayAllNsfw" class="text-sm cursor-pointer">Unblur all NSFW sentences</label>
      </div>

      <div v-if="auth.isAuthenticated" class="flex items-center gap-2 py-1">
        <Checkbox v-model="hideCoverageBorders" input-id="hideCoverageBorders" name="hideCoverageBorders" :binary="true" />
        <label for="hideCoverageBorders" class="text-sm cursor-pointer">Hide coverage borders</label>
      </div>

      <div v-if="auth.isAuthenticated" class="flex items-center gap-2 py-1">
        <Checkbox v-model="hideGenres" input-id="hideGenres" name="hideGenres" :binary="true" />
        <label for="hideGenres" class="text-sm cursor-pointer">Hide genres</label>
      </div>

      <div v-if="auth.isAuthenticated" class="flex items-center gap-2 py-1">
        <Checkbox v-model="hideTags" input-id="hideTags" name="hideTags" :binary="true" />
        <label for="hideTags" class="text-sm cursor-pointer">Hide tags</label>
      </div>

      <div v-if="auth.isAuthenticated && auth.isAdmin" class="flex items-center gap-2 py-1">
        <Checkbox v-model="displayAdminFunctions" input-id="displayAdminFunctions" name="adminFunctions" :binary="true" />
        <label for="displayAdminFunctions" class="text-sm cursor-pointer">Display admin functions</label>
      </div>

      <Divider class="!my-1 md:!my-2 !mx-2" />

      <FloatLabel variant="on" class="">
        <Select
          v-model="difficultyDisplayStyle"
          :options="difficultyDisplayStyleOptions"
          option-label="label"
          option-value="value"
          placeholder="Difficulty Style"
          input-id="difficultyDisplayStyle"
          @show="isSettingsInteracted = true"
          @hide="isSettingsInteracted = false"
        />
        <label for="difficultyDisplayStyle">Difficulty Style</label>
      </FloatLabel>

      <FloatLabel variant="on" class="">
        <Select
          v-model="difficultyValueDisplayStyle"
          :options="difficultyValueDisplayStyleOptions"
          option-label="label"
          option-value="value"
          placeholder="Difficulty Value Style"
          input-id="difficultyValueDisplayStyle"
          @show="isSettingsInteracted = true"
          @hide="isSettingsInteracted = false"
        />
        <label for="difficultyValueDisplayStyle">Difficulty Value Style</label>
      </FloatLabel>
    </div>
  </Popover>
</template>

<style scoped></style>
