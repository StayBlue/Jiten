namespace Jiten.Core.Data.User;

public class UserAccomplishment
{
    public int AccomplishmentId { get; set; }

    public string UserId { get; set; } = string.Empty;
    public MediaType? MediaType { get; set; }  // null = global (all types)

    // Aggregated statistics
    public int CompletedDeckCount { get; set; }
    public long TotalCharacterCount { get; set; }
    public long TotalWordCount { get; set; }
    public int UniqueWordCount { get; set; }
    public int UniqueWordUsedOnceCount { get; set; }
    public int UniqueKanjiCount { get; set; }

    public DateTimeOffset LastComputedAt { get; set; }
}
