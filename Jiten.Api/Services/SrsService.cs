using Jiten.Core;
using Jiten.Core.Data.FSRS;
using Jiten.Core.Data.JMDict;
using Microsoft.EntityFrameworkCore;

namespace Jiten.Api.Services;

/// <summary>
/// Service for synchronising FSRS state from kanji reading cards to kana reading cards.
/// </summary>
public class SrsService(JitenDbContext context, UserDbContext userContext, ILogger<SrsService> logger) : ISrsService
{
    /// <summary>
    /// Syncs FSRS state from a single kanji reading card to its corresponding kana reading card.
    /// </summary>
    public async Task SyncKanaReading(string userId, int wordId, byte readingIndex, FsrsCard sourceCard, DateTime syncDateTime)
    {
        // Fetch word reading types
        var jmdictWord = await context.JMDictWords
            .AsNoTracking()
            .Where(w => w.WordId == wordId)
            .Select(w => new { w.WordId, w.ReadingTypes })
            .FirstOrDefaultAsync();

        if (jmdictWord == null) return;
        if (readingIndex >= jmdictWord.ReadingTypes.Count) return;

        // Only sync from Reading (kanji) to KanaReading
        if (jmdictWord.ReadingTypes[readingIndex] != JmDictReadingType.Reading) return;

        // Find kana reading index
        var kanaIndex = jmdictWord.ReadingTypes.FindIndex(t => t == JmDictReadingType.KanaReading);
        if (kanaIndex < 0) return; // No kana variant exists

        // Load or create kana card
        var kanaCard = await userContext.FsrsCards
            .FirstOrDefaultAsync(c => c.UserId == userId &&
                                      c.WordId == wordId &&
                                      c.ReadingIndex == (byte)kanaIndex);

        if (kanaCard == null)
        {
            // Create new synced card
            kanaCard = new FsrsCard(
                userId,
                wordId,
                (byte)kanaIndex,
                state: sourceCard.State,
                due: sourceCard.Due,
                lastReview: syncDateTime)
            {
                Stability = sourceCard.Stability,
                Difficulty = sourceCard.Difficulty,
                Step = sourceCard.Step
            };
            await userContext.FsrsCards.AddAsync(kanaCard);
            logger.LogInformation("Synced kana reading: WordId={WordId}, KanaIndex={KanaIndex}, State={State}",
                wordId, kanaIndex, sourceCard.State);
        }
        else
        {
            // Update existing card with synced state
            kanaCard.State = sourceCard.State;
            kanaCard.Stability = sourceCard.Stability;
            kanaCard.Difficulty = sourceCard.Difficulty;
            kanaCard.Due = sourceCard.Due;
            kanaCard.LastReview = syncDateTime;
            kanaCard.Step = sourceCard.Step;

            logger.LogInformation("Updated synced kana reading: WordId={WordId}, KanaIndex={KanaIndex}, State={State}",
                wordId, kanaIndex, sourceCard.State);
        }

        await userContext.SaveChangesAsync();
    }

    /// <summary>
    /// Syncs FSRS state from multiple kanji reading cards to their corresponding kana reading cards (batch optimised).
    /// </summary>
    public async Task SyncKanaReadingBatch(string userId, IEnumerable<(int WordId, byte ReadingIndex, FsrsCard SourceCard, bool Overwrite)> cards, DateTime syncDateTime)
    {
        var cardsList = cards.ToList();
        if (cardsList.Count == 0) return;

        // Step 1: Extract all unique WordIds
        var wordIds = cardsList.Select(c => c.WordId).Distinct().ToList();

        // Step 2: Fetch all JmDictWords with ReadingTypes in a single query
        var jmdictWords = await context.JMDictWords
            .AsNoTracking()
            .Where(w => wordIds.Contains(w.WordId))
            .Select(w => new { w.WordId, w.ReadingTypes })
            .ToListAsync();

        // Step 3: Build lookup: WordId → ReadingTypes[]
        var readingTypesLookup = jmdictWords.ToDictionary(w => w.WordId, w => w.ReadingTypes);

        // Step 4: Filter cards that need sync and build kana card requirements
        var kanaCardsToSync = new List<(int WordId, byte KanaIndex, FsrsCard SourceCard, bool Overwrite)>();

        foreach (var (wordId, readingIndex, sourceCard, overwrite) in cardsList)
        {
            // Check if word exists in JMDict
            if (!readingTypesLookup.TryGetValue(wordId, out var readingTypes)) continue;
            if (readingIndex >= readingTypes.Count) continue;

            // Only sync from Reading (kanji) to KanaReading
            if (readingTypes[readingIndex] != JmDictReadingType.Reading) continue;

            // Find kana reading index
            var kanaIndex = readingTypes.FindIndex(t => t == JmDictReadingType.KanaReading);
            if (kanaIndex < 0) continue; // No kana variant exists

            kanaCardsToSync.Add((wordId, (byte)kanaIndex, sourceCard, overwrite));
        }

        if (kanaCardsToSync.Count == 0) return;

        // Step 5: Fetch all existing kana FsrsCards in a single query
        var targetWordIds = kanaCardsToSync.Select(k => k.WordId).Distinct().ToList();
        
        var existingKanaCards = await userContext.FsrsCards
                                                 .Where(c => c.UserId == userId && targetWordIds.Contains(c.WordId))
                                                 .ToListAsync();

        // Step 6: Build lookup: (WordId, ReadingIndex) → existing FsrsCard
        var existingKanaCardsLookup = existingKanaCards.ToDictionary(c => (c.WordId, c.ReadingIndex));

        // Step 7: Process each kana card (deduplicate to avoid duplicate key errors when multiple kanji readings map to same kana)
        var newKanaCards = new List<FsrsCard>();
        var updatedCount = 0;
        var processedKanaKeys = new HashSet<(int, byte)>();

        foreach (var (wordId, kanaIndex, sourceCard, overwrite) in kanaCardsToSync)
        {
            // Skip if we've already processed this kana card in this batch
            if (!processedKanaKeys.Add((wordId, kanaIndex)))
                continue;

            if (existingKanaCardsLookup.TryGetValue((wordId, kanaIndex), out var existingCard))
            {
                // Card exists - only update if Overwrite is true
                if (overwrite)
                {
                    existingCard.State = sourceCard.State;
                    existingCard.Stability = sourceCard.Stability;
                    existingCard.Difficulty = sourceCard.Difficulty;
                    existingCard.Due = sourceCard.Due;
                    existingCard.LastReview = syncDateTime;
                    existingCard.Step = sourceCard.Step;
                    updatedCount++;
                }
            }
            else
            {
                // Card doesn't exist - create new one
                var newCard = new FsrsCard(
                    userId,
                    wordId,
                    kanaIndex,
                    state: sourceCard.State,
                    due: sourceCard.Due,
                    lastReview: syncDateTime)
                {
                    Stability = sourceCard.Stability,
                    Difficulty = sourceCard.Difficulty,
                    Step = sourceCard.Step
                };
                newKanaCards.Add(newCard);
            }
        }

        // Step 8: Bulk insert new kana cards
        if (newKanaCards.Count > 0)
        {
            await userContext.FsrsCards.AddRangeAsync(newKanaCards);
        }

        // Step 9: Save all changes
        if (newKanaCards.Count > 0 || updatedCount > 0)
        {
            await userContext.SaveChangesAsync();
            logger.LogInformation("Batch synced kana readings: {NewCount} created, {UpdatedCount} updated",
                newKanaCards.Count, updatedCount);
        }
    }
}
