using Jiten.Core.Data;
using StackExchange.Redis;

namespace Jiten.Parser.Data.Redis;

public interface IDeckWordCache
{
    Task<DeckWord?> GetAsync(DeckWordCacheKey key);
    Task SetAsync(DeckWordCacheKey key, DeckWord word, CommandFlags flags = CommandFlags.None);
}

public record DeckWordCacheKey(string Text, PartOfSpeech PartOfSpeech, string DictionaryForm, bool IsPersonNameContext, bool IsNameLikeSudachiNoun);
