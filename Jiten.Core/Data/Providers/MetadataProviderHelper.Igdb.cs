using System.Text.Json;
using Jiten.Core.Data;
using Jiten.Core.Data.Providers;
using Jiten.Core.Data.Providers.Igdb;

namespace Jiten.Core;

public static partial class MetadataProviderHelper
{
    private static IgdbAccessToken? _igdbAccessToken;

    private static async Task<string> GetIgdbAccessToken(string clientId, string clientSecret)
    {
        if (_igdbAccessToken is { IsExpired: false })
            return _igdbAccessToken.AccessToken;

        var http = new HttpClient();
        var response = await http.PostAsync(
                                            $"https://id.twitch.tv/oauth2/token?client_id={clientId}&client_secret={clientSecret}&grant_type=client_credentials",
                                            null);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"Failed to obtain IGDB access token: {response.StatusCode}");

        var content = await response.Content.ReadAsStringAsync();
        var token = JsonSerializer.Deserialize<IgdbAccessToken>(content);

        if (token == null)
            throw new Exception("Failed to deserialize IGDB access token");

        token.ExpirationTime = DateTime.UtcNow.AddSeconds(token.ExpiresIn);
        _igdbAccessToken = token;

        return token.AccessToken;
    }

    private static async Task<T> MakeIgdbRequest<T>(string endpoint, string query, string clientId, string accessToken)
    {
        var http = new HttpClient();
        var request = new HttpRequestMessage(HttpMethod.Post, $"https://api.igdb.com/v4/{endpoint}");
        request.Headers.Add("Client-ID", clientId);
        request.Headers.Add("Authorization", $"Bearer {accessToken}");
        request.Content = new StringContent(query);

        var response = await http.SendAsync(request);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"IGDB API request failed: {response.StatusCode}");

        var content = await response.Content.ReadAsStringAsync();

        var jsonOptions = new JsonSerializerOptions {
            PropertyNameCaseInsensitive = true
        };

        var result = JsonSerializer.Deserialize<T>(content, jsonOptions);

        if (result == null)
            throw new Exception($"Failed to deserialize IGDB response for {endpoint}");

        return result;
    }

    public static async Task<List<Metadata>> IgdbSearchApi(string clientId, string clientSecret, string search)
    {
        var accessToken = await GetIgdbAccessToken(clientId, clientSecret);
        var query = $"fields id, url, storyline, summary, cover, first_release_date, name, game_localizations, rating, genres, themes; search \"{search}\"; limit 10;";

        var games = await MakeIgdbRequest<List<IgdbGame>>("games", query, clientId, accessToken);

        var metadatas = new List<Metadata>();

        foreach (var game in games)
        {
            var metadata = await CreateMetadataFromIgdbGame(game, clientId, accessToken);
            metadatas.Add(metadata);
        }

        return metadatas;
    }

    public static async Task<Metadata?> IgdbApi(string url, string clientId, string clientSecret)
    {
        var accessToken = await GetIgdbAccessToken(clientId, clientSecret);
        var query = $"fields id, url, summary, cover, first_release_date, name, game_localizations, rating, genres, themes; where url = \"{url}\"; limit 10;";

        var games = await MakeIgdbRequest<List<IgdbGame>>("games", query, clientId, accessToken);

        if (games.Count == 0)
            return null;

        return await CreateMetadataFromIgdbGame(games[0], clientId, accessToken);
    }

    private static async Task<Metadata> CreateMetadataFromIgdbGame(IgdbGame game, string clientId, string accessToken)
    {
        var genresAndThemes = game.Genres;
        genresAndThemes.AddRange(game.Themes);
        
        var metadata = new Metadata
                       {
                           EnglishTitle = game.Name, Description = game.Storyline ?? game.Summary ?? "",
                           ReleaseDate = DateTimeOffset.FromUnixTimeSeconds((long)game.FirstReleaseDate).DateTime,
                           Links = [new Link { LinkType = LinkType.Igdb, Url = game.Url }],
                           Rating = (int)Math.Round(game.Rating),
                           Genres = genresAndThemes.Select(g => g.ToString()).ToList(),
                           Tags = genresAndThemes.Select(g => (g.ToString(), 100)).ToList(),
                       };

        // Get Japanese title from game_localizations if available (region 3 is Japan)
        if (game.GameLocalizations?.Length > 0)
        {
            var localizationIds = string.Join(",", game.GameLocalizations);
            var localizationQuery = $"fields id, name, region; where id = ({localizationIds});";
            var localizations =
                await MakeIgdbRequest<List<IgdbGameLocalization>>("game_localizations", localizationQuery, clientId, accessToken);

            var japaneseLocalization = localizations.FirstOrDefault(l => l.Region == 3);
            metadata.OriginalTitle = japaneseLocalization != null ? japaneseLocalization.Name ?? "Unknown" : game.Name;
        }
        else
        {
            metadata.OriginalTitle = game.Name;
        }

        // Get cover image if available
        if (game.Cover <= 0) return metadata;
        var coverQuery = $"fields url; where id = {game.Cover};";
        var covers = await MakeIgdbRequest<List<IgdbCover>>("covers", coverQuery, clientId, accessToken);

        if (covers.Count <= 0) return metadata;
        var imageUrl = covers[0].Url;
        if (imageUrl.StartsWith("//"))
        {
            imageUrl = $"https:{imageUrl}";
        }

        // Get the bigger size one
        imageUrl = imageUrl.Replace("t_thumb", "t_cover_big");

        metadata.Image = imageUrl;

        return metadata;
    }
}