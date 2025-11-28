namespace Jiten.Core.Data.User;

public enum KnownState
{
    New = 0,
    Young = 1,
    Mature = 2,
    Blacklisted = 3,
    Due = 4
}

public class UserKnownWord
{
    public string UserId { get; set; } = string.Empty;
    public int WordId { get; set; }
    public byte ReadingIndex { get; set; }
    public DateTime LearnedDate { get; set; } = DateTime.UtcNow;
    public KnownState KnownState { get; set; } = KnownState.Mature;
}