import { type Deck, TitleLanguage } from '~/types';

export function localiseTitle(deck: Deck): string {
  const store = useJitenStore();

  if (store.titleLanguage === TitleLanguage.Original) {
    return deck.originalTitle;
  }

  if (store.titleLanguage === TitleLanguage.Romaji) {
    if (deck.romajiTitle) {
      return deck.romajiTitle;
    } else {
      return deck.originalTitle;
    }
  }

  if (store.titleLanguage === TitleLanguage.English) {
    if (deck.englishTitle) {
      return deck.englishTitle;
    } else if (deck.romajiTitle) {
      return deck.romajiTitle;
    } else {
      return deck.originalTitle;
    }
  }
}
