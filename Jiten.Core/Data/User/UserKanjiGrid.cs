using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace Jiten.Core.Data;

public class UserKanjiGrid
{
    public string UserId { get; set; } = string.Empty;

    // Stored as JSON string in database
    // Format: {"日": [5.5, 10], "本": [3.0, 5]} where array is [score, wordCount]
    public string KanjiScoresJson { get; set; } = "{}";

    // Backing field for cached deserialization
    [NotMapped]
    private Dictionary<string, double[]>? _cachedScores;

    // Helper property for working with the data in code (deserializes every time - use GetKanjiScoresOnce for read-only access)
    [NotMapped]
    public Dictionary<string, double[]> KanjiScores
    {
        get => JsonSerializer.Deserialize<Dictionary<string, double[]>>(KanjiScoresJson) ?? new();
        set
        {
            KanjiScoresJson = JsonSerializer.Serialize(value);
            _cachedScores = null;
        }
    }

    // Deserializes once and caches the result for read-only access
    public Dictionary<string, double[]> GetKanjiScoresOnce()
    {
        return _cachedScores ??= JsonSerializer.Deserialize<Dictionary<string, double[]>>(KanjiScoresJson) ?? new();
    }

    public DateTimeOffset LastComputedAt { get; set; }
}
