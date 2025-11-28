using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Jiten.Core.Data;
using Jiten.Core.Data.JMDict;
using Microsoft.EntityFrameworkCore;

namespace Jiten.Core;

public static class YomitanHelper
{
    /// <summary>
    /// Generates the content for the index.json file in a Yomitan dictionary.
    /// </summary>
    public static string GetIndexJson(MediaType? mediaType)
    {
        string title = mediaType != null ? $"Jiten ({mediaType})" : "Jiten";
        string revision = mediaType != null ? $"Jiten ({mediaType}) {DateTime.UtcNow:yy-MM-dd}" : $"Jiten {DateTime.UtcNow:yy-MM-dd}";
        string description = mediaType != null
            ? $"Dictionary based on frequency data of {mediaType} from jiten.moe"
            : "Dictionary based on frequency data of all media from jiten.moe";
        string indexUrl = mediaType != null
            ? $"https://api.jiten.moe/api/frequency-list/index?mediaType={mediaType}"
            : "https://api.jiten.moe/api/frequency-list/index";
        string downloadUrl = mediaType != null
            ? $"https://api.jiten.moe/api/frequency-list/download?mediaType={mediaType}"
            : "https://api.jiten.moe/api/frequency-list/download";

        return
            $$"""{"title":"{{title}}","format":3,"revision":"{{revision}}","isUpdatable":true,"indexUrl":"{{indexUrl}}","downloadUrl":"{{downloadUrl}}","sequenced":false,"frequencyMode":"rank-based","author":"Jiten","url":"https://jiten.moe","description":"{{description}}"}""";
    }

