namespace Jiten.Core.Data;

public class UserCoverage
{
    public string UserId { get; set; } = string.Empty;
    public int DeckId { get; set; }

    public double Coverage { get; set; }
    public double UniqueCoverage { get; set; }
}