using System.Text.Json.Serialization;

namespace Jiten.Core.Data.Providers.Tmdb;

public class TmdbTvAlternativeTitle
{
    [JsonPropertyName("id")] public int Id { get; set; }

    [JsonPropertyName("results")] public List<TmdbTvAlternativeTitleItem> Titles { get; set; } = [];
}

public class TmdbTvAlternativeTitleItem
{
    [JsonPropertyName("iso_3166_1")] public string Iso31661 { get; set; } = default!;
    [JsonPropertyName("title")] public string Title { get; set; } = default!;
}