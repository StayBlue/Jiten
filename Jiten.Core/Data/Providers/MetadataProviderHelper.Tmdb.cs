using System.Text.Json;
using Jiten.Core.Data;
using Jiten.Core.Data.Providers;
using Jiten.Core.Data.Providers.Tmdb;

namespace Jiten.Core;

public static partial class MetadataProviderHelper
{
    public static async Task<Metadata> TmdbMovieApi(string tmdbId, string tmdbApiKey)
    {
        var http = new HttpClient();

        var response = await http.GetAsync($"https://api.themoviedb.org/3/movie/{tmdbId}?api_key={tmdbApiKey}");
        if (!response.IsSuccessStatusCode) return new Metadata();

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<TmdbMovie>(content);

        if (result == null)
            return new Metadata();

        // Get aliases
        List<string> aliases = new();
        response = await http.GetAsync($"https://api.themoviedb.org/3/movie/{tmdbId}/alternative_titles?api_key={tmdbApiKey}");
        if (response.IsSuccessStatusCode)
        {
            content = await response.Content.ReadAsStringAsync();
            var alternativeTitles = JsonSerializer.Deserialize<TmdbMovieAlternativeTitle>(content);
            if (alternativeTitles != null)
                aliases = alternativeTitles.Titles.Select(t => t.Title).ToList();
        }

        List<TmdbGenre> keywords = new();
        response = await http.GetAsync($"https://api.themoviedb.org/3/movie/{tmdbId}/keywords?api_key={tmdbApiKey}");
        if (response.IsSuccessStatusCode)
        {
            content = await response.Content.ReadAsStringAsync();
            keywords = JsonSerializer.Deserialize<TmdbGenreWrapper>(content)?.Keywords ?? [];
        }

        if (result.PosterPath != null)
            result.PosterPath = $"https://image.tmdb.org/t/p/w500/{result.PosterPath}";

        var links = new List<Link>();
        if (result.ImdbId != null)
        {
            links.Add(new Link { LinkType = LinkType.Imdb, Url = $"https://www.imdb.com/title/{result.ImdbId}" });
        }

        links.Add(new Link { LinkType = LinkType.Tmdb, Url = $"https://www.themoviedb.org/movie/{tmdbId}" });

        return new Metadata
               {
                   OriginalTitle = result.OriginalTitle, EnglishTitle = result.Title, ReleaseDate = result.ReleaseDate, Links = links,
                   Image = result.PosterPath, Description = result.Description, Aliases = aliases, Rating = (int)(result.VoteAverage * 10),
                   IsAdultOnly = result.Adult, Genres = result.Genres.Select(g => g.Name).ToList(),
                   Tags = keywords.Select(k => (k.Name, 100)).ToList()
               };
    }

    public static async Task<Metadata> TmdbTvApi(string tmdbId, string tmdbApiKey)
    {
        var http = new HttpClient();

        var response = await http.GetAsync($"https://api.themoviedb.org/3/tv/{tmdbId}?api_key={tmdbApiKey}");
        if (!response.IsSuccessStatusCode) return new Metadata();

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<TmdbTv>(content);

        if (result == null)
            return new Metadata();

        // Get aliases
        List<string> aliases = new();
        response = await http.GetAsync($"https://api.themoviedb.org/3/tv/{tmdbId}/alternative_titles?api_key={tmdbApiKey}");
        if (response.IsSuccessStatusCode)
        {
            content = await response.Content.ReadAsStringAsync();
            var alternativeTitles = JsonSerializer.Deserialize<TmdbTvAlternativeTitle>(content);
            if (alternativeTitles != null)
                aliases = alternativeTitles.Titles.Select(t => t.Title).ToList();
        }

        List<TmdbGenre> keywords = new();
        response = await http.GetAsync($"https://api.themoviedb.org/3/tv/{tmdbId}/keywords?api_key={tmdbApiKey}");
        if (response.IsSuccessStatusCode)
        {
            content = await response.Content.ReadAsStringAsync();
            keywords = JsonSerializer.Deserialize<TmdbGenreWrapper>(content)?.Results ?? [];
        }

        if (result.PosterPath != null)
            result.PosterPath = $"https://image.tmdb.org/t/p/w500/{result.PosterPath}";

        return new Metadata
               {
                   OriginalTitle = result.OriginalName, EnglishTitle = result.Name, ReleaseDate = result.FirstAirDate,
                   Image = result.PosterPath,
                   Links = [new Link { LinkType = LinkType.Tmdb, Url = $"https://www.themoviedb.org/tv/{tmdbId}" }],
                   Description = result.Description, Aliases = aliases, Rating = (int)(result.VoteAverage * 10), IsAdultOnly = result.Adult,
                   Genres = result.Genres.Select(g => g.Name).ToList(), Tags = keywords.Select(k => (k.Name, 100)).ToList()
               };
    }
}