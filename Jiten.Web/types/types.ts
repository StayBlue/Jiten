import { type DeckRelationshipType, type DeckStatus, type FsrsRating, type FsrsState, type Genre, type KnownState, type LinkType, type MediaType, type ReadingType } from '~/types';

export interface Deck {
  deckId: number;
  creationDate: Date;
  releaseDate: Date;
  coverName?: string;
  mediaType: MediaType;
  originalTitle: string;
  romajiTitle?: string;
  englishTitle?: string;
  description?: string;
  characterCount: number;
  wordCount: number;
  uniqueWordCount: number;
  uniqueWordUsedOnceCount: number;
  uniqueKanjiCount: number;
  uniqueKanjiUsedOnceCount: number;
  difficulty: number;
  difficultyRaw: number;
  difficultyOverride: number;
  averageSentenceLength: number;
  parentDeckId: number;
  deckWords: DeckWord[];
  links: Link[];
  aliases: string[];
  childrenDeckCount: number;
  selectedWordOccurrences: number;
  dialoguePercentage: number;
  coverage: number;
  uniqueCoverage: number;
  hideDialoguePercentage: boolean;
  externalRating: number;
  exampleSentence?: ExampleSentence;
  genres?: Genre[];
  tags?: TagWithPercentage[];
  relationships?: DeckRelationship[];
  status?: DeckStatus;
  isFavourite?: boolean;
  isIgnored?: boolean;
}

export interface DeckDetail {
  parentDeck: Deck | null;
  mainDeck: Deck;
  subDecks: Deck[];
}

export interface DeckVocabularyList {
  parentDeck: Deck | null;
  deck: Deck;
  words: DeckWord[];
}

export interface MediaSuggestion {
  deckId: number;
  originalTitle: string;
  romajiTitle?: string;
  englishTitle?: string;
  mediaType: MediaType;
  coverName: string;
}

export interface MediaSuggestionsResponse {
  suggestions: MediaSuggestion[];
  totalCount: number;
}

export interface DeckWord {
  deckId: number;
  originalText: string;
  wordId: number;
  readingType: string;
  readingIndex: number;
  conjugations: string[];
}

export interface ParseNormalisedResult {
  normalisedText: string;
  words: DeckWord[];
}

export interface Link {
  linkId: number;
  url: string;
  linkType: LinkType;
  deckId: number;
}

export interface TagWithPercentage {
  tagId: number;
  name: string;
  percentage: number;
}

export interface DeckRelationship {
  targetDeckId: number;
  targetDeck: Deck;
  relationshipType: DeckRelationshipType;
  isInverse: boolean;
}

export interface MetadataTag {
  name: string;
  percentage: number;
}

export interface Word {
  wordId: number;
  mainReading: Reading;
  alternativeReadings: Reading[];
  partsOfSpeech: string[];
  definitions: Definition[];
  occurrences: number;
  pitchAccents: number[];
  knownStates: KnownState[];
}

export interface Reading {
  text: string;
  readingType: ReadingType;
  readingIndex: number;
  frequencyRank: number;
  frequencyPercentage: number;
  usedInMediaAmount: number;
  usedInMediaAmountByType: Record<MediaType, number>;
}

export interface Definition {
  index: number;
  meanings: string[];
  partsOfSpeech: string[];
}

export class PaginatedResponse<T> {
  constructor(
    public readonly data: T,
    public readonly totalItems: number,
    public readonly pageSize: number,
    public readonly currentOffset: number
  ) {}

  get totalPages(): number {
    return Math.ceil(this.totalItems / this.pageSize);
  }

  get currentPage(): number {
    return Math.floor(this.currentOffset / this.pageSize) + 1;
  }

  get hasPreviousPage(): boolean {
    return this.currentPage > 1;
  }

  get hasNextPage(): boolean {
    return this.currentPage < this.totalPages;
  }

  get previousOffset(): number | null {
    return this.hasPreviousPage ? Math.max(0, this.currentOffset - this.pageSize) : null;
  }

  get nextOffset(): number | null {
    return this.hasNextPage ? Math.min(this.totalItems, this.currentOffset + this.pageSize) : null;
  }
}

export interface GlobalStats {
  mediaByType: Record<MediaType, number>;
  totalMojis: number;
  totalMedia: number;
}

export interface FsrsParametersResponse {
  parameters: string;
  isDefault: boolean;
  desiredRetention: number;
}

export interface SrsRecomputeBatchResponse {
  processed: number;
  total: number;
  lastCardId: number;
  done: boolean;
}

export interface MetadataRelation {
  externalId: string;
  linkType: number;
  relationshipType: number;
  targetMediaType?: number;
  swapDirection: boolean;
}

export interface Metadata {
  originalTitle: string;
  romajiTitle: string;
  englishTitle: string;
  image: string;
  releaseDate: string;
  description: string;
  links: Link[];
  aliases: string[];
  rating: number;
  genres?: string[];
  tags?: MetadataTag[];
  isAdultOnly?: boolean;
  isNotOriginallyJapanese?: boolean;
  relations?: MetadataRelation[];
}

