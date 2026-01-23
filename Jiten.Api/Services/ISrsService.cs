using Jiten.Core.Data.FSRS;

namespace Jiten.Api.Services;

/// <summary>
/// Service for synchronising FSRS state from kanji reading cards to kana reading cards.
/// </summary>
public interface ISrsService
{
    /// <summary>
    /// Syncs FSRS state from a single kanji reading card to its corresponding kana reading card.
    /// </summary>
    /// <param name="userId">User ID</param>
    /// <param name="wordId">Word ID</param>
    /// <param name="readingIndex">Reading index of the source card</param>
    /// <param name="sourceCard">Source FSRS card to sync from</param>
    /// <param name="syncDateTime">Timestamp for the sync operation</param>
    Task SyncKanaReading(string userId, int wordId, byte readingIndex, FsrsCard sourceCard, DateTime syncDateTime);

    /// <summary>
    /// Syncs FSRS state from multiple kanji reading cards to their corresponding kana reading cards (batch optimised).
    /// </summary>
    /// <param name="userId">User ID</param>
    /// <param name="cards">Collection of cards to sync with their overwrite flags</param>
    /// <param name="syncDateTime">Timestamp for the sync operation</param>
    /// <returns>Number of new kana cards created</returns>
    Task<int> SyncKanaReadingBatch(string userId, IEnumerable<(int WordId, byte ReadingIndex, FsrsCard SourceCard, bool Overwrite)> cards, DateTime syncDateTime);
}