    /// <summary>
    /// Generates a zipped Yomitan frequency dictionary for a given media type.
    /// </summary>
    public static async Task<byte[]> GenerateYomitanFrequencyDeck(IDbContextFactory<JitenDbContext> contextFactory,
                                                                  List<JmDictWordFrequency> frequencies, MediaType? mediaType,
                                                                  string indexJson)
    {
        await using var context = await contextFactory.CreateDbContextAsync();

        var wordIds = frequencies.Select(f => f.WordId).ToList();
        var allWords = await context.JMDictWords.AsNoTracking()
                                    .Where(w => wordIds.Contains(w.WordId))
                                    .ToDictionaryAsync(w => w.WordId);

        var yomitanTermList = new List<List<object>>();
        var addedEntries = new HashSet<string>();

        foreach (var freq in frequencies)
        {
            if (!allWords.TryGetValue(freq.WordId, out JmDictWord? word)) continue;

            // Get valid readings. Includes observed readings AND the main reading (even if not observed).
            var kanaReadings = GetKanaReadingsWithFrequencies(word, freq);
            if (kanaReadings.Count == 0) continue;

            // The list is ordered by index, so the first one is the Main Reading.
            string mainKanaReading = kanaReadings[0].kanaText;

            // CASE 1: Create standalone kana entries
            foreach (var (kanaText, kanaRank, kanaIndex) in kanaReadings)
            {
                // Only generate standalone entry if it was actually observed
                if (freq.ReadingsUsedInMediaAmount[kanaIndex] <= 0)
                    continue;

                if (addedEntries.Contains(kanaText))
                    continue;

                yomitanTermList.Add([
                    kanaText, "freq",
                    new Dictionary<string, object> { ["value"] = kanaRank, ["displayValue"] = $"{kanaRank}㋕" }
                ]);
                addedEntries.Add(kanaText);
            }

            // CASE 2: Create kanji entries
            for (int i = 0; i < word.Readings.Count; i++)
            {
                if (word.ReadingTypes[i] != JmDictReadingType.Reading)
                    continue;

                var kanjiTerm = word.Readings[i];
                var kanjiRank = freq.ReadingsFrequencyRank[i];

                // Skip if kanji form was never observed
                if (freq.ReadingsUsedInMediaAmount[i] <= 0)
                    continue;

                // Also skip if rank is invalid
                if (kanjiRank is <= 0 or >= int.MaxValue - 1000)
                    continue;

                foreach (var (kanaText, kanaRank, kanaIndex) in kanaReadings)
                {
                    bool isKanaObserved = freq.ReadingsUsedInMediaAmount[kanaIndex] > 0;

                    // Entry 1: Kanji term with KANA frequency
                    // Only generate this if the specific kana reading was observed
                    if (isKanaObserved)
                    {
                        string entryKey1 = $"{kanjiTerm}:{kanaText}:kana";
                        if (!addedEntries.Contains(entryKey1))
                        {
                            yomitanTermList.Add([
                                kanjiTerm, "freq",
                                new Dictionary<string, object>
                                {
                                    ["reading"] = kanaText,
                                    ["frequency"] = new Dictionary<string, object>
                                                    {
                                                        ["value"] = kanaRank, ["displayValue"] = $"{kanaRank}㋕"
                                                    }
                                }
                            ]);
                            addedEntries.Add(entryKey1);
                        }
                    }

                    // Entry 2: Kanji term with KANJI frequency
                    // This ONLY applies to the MAIN reading
                    // We generate this even if the main kana reading itself wasn't observed, 
                    // because the Kanji WAS observed.
                    if (kanaText != mainKanaReading)
                        continue;
                    
                    string entryKey2 = $"{kanjiTerm}:{kanaText}:kanji";
                    if (addedEntries.Contains(entryKey2)) 
                        continue;
                        
                    yomitanTermList.Add([
                        kanjiTerm, "freq",
                        new Dictionary<string, object>
                        {
                            ["reading"] = kanaText,
                            ["frequency"] = new Dictionary<string, object>
                                            {
                                                ["value"] = kanjiRank, ["displayValue"] = kanjiRank.ToString()
                                            }
                        }
                    ]);
                    addedEntries.Add(entryKey2);
                }
            }
        }

        // Sort by frequency rank (best/lowest rank first)
        yomitanTermList.Sort((a, b) =>
        {
            int GetRank(List<object> entry)
            {
                var freqData = (Dictionary<string, object>)entry[2];
                if (freqData.ContainsKey("frequency"))
                    return (int)((Dictionary<string, object>)freqData["frequency"])["value"];
                else
                    return (int)freqData["value"];
            }

            return GetRank(a).CompareTo(GetRank(b));
        });

        var termBankJson = JsonSerializer.Serialize(yomitanTermList,
                                                    new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

        using var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
        {
            var indexEntry = archive.CreateEntry("index.json", CompressionLevel.Optimal);
            await using (var entryStream = indexEntry.Open())
            await using (var streamWriter = new StreamWriter(entryStream, new UTF8Encoding(false)))
            {
                await streamWriter.WriteAsync(indexJson);
            }

            var termBankEntry = archive.CreateEntry("term_meta_bank_1.json", CompressionLevel.Optimal);
            await using (var entryStream = termBankEntry.Open())
            await using (var streamWriter = new StreamWriter(entryStream, new UTF8Encoding(false)))
            {
                await streamWriter.WriteAsync(termBankJson);
            }
        }

        return memoryStream.ToArray();
    }

    /// <summary>
    /// Retrieves kana readings. Includes:
    /// 1. Any reading that was observed (UsedInMediaAmount > 0)
    /// 2. The FIRST defined kana reading (Main Reading), if the word was observed at all.
    /// </summary>
    private static List<(string kanaText, int rank, int readingIndex)> GetKanaReadingsWithFrequencies(
        JmDictWord word,
        JmDictWordFrequency freq)
    {
        // Check if the word as a whole was observed
        bool wordWasObserved = freq.ReadingsUsedInMediaAmount.Any(amount => amount > 0);
        if (!wordWasObserved) return new List<(string, int, int)>();

        // Find the index of the first kana reading (default/main reading)
        int firstKanaIndex = -1;
        for (int i = 0; i < word.ReadingTypes.Count; i++)
        {
            if (word.ReadingTypes[i] != JmDictReadingType.KanaReading)
                continue;

            firstKanaIndex = i;
            break;
        }

        var kanaReadings = new List<(string kanaText, int rank, int readingIndex)>();

        for (int i = 0; i < word.Readings.Count; i++)
        {
            if (word.ReadingTypes[i] != JmDictReadingType.KanaReading) continue;

            // Include if:
            // 1. It was observed
            // OR
            // 2. It is the Main Reading (index == firstKanaIndex)
            bool isObserved = freq.ReadingsUsedInMediaAmount[i] > 0;
            bool isMain = (i == firstKanaIndex);

            if (!isObserved && !isMain)
                continue;

            // Note: Rank might be bad/default if !isObserved, but we need it for the struct.
            // The generation loop will filter out the ㋕ entry based on isObserved.
            kanaReadings.Add((word.Readings[i], freq.ReadingsFrequencyRank[i], i));
        }

        return kanaReadings;
    }

    /// <summary>
    /// Generates a zipped Yomitan frequency dictionary from a Deck.
    /// </summary>
    public static async Task<byte[]> GenerateYomitanFrequencyDeckFromDeck(IDbContextFactory<JitenDbContext> contextFactory, Deck deck)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        string title = "";
        if (deck.ParentDeckId != null)
        {
            var parentDeck = await context.Decks.AsNoTracking()
                                          .FirstOrDefaultAsync(d => d.DeckId == deck.ParentDeckId);
            if (parentDeck != null)
            {
                title += parentDeck.OriginalTitle.Substring(0, Math.Min(parentDeck.OriginalTitle.Length, 15)) + " - ";
            }
        }

        title += deck.OriginalTitle.Substring(0, Math.Min(deck.OriginalTitle.Length, 15));
        string revision = $"Jiten ({title}) {DateTime.UtcNow:yy-MM-dd}";
        string description = $"Dictionary based on frequency data of {title} from jiten.moe";

        var indexJson =
            $$"""{"title":"{{title}}","format":3,"revision":"{{revision}}","sequenced":false,"frequencyMode":"occurrence-based","author":"Jiten","url":"https://jiten.moe","description":"{{description}}"}""";

        var deckWords = context.DeckWords.AsNoTracking().Where(dw => dw.DeckId == deck.DeckId).ToList();

        var wordIds = deckWords.Select(dw => dw.WordId).Distinct().ToList();
        var allWords = await context.JMDictWords.AsNoTracking()
                                    .Where(w => wordIds.Contains(w.WordId))
                                    .ToDictionaryAsync(w => w.WordId);

        var yomitanTermList = new List<List<object>>();
        var addedEntries = new HashSet<string>();

        var deckWordsByWordIdAndReading = deckWords
                                          .GroupBy(dw => new { dw.WordId, dw.ReadingIndex })
                                          .ToDictionary(g => g.Key, g => g.Sum(dw => dw.Occurrences));

        var deckWordsByWordId = deckWords.GroupBy(dw => dw.WordId)
                                         .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var wordId in wordIds)
        {
            if (!allWords.TryGetValue(wordId, out JmDictWord? word) ||
                !deckWordsByWordId.TryGetValue(wordId, out var wordDeckWords)) continue;

            var kanaReadings = GetKanaReadingsWithOccurrences(word, deckWordsByWordIdAndReading, wordId);
            if (kanaReadings.Count == 0) continue;

            string mainKanaReading = kanaReadings[0].kanaText;

            // CASE 1: Create standalone kana entries
            foreach (var (kanaText, kanaOccurrences, kanaIndex) in kanaReadings)
            {
                // Only if observed
                if (kanaOccurrences <= 0) continue;

                if (addedEntries.Contains(kanaText)) continue;

                yomitanTermList.Add([
                    kanaText, "freq",
                    new Dictionary<string, object> { ["value"] = kanaOccurrences, ["displayValue"] = $"{kanaOccurrences}㋕" }
                ]);
                addedEntries.Add(kanaText);
            }

            // CASE 2: Create kanji entries
            for (int i = 0; i < word.Readings.Count; i++)
            {
                if (word.ReadingTypes[i] != JmDictReadingType.Reading) continue;

                var kanjiTerm = word.Readings[i];
                var key = new { WordId = wordId, ReadingIndex = (byte)i };
                var hasKanjiOccurrences = deckWordsByWordIdAndReading.TryGetValue(key, out int kanjiOccurrences);

                // Skip if kanji form has no occurrences
                if (!hasKanjiOccurrences || kanjiOccurrences == 0)
                    continue;

                foreach (var (kanaText, kanaOccurrences, kanaIndex) in kanaReadings)
                {
                    bool isKanaObserved = kanaOccurrences > 0;

                    // Entry 1: Kanji term with KANA occurrences
                    if (isKanaObserved)
                    {
                        string entryKey1 = $"{kanjiTerm}:{kanaText}:kana";
                        if (!addedEntries.Contains(entryKey1))
                        {
                            yomitanTermList.Add(new List<object>
                                                {
                                                    kanjiTerm, "freq",
                                                    new Dictionary<string, object>
                                                    {
                                                        ["reading"] = kanaText,
                                                        ["frequency"] = new Dictionary<string, object>
                                                                        {
                                                                            ["value"] = kanaOccurrences,
                                                                            ["displayValue"] = $"{kanaOccurrences}㋕"
                                                                        }
                                                    }
                                                });
                            addedEntries.Add(entryKey1);
                        }
                    }

                    // Entry 2: Kanji term with KANJI occurrences (Main Reading only)
                    if (kanaText != mainKanaReading) continue;

                    string entryKey2 = $"{kanjiTerm}:{kanaText}:kanji";
                    if (addedEntries.Contains(entryKey2)) continue;
                    yomitanTermList.Add([
                        kanjiTerm,
                        "freq",
                        new Dictionary<string, object>
                        {
                            ["reading"] = kanaText,
                            ["frequency"] = new Dictionary<string, object>
                                            {
                                                ["value"] = kanjiOccurrences, ["displayValue"] = kanjiOccurrences.ToString()
                                            }
                        }
                    ]);
                    addedEntries.Add(entryKey2);
                }
            }
        }

