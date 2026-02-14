using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jiten.Cli.ML;
using Jiten.Core.Data;
using Jiten.Core.Data.JMDict;
using Microsoft.EntityFrameworkCore;

namespace Jiten.Cli.Commands;

public class MlCommands(CliContext context)
{
    public async Task ExtractFeatures(string path)
    {
        Console.WriteLine("Extracting features...");
        var featureExtractor = new FileFeatureExtractor(context.ContextFactory);
        await featureExtractor.ExtractFeatures(
            (factory, text, storeRaw, predictDiff, mediaType) =>
                Jiten.Parser.Parser.ParseTextToDeck(factory, text, storeRaw, predictDiff, mediaType),
            path);
        Console.WriteLine("All features extracted.");
    }

    public async Task ExtractFeaturesDb(string path)
    {
        Console.WriteLine("Extracting features...");
        var featureExtractor = new DbFeatureExtractor(context.ContextFactory);
        await featureExtractor.ExtractFeatures(
            (factory, text, storeRaw, predictDiff, mediaType) =>
                Jiten.Parser.Parser.ParseTextToDeck(factory, text, storeRaw, predictDiff, mediaType),
            path);
        Console.WriteLine("All features extracted.");
    }

    public Task<bool> ExtractMorphemes()
    {
        Console.WriteLine("This function is not supported at this time.");
        return Task.FromResult(true);

        // The code below is commented out as it doesn't work correctly
        /*
        await using var context = await _context.ContextFactory.CreateDbContextAsync();
        var allWords = await context.JMDictWords.Include(w => w.Forms).Select(w => new { w.WordId, w.Forms }).ToListAsync();
        int error = 0;
        int noMorphemes = 0;
        int multipleMorphemes = 0;

        List<(int wordId, string reading, List<int> morphemes)> parsedMorphemes = new();
        foreach (var word in allWords)
        {
            string reading = "";
            foreach (var form in word.Forms.OrderBy(f => f.ReadingIndex))
            {
                if (form.FormType == JmDictFormType.KanjiForm)
                {
                    reading = form.Text;
                    break;
                }
            }

            if (string.IsNullOrEmpty(reading))
            {
                Console.WriteLine($"Error: haven't found reading for word id {word.WordId}");
                continue;
            }

            parsedMorphemes.Add((word.WordId, reading, new List<int>()));
        }

        const int BATCH_SIZE = 10000;
        int totalProcessed = 0;

        for (int batchStart = 0; batchStart < parsedMorphemes.Count; batchStart += BATCH_SIZE)
        {
            int currentBatchSize = Math.Min(BATCH_SIZE, parsedMorphemes.Count - batchStart);
            Console.WriteLine($"Processing batch {batchStart / BATCH_SIZE + 1} of {(parsedMorphemes.Count + BATCH_SIZE - 1) / BATCH_SIZE} ({batchStart}-{batchStart + currentBatchSize - 1})");

            var batchReadings = parsedMorphemes
                                .Skip(batchStart)
                                .Take(currentBatchSize)
                                .Select(p => p.reading);
            var batchText = String.Join(" \n", batchReadings);

            var results = await Jiten.Parser.Parser.ParseMorphenes(_context.ContextFactory, batchText);

            int currentMorphemeIndex = batchStart;
            bool lastWasNull = false;
            for (int i = 0; i < results.Count; i++)
            {
                if (results[i] != null)
                {
                    parsedMorphemes[currentMorphemeIndex].morphemes.Add(results[i]!.WordId);
                    lastWasNull = false;
                }
                else
                {
                    if (lastWasNull)
                        continue;

                    lastWasNull = true;
                    currentMorphemeIndex++;
                }
            }

            totalProcessed += currentBatchSize;
            Console.WriteLine($"Processed {totalProcessed} of {parsedMorphemes.Count} words ({totalProcessed * 100 / parsedMorphemes.Count}%)");
        }

        foreach (var morpheme in parsedMorphemes)
        {
            if (morpheme.morphemes.Count == 0)
            {
                Console.WriteLine($"Error: haven't found any results for reading {morpheme.reading}");
                error++;
            }
            else if (morpheme.morphemes.Count == 1)
            {
                noMorphemes++;
            }
            else
            {
                multipleMorphemes++;
            }
        }

        Console.WriteLine($"Error: {error}");
        Console.WriteLine($"No morphemes: {noMorphemes}");
        Console.WriteLine($"Multiple morphemes: {multipleMorphemes}");
        var totalMorphenes = parsedMorphemes.Select(p => p.morphemes.Count).Sum() + error + noMorphemes;
        Console.WriteLine($"Total: {totalMorphenes} for {parsedMorphemes.Count} words (Average: {totalMorphenes / parsedMorphemes.Count:0.00)}");
        return true;
        */
    }

