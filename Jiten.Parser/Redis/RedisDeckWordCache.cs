using System.Text.Json;
using Jiten.Core.Data;
using Microsoft.Extensions.Configuration;
using StackExchange.Redis;

namespace Jiten.Parser.Data.Redis;

public class RedisDeckWordCache : IDeckWordCache
{
    private readonly IDatabase _redisDb;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public RedisDeckWordCache(IConfiguration configuration)
    {
        _redisDb = RedisConnectionManager.GetDatabase(configuration);
    }

    private string BuildRedisKey(DeckWordCacheKey key)
    {
        // Context flags are part of the key because they can affect dictionary matching (e.g., honorifics, name-likeness).
        return $"deckword:{key.Text}:{key.PartOfSpeech}:{key.DictionaryForm}:{(key.IsPersonNameContext ? 1 : 0)}:{(key.IsNameLikeSudachiNoun ? 1 : 0)}";
    }

    public async Task<DeckWord?> GetAsync(DeckWordCacheKey key)
    {
        var redisKey = BuildRedisKey(key);
        var json = await _redisDb.StringGetAsync(redisKey);
        if (json.IsNullOrEmpty)
            return null;

        return JsonSerializer.Deserialize<DeckWord>(json!, _jsonOptions);
    }

    public async Task SetAsync(DeckWordCacheKey key, DeckWord word, CommandFlags flags = CommandFlags.None)
    {
        var redisKey = BuildRedisKey(key);
        var json = JsonSerializer.Serialize(word, _jsonOptions);

        await _redisDb.StringSetAsync(redisKey, json, expiry: TimeSpan.FromDays(30), flags: flags);
    }
}
