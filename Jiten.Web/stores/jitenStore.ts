import { defineStore } from 'pinia';
import { DifficultyDisplayStyle, DifficultyValueDisplayStyle, TitleLanguage } from '~/types';

export const useJitenStore = defineStore('jiten', () => {
  const titleLanguageCookie = useCookie<TitleLanguage>('jiten-title-language', {
    default: () => TitleLanguage.Romaji,
    watch: true,
    maxAge: 60 * 60 * 24 * 365, // 1 year
    path: '/',
  });

  const titleLanguage = ref<TitleLanguage>(titleLanguageCookie.value);

  watch(titleLanguage, (newValue) => {
    titleLanguageCookie.value = newValue;
  });

  const displayFuriganaCookie = useCookie<boolean>('jiten-display-furigana', {
    default: () => true,
    watch: true,
    maxAge: 60 * 60 * 24 * 365, // 1 year
    path: '/',
  });

  const displayFurigana = ref<boolean>(displayFuriganaCookie.value);

  watch(displayFurigana, (newValue) => {
    displayFuriganaCookie.value = newValue;
  });

  const darkModeCookie = useCookie<boolean>('jiten-dark-mode', {
    default: () => false,
    watch: true,
    maxAge: 60 * 60 * 24 * 365, // 1 year
    path: '/',
  });

  const darkMode = ref<boolean>(darkModeCookie.value);

  watch(darkMode, (newValue) => {
    darkModeCookie.value = newValue;
  });

  const displayAdminFunctionsCookie = useCookie<boolean>('jiten-display-admin-functions', {
    default: () => false,
    watch: true,
    maxAge: 60 * 60 * 24 * 365, // 1 year
    path: '/',
  });

  const displayAdminFunctions = ref<boolean>(displayAdminFunctionsCookie.value);

  watch(displayAdminFunctions, (newValue) => {
    displayAdminFunctionsCookie.value = newValue;
  });

  const readingSpeedCookie = useCookie<number>('jiten-reading-speed', {
    default: () => 14000,
    watch: true,
    maxAge: 60 * 60 * 24 * 365, // 1 year
    path: '/',
  });

  const readingSpeed = ref<number>(readingSpeedCookie.value);

  watch(readingSpeed, (newValue) => {
    readingSpeedCookie.value = newValue;
  });

  const displayAllNsfwCookie = useCookie<boolean>('jiten-display-all-nsfw', {
    default: () => false,
    watch: true,
    maxAge: 60 * 60 * 24 * 365, // 1 year
    path: '/',
  });

  const displayAllNsfw = ref<boolean>(displayAllNsfwCookie.value);

  watch(displayAllNsfw, (newValue) => {
    displayAllNsfwCookie.value = newValue;
  });

  // Hide vocabulary definition
  const hideVocabularyDefinitionsCookie = useCookie<boolean>('jiten-hide-vocabulary-definitions', {
    default: () => false,
    watch: true,
    maxAge: 60 * 60 * 24 * 365, // 1 year
    path: '/',
  });

  const hideVocabularyDefinitions = ref<boolean>(hideVocabularyDefinitionsCookie.value);

  watch(hideVocabularyDefinitions, (newValue) => {
    hideVocabularyDefinitionsCookie.value = newValue;
  });

  // Borders display
  const hideCoverageBordersCookie = useCookie<boolean>('jiten-hide-coverage-borders', {
    default: () => false,
    watch: true,
    maxAge: 60 * 60 * 24 * 365, // 1 year
    path: '/',
  });

  const hideCoverageBorders = ref<boolean>(hideCoverageBordersCookie.value);

  watch(hideCoverageBorders, (newValue) => {
    hideCoverageBordersCookie.value = newValue;
  });

  // Genres display
  const hideGenresCookie = useCookie<boolean>('jiten-hide-genres', {
    default: () => false,
    watch: true,
    maxAge: 60 * 60 * 24 * 365, // 1 year
    path: '/',
  });

  const hideGenres = ref<boolean>(hideGenresCookie.value);

  watch(hideGenres, (newValue) => {
    hideGenresCookie.value = newValue;
  });

  // Tags display
  const hideTagsCookie = useCookie<boolean>('jiten-hide-tags', {
    default: () => false,
    watch: true,
    maxAge: 60 * 60 * 24 * 365, // 1 year
    path: '/',
  });

  const hideTags = ref<boolean>(hideTagsCookie.value);

  watch(hideTags, (newValue) => {
    hideTagsCookie.value = newValue;
  });

  // Relations display
  const hideRelationsCookie = useCookie<boolean>('jiten-hide-relations', {
    default: () => false,
    watch: true,
    maxAge: 60 * 60 * 24 * 365, // 1 year
    path: '/',
  });

  const hideRelations = ref<boolean>(hideRelationsCookie.value);

  watch(hideRelations, (newValue) => {
    hideRelationsCookie.value = newValue;
  });

  const difficultyDisplayStyleCookie = useCookie<DifficultyDisplayStyle>('jiten-difficulty-display-style', {
    default: () => 0,
    watch: true,
    maxAge: 60 * 60 * 24 * 365, // 1 year
    path: '/',
  });

  const difficultyDisplayStyle = ref<DifficultyDisplayStyle>(difficultyDisplayStyleCookie.value);

  watch(difficultyDisplayStyle, (newValue) => {
    difficultyDisplayStyleCookie.value = newValue;
  });

  const difficultyValueDisplayStyleCookie = useCookie<DifficultyValueDisplayStyle>('jiten-difficulty-value-display-style', {
    default: () => DifficultyValueDisplayStyle.ZeroToFive,
    watch: true,
    maxAge: 60 * 60 * 24 * 365, // 1 year
    path: '/',
  });

  // Migrate users from removed "1 to 6" option (value 0) to "0 to 5" (value 1)
  if (difficultyValueDisplayStyleCookie.value === 0) {
    difficultyValueDisplayStyleCookie.value = DifficultyValueDisplayStyle.ZeroToFive;
  }

  const difficultyValueDisplayStyle = ref<DifficultyValueDisplayStyle>(difficultyValueDisplayStyleCookie.value);

  watch(difficultyValueDisplayStyle, (newValue) => {
    difficultyValueDisplayStyleCookie.value = newValue;
  });


  const getKnownWordIds = (): number[] => {
    if (import.meta.client) {
      try {
        const stored = localStorage.getItem('jiten-known-word-ids');
        return stored ? JSON.parse(stored) : [];
      } catch (error) {
        console.error('Error reading known word IDs from localStorage:', error);
        return [];
      }
    }
    return [];
  };

  const knownWordIds = ref<number[]>([]);
  let isInitialized = false;

  const ensureInitialized = () => {
    if (!isInitialized && import.meta.client) {
      knownWordIds.value = getKnownWordIds();
      isInitialized = true;
    }
  };

  onMounted(() => {
    ensureInitialized();
  });

  return {
    getKnownWordIds,

    // state
    titleLanguage,
    displayFurigana,
    darkMode,
    displayAdminFunctions,
    readingSpeed,
    knownWordIds,
    displayAllNsfw,
    hideVocabularyDefinitions,
    hideCoverageBorders,
    hideGenres,
    hideTags,
    hideRelations,
    difficultyDisplayStyle,
    difficultyValueDisplayStyle
  };
});
