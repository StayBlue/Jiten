using System.Text.Json;
using Jiten.Core;
using Jiten.Core.Data.JMDict;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using StackExchange.Redis;

namespace Jiten.Parser.Data.Redis;

public class RedisJmDictCache : IJmDictCache
{
    private readonly IDatabase _redisDb;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private readonly TimeSpan _cacheExpiry = TimeSpan.FromDays(30); // Long cache time for dictionary data
    private const string InitializedKey = "jmdict:initialized";
    private readonly IDbContextFactory<JitenDbContext> _contextFactory;

    // Semaphore to limit concurrent database access
    private static readonly SemaphoreSlim DbSemaphore = new SemaphoreSlim(10, 10);
    private static readonly Random Jitter = new Random();

    public RedisJmDictCache(IConfiguration configuration, IDbContextFactory<JitenDbContext> contextFactory)
    {
        var connection = ConnectionMultiplexer.Connect(configuration.GetConnectionString("Redis")!);
        _redisDb = connection.GetDatabase();
        _contextFactory = contextFactory;
    }

    private string BuildLookupKey(string lookupText)
    {
        return $"jmdict:lookup:{lookupText}";
    }

    private string BuildWordKey(int wordId)
    {
        return $"jmdict:word:{wordId}";
    }

    public async Task<List<int>> GetLookupIdsAsync(string key)
    {
        var redisKey = BuildLookupKey(key);
        var json = await _redisDb.StringGetAsync(redisKey);
        if (json.IsNullOrEmpty)
        {
            await using var dbContext = await _contextFactory.CreateDbContextAsync();
            // Fetch the lookup from database
            var lookupIds = await dbContext.Lookups
                                           .AsNoTracking()
                                           .Where(l => l.LookupKey == key)
                                           .Select(l => l.WordId)
                                           .ToListAsync();

            // Cache the result if found
            if (lookupIds.Any())
            {
                var newJson = JsonSerializer.Serialize(lookupIds, _jsonOptions);
                await _redisDb.StringSetAsync(redisKey, newJson, expiry: _cacheExpiry);
            }

            return lookupIds;
        }

        return JsonSerializer.Deserialize<List<int>>(json!, _jsonOptions) ?? new List<int>();
    }

    public async Task<Dictionary<string, List<int>>> GetLookupIdsAsync(IEnumerable<string> keys)
    {
        var uniqueKeys = keys.Distinct().ToList();
        if (!uniqueKeys.Any())
        {
            return new Dictionary<string, List<int>>();
        }

        var redisKeys = uniqueKeys.Select(k => (RedisKey)BuildLookupKey(k)).ToArray();

        // 1. Fetch all keys from Redis in a single MGET command
        var redisValues = await _redisDb.StringGetAsync(redisKeys);

        var results = new Dictionary<string, List<int>>();
        var missedKeys = new List<string>();

        // 2. Process the results from Redis
        for (int i = 0; i < redisKeys.Length; i++)
        {
            var key = uniqueKeys[i];
            var value = redisValues[i];

            if (value.IsNullOrEmpty)
            {
                missedKeys.Add(key);
            }
            else
            {
                results[key] = JsonSerializer.Deserialize<List<int>>(value!, _jsonOptions) ?? new List<int>();
            }
        }

        // 3. If any keys were not in the cache, fetch them from the database in a single query
        if (missedKeys.Any())
        {
            await using var dbContext = await _contextFactory.CreateDbContextAsync();

            var dbLookups = await dbContext.Lookups
                                           .AsNoTracking()
                                           .Where(l => missedKeys.Contains(l.LookupKey))
                                           .Select(l => new { l.LookupKey, l.WordId })
                                           .ToListAsync();

            var dbResults = dbLookups
                            .GroupBy(l => l.LookupKey)
                            .ToDictionary(g => g.Key, g => g.Select(l => l.WordId).ToList());

            // 4. Add the database results to our main results and prepare to cache them
            var cacheBatch = _redisDb.CreateBatch();
            foreach (var kvp in dbResults)
            {
                results[kvp.Key] = kvp.Value;
                var redisKey = BuildLookupKey(kvp.Key);
                var json = JsonSerializer.Serialize(kvp.Value, _jsonOptions);
                // Don't await here, just add to the batch
                cacheBatch.StringSetAsync(redisKey, json, expiry: _cacheExpiry);
            }

            // Execute the batch to write all new entries to Redis
            cacheBatch.Execute();
        }

        return results;
    }