export interface Issues {
  missingRomajiTitles: number[];
  missingLinks: number[];
  zeroCharacters: number[];
  missingReleaseDate: number[];
  missingDescription: number[];
  missingGenres: number[];
  missingTags: number[];
}

export interface LoginRequest {
  usernameOrEmail: string;
  password: string;
}

export interface TokenResponse {
  accessToken: string;
  accessTokenExpiration: Date;
  refreshToken: string;
}

export interface ExampleSentence {
  text: string;
  wordPosition: number;
  wordLength: number;
  sourceDeck: Deck;
  sourceDeckParent: Deck;
}

export interface GoogleSignInResponse {
  requiresRegistration?: boolean;
  tempToken?: string;
  email?: string;
  name?: string;
  picture?: string;
  accessToken?: string;
  refreshToken?: string;
}

export interface GoogleRegistrationData {
  tempToken: string;
  email: string;
  name: string;
  picture?: string;
  username: string;
}

export interface CompleteGoogleRegistrationRequest {
  tempToken: string;
  username: string;
  tosAccepted: boolean;
  receiveNewsletter: boolean;
}

export interface UserMetadata {
  coverageRefreshedAt?: Date;
}

export interface ApiKeyInfo {
  id: number;
  createdAt: string;
  lastUsedAt?: string;
  expiresAt?: string;
  isRevoked: boolean;
  keyPreview: string;
}

export interface CreateApiKeyResponse {
  apiKey: string;
  id: number;
  message: string;
}

export interface FsrsReviewLogExportDto {
  rating: FsrsRating;
  reviewDateTime: Date;
  reviewDuration?: number;
}

export interface FsrsCardExportDto {
  wordId: number;
  readingIndex: number;
  state: FsrsState;
  step?: number;
  stability?: number;
  difficulty?: number;
  due: Date;
  lastReview?: Date;
  reviewLogs: FsrsReviewLogExportDto[];
}

export interface FsrsExportDto {
  exportDate: Date;
  userId: string;
  totalCards: number;
  totalReviews: number;
  cards: FsrsCardExportDto[];
}

export interface FsrsCardWithWordDto {
  cardId: number;
  wordId: number;
  readingIndex: number;
  state: FsrsState;
  step?: number;
  stability?: number;
  difficulty?: number;
  due: Date;
  lastReview?: Date;
  wordText: string;
  readingType: ReadingType;
}

export interface FsrsImportResultDto {
  cardsImported: number;
  cardsSkipped: number;
  cardsUpdated: number;
  reviewLogsImported: number;
  validationErrors: string[];
}

export interface Tag {
  tagId: number;
  name: string;
}

export interface TagUsage {
  deckCount: number;
  mappingCount: number;
}

export interface GenreMapping {
  externalGenreMappingId: number;
  provider: LinkType;
  providerName: string;
  externalGenreName: string;
  jitenGenre: Genre;
  jitenGenreName: string;
}

export interface TagMapping {
  externalTagMappingId: number;
  provider: LinkType;
  providerName: string;
  externalTagName: string;
  tagId: number;
  tagName: string;
}

export interface TagMappingSummary {
  totalMappings: number;
  mappingsByProvider: Record<string, number>;
}

export interface DeckCoverageStats {
  deckId: number;
  totalUniqueWords: number;
  computedAt: Date;
  rSquared: number;
  milestones: Record<string, number>;
}

export interface CurveDatum {
  rank: number;
  coverage: number;
}

export interface UserProfile {
  userId: string;
  username: string;
  isPublic: boolean;
}

export interface UserAccomplishment {
  accomplishmentId: number;
  userId: string;
  mediaType: MediaType | null;
  completedDeckCount: number;
  totalCharacterCount: number;
  totalWordCount: number;
  uniqueWordCount: number;
  uniqueWordUsedOnceCount: number;
  uniqueKanjiCount: number;
  lastComputedAt: string;
}

export interface AccomplishmentVocabularyDto {
  words: Word[];
}

export interface Kanji {
  character: string;
  onReadings: string[];
  kunReadings: string[];
  meanings: string[];
  strokeCount: number;
  jlptLevel: number | null;
  grade: number | null;
  frequencyRank: number | null;
  topWords?: WordSummary[];
}

export interface KanjiList {
  character: string;
  meanings: string[];
  strokeCount: number;
  jlptLevel: number | null;
  frequencyRank: number | null;
}

export interface WordSummary {
  wordId: number;
  readingIndex: number;
  reading: string;
  readingFurigana: string;
  mainDefinition: string | null;
  frequencyRank: number | null;
}

export interface KanjiGridItem {
  character: string;
  frequencyRank: number | null;
  jlptLevel: number | null;
  score: number;
  wordCount: number;
}

export interface KanjiGridResponse {
  kanji: KanjiGridItem[];
  maxScoreThreshold: number;
  totalKanjiCount: number;
  seenKanjiCount: number;
  lastComputedAt: string | null;
}

export interface ProgressionSegmentDto {
  segment: number;
  difficulty: number;
  peak: number;
  childStartOrder?: number;
  childEndOrder?: number;
}

export interface DeckDifficultyDto {
  difficulty: number;
  peak: number;
  deciles: Record<string, number>;
  progression: ProgressionSegmentDto[];
  lastUpdated: Date;
}
