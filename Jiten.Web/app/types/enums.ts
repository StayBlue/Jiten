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

export enum Genre {
  Action = 1,
  Adventure = 2,
  Comedy = 3,
  Drama = 4,
  Ecchi = 5,
  Fantasy = 6,
  Horror = 7,
  Mecha = 8,
  Music = 9,
  Mystery = 10,
  Psychological = 11,
  Romance = 12,
  SciFi = 13,
  SliceOfLife = 14,
  Sports = 15,
  Supernatural = 16,
  Thriller = 17,
  AdultOnly = 18
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
  TargetCoverage = 5,
  OccurrenceCount = 6,
}

export enum DeckFormat {
  Anki = 1,
  Csv = 2,
  Txt = 3,
  TxtRepeated = 4,
  Yomitan = 5,
  Learn = 6,
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
  New = 0,
  Young = 1,
  Mature = 2,
  Blacklisted = 3,
  Due = 4,
  Mastered = 5
}

export enum DifficultyDisplayStyle {
  Name = 0,
  NameAndValue = 1,
  Value = 2
}

export enum DifficultyValueDisplayStyle {
  ZeroToFive = 1,
  Percentage = 2,
}

export enum ThemeMode {
  Light = 'light',
  Dark = 'dark',
  Auto = 'auto',
}

export enum DeckStatus {
  None = 0,
  Planning = 1,
  Ongoing = 2,
  Completed = 3,
  Dropped = 4,
}

export enum FsrsState {
  New = 0,
  Learning = 1,
  Review = 2,
  Relearning = 3,
  Blacklisted = 4,
  Mastered = 5,
}

export enum FsrsRating {
  Again = 1,
  Hard = 2,
  Good = 3,
  Easy = 4,
}

export enum DeckRelationshipType {
  Sequel = 1,
  Fandisc = 2,
  Spinoff = 3,
  SideStory = 4,
  Adaptation = 5,
  Alternative = 6,

  // Inverse relationships
  Prequel = 101,
  HasFandisc = 102,
  HasSpinoff = 103,
  HasSideStory = 104,
  SourceMaterial = 105
}

export enum WordSetStateType {
  Blacklisted = 1,
  Mastered = 2,
}