        // Sort by occurrences (highest first for occurrence-based)
        yomitanTermList.Sort((a, b) =>
        {
            int GetOccurrences(List<object> entry)
            {
                var freqData = (Dictionary<string, object>)entry[2];
                if (freqData.ContainsKey("frequency"))
                    return (int)((Dictionary<string, object>)freqData["frequency"])["value"];
                else
                    return (int)freqData["value"];
            }

            return GetOccurrences(b).CompareTo(GetOccurrences(a));
        });

        var termBankJson = JsonSerializer.Serialize(yomitanTermList,
                                                    new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

        using var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
        {
            var indexEntry = archive.CreateEntry("index.json", CompressionLevel.Optimal);
            await using (var entryStream = indexEntry.Open())
            await using (var streamWriter = new StreamWriter(entryStream, new UTF8Encoding(false)))
            {
                await streamWriter.WriteAsync(indexJson);
            }

            var termBankEntry = archive.CreateEntry("term_meta_bank_1.json", CompressionLevel.Optimal);
            await using (var entryStream = termBankEntry.Open())
            await using (var streamWriter = new StreamWriter(entryStream, new UTF8Encoding(false)))
            {
                await streamWriter.WriteAsync(termBankJson);
            }
        }

        return memoryStream.ToArray();
    }

