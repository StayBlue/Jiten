using System.Text;
using CsvHelper;
using Jiten.Core;
using Jiten.Core.Data;
using Jiten.Core.Data.JMDict;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using Hangfire;
using Jiten.Core.Data.User;

namespace Jiten.Api.Jobs;

public class ComputationJob(
    IDbContextFactory<JitenDbContext> contextFactory,
    IDbContextFactory<UserDbContext> userContextFactory,
    IConfiguration configuration,
    IBackgroundJobClient backgroundJobs)
{
    private static readonly object CoverageComputeLock = new();
    private static readonly HashSet<string> CoverageComputingUserIds = new();

    [Queue("coverage")]
    public async Task DailyUserCoverage()
    {
        await using var userContext = await userContextFactory.CreateDbContextAsync();

        var userIds = await userContext.Users
                                       .AsNoTracking()
                                       .Select(u => u.Id)
                                       .ToListAsync();

        foreach (var userId in userIds)
        {
            backgroundJobs.Enqueue<ComputationJob>(job => job.ComputeUserCoverage(userId));
        }
    }

    [Queue("coverage")]
    public async Task ComputeUserCoverage(string userId)
    {
        // Prevent duplicate concurrent computations for the same user
        lock (CoverageComputeLock)
        {
            if (!CoverageComputingUserIds.Add(userId))
            {
                return;
            }
        }

        await using var context = await contextFactory.CreateDbContextAsync();
        await using var userContext = await userContextFactory.CreateDbContextAsync();

        try
        {
            // Only compute coverage for users with at least 10 known words
            if (await userContext.FsrsCards.CountAsync(ukw => ukw.UserId == userId) < 10)
            {
                // Remove existing coverages if they exist, if the user cleared his words for example
                await userContext.UserCoverages.Where(uc => uc.UserId == userId).ExecuteDeleteAsync();

                await userContext.SaveChangesAsync();

                return;
            }

            var sql = """

                                      SELECT
                                          d."DeckId",
                                          CASE
                                              WHEN d."WordCount" = 0 THEN 0.0
                                              ELSE ROUND((SUM(CASE WHEN kw."WordId" IS NOT NULL THEN dw."Occurrences" ELSE 0 END)::NUMERIC / d."WordCount"::NUMERIC * 100), 2)
                                          END AS "Coverage",
                                          CASE
                                              WHEN d."UniqueWordCount" = 0 THEN 0.0
                                              ELSE ROUND((COUNT(CASE WHEN kw."WordId" IS NOT NULL THEN 1 END)::NUMERIC / d."UniqueWordCount"::NUMERIC * 100), 2)
                                          END AS "UniqueCoverage"
                                      FROM "jiten"."Decks" d
                                      LEFT JOIN "jiten"."DeckWords" dw ON d."DeckId" = dw."DeckId"
                                      LEFT JOIN "user"."FsrsCards" kw
                                          ON kw."UserId" = {0}::uuid
                                          AND kw."WordId" = dw."WordId"
                                          AND kw."ReadingIndex" = dw."ReadingIndex"
                                          AND (
                                              kw."State" = 4
                                              OR kw."State" = 5
                                              OR (kw."LastReview" IS NOT NULL
                                                  AND (kw."Due" - kw."LastReview") >= INTERVAL '21 days')
                                          )
                                      WHERE d."ParentDeckId" IS NULL
                                      GROUP BY d."DeckId", d."WordCount", d."UniqueWordCount";

                      """;

            var coverageResults = await context.Database
                                               .SqlQueryRaw<DeckCoverageResult>(sql, userId)
                                               .ToListAsync();

            const int batchSize = 1000;

            for (int i = 0; i < coverageResults.Count; i += batchSize)
            {
                var batch = coverageResults.Skip(i).Take(batchSize).ToList();

                var valuesList = string.Join(", ", batch.Select(r =>
                                                                    $"('{userId}'::uuid, {r.DeckId}::numeric, {r.Coverage}::numeric, {r.UniqueCoverage}::numeric)"));

                var upsertSql = $"""
                                                 INSERT INTO "user"."UserCoverages" ("UserId", "DeckId", "Coverage", "UniqueCoverage")
                                                 VALUES {valuesList}
                                                 ON CONFLICT ("UserId", "DeckId")
                                                 DO UPDATE SET
                                                     "Coverage" = EXCLUDED."Coverage",
                                                     "UniqueCoverage" = EXCLUDED."UniqueCoverage";

                                 """;

                await context.Database.ExecuteSqlRawAsync(upsertSql);
            }
        }
        finally
        {
            // Ensure removal even if an exception occurs
            lock (CoverageComputeLock)
            {
                CoverageComputingUserIds.Remove(userId);
            }

            var metadata = await userContext.UserMetadatas
                                            .SingleOrDefaultAsync(um => um.UserId == userId);

            if (metadata is null)
            {
                metadata = new UserMetadata { UserId = userId, CoverageRefreshedAt = DateTime.UtcNow };
                await userContext.UserMetadatas.AddAsync(metadata);
            }
            else
            {
                metadata.CoverageRefreshedAt = DateTime.UtcNow;
            }

            await userContext.SaveChangesAsync();

            // Queue kanji grid computation
            backgroundJobs.Enqueue<ComputationJob>(job => job.ComputeUserKanjiGrid(userId));
        }
    }

    [Queue("coverage")]
    public async Task ComputeUserDeckCoverage(string userId, int deckId)
    {
        // Prevent duplicate concurrent computations for the same user
        lock (CoverageComputeLock)
        {
            if (!CoverageComputingUserIds.Add(userId))
            {
                return;
            }
        }

        await using var context = await contextFactory.CreateDbContextAsync();
        await using var userContext = await userContextFactory.CreateDbContextAsync();

        try
        {
            // Only compute coverage for users with at least 10 known words
            if (await userContext.FsrsCards.CountAsync(ukw => ukw.UserId == userId) < 10)
            {
                // Remove existing coverage if it exists, if the user cleared his words for example
                await userContext.UserCoverages
                                 .Where(uc => uc.UserId == userId && uc.DeckId == deckId)
                                 .ExecuteDeleteAsync();

                await userContext.SaveChangesAsync();

                return;
            }

            var sql = """

                                      SELECT
                                          d."DeckId",
                                          CASE
                                              WHEN d."WordCount" = 0 THEN 0.0
                                              ELSE ROUND((SUM(CASE WHEN kw."WordId" IS NOT NULL THEN dw."Occurrences" ELSE 0 END)::NUMERIC / d."WordCount"::NUMERIC * 100), 2)
                                          END AS "Coverage",
                                          CASE
                                              WHEN d."UniqueWordCount" = 0 THEN 0.0
                                              ELSE ROUND((COUNT(CASE WHEN kw."WordId" IS NOT NULL THEN 1 END)::NUMERIC / d."UniqueWordCount"::NUMERIC * 100), 2)
                                          END AS "UniqueCoverage"
                                      FROM "jiten"."Decks" d
                                      LEFT JOIN "jiten"."DeckWords" dw ON d."DeckId" = dw."DeckId"
                                      LEFT JOIN "user"."FsrsCards" kw
                                          ON kw."UserId" = {0}::uuid
                                          AND kw."WordId" = dw."WordId"
                                          AND kw."ReadingIndex" = dw."ReadingIndex"
                                          AND (
                                              kw."State" = 4
                                              OR kw."State" = 5
                                              OR (kw."LastReview" IS NOT NULL
                                                  AND (kw."Due" - kw."LastReview") >= INTERVAL '21 days')
                                          )
                                      WHERE d."DeckId" = {1}
                                      GROUP BY d."DeckId", d."WordCount", d."UniqueWordCount";

                      """;

            var coverageResults = await context.Database
                                               .SqlQueryRaw<DeckCoverageResult>(sql, userId, deckId)
                                               .ToListAsync();

            // Should only have one result for a single deck
            if (coverageResults.Count > 0)
            {
                var result = coverageResults[0];

                var upsertSql = $"""
                                                 INSERT INTO "user"."UserCoverages" ("UserId", "DeckId", "Coverage", "UniqueCoverage")
                                                 VALUES ('{userId}'::uuid, {result.DeckId}::numeric, {result.Coverage}::numeric, {result.UniqueCoverage}::numeric)
                                                 ON CONFLICT ("UserId", "DeckId")
                                                 DO UPDATE SET
                                                     "Coverage" = EXCLUDED."Coverage",
                                                     "UniqueCoverage" = EXCLUDED."UniqueCoverage";

                                 """;

                await context.Database.ExecuteSqlRawAsync(upsertSql);
            }
        }
        finally
        {
            // Ensure removal even if an exception occurs
            lock (CoverageComputeLock)
            {
                CoverageComputingUserIds.Remove(userId);
            }

            var metadata = await userContext.UserMetadatas
                                            .SingleOrDefaultAsync(um => um.UserId == userId);

            if (metadata is null)
            {
                metadata = new UserMetadata { UserId = userId, CoverageRefreshedAt = DateTime.UtcNow };
                await userContext.UserMetadatas.AddAsync(metadata);
            }
            else
            {
                metadata.CoverageRefreshedAt = DateTime.UtcNow;
            }

            await userContext.SaveChangesAsync();
        }
    }

    private class DeckCoverageResult
    {
        public int DeckId { get; set; }
        public double Coverage { get; set; }
        public double UniqueCoverage { get; set; }
    }

    private static readonly object KanjiGridComputeLock = new();
    private static readonly HashSet<string> KanjiGridComputingUserIds = new();

    [Queue("coverage")]
    public async Task ComputeUserKanjiGrid(string userId)
    {
        lock (KanjiGridComputeLock)
        {
            if (!KanjiGridComputingUserIds.Add(userId))
            {
                return;
            }
        }

        try
        {
            await using var context = await contextFactory.CreateDbContextAsync();
            await using var userContext = await userContextFactory.CreateDbContextAsync();

            // Get scoring weights from configuration
            var youngWeight = double.Parse(configuration["KanjiGrid:YoungScoreWeight"] ?? "0.5");
            var matureWeight = double.Parse(configuration["KanjiGrid:MatureScoreWeight"] ?? "1.0");
            var masteredWeight = double.Parse(configuration["KanjiGrid:MasteredScoreWeight"] ?? "1.0");

            // Compute kanji scores
            var sql = $$"""
                WITH user_known_words AS (
                    SELECT
                        fc."WordId",
                        fc."ReadingIndex",
                        CASE
                            WHEN fc."State" = 5 THEN {{masteredWeight}}
                            WHEN fc."LastReview" IS NOT NULL
                                 AND (fc."Due" - fc."LastReview") >= INTERVAL '21 days'
                            THEN {{matureWeight}}
                            WHEN fc."LastReview" IS NOT NULL
                            THEN {{youngWeight}}
                            ELSE 0
                        END AS weight
                    FROM "user"."FsrsCards" fc
                    WHERE fc."UserId" = {0}::uuid
                      AND fc."State" NOT IN (0, 4)
                ),
                kanji_scores AS (
                    SELECT
                        wk."KanjiCharacter",
                        SUM(ukw.weight) AS score,
                        COUNT(DISTINCT (ukw."WordId", ukw."ReadingIndex")) AS word_count
                    FROM user_known_words ukw
                    INNER JOIN "jmdict"."WordKanji" wk
                        ON wk."WordId" = ukw."WordId"
                        AND wk."ReadingIndex" = ukw."ReadingIndex"
                    WHERE ukw.weight > 0
                    GROUP BY wk."KanjiCharacter"
                )
                SELECT
                    "KanjiCharacter",
                    score AS "Score",
                    word_count AS "WordCount"
                FROM kanji_scores
            """;

            var kanjiScores = await context.Database
                .SqlQueryRaw<KanjiScoreResult>(sql, userId)
                .ToListAsync();

            // Build dictionary for JSONB storage
            var scoresDict = kanjiScores.ToDictionary(
                ks => ks.KanjiCharacter,
                ks => new[] { ks.Score, ks.WordCount }
            );

            // Upsert the kanji grid
            var existingGrid = await userContext.UserKanjiGrids
                .SingleOrDefaultAsync(ukg => ukg.UserId == userId);

            if (existingGrid is null)
            {
                existingGrid = new UserKanjiGrid
                {
                    UserId = userId,
                    KanjiScores = scoresDict,
                    LastComputedAt = DateTimeOffset.UtcNow
                };
                await userContext.UserKanjiGrids.AddAsync(existingGrid);
            }
            else
            {
                existingGrid.KanjiScores = scoresDict;
                existingGrid.LastComputedAt = DateTimeOffset.UtcNow;
            }

            await userContext.SaveChangesAsync();
        }
        finally
        {
            lock (KanjiGridComputeLock)
            {
                KanjiGridComputingUserIds.Remove(userId);
            }
        }
    }

    private class KanjiScoreResult
    {
        public string KanjiCharacter { get; set; } = string.Empty;
        public double Score { get; set; }
        public int WordCount { get; set; }
    }

    private static readonly object AccomplishmentComputeLock = new();
    private static readonly HashSet<string> AccomplishmentComputingUserIds = new();
    private const int GLOBAL_MEDIA_TYPE_KEY = -1;

    [Queue("coverage")]
    public async Task ComputeUserAccomplishments(string userId)
    {
        lock (AccomplishmentComputeLock)
        {
            if (!AccomplishmentComputingUserIds.Add(userId))
            {
                return;
            }
        }

        try
        {
            await using var context = await contextFactory.CreateDbContextAsync();
            await using var userContext = await userContextFactory.CreateDbContextAsync();

            var completedDeckIds = await userContext.UserDeckPreferences
                                                    .Where(udp => udp.UserId == userId && udp.Status == DeckStatus.Completed)
                                                    .Select(udp => udp.DeckId)
                                                    .ToListAsync();

            if (completedDeckIds.Count == 0)
            {
                await userContext.UserAccomplishments
                                 .Where(ua => ua.UserId == userId)
                                 .ExecuteDeleteAsync();
                return;
            }

            // Load all completed decks (both parents and children)
            var allCompletedDecks = await context.Decks
                                                 .AsNoTracking()
                                                 .Where(d => completedDeckIds.Contains(d.DeckId))
                                                 .Select(d => new { d.DeckId, d.ParentDeckId, d.MediaType, d.CharacterCount, d.WordCount })
                                                 .ToListAsync();

            // Build effective deck set: include parents, and children only if their parent is NOT completed
            var completedParentIds = allCompletedDecks
                                     .Where(d => d.ParentDeckId == null)
                                     .Select(d => d.DeckId)
                                     .ToHashSet();

            var completedDecks = allCompletedDecks
                                 .Where(d => d.ParentDeckId == null || !completedParentIds.Contains(d.ParentDeckId.Value))
                                 .ToList();

            // Clear accomplishments if no effective decks remain
            if (completedDecks.Count == 0)
            {
                // Delete existing accomplishments
                await userContext.UserAccomplishments
                                 .Where(ua => ua.UserId == userId)
                                 .ExecuteDeleteAsync();
                return;
            }

            var usedDeckIds = completedDecks.Select(d => d.DeckId).ToList();
            var usedMediaTypes = completedDecks.Select(d => d.MediaType).Distinct().ToList();

            var uniqueWordCounts = await ComputeUniqueWordCounts(context, usedDeckIds, usedMediaTypes);
            var uniqueWordUsedOnceCounts = await ComputeUniqueWordUsedOnceCounts(context, usedDeckIds, usedMediaTypes);
            var uniqueKanjiCounts = await ComputeUniqueKanjiCounts(context, usedDeckIds, usedMediaTypes);

            var accomplishments = new List<UserAccomplishment>();
            var now = DateTimeOffset.UtcNow;

            // Global
            accomplishments.Add(new UserAccomplishment
                                {
                                    UserId = userId, MediaType = null, CompletedDeckCount = completedDecks.Count,
                                    TotalCharacterCount = completedDecks.Sum(d => (long)d.CharacterCount),
                                    TotalWordCount = completedDecks.Sum(d => (long)d.WordCount),
                                    UniqueWordCount = uniqueWordCounts.GetValueOrDefault(GLOBAL_MEDIA_TYPE_KEY, 0),
                                    UniqueWordUsedOnceCount = uniqueWordUsedOnceCounts.GetValueOrDefault(GLOBAL_MEDIA_TYPE_KEY, 0),
                                    UniqueKanjiCount = uniqueKanjiCounts.GetValueOrDefault(GLOBAL_MEDIA_TYPE_KEY, 0), LastComputedAt = now
                                });

            // By media type
            foreach (var mediaType in usedMediaTypes)
            {
                var typeDecks = completedDecks.Where(d => d.MediaType == mediaType).ToList();
                accomplishments.Add(new UserAccomplishment
                                    {
                                        UserId = userId, MediaType = mediaType, CompletedDeckCount = typeDecks.Count,
                                        TotalCharacterCount = typeDecks.Sum(d => (long)d.CharacterCount),
                                        TotalWordCount = typeDecks.Sum(d => (long)d.WordCount),
                                        UniqueWordCount = uniqueWordCounts.GetValueOrDefault((int)mediaType, 0),
                                        UniqueWordUsedOnceCount = uniqueWordUsedOnceCounts.GetValueOrDefault((int)mediaType, 0),
                                        UniqueKanjiCount = uniqueKanjiCounts.GetValueOrDefault((int)mediaType, 0), LastComputedAt = now
                                    });
            }

            // Delete existing accomplishments and insert new ones
            await userContext.UserAccomplishments
                             .Where(ua => ua.UserId == userId)
                             .ExecuteDeleteAsync();

            await userContext.UserAccomplishments.AddRangeAsync(accomplishments);
            await userContext.SaveChangesAsync();
        }
        finally
        {
            lock (AccomplishmentComputeLock)
            {
                AccomplishmentComputingUserIds.Remove(userId);
            }
        }
    }

    private async Task<Dictionary<int, int>> ComputeUniqueWordCounts(
        JitenDbContext context,
        List<int> deckIds,
        List<MediaType> mediaTypes)
    {
        var result = new Dictionary<int, int>();
        if (deckIds.Count == 0) return result;

        var deckIdsParam = string.Join(",", deckIds);

        // Global unique word count
        var globalSql = $"""
                             SELECT COUNT(DISTINCT ("WordId", "ReadingIndex"))::int AS "Value"
                             FROM jiten."DeckWords"
                             WHERE "DeckId" IN ({deckIdsParam})
                         """;
        var globalCount = await context.Database.SqlQueryRaw<int>(globalSql).FirstOrDefaultAsync();
        result[GLOBAL_MEDIA_TYPE_KEY] = globalCount;

        // Per-MediaType unique word counts
        foreach (var mediaType in mediaTypes)
        {
            var mediaTypeDecks = await context.Decks
                                              .AsNoTracking()
                                              .Where(d => deckIds.Contains(d.DeckId) && d.MediaType == mediaType)
                                              .Select(d => d.DeckId)
                                              .ToListAsync();

            if (mediaTypeDecks.Count == 0)
            {
                result[(int)mediaType] = 0;
                continue;
            }

            var mediaTypeDeckIdsParam = string.Join(",", mediaTypeDecks);
            var sql = $"""
                           SELECT COUNT(DISTINCT ("WordId", "ReadingIndex"))::int AS "Value"
                           FROM jiten."DeckWords"
                           WHERE "DeckId" IN ({mediaTypeDeckIdsParam})
                       """;
            var count = await context.Database.SqlQueryRaw<int>(sql).FirstOrDefaultAsync();
            result[(int)mediaType] = count;
        }

        return result;
    }

    private async Task<Dictionary<int, int>> ComputeUniqueKanjiCounts(
        JitenDbContext context,
        List<int> deckIds,
        List<MediaType> mediaTypes)
    {
        var result = new Dictionary<int, int>();
        if (deckIds.Count == 0) return result;

        // Get deck info including children for parent decks
        var decksWithChildren = await context.Decks
                                             .AsNoTracking()
                                             .Where(d => deckIds.Contains(d.DeckId))
                                             .Select(d => new
                                              {
                                                  d.DeckId,
                                                  d.MediaType,
                                                  ChildIds = d.Children.Select(c => c.DeckId).ToList()
                                              })
                                             .ToListAsync();

        var deckMediaTypes = new Dictionary<int, MediaType>();
        var rawTextDeckIds = new HashSet<int>();

        foreach (var deck in decksWithChildren)
        {
            if (deck.ChildIds.Count > 0)
            {
                // For parents, we need to fetch the child raw texts
                foreach (var childId in deck.ChildIds)
                {
                    rawTextDeckIds.Add(childId);
                    deckMediaTypes[childId] = deck.MediaType;
                }
            }
            else
            {
                rawTextDeckIds.Add(deck.DeckId);
                deckMediaTypes[deck.DeckId] = deck.MediaType;
            }
        }

        var rawTexts = await context.DeckRawTexts
                                    .AsNoTracking()
                                    .Where(rt => rawTextDeckIds.Contains(rt.DeckId))
                                    .Select(rt => new { rt.DeckId, rt.RawText })
                                    .ToListAsync();

        // Global kanji set
        var globalKanji = new HashSet<Rune>();

        // Per-MediaType kanji sets
        var mediaTypeKanji = mediaTypes.ToDictionary(mt => mt, _ => new HashSet<Rune>());

        foreach (var rt in rawTexts)
        {
            if (string.IsNullOrEmpty(rt.RawText)) continue;

            var mediaType = deckMediaTypes.GetValueOrDefault(rt.DeckId);

            foreach (var rune in rt.RawText.EnumerateRunes())
            {
                if (!JapaneseTextHelper.IsKanji(rune)) continue;

                globalKanji.Add(rune);
                if (mediaTypeKanji.TryGetValue(mediaType, out var kanjiSet))
                {
                    kanjiSet.Add(rune);
                }
            }
        }

        result[GLOBAL_MEDIA_TYPE_KEY] = globalKanji.Count;
        foreach (var mediaType in mediaTypes)
        {
            result[(int)mediaType] = mediaTypeKanji[mediaType].Count;
        }

        return result;
    }

    private async Task<Dictionary<int, int>> ComputeUniqueWordUsedOnceCounts(
        JitenDbContext context,
        List<int> deckIds,
        List<MediaType> mediaTypes)
    {
        var result = new Dictionary<int, int>();
        if (deckIds.Count == 0) return result;

        var deckIdsParam = string.Join(",", deckIds);

        var globalSql = $"""
                             SELECT COUNT(*)::int AS "Value"
                             FROM (
                                 SELECT "WordId", "ReadingIndex"
                                 FROM jiten."DeckWords"
                                 WHERE "DeckId" IN ({deckIdsParam})
                                 GROUP BY "WordId", "ReadingIndex"
                                 HAVING SUM("Occurrences") = 1
                             ) AS uniq
                         """;
        var globalCount = await context.Database.SqlQueryRaw<int>(globalSql).FirstOrDefaultAsync();
        result[GLOBAL_MEDIA_TYPE_KEY] = globalCount;

        // Per-MediaType unique words used once
        foreach (var mediaType in mediaTypes)
        {
            var mediaTypeDecks = await context.Decks
                                              .AsNoTracking()
                                              .Where(d => deckIds.Contains(d.DeckId) && d.MediaType == mediaType)
                                              .Select(d => d.DeckId)
                                              .ToListAsync();

            if (mediaTypeDecks.Count == 0)
            {
                result[(int)mediaType] = 0;
                continue;
            }

            var mediaTypeDeckIdsParam = string.Join(",", mediaTypeDecks);
            var sql = $"""
                           SELECT COUNT(*)::int AS "Value"
                           FROM (
                               SELECT "WordId", "ReadingIndex"
                               FROM jiten."DeckWords"
                               WHERE "DeckId" IN ({mediaTypeDeckIdsParam})
                               GROUP BY "WordId", "ReadingIndex"
                               HAVING SUM("Occurrences") = 1
                           ) AS uniq
                       """;
            var count = await context.Database.SqlQueryRaw<int>(sql).FirstOrDefaultAsync();
            result[(int)mediaType] = count;
        }

        return result;
    }

    public async Task RecomputeFrequencies()
    {
        string path = Path.Join(configuration["StaticFilesPath"], "yomitan");
        Directory.CreateDirectory(path);

        Console.WriteLine("Computing global frequencies...");
        var frequencies = await JitenHelper.ComputeFrequencies(contextFactory, null);
        await JitenHelper.SaveFrequenciesToDatabase(contextFactory, frequencies);

        // Save frequencies to CSV
        await SaveFrequenciesToCsv(frequencies, Path.Join(path, "jiten_freq_global.csv"));

        // Generate Yomitan deck
        string index = YomitanHelper.GetIndexJson(null);
        var bytes = await YomitanHelper.GenerateYomitanFrequencyDeck(contextFactory, frequencies, null, index);
        var filePath = Path.Join(path, "jiten_freq_global.zip");
        string indexFilePath = Path.Join(path, "jiten_freq_global.json");
        await File.WriteAllBytesAsync(filePath, bytes);
        await File.WriteAllTextAsync(indexFilePath, index);

        foreach (var mediaType in Enum.GetValues<MediaType>())
        {
            Console.WriteLine($"Computing {mediaType} frequencies...");
            frequencies = await JitenHelper.ComputeFrequencies(contextFactory, mediaType);

            // Save frequencies to CSV
            await SaveFrequenciesToCsv(frequencies, Path.Join(path, $"jiten_freq_{mediaType.ToString()}.csv"));

            // Generate Yomitan deck
            index = YomitanHelper.GetIndexJson(mediaType);
            bytes = await YomitanHelper.GenerateYomitanFrequencyDeck(contextFactory, frequencies, mediaType, index);
            filePath = Path.Join(path, $"jiten_freq_{mediaType.ToString()}.zip");
            indexFilePath = Path.Join(path, $"jiten_freq_{mediaType.ToString()}.json");
            await File.WriteAllBytesAsync(filePath, bytes);
            await File.WriteAllTextAsync(indexFilePath, index);
        }
    }

    public async Task RecomputeKanjiFrequencies()
    {
        string path = Path.Join(configuration["StaticFilesPath"], "yomitan");
        Directory.CreateDirectory(path);

        Console.WriteLine("Computing kanji frequencies...");
        var kanjiFrequencies = await JitenHelper.ComputeKanjiFrequencies(contextFactory);

        // Save to CSV
        await SaveKanjiFrequenciesToCsv(kanjiFrequencies, Path.Join(path, "jiten_kanji_freq.csv"));

        // Generate Yomitan deck
        string index = YomitanHelper.GetKanjiIndexJson();
        var bytes = YomitanHelper.GenerateYomitanKanjiFrequencyDeck(kanjiFrequencies);
        var filePath = Path.Join(path, "jiten_kanji_freq.zip");
        string indexFilePath = Path.Join(path, "jiten_kanji_freq.json");
        await File.WriteAllBytesAsync(filePath, bytes);
        await File.WriteAllTextAsync(indexFilePath, index);

        Console.WriteLine("Updating kanji frequency ranks in database...");
        await using var jitenContext = await contextFactory.CreateDbContextAsync();

        var kanjiRanks = kanjiFrequencies.ToDictionary(f => f.kanji, f => f.rank);
        var kanjiChars = kanjiRanks.Keys.ToHashSet();

        var kanjisToUpdate = await jitenContext.Kanjis
            .Where(k => kanjiChars.Contains(k.Character))
            .ToListAsync();

        foreach (var kanji in kanjisToUpdate)
        {
            if (kanjiRanks.TryGetValue(kanji.Character, out int rank))
            {
                kanji.FrequencyRank = rank;
            }
        }

        await jitenContext.SaveChangesAsync();

        Console.WriteLine("Kanji frequency computation complete.");
    }

    private async Task SaveKanjiFrequenciesToCsv(List<(string kanji, int rank)> frequencies, string filePath)
    {
        using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        await using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);

        var frequencyListCsv = frequencies.Select(f => new { Kanji = f.kanji, Rank = f.rank }).ToArray();

        await csv.WriteRecordsAsync(frequencyListCsv);
        await writer.FlushAsync();

        stream.Position = 0;
        await using var fileStream = new FileStream(filePath, FileMode.Create);
        await stream.CopyToAsync(fileStream);
    }

    private async Task SaveFrequenciesToCsv(List<JmDictWordFrequency> frequencies, string filePath)
    {
        await using var context = await contextFactory.CreateDbContextAsync();

        // Fetch words from the database
        Dictionary<int, JmDictWord> allWords = await context.JMDictWords.AsNoTracking()
                                                            .Where(w => frequencies.Select(f => f.WordId).Contains(w.WordId))
                                                            .ToDictionaryAsync(w => w.WordId);

        List<(string word, int rank)> frequencyList = new();

        foreach (var frequency in frequencies)
        {
            if (!allWords.TryGetValue(frequency.WordId, out var word)) continue;

            var highestPercentage = frequency.ReadingsFrequencyPercentage.Max();
            var index = frequency.ReadingsFrequencyPercentage.IndexOf(highestPercentage);
            string readingWord = word.Readings[index];

            frequencyList.Add((readingWord, frequency.FrequencyRank));
        }

        // Create CSV file
        using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        await using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);

        // Create anonymous object for CsvWriter
        var frequencyListCsv = frequencyList.Select(f => new { Word = f.word, Rank = f.rank }).ToArray();

        await csv.WriteRecordsAsync(frequencyListCsv);
        await writer.FlushAsync();

        stream.Position = 0;
        await using var fileStream = new FileStream(filePath, FileMode.Create);
        await stream.CopyToAsync(fileStream);
    }
}