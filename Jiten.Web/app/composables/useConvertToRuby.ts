import { useJitenStore } from '~/stores/jitenStore';
import { convertToRubyWithFurigana } from '~/utils/convertToRuby';

export function useConvertToRuby() {
  const store = useJitenStore();

  return (text: string): string => {
    return convertToRubyWithFurigana(text, store.displayFurigana);
  };
}
