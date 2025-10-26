export enum MediaType {
  Anime = 1,
  Drama = 2,
  Movie = 3,
  Novel = 4,
  NonFiction = 5,
  VideoGame = 6,
  VisualNovel = 7,
  WebNovel = 8,
  Manga = 9,
}

export enum LinkType {
  Web = 1,
  Vndb = 2,
  Tmdb = 3,
  Anilist = 4,
  Mal = 5, // Myanimelist
  GoogleBooks = 6,
  Imdb = 7,
  Igdb = 8,
  Syosetsu = 9,
}

export enum ReadingType {
  Reading = 1,
  KanaReading = 2,
}

export enum DeckDownloadType {
  Full = 1,
  TopGlobalFrequency = 2,
  TopDeckFrequency = 3,
  TopChronological = 4,
}

export enum DeckFormat {
  Anki = 1,
  Csv = 2,
  Txt = 3,
  TxtRepeated = 4,
  Yomitan = 5,
}

export enum DeckOrder {
  Chronological = 1,
  GlobalFrequency = 2,
  DeckFrequency = 3,
}

export enum SortOrder {
  Ascending = 0,
  Descending = 1,
}

export enum TitleLanguage {
  Original = 0,
  Romaji = 1,
  English = 2,
}

export enum DisplayStyle {
  Card = 0,
  Compact = 1,
  Table = 2,
}

export enum KnownState {
  Unknown = 0,
  Young = 1,
  Mature = 2,
  Suspended = 3,
}

export enum DifficultyDisplayStyle {
  Name = 0,
  NameAndValue = 1,
  Value = 2
}

export enum DifficultyValueDisplayStyle {
  StartsAt1 = 0,
  StartsAt0 = 1,
  Percentage = 2,
}
