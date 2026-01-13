using System.Text.Json.Serialization;

namespace Jiten.Core.Data.Providers.Tmdb;

public class TmdbTv
{
    [JsonPropertyName("id")] public int Id { get; set; }

    /// <summary>
    /// Japanese title
    /// </summary>
    [JsonPropertyName("original_name")]
    public required string OriginalName { get; set; }

    /// <summary>
    /// English or romaji title
    /// </summary>
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("first_air_date")] public DateTime FirstAirDate { get; set; }

    [JsonPropertyName("poster_path")] public string? PosterPath { get; set; }

    [JsonPropertyName("overview")] public required string Description { get; set; }
    [JsonPropertyName("vote_average")] public double VoteAverage { get; set; }
    [JsonPropertyName("genres")] public List<TmdbGenre> Genres { get; set; } = new();
    [JsonPropertyName("adult")] public bool Adult { get; set; }
    [JsonPropertyName("original_language")] public string? OriginalLanguage { get; set; }
}