    public async Task<JmDictWord?> GetWordAsync(int wordId)
    {
        var redisKey = BuildWordKey(wordId);
        var json = await _redisDb.StringGetAsync(redisKey);
        if (json.IsNullOrEmpty)
        {
            await using var dbContext = await _contextFactory.CreateDbContextAsync();

            // Fetch the word from database
            var word = await dbContext.JMDictWords
                                      .AsNoTracking()
                                      .FirstOrDefaultAsync(w => w.WordId == wordId);

            // Cache the result if found
            if (word != null)
            {
                var newJson = JsonSerializer.Serialize(word, _jsonOptions);
                await _redisDb.StringSetAsync(redisKey, newJson, expiry: _cacheExpiry);
            }

            return word;
        }

        return JsonSerializer.Deserialize<JmDictWord>(json!, _jsonOptions);
    }
    
    
    public async Task<Dictionary<int, JmDictWord>> GetWordsAsync(IEnumerable<int> wordIds)
    {
        var uniqueIds = wordIds.Distinct().ToList();
        if (!uniqueIds.Any())
        {
            return new Dictionary<int, JmDictWord>();
        }
    
        var redisKeys = uniqueIds.Select(id => (RedisKey)BuildWordKey(id)).ToArray();
        var redisValues = await _redisDb.StringGetAsync(redisKeys);
    
        var results = new Dictionary<int, JmDictWord>();
        var missedIds = new List<int>();
    
        for (int i = 0; i < redisKeys.Length; i++)
        {
            var id = uniqueIds[i];
            var value = redisValues[i];
    
            if (value.IsNullOrEmpty)
            {
                missedIds.Add(id);
            }
            else
            {
                var word = JsonSerializer.Deserialize<JmDictWord>(value!, _jsonOptions);
                if (word != null)
                {
                    results[id] = word;
                }
            }
        }
    
        if (missedIds.Any())
        {
            // Process missed IDs in batches to avoid overwhelming the database
            const int batchSize = 1000;
            
            for (int i = 0; i < missedIds.Count; i += batchSize)
            {
                var batchIds = missedIds.Skip(i).Take(batchSize).ToList();
                
                // Try to acquire semaphore with timeout
                if (!await DbSemaphore.WaitAsync(TimeSpan.FromSeconds(5)))
                {
                    // If semaphore acquisition times out, return what we have so far
                    // This prevents blocking indefinitely and contributes to graceful degradation
                    continue;
                }
                
                try
                {
                    // Retry logic for database operations
                    const int maxRetries = 3;
                    for (int retry = 0; retry < maxRetries; retry++)
                    {
                        try
                        {
                            await using var dbContext = await _contextFactory.CreateDbContextAsync();

                            // Set a timeout for the command to avoid long-running queries
                            dbContext.Database.SetCommandTimeout(TimeSpan.FromSeconds(5));
                            
                            var dbWords = await dbContext.JMDictWords
                                .AsNoTracking()
                                .Where(w => batchIds.Contains(w.WordId))
                                .ToListAsync();
    
                            if (dbWords.Any())
                            {
                                // Cache the results in Redis
                                var cacheBatch = _redisDb.CreateBatch();
                                foreach (var word in dbWords)
                                {
                                    results[word.WordId] = word;
                                    var redisKey = BuildWordKey(word.WordId);
                                    var json = JsonSerializer.Serialize(word, _jsonOptions);
                                    // Use FireAndForget to avoid waiting for Redis response
                                    cacheBatch.StringSetAsync(redisKey, json, expiry: _cacheExpiry, flags: CommandFlags.FireAndForget);
                                }
                                cacheBatch.Execute();
                            }
                            
                            // If we get here, the operation was successful, so break out of the retry loop
                            break;
                        }
                        catch (Npgsql.PostgresException pgEx) when (pgEx.SqlState == "53300" && retry < maxRetries - 1)
                        {
                            // Connection limit reached, wait with exponential backoff before retrying
                            var backoffMs = (int)Math.Pow(2, retry) * 100 + Jitter.Next(50);
                            await Task.Delay(backoffMs);
                        }
                        catch when (retry < maxRetries - 1)
                        {
                            // For other transient errors, also retry with backoff
                            var backoffMs = (int)Math.Pow(2, retry) * 200 + Jitter.Next(100);
                            await Task.Delay(backoffMs);
                        }
                    }
                }
                finally
                {
                    DbSemaphore.Release();
                }
            }
        }

        return results;
    }

    public async Task<bool> SetLookupIdsAsync(Dictionary<string, List<int>> lookups)
    {
        var batch = _redisDb.CreateBatch();
        var tasks = new List<Task<bool>>();

        foreach (var lookup in lookups)
        {
            var redisKey = BuildLookupKey(lookup.Key);
            var json = JsonSerializer.Serialize(lookup.Value, _jsonOptions);
            tasks.Add(batch.StringSetAsync(redisKey, json, expiry: _cacheExpiry));
        }

        batch.Execute();
        await Task.WhenAll(tasks);

        return tasks.All(t => t.Result);
    }

    public async Task<bool> SetWordAsync(int wordId, JmDictWord word)
    {
        var redisKey = BuildWordKey(wordId);
        var json = JsonSerializer.Serialize(word, _jsonOptions);
        return await _redisDb.StringSetAsync(redisKey, json, expiry: _cacheExpiry);
    }

    public async Task<bool> SetWordsAsync(Dictionary<int, JmDictWord> words)
    {
        var batch = _redisDb.CreateBatch();
        var tasks = new List<Task<bool>>();

        foreach (var (wordId, word) in words)
        {
            var redisKey = BuildWordKey(wordId);
            var json = JsonSerializer.Serialize(word, _jsonOptions);
            tasks.Add(batch.StringSetAsync(redisKey, json, expiry: _cacheExpiry));
        }

        batch.Execute();
        await Task.WhenAll(tasks);

        return tasks.All(t => t.Result);
    }

    public async Task<bool> IsCacheInitializedAsync()
    {
        return await _redisDb.KeyExistsAsync(InitializedKey);
    }

    public async Task SetCacheInitializedAsync()
    {
        await _redisDb.StringSetAsync(InitializedKey, "1", expiry: _cacheExpiry);
    }
}