    public async Task ImportDeckDifficulty(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
        {
            Console.WriteLine($"Directory not found: {directoryPath}");
            return;
        }

        var jsonFiles = Directory.GetFiles(directoryPath, "*.json");
        Console.WriteLine($"Found {jsonFiles.Length} JSON files to process.");

        if (jsonFiles.Length == 0)
            return;

        var difficulties = new ConcurrentBag<(int DeckId, DeckDifficultyInput Input)>();
        int parseErrors = 0;
        int filesRead = 0;

        Console.WriteLine("Reading JSON files...");

        await Parallel.ForEachAsync(jsonFiles, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount * 2 }, async (file, _) =>
        {
            var fileName = Path.GetFileNameWithoutExtension(file);
            if (!int.TryParse(fileName, out int deckId))
            {
                Interlocked.Increment(ref parseErrors);
                return;
            }

            try
            {
                var json = await File.ReadAllTextAsync(file);
                var input = JsonSerializer.Deserialize<DeckDifficultyInput>(json);
                if (input == null)
                {
                    Interlocked.Increment(ref parseErrors);
                    return;
                }

                difficulties.Add((deckId, input));

                var count = Interlocked.Increment(ref filesRead);
                if (count % 10000 == 0)
                    Console.WriteLine($"Read {count}/{jsonFiles.Length} files...");
            }
            catch (JsonException)
            {
                Interlocked.Increment(ref parseErrors);
            }
        });

        Console.WriteLine($"Successfully parsed {difficulties.Count} files. ({parseErrors} errors)");

        if (difficulties.IsEmpty)
            return;

        var difficultiesList = difficulties.ToList();

        const int batchSize = 1000;
        int totalProcessed = 0;
        int created = 0;
        int updated = 0;
        int skipped = 0;

        await using var db = await context.ContextFactory.CreateDbContextAsync();

        var allDeckIds = difficultiesList.Select(d => d.DeckId).ToHashSet();

        var existingDifficulties = await db.DeckDifficulties
            .Where(dd => allDeckIds.Contains(dd.DeckId))
            .ToDictionaryAsync(dd => dd.DeckId);

        var decksToUpdate = await db.Decks
            .Where(d => allDeckIds.Contains(d.DeckId))
            .ToDictionaryAsync(d => d.DeckId);

        for (int i = 0; i < difficultiesList.Count; i += batchSize)
        {
            var batch = difficultiesList.Skip(i).Take(batchSize).ToList();

            foreach (var (deckId, input) in batch)
            {
                if (!decksToUpdate.TryGetValue(deckId, out var deck))
                {
                    skipped++;
                    continue;
                }

                deck.Difficulty = (float)input.Difficulty;

                var progression = input.Progression?.Select(p => new ProgressionSegment
                {
                    Segment = p.Segment,
                    Difficulty = (decimal)p.Difficulty,
                    Peak = (decimal)p.Peak
                }).ToList() ?? [];

                var deciles = input.Deciles?.ToDictionary(
                    kvp => kvp.Key,
                    kvp => (decimal)kvp.Value) ?? new Dictionary<string, decimal>();

                if (existingDifficulties.TryGetValue(deckId, out var existing))
                {
                    existing.Difficulty = (decimal)input.Difficulty;
                    existing.Peak = (decimal)input.Peak;
                    existing.Deciles = deciles;
                    existing.Progression = progression;
                    existing.LastUpdated = DateTimeOffset.UtcNow;
                    updated++;
                }
                else
                {
                    var deckDifficulty = new DeckDifficulty
                    {
                        DeckId = deckId,
                        Difficulty = (decimal)input.Difficulty,
                        Peak = (decimal)input.Peak,
                        Deciles = deciles,
                        Progression = progression,
                        LastUpdated = DateTimeOffset.UtcNow
                    };
                    db.DeckDifficulties.Add(deckDifficulty);
                    existingDifficulties[deckId] = deckDifficulty;
                    created++;
                }
            }

            await db.SaveChangesAsync();
            totalProcessed += batch.Count;

            Console.WriteLine($"Processed {totalProcessed}/{difficultiesList.Count} ({totalProcessed * 100 / difficultiesList.Count}%) - Created: {created}, Updated: {updated}, Skipped: {skipped}");
        }

        Console.WriteLine($"Import complete. Created: {created}, Updated: {updated}, Skipped (deck not found): {skipped}");
    }
}

public class DeckDifficultyInput
{
    [JsonPropertyName("difficulty")]
    public double Difficulty { get; set; }

    [JsonPropertyName("baseline")]
    public double Baseline { get; set; }

    [JsonPropertyName("peak")]
    public double Peak { get; set; }

    [JsonPropertyName("deciles")]
    public Dictionary<string, double>? Deciles { get; set; }

    [JsonPropertyName("progression")]
    public List<DeckDifficultyProgressionInput>? Progression { get; set; }

    [JsonPropertyName("sentences_scored")]
    public int SentencesScored { get; set; }

    [JsonPropertyName("level_counts")]
    public Dictionary<string, int>? LevelCounts { get; set; }

    [JsonPropertyName("ensemble_runs")]
    public int EnsembleRuns { get; set; }
}

public class DeckDifficultyProgressionInput
{
    [JsonPropertyName("segment")]
    public int Segment { get; set; }

    [JsonPropertyName("start_pct")]
    public int StartPct { get; set; }

    [JsonPropertyName("end_pct")]
    public int EndPct { get; set; }

    [JsonPropertyName("difficulty")]
    public double Difficulty { get; set; }

    [JsonPropertyName("peak")]
    public double Peak { get; set; }

    [JsonPropertyName("samples")]
    public int Samples { get; set; }
}
