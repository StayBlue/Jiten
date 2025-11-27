using System.Text.Json.Serialization;

namespace Jiten.Core.Data.Providers.Tmdb;

public class TmdbGenreWrapper
{
    // For Movies
    [JsonPropertyName("keywords")]
    public List<TmdbGenre> Keywords { get; set; } = new();
    
    // For TV
    [JsonPropertyName("results")]
    public List<TmdbGenre> Results { get; set; } = new();
}

public class TmdbGenre
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
}