    private static List<(string kanaText, int occurrences, int readingIndex)> GetKanaReadingsWithOccurrences<TKey>(
        JmDictWord word,
        IReadOnlyDictionary<TKey, int> deckWordsByWordIdAndReading,
        int wordId) where TKey : notnull
    {
        // Check if word is observed
        bool wordObserved = false;
        // Simple check: iterate all reading indices for this word
        for (int i = 0; i < word.Readings.Count; i++)
        {
            var key = (TKey)(object)new { WordId = wordId, ReadingIndex = (byte)i };
            if (deckWordsByWordIdAndReading.TryGetValue(key, out int occ) && occ > 0)
            {
                wordObserved = true;
                break;
            }
        }

        if (!wordObserved) return new List<(string, int, int)>();

        int firstKanaIndex = -1;
        for (int i = 0; i < word.ReadingTypes.Count; i++)
        {
            if (word.ReadingTypes[i] == JmDictReadingType.KanaReading)
            {
                firstKanaIndex = i;
                break;
            }
        }

        var kanaReadings = new List<(string kanaText, int occurrences, int readingIndex)>();

        for (int i = 0; i < word.Readings.Count; i++)
        {
            if (word.ReadingTypes[i] != JmDictReadingType.KanaReading) continue;

            var key = (TKey)(object)new { WordId = wordId, ReadingIndex = (byte)i };
            int occurrences = 0;
            if (deckWordsByWordIdAndReading.TryGetValue(key, out int val))
                occurrences = val;

            bool isObserved = occurrences > 0;
            bool isMain = (i == firstKanaIndex);

            if (!isObserved && !isMain)
                continue;

            kanaReadings.Add((word.Readings[i], occurrences, i));
        }

        return kanaReadings;
    }
}