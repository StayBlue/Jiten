using Jiten.Core;
using Jiten.Core.Data.JMDict;
using Jiten.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace Jiten.Cli.Commands;

public class WordSetCommands(CliContext context)
{
    public async Task CreateWordSetFromPartOfSpeech(string slug, string name, string? description, string pos, bool syncKana)
    {
        var posValues = pos.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Console.WriteLine($"Creating WordSet '{name}' from PoS containing '{string.Join("', '", posValues)}'...");

        await using var jitenContext = new JitenDbContext(context.DbOptions);

        if (await jitenContext.WordSets.AnyAsync(ws => ws.Slug == slug))
        {
            Console.WriteLine($"WordSet with slug '{slug}' already exists.");
            return;
        }

        var seenWordIds = new HashSet<int>();
        var matchingWords = new List<(int WordId, List<JmDictReadingType> ReadingTypes)>();

        foreach (var p in posValues)
        {
            var words = await jitenContext.JMDictWords
                .AsNoTracking()
                .Where(w => w.PartsOfSpeech.Any(ps => ps.Contains(p)))
                .Select(w => new { w.WordId, w.ReadingTypes })
                .ToListAsync();

            int added = 0;
            foreach (var w in words)
            {
                if (seenWordIds.Add(w.WordId))
                {
                    matchingWords.Add((w.WordId, w.ReadingTypes));
                    added++;
                }
            }
            Console.WriteLine($"PoS '{p}': {words.Count} words found, {added} new (deduplicated).");
        }

        Console.WriteLine($"Total: {matchingWords.Count} unique words.");

        var wordSet = new WordSet
        {
            Slug = slug,
            Name = name,
            Description = description,
            WordCount = 0
        };
        await jitenContext.WordSets.AddAsync(wordSet);
        await jitenContext.SaveChangesAsync();

        var wordReadings = new List<(int WordId, short ReadingIndex)>();
        int kanjiReadingsCount = 0;
        int kanaOnlyReadingsCount = 0;
        foreach (var (wordId, readingTypes) in matchingWords)
        {
            for (int i = 0; i < readingTypes.Count; i++)
            {
                if (readingTypes[i] == JmDictReadingType.Reading)
                {
                    wordReadings.Add((wordId, (short)i));
                    kanjiReadingsCount++;
                }
                else if (readingTypes[i] == JmDictReadingType.KanaReading &&
                         !readingTypes.Contains(JmDictReadingType.Reading))
                {
                    wordReadings.Add((wordId, (short)i));
                    kanaOnlyReadingsCount++;
                }
            }
        }
        Console.WriteLine($"Selected {kanjiReadingsCount} kanji readings, {kanaOnlyReadingsCount} kana-only readings (total: {wordReadings.Count}).");

        await AddWordSetMembersWithKanaSync(jitenContext, wordSet.SetId, wordReadings, syncKana);

        var memberStats = await jitenContext.WordSetMembers
            .Where(m => m.SetId == wordSet.SetId)
            .GroupBy(_ => 1)
            .Select(g => new { WordCount = g.Select(m => m.WordId).Distinct().Count(), FormCount = g.Count() })
            .FirstOrDefaultAsync();

        wordSet.WordCount = memberStats?.WordCount ?? 0;
        await jitenContext.SaveChangesAsync();

        Console.WriteLine($"Created WordSet '{name}' with {wordSet.WordCount} words ({memberStats?.FormCount ?? 0} forms).");
    }

    public async Task CreateWordSetFromCsv(string slug, string name, string? description, string csvFile, bool syncKana)
    {
        Console.WriteLine($"Creating WordSet '{name}' from CSV file...");

        await using var jitenContext = new JitenDbContext(context.DbOptions);

        if (await jitenContext.WordSets.AnyAsync(ws => ws.Slug == slug))
        {
            Console.WriteLine($"WordSet with slug '{slug}' already exists.");
            return;
        }

        var lines = await File.ReadAllLinesAsync(csvFile);
        var wordReadings = new List<(int WordId, short ReadingIndex)>();

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            var parts = line.Split(',');
            if (parts.Length < 2) continue;

            if (!int.TryParse(parts[0].Trim(), out var wordId)) continue;
            if (!short.TryParse(parts[1].Trim(), out var readingIndex)) continue;
            if (readingIndex < 0 || readingIndex > byte.MaxValue) continue;

            wordReadings.Add((wordId, readingIndex));
        }

