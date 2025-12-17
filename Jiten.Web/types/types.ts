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

export interface DeckWord {
  deckId: number;
  originalText: string;
  wordId: number;
  readingType: string;
  readingIndex: number;
  conjugations: string[];
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
  targetTitle: string;
  relationshipType: DeckRelationshipType;
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
