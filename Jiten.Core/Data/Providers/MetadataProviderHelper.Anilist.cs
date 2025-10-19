using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Jiten.Core.Data;
using Jiten.Core.Data.Providers;
using Jiten.Core.Data.Providers.Anilist;

namespace Jiten.Core;

public static partial class MetadataProviderHelper
{
    public static async Task<List<Metadata>> AnilistNovelSearchApi(string query)
    {
        return await AnilistSearchApi(query, "NOVEL");
    }

    public static async Task<List<Metadata>> AnilistMangaSearchApi(string query)
    {
        return await AnilistSearchApi(query, "MANGA");
    }

    public static async Task<List<Metadata>> AnilistSearchApi(string query, string format)
    {
        var requestBody = new
                          {
                              query = """

                                              query ($search: String, $type: MediaType, $format: MediaFormat) {
                                                Page {
                                                  media (search: $search, type: $type, format: $format) {
                                                    id
                                                    idMal
                                                    description
                                                    title {
                                                      romaji
                                                      english
                                                      native
                                                    }
                                                    startDate {
                                                      day
                                                      month
                                                      year
                                                    }
                                                    bannerImage
                                                    coverImage {
                                                      extraLarge
                                                    },
                                                    synonyms,
                                                    averageScore,
                                                    meanScore
                                                  }
                                                }
                                              }
                                      """,
                              variables = new { search = query, type = "MANGA", format = format }
                          };

        var httpClient = new HttpClient();
        var requestContent = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
        var response = await httpClient.PostAsync("https://graphql.anilist.co", requestContent);

        if (!response.IsSuccessStatusCode)
            return [];

        var contentStream = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<AnilistResult>(contentStream,
                                                               new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        return result?.Data?.Page?.Media.Select(media => new Metadata
                                                         {
                                                             OriginalTitle = media.Title.Native, RomajiTitle = media.Title.Romaji,
                                                             EnglishTitle = media.Title.English, ReleaseDate = media.ReleaseDate,
                                                             Description = Regex.Replace(media.Description ?? "", "<.*?>", "").Trim(),
                                                             Links =
                                                             [
                                                                 new Link
                                                                 {
                                                                     LinkType = LinkType.Anilist,
                                                                     Url = $"https://anilist.co/manga/{media.Id}"
                                                                 }
                                                             ],
                                                             Image = media.CoverImage.ExtraLarge, Aliases = media.Synonyms,
                                                             Rating = media.AverageScore ?? media.MeanScore ?? 0
                                                         }).ToList() ?? [];
    }

    public static async Task<Metadata?> AnilistApi(int id)
    {
        var requestBody = new
                          {
                              query = """
                                              query ($id: Int) {
                                                  Media (id: $id) {
                                                    id
                                                    idMal
                                                    description
                                                    title {
                                                      romaji
                                                      english
                                                      native
                                                    }
                                                    startDate {
                                                      day
                                                      month
                                                      year
                                                    }
                                                    bannerImage
                                                    coverImage {
                                                      extraLarge
                                                    },
                                                    synonyms,
                                                    averageScore,
                                                    meanScore
                                                  }
                                              }
                                      """,
                              variables = new { id = id }
                          };

        var httpClient = new HttpClient();
        var requestContent = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
        var response = await httpClient.PostAsync("https://graphql.anilist.co", requestContent);

        if (!response.IsSuccessStatusCode)
            return null;

        var contentStream = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<AnilistResult>(contentStream,
                                                               new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        var media = result?.Data?.Media;

        if (media == null)
            return null;

        return new Metadata
               {
                   OriginalTitle = media.Title.Native, RomajiTitle = media.Title.Romaji, EnglishTitle = media.Title.English,
                   ReleaseDate = media.ReleaseDate, Description = Regex.Replace(media.Description ?? "", "<.*?>", "").Trim(), Links =
                   [
                       new Link { LinkType = LinkType.Anilist, Url = $"https://anilist.co/manga/{media.Id}" }
                   ],
                   Image = media.CoverImage.ExtraLarge, Aliases = media.Synonyms, Rating = media.AverageScore ?? media.MeanScore ?? 0
               };
    }

    public static async Task<Metadata> AnilistAnimeApi(int anilistId)
    {
        var requestBody = new
                          {
                              query = """
                                              query ($id: Int) {
                                                  Media (id: $id) {
                                                    id
                                                    idMal
                                                    title {
                                                      romaji
                                                      english
                                                      native
                                                    }
                                                    startDate {
                                                      day
                                                      month
                                                      year
                                                    }
                                                    bannerImage
                                                    coverImage {
                                                      extraLarge
                                                    },
                                                    synonyms,
                                                    averageScore,
                                                    meanScore
                                                  }
                                              }
                                      """,
                              variables = new { id = anilistId }
                          };

        var httpClient = new HttpClient();
        var requestContent = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
        var response = await httpClient.PostAsync("https://graphql.anilist.co", requestContent);

        if (!response.IsSuccessStatusCode)
        {
            return new Metadata();
        }

        var contentStream = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<AnilistResult>(contentStream,
                                                               new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (result?.Data?.Media == null)
        {
            return new Metadata();
        }

        var media = result.Data.Media;
        return new Metadata
               {
                   OriginalTitle = media.Title.Native, RomajiTitle = media.Title.Romaji, EnglishTitle = media.Title.English,
                   ReleaseDate = media.ReleaseDate, Links =
                   [
                       new Link { LinkType = LinkType.Anilist, Url = $"https://anilist.co/anime/{media.Id}" },
                       new Link { LinkType = LinkType.Mal, Url = $"https://myanimelist.net/anime/{media.IdMal}" }
                   ],
                   Image = media.CoverImage.ExtraLarge, Aliases = media.Synonyms, Rating = media.AverageScore ?? media.MeanScore ?? 0
               };
    }
}