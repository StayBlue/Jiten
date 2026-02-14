using System.Text.Json.Serialization;

namespace Jiten.Core.Data.Providers.Tmdb;

public class TmdbMovieAlternativeTitle
{
    [JsonPropertyName("id")] public int Id { get; set; }

    [JsonPropertyName("titles")] public List<TmdbMovieAlternativeTitleItem> Titles { get; set; } = [];
}

public class TmdbMovieAlternativeTitleItem
{
    [JsonPropertyName("iso_3166_1")] public string Iso31661 { get; set; } = default!;
    [JsonPropertyName("title")] public string Title { get; set; } = default!;
}