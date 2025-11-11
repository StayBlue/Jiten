import { type DeckStatus, type KnownState, type MediaType, type ReadingType } from '~/types';

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
  linkType: string;
  deckId: number;
}

export interface Word {
  wordId: number;
  mainReading: Reading;
  alternativeReadings: Reading[];
  partsOfSpeech: string[];
  definitions: Definition[];
  occurrences: number;
  pitchAccents: number[];
  knownState: KnownState;
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
}

export interface Issues {
  missingRomajiTitles: number[];
  missingLinks: number[];
  zeroCharacters: number[];
  missingReleaseDate: number[];
  missingDescription: number[];
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
