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
        return await AnilistSearchApi(query, ["NOVEL"]);
    }

    public static async Task<List<Metadata>> AnilistMangaSearchApi(string query)
    {
        return await AnilistSearchApi(query, ["MANGA", "ONE_SHOT"]);
    }

    public static async Task<List<Metadata>> AnilistSearchApi(string query, string[] format)
    {
        var requestBody = new
                          {
                              query = """

                                              query ($search: String, $type: MediaType, $format: [MediaFormat]) {
                                                Page {
                                                  media (search: $search, type: $type, format_in: $format) {
                                                    id
                                                    idMal
                                                    description
                                                    genres
                                                    isAdult
                                                    countryOfOrigin
                                                    tags {
                                                      name
                                                      rank
                                                      isMediaSpoiler
                                                    }
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
                                                    meanScore,
                                                    relations {
                                                      edges {
                                                        relationType(version: 2)
                                                        node {
                                                          id
                                                          type
                                                          format
                                                        }
                                                      }
                                                    }
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
                                                             Rating = media.AverageScore ?? media.MeanScore ?? 0,
                                                             Genres = media.Genres.Distinct().ToList(), Tags = media.Tags
                                                                 .Where(t => !t.IsMediaSpoiler).Distinct()
                                                                 .Select(tag => new MetadataTag
                                                                 {
                                                                     Name = tag.Name,
                                                                     Percentage = tag.Rank
                                                                 }).ToList(),
                                                             IsAdultOnly = media.IsAdult,
                                                             IsNotOriginallyJapanese = media.CountryOfOrigin != "JP",
                                                             Relations = MapAnilistRelations(media.Relations)
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
                                                    genres
                                                    isAdult
                                                    countryOfOrigin
                                                    tags {
                                                      name
                                                      rank
                                                      isMediaSpoiler
                                                    }
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
                                                    meanScore,
                                                    relations {
                                                      edges {
                                                        relationType(version: 2)
                                                        node {
                                                          id
                                                          type
                                                          format
                                                        }
                                                      }
                                                    }
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

        var genres = media.Genres.Distinct().ToList();
        var tags = media.Tags.Where(t => !t.IsMediaSpoiler).Distinct().ToList();

        return new Metadata
               {
                   OriginalTitle = media.Title.Native, RomajiTitle = media.Title.Romaji, EnglishTitle = media.Title.English,
                   ReleaseDate = media.ReleaseDate, Description = Regex.Replace(media.Description ?? "", "<.*?>", "").Trim(), Links =
                   [
                       new Link { LinkType = LinkType.Anilist, Url = $"https://anilist.co/manga/{media.Id}" }
                   ],
                   Image = media.CoverImage.ExtraLarge, Aliases = media.Synonyms, Rating = media.AverageScore ?? media.MeanScore ?? 0,
                   Genres = genres, Tags = tags.Select(tag => new MetadataTag
                   {
                       Name = tag.Name,
                       Percentage = tag.Rank
                   }).ToList(), IsAdultOnly = media.IsAdult,
                   IsNotOriginallyJapanese = media.CountryOfOrigin != "JP",
                   Relations = MapAnilistRelations(media.Relations)
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
                                                    description
                                                    genres
                                                    isAdult
                                                    countryOfOrigin
                                                    tags {
                                                      name
                                                      rank
                                                      isMediaSpoiler
                                                    }
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
                                                    meanScore,
                                                    relations {
                                                      edges {
                                                        relationType(version: 2)
                                                        node {
                                                          id
                                                          type
                                                          format
                                                        }
                                                      }
                                                    }
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

        var genres = media.Genres.Distinct().ToList();
        var tags = media.Tags.Where(t => !t.IsMediaSpoiler).Distinct().ToList();

        return new Metadata
               {
                   OriginalTitle = media.Title.Native, RomajiTitle = media.Title.Romaji, EnglishTitle = media.Title.English,
                   ReleaseDate = media.ReleaseDate, Links =
                   [
                       new Link { LinkType = LinkType.Anilist, Url = $"https://anilist.co/anime/{media.Id}" },
                       new Link { LinkType = LinkType.Mal, Url = $"https://myanimelist.net/anime/{media.IdMal}" }
                   ],
                   Image = media.CoverImage.ExtraLarge, Aliases = media.Synonyms, Rating = media.AverageScore ?? media.MeanScore ?? 0,
                   Genres = genres, Tags = tags.Select(tag => new MetadataTag
                   {
                       Name = tag.Name,
                       Percentage = tag.Rank
                   }).ToList(), IsAdultOnly = media.IsAdult,
                   IsNotOriginallyJapanese = media.CountryOfOrigin != "JP",
                   Relations = MapAnilistRelations(media.Relations)
               };
    }

    private static List<MetadataRelation> MapAnilistRelations(AnilistRelations? relations)
    {
        if (relations?.Edges == null)
            return [];

        var result = new List<MetadataRelation>();

        foreach (var edge in relations.Edges)
        {
            var mapping = MapAnilistRelationType(edge.RelationType);
            if (mapping == null)
                continue;

            var targetMediaType = MapAnilistTypeToMediaType(edge.Node.Type, edge.Node.Format);

            result.Add(new MetadataRelation
            {
                ExternalId = edge.Node.Id.ToString(),
                LinkType = LinkType.Anilist,
                RelationshipType = mapping.Value.Type,
                TargetMediaType = targetMediaType,
                SwapDirection = mapping.Value.SwapDirection
            });
        }

        return result;
    }

    private static (DeckRelationshipType Type, bool SwapDirection)? MapAnilistRelationType(string relationType)
    {
        return relationType switch
        {
            "SEQUEL" => (DeckRelationshipType.Sequel, true),
            "PREQUEL" => (DeckRelationshipType.Sequel, false),
            "SIDE_STORY" => (DeckRelationshipType.SideStory, true),
            // "PARENT" => (DeckRelationshipType.SideStory, false),
            "SPIN_OFF" => (DeckRelationshipType.Spinoff, true),
            "ALTERNATIVE" => (DeckRelationshipType.Alternative, false),
            "ADAPTATION" => (DeckRelationshipType.Adaptation, false),
            "SOURCE" => (DeckRelationshipType.Adaptation, true),
            _ => null
        };
    }

    private static MediaType? MapAnilistTypeToMediaType(string type, string? format)
    {
        return (type, format) switch
        {
            ("ANIME", _) => MediaType.Anime,
            ("MANGA", "NOVEL") => MediaType.Novel,
            ("MANGA", "ONE_SHOT") => MediaType.Manga,
            ("MANGA", "MANGA") => MediaType.Manga,
            ("MANGA", _) => MediaType.Manga,
            _ => null
        };
    }
}