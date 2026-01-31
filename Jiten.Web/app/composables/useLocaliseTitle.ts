import type { Deck } from '~/types';
import { useJitenStore } from '~/stores/jitenStore';
import { localiseTitleWithLanguage } from '~/utils/localiseTitle';

export function useLocaliseTitle() {
  const store = useJitenStore();

  return (deck: Deck): string => {
    return localiseTitleWithLanguage(deck, store.titleLanguage);
  };
}
