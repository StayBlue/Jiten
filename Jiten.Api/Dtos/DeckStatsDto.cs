namespace Jiten.Api.Dtos;

/// <summary>
/// Advanced deck statistics response DTO
/// </summary>
public class DeckStatsDto
{
    /// <summary>
    /// Deck ID
    /// </summary>
    public int DeckId { get; set; }

    /// <summary>
    /// Total unique words in the deck
    /// </summary>
    public int TotalUniqueWords { get; set; }

    /// <summary>
    /// When the statistics were computed
    /// </summary>
    public DateTimeOffset ComputedAt { get; set; }

    /// <summary>
    /// Goodness of fit (R-squared value, 0-1)
    /// </summary>
    public double RSquared { get; set; }

    /// <summary>
    /// Coverage milestones: percentage → words needed
    /// </summary>
    public Dictionary<string, int> Milestones { get; set; } = new();
}

/// <summary>
/// Single data point on the coverage curve
/// </summary>
public class CurveDatumDto
{
    /// <summary>
    /// Word rank (1-based)
    /// </summary>
    public int Rank { get; set; }

    /// <summary>
    /// Coverage percentage at this rank (0-100)
    /// </summary>
    public double Coverage { get; set; }
}
