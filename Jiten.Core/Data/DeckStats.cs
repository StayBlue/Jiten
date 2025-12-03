using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Globalization;
using System.Text.Json.Serialization;

namespace Jiten.Core.Data;

/// <summary>
/// Parametric statistics for a deck (coverage curve, etc.)
/// </summary>
public class DeckStats
{
    /// <summary>
    /// Primary key
    /// </summary>
    [Key]
    [ForeignKey("Deck")]
    public int DeckId { get; set; }

    /// <summary>
    /// Coverage curve parameters (CSV format: "A,B,C,RSquared,RMSE,TotalWords")
    /// Example: "12.5,2.3,-0.45,0.987,1.23,5432"
    /// Power law model: Coverage(rank) = min(100, A × (rank + B)^C)
    /// </summary>
    [MaxLength(100)]
    public string? CoverageCurve { get; set; }

    /// <summary>
    /// When the statistics were computed
    /// </summary>
    public DateTimeOffset ComputedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Sampled coverage data for accurate milestone calculation
    /// Format: JSON array of [rank, coverage] pairs
    /// Sampled at: 1% intervals (0-99%), then 0.1% intervals (99-100%)
    /// Example: "[[1,12.5],[50,45.2],[100,58.3],...]"
    /// </summary>
    [MaxLength(10000)]
    public string? CoverageSamplesJson { get; set; }

    /// <summary>
    /// Parameter A: Amplitude/scaling factor
    /// </summary>
    [NotMapped]
    [JsonIgnore]
    public double? ParameterA => ParseCoverageParam(0);

    /// <summary>
    /// Parameter B: Offset parameter
    /// </summary>
    [NotMapped]
    [JsonIgnore]
    public double? ParameterB => ParseCoverageParam(1);

    /// <summary>
    /// Parameter C: Exponent parameter
    /// </summary>
    [NotMapped]
    [JsonIgnore]
    public double? ParameterC => ParseCoverageParam(2);

    /// <summary>
    /// Goodness of fit (R-squared value, 0-1)
    /// </summary>
    [NotMapped]
    [JsonIgnore]
    public double? RSquared => ParseCoverageParam(3);

    /// <summary>
    /// Root Mean Square Error of the fit
    /// </summary>
    [NotMapped]
    [JsonIgnore]
    public double? RMSE => ParseCoverageParam(4);

    /// <summary>
    /// Total unique words in the deck
    /// </summary>
    [NotMapped]
    [JsonIgnore]
    public int? TotalUniqueWords => (int?)ParseCoverageParam(5);
    
    /// <summary>
    /// Navigation property to parent Deck
    /// </summary>
    [JsonIgnore]
    public Deck Deck { get; set; } = null!;

    private double? ParseCoverageParam(int index)
    {
        if (string.IsNullOrEmpty(CoverageCurve)) return null;
        var parts = CoverageCurve.Split(',');
        if (parts.Length <= index) return null;
        return double.TryParse(parts[index], NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;
    }
}
