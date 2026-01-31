import { type Deck, TitleLanguage } from '~/types';

export function localiseTitleWithLanguage(deck: Deck, titleLanguage: TitleLanguage): string {
  if (titleLanguage === TitleLanguage.Original) {
    return deck.originalTitle;
  }

  if (titleLanguage === TitleLanguage.Romaji) {
    return deck.romajiTitle ?? deck.originalTitle;
  }

  if (titleLanguage === TitleLanguage.English) {
    return deck.englishTitle ?? deck.romajiTitle ?? deck.originalTitle;
  }

  return deck.originalTitle;
}
