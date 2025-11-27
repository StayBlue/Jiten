using System.Text.Json.Serialization;

namespace Jiten.Core.Data.Providers.Tmdb;

public class TmdbMovie
{
    [JsonPropertyName("id")] public int Id { get; set; }

    /// <summary>
    /// Japanese title
    /// </summary>
    [JsonPropertyName("original_title")]
    public required string OriginalTitle { get; set; }

    /// <summary>
    /// English or romaji title
    /// </summary>
    [JsonPropertyName("title")]
    public required string Title { get; set; }

    [JsonPropertyName("imdb_id")] public string? ImdbId { get; set; }

    [JsonPropertyName("release_date")] public DateTime ReleaseDate { get; set; }

    [JsonPropertyName("poster_path")] public string? PosterPath { get; set; }
    
    [JsonPropertyName("overview")] public required string Description { get; set; } 
    [JsonPropertyName("vote_average")] public double VoteAverage { get; set; }
    [JsonPropertyName("genres")] public List<TmdbGenre> Genres { get; set; } = new();
    [JsonPropertyName("adult")] public bool Adult { get; set; } 
}