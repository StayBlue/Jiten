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
        string indexUrl = mediaType != null ? $"https://api.jiten.moe/api/frequency-list/index?mediaType={mediaType}" : "https://api.jiten.moe/api/frequency-list/index" ;
        string downloadUrl = mediaType != null ? $"https://api.jiten.moe/api/frequency-list/download?mediaType={mediaType}" : "https://api.jiten.moe/api/frequency-list/download" ;

        return
            $$"""{"title":"{{title}}","format":3,"revision":"{{revision}}","isUpdatable":true,"indexUrl":"{{indexUrl}}","downloadUrl":"{{downloadUrl}}","sequenced":false,"frequencyMode":"rank-based","author":"Jiten","url":"https://jiten.moe","description":"{{description}}"}""";
    }

    /// <summary>
    /// Generates a zipped Yomitan frequency dictionary for a given media type.
    /// </summary>
    /// <param name="context">The database context.</param>
    /// <param name="mediaType">The media type to generate the frequency deck for. null for global</param>
    /// <returns>A byte array representing the zipped dictionary file.</returns>
    public static async Task<byte[]> GenerateYomitanFrequencyDeck(IDbContextFactory<JitenDbContext> contextFactory,
                                                                  List<JmDictWordFrequency> frequencies, MediaType? mediaType, string indexJson)
    {
        await using var context = await contextFactory.CreateDbContextAsync();

        var wordIds = frequencies.Select(f => f.WordId).ToList();
        var allWords = await context.JMDictWords.AsNoTracking()
                                    .Where(w => wordIds.Contains(w.WordId))
                                    .ToDictionaryAsync(w => w.WordId);

        var yomitanTermList = new List<List<object>>();
        var addedEntries = new HashSet<string>(); // Prevents adding duplicate (term, reading) pairs

        foreach (var freq in frequencies)
        {
            if (!allWords.TryGetValue(freq.WordId, out JmDictWord? word)) continue;

            // Find the best (most frequent) kana reading to use as the default for kanji forms
            string? bestKanaReading = null;
            int bestKanaRank = int.MaxValue;

            for (int i = 0; i < word.Readings.Count; i++)
            {
                if (word.ReadingTypes[i] != JmDictReadingType.KanaReading) continue;
                if (freq.ReadingsFrequencyRank[i] >= bestKanaRank) continue;
                bestKanaRank = freq.ReadingsFrequencyRank[i];
                bestKanaReading = word.Readings[i];
            }

            // Now, iterate through all readings and create the appropriate entries
            for (int i = 0; i < word.Readings.Count; i++)
            {
                // Skip if this specific reading has a frequency of 0 in the selected media
                if (freq.ReadingsUsedInMediaAmount[i] == 0) continue;

                var currentTerm = word.Readings[i];
                var currentRank = freq.ReadingsFrequencyRank[i];
                var currentType = word.ReadingTypes[i];

                // Case 1: The reading is a Kanji form
                if (currentType == JmDictReadingType.Reading)
                {
                    // A kanji form needs a kana reading to be valid in Yomitan freq lists.
                    // We use the best one we found earlier.
                    if (bestKanaReading == null) continue;

                    string entryKey = $"{currentTerm}:{bestKanaReading}";
                    if (addedEntries.Contains(entryKey)) continue;

                    yomitanTermList.Add(new List<object>
                                        {
                                            currentTerm, "freq",
                                            new
                                            {
                                                reading = bestKanaReading,
                                                frequency = new { value = currentRank, displayValue = currentRank.ToString() }
                                            }
                                        });
                    addedEntries.Add(entryKey);
                }
                // Case 2: The reading is a Kana form
                else
                {
                    if (addedEntries.Contains(currentTerm)) continue;

                    yomitanTermList.Add([currentTerm, "freq", new { value = currentRank, displayValue = currentRank.ToString() }]);
                    addedEntries.Add(currentTerm);
                }
            }
        }

        var termBankJson = JsonSerializer.Serialize(yomitanTermList,
                                                    new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

        // 3. Zip the index and term bank files into a memory stream
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
    /// Generates a zipped Yomitan frequency dictionary from a Deck.
    /// </summary>
    /// <param name="options">Database context options.</param>
    /// <param name="deck"></param>
    /// <param name="title"></param>
    /// <returns>A byte array representing the zipped dictionary file.</returns>
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
        var addedEntries = new HashSet<string>(); // Prevents adding duplicate (term, reading) pairs

        // Group deckWords by WordId AND ReadingIndex to get occurrences for each specific reading
        var deckWordsByWordIdAndReading = deckWords
                                          .GroupBy(dw => new { dw.WordId, dw.ReadingIndex })
                                          .ToDictionary(g => g.Key, g => g.Sum(dw => dw.Occurrences));

        // Group by WordId to get all readings for each word
        var deckWordsByWordId = deckWords.GroupBy(dw => dw.WordId)
                                         .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var wordId in wordIds)
        {
            if (!allWords.TryGetValue(wordId, out JmDictWord? word) ||
                !deckWordsByWordId.TryGetValue(wordId, out var wordDeckWords)) continue;

            // For each reading in the word, check if it has occurrences in the deck
            for (int readingIndex = 0; readingIndex < word.Readings.Count; readingIndex++)
            {
                var key = new { WordId = wordId, ReadingIndex = (byte)readingIndex };

                // Skip if this specific reading has no occurrences
                if (!deckWordsByWordIdAndReading.TryGetValue(key, out int occurrences) || occurrences == 0)
                    continue;

                var currentTerm = word.Readings[readingIndex];
                var currentType = word.ReadingTypes[readingIndex];

                // Case 1: The reading is a Kanji form (needs a kana reading)
                if (currentType == JmDictReadingType.Reading)
                {
                    // Find the best kana reading for this kanji form
                    // We'll use the most frequent kana reading from this word, or the first available one
                    string? kanjiReading = null;
                    int bestKanaOccurrences = 0;

                    // Look for kana readings in the same word
                    for (int i = 0; i < word.Readings.Count; i++)
                    {
                        if (word.ReadingTypes[i] != JmDictReadingType.KanaReading) continue;

                        var kanaKey = new { WordId = wordId, ReadingIndex = (byte)i };
                        if (deckWordsByWordIdAndReading.TryGetValue(kanaKey, out int kanaOccurrences) &&
                            kanaOccurrences > bestKanaOccurrences)
                        {
                            bestKanaOccurrences = kanaOccurrences;
                            kanjiReading = word.Readings[i];
                        }
                    }

                    // If no kana reading found with occurrences, use the first kana reading as fallback
                    if (kanjiReading == null)
                    {
                        for (int i = 0; i < word.Readings.Count; i++)
                        {
                            if (word.ReadingTypes[i] == JmDictReadingType.KanaReading)
                            {
                                kanjiReading = word.Readings[i];
                                break;
                            }
                        }
                    }

                    // Skip if we still don't have a kana reading
                    if (kanjiReading == null) continue;

                    string entryKey = $"{currentTerm}:{kanjiReading}";
                    if (addedEntries.Contains(entryKey)) continue;

                    yomitanTermList.Add(new List<object>
                                        {
                                            currentTerm, "freq",
                                            new
                                            {
                                                reading = kanjiReading,
                                                frequency = new { value = occurrences, displayValue = occurrences.ToString() }
                                            }
                                        });
                    addedEntries.Add(entryKey);
                }
                // Case 2: The reading is a Kana form
                else if (currentType == JmDictReadingType.KanaReading)
                {
                    if (addedEntries.Contains(currentTerm)) continue;

                    yomitanTermList.Add([currentTerm, "freq", new { value = occurrences, displayValue = occurrences.ToString() }]);
                    addedEntries.Add(currentTerm);
                }
            }
        }

        var termBankJson = JsonSerializer.Serialize(yomitanTermList,
                                                    new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

        // 3. Zip the index and term bank files into a memory stream
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
}
