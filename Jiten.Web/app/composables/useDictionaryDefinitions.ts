import type { Definition } from '~/types';
import type { ResolvedDefinitionGroup } from './useYomitanDictionary';
import { JMDICT_DICTIONARY_ID } from './useYomitanDictionary';

function parseFurigana(furiganaText: string): { expression: string; reading: string } {
  if (!furiganaText) return { expression: '', reading: '' };

  const expression = furiganaText.replace(/\[.*?\]/g, '');
  const reading = furiganaText.replace(/([^\[\]]+)\[([^\]]+)\]/g, '$2').replace(/\[|\]/g, '');

  return { expression, reading };
}

export function useDictionaryDefinitions(
  furiganaText: Ref<string | undefined> | ComputedRef<string | undefined>,
  jmDictDefinitions: Ref<Definition[] | undefined> | ComputedRef<Definition[] | undefined>,
) {
  const { resolveDefinitions, hasCustomDictionaries, loadDictionaries, dictionaries } = useYomitanDictionary();
  const resolvedGroups = ref<ResolvedDefinitionGroup[]>([]);

  if (import.meta.client) {
    const resolve = async () => {
      const text = unref(furiganaText);
      const defs = unref(jmDictDefinitions);

      if (!text) {
        resolvedGroups.value = [];
        return;
      }

      if (dictionaries.value.length === 0) {
        await loadDictionaries();
      }

      if (!hasCustomDictionaries.value) {
        if (defs && defs.length > 0) {
          resolvedGroups.value = [{
            dictionaryId: JMDICT_DICTIONARY_ID,
            dictionaryName: 'Default',
            isJmDict: true,
            jmDictDefinitions: defs,
          }];
        } else {
          resolvedGroups.value = [];
        }
        return;
      }

      const { expression, reading } = parseFurigana(text);
      resolvedGroups.value = await resolveDefinitions(expression, reading, defs);
    };

    watch([furiganaText, jmDictDefinitions], resolve, { immediate: true });
  }

  return { resolvedGroups: resolvedGroups as Readonly<Ref<ResolvedDefinitionGroup[]>> };
}
