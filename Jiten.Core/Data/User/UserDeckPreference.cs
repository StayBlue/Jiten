namespace Jiten.Core.Data.User;

public class UserDeckPreference
{
    public string UserId { get; set; } = null!;
    public int DeckId { get; set; }
    public DeckStatus Status { get; set; }
    public bool IsFavourite { get; set; }
    public bool IsIgnored { get; set; }
}
