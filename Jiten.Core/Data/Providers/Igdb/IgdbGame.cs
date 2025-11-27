using System.Text.Json.Serialization;

namespace Jiten.Core.Data.Providers.Igdb;

public class IgdbGame
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("storyline")]
    public string? Storyline { get; set; }

    [JsonPropertyName("summary")]
    public string? Summary { get; set; }

    [JsonPropertyName("cover")]
    public int Cover { get; set; }

    [JsonPropertyName("first_release_date")]
    public double FirstReleaseDate { get; set; }

    [JsonPropertyName("game_localizations")]
    public int[] GameLocalizations { get; set; } = [];

    [JsonPropertyName("url")]
    public required string Url { get; set; }

    [JsonPropertyName("rating")]
    public double Rating { get; set; }
    
    [JsonPropertyName("genres")]
    public List<int> Genres { get; set; } = new();
    
    [JsonPropertyName("themes")]
    public List<int> Themes { get; set; } = new();
}