        Console.WriteLine($"Parsed {wordReadings.Count} word-reading pairs from CSV.");

        var wordIds = wordReadings.Select(w => w.WordId).Distinct().ToList();
        var existingWordIds = await jitenContext.JMDictWords
            .AsNoTracking()
            .Where(w => wordIds.Contains(w.WordId))
            .Select(w => w.WordId)
            .ToHashSetAsync();

        var validReadings = wordReadings.Where(w => existingWordIds.Contains(w.WordId)).ToList();
        var invalidCount = wordReadings.Count - validReadings.Count;
        if (invalidCount > 0)
        {
            Console.WriteLine($"Skipped {invalidCount} entries with invalid WordIds.");
        }

        var wordSet = new WordSet
        {
            Slug = slug,
            Name = name,
            Description = description,
            WordCount = 0
        };
        await jitenContext.WordSets.AddAsync(wordSet);
        await jitenContext.SaveChangesAsync();

        await AddWordSetMembersWithKanaSync(jitenContext, wordSet.SetId, validReadings, syncKana);

        var memberStats = await jitenContext.WordSetMembers
            .Where(m => m.SetId == wordSet.SetId)
            .GroupBy(_ => 1)
            .Select(g => new { WordCount = g.Select(m => m.WordId).Distinct().Count(), FormCount = g.Count() })
            .FirstOrDefaultAsync();

        wordSet.WordCount = memberStats?.WordCount ?? 0;
        await jitenContext.SaveChangesAsync();

        Console.WriteLine($"Created WordSet '{name}' with {wordSet.WordCount} words ({memberStats?.FormCount ?? 0} forms).");
    }

    private static async Task AddWordSetMembersWithKanaSync(
        JitenDbContext jitenContext,
        int setId,
        IEnumerable<(int WordId, short ReadingIndex)> words,
        bool syncKana)
    {
        var wordsList = words.ToList();
        if (wordsList.Count == 0) return;

        var wordIds = wordsList.Select(w => w.WordId).Distinct().ToList();

        Dictionary<int, List<JmDictReadingType>>? readingTypes = null;
        if (syncKana)
        {
            readingTypes = await jitenContext.JMDictWords
                .AsNoTracking()
                .Where(w => wordIds.Contains(w.WordId))
                .Select(w => new { w.WordId, w.ReadingTypes })
                .ToDictionaryAsync(w => w.WordId, w => w.ReadingTypes);
        }

        var processedKeys = new HashSet<(int WordId, short ReadingIndex)>();
        var members = new List<WordSetMember>();
        int position = 0;

        int kanaAdded = 0;
        int skippedNoTypes = 0;
        int skippedNotReading = 0;
        int skippedNoKana = 0;

        foreach (var (wordId, readingIndex) in wordsList)
        {
            if (processedKeys.Add((wordId, readingIndex)))
            {
                members.Add(new WordSetMember
                {
                    SetId = setId,
                    WordId = wordId,
                    ReadingIndex = readingIndex,
                    Position = position++
                });
            }

            if (!syncKana || readingTypes == null) continue;
            if (!readingTypes.TryGetValue(wordId, out var types))
            {
                skippedNoTypes++;
                continue;
            }
            if (readingIndex >= types.Count) continue;
            if (types[readingIndex] != JmDictReadingType.Reading)
            {
                skippedNotReading++;
                continue;
            }

            var kanaIndex = types.FindIndex(t => t == JmDictReadingType.KanaReading);
            if (kanaIndex < 0)
            {
                skippedNoKana++;
                continue;
            }

            if (processedKeys.Add((wordId, (short)kanaIndex)))
            {
                members.Add(new WordSetMember
                {
                    SetId = setId,
                    WordId = wordId,
                    ReadingIndex = (short)kanaIndex,
                    Position = position++
                });
                kanaAdded++;
            }
        }

        if (syncKana)
        {
            Console.WriteLine($"Kana sync: {kanaAdded} kana forms added, {skippedNoTypes} missing types, {skippedNotReading} not Reading type, {skippedNoKana} no KanaReading found.");
        }

        const int batchSize = 10000;
        for (int i = 0; i < members.Count; i += batchSize)
        {
            var batch = members.Skip(i).Take(batchSize).ToList();
            await jitenContext.WordSetMembers.AddRangeAsync(batch);
            await jitenContext.SaveChangesAsync();
            Console.WriteLine($"Inserted {Math.Min(i + batchSize, members.Count)}/{members.Count} members...");
        }
    }
}
