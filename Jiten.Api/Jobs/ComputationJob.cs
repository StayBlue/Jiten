using System.Text;
using CsvHelper;
using Jiten.Core;
using Jiten.Core.Data;
using Jiten.Core.Data.JMDict;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Runtime.CompilerServices;
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