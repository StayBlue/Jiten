using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using AnkiNet;
using Jiten.Api.Dtos;
using Jiten.Api.Dtos.Requests;
using Jiten.Api.Enums;
using Jiten.Api.Helpers;
using Jiten.Api.Services;
using Jiten.Core;
using Jiten.Core.Data;
using Jiten.Core.Data.FSRS;
using Jiten.Core.Data.JMDict;
using Jiten.Core.Data.User;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using WanaKanaShaapu;
using Swashbuckle.AspNetCore.Annotations;

namespace Jiten.Api.Controllers;

/// <summary>
/// Endpoints for browsing media decks, vocabulary, downloads and related statistics.
/// </summary>
[ApiController]
[Route("api/media-deck")]
[EnableRateLimiting("fixed")]
[Produces("application/json")]
[SwaggerTag("Media decks and vocabulary")]
public class MediaDeckController(
    JitenDbContext context,
    IDbContextFactory<JitenDbContext> contextFactory,
    UserDbContext userContext,
    ICurrentUserService currentUserService,
    IConfiguration configuration,
    ILogger<MediaDeckController> logger) : ControllerBase
{
    private class DeckWithOccurrences
    {
        public Deck Deck { get; set; } = null!;
        public int Occurrences { get; set; }
    }

    /// <summary>
    /// Returns the IDs of all parent media decks.
    /// </summary>
    /// <returns>List of deck IDs.</returns>
    [HttpGet("get-media-decks-id")]
    [ResponseCache(Duration = 60 * 60)]
    [SwaggerOperation(Summary = "Get IDs of top-level media decks")]
    [ProducesResponseType(typeof(List<int>), StatusCodes.Status200OK)]
    public async Task<List<int>> GetMediaDecksId()
    {
        return await context.Decks.AsNoTracking().Where(d => d.ParentDeckId == null).Select(d => d.DeckId).ToListAsync();
    }

    /// <summary>
    /// Returns the deck dto of all parent media decks.
    /// </summary>
    /// <returns>List of decks with titles and ids.</returns>
    [HttpGet("get-media-decks-by-type/{mediaType}")]
    [ResponseCache(Duration = 60 * 60)]
    [SwaggerOperation(Summary = "Get list of top-level media decks by type")]
    [ProducesResponseType(typeof(List<DeckDto>), StatusCodes.Status200OK)]
    public async Task<List<DeckDto>> GetMediaDecksByType(MediaType mediaType)
    {
        var decks = await context.Decks.AsNoTracking().Where(d => d.ParentDeckId == null && d.MediaType == mediaType)
                                 .OrderBy(d => d.RomajiTitle).Include(d => d.Links).Include(d => d.Titles).ToListAsync();
        var dtos = new List<DeckDto>();
        foreach (var deck in decks)
        {
            dtos.Add(new DeckDto(deck));
        }

        return dtos;
    }

    /// <summary>
    /// Returns lightweight media deck suggestions for autocomplete search.
    /// </summary>
    /// <param name="query">Search query (minimum 2 characters).</param>
    /// <param name="limit">Maximum number of results (default 5, max 10).</param>
    /// <returns>Media suggestions with total count.</returns>
    [HttpGet("search-suggestions")]
    [ResponseCache(Duration = 60, VaryByQueryKeys = ["query", "limit"])]
    [SwaggerOperation(Summary = "Get media suggestions for autocomplete")]
    [ProducesResponseType(typeof(MediaSuggestionsResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<MediaSuggestionsResponse>> GetSearchSuggestions(
        [FromQuery] string? query,
        [FromQuery] int limit = 5)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
            return Ok(new MediaSuggestionsResponse());

        limit = Math.Clamp(limit, 1, 10);

        var normalizedFilter = query.Trim();
        var filterNoSpaces = normalizedFilter.Replace(" ", "");
        var queryLength = normalizedFilter.Length;

        FormattableString sql = $$"""
                                  WITH exact_matches AS (
                                      SELECT DISTINCT dt."DeckId",
                                             0 AS match_priority,
                                             100.0 AS score,
                                             dt."TitleType",
                                             LENGTH(dt."Title") AS title_length
                                      FROM jiten."DeckTitles" dt
                                      WHERE LOWER(dt."Title") = LOWER({{normalizedFilter}})
                                         OR LOWER(dt."TitleNoSpaces") = LOWER({{filterNoSpaces}})
                                  ),
                                  fuzzy_title_matches AS (
                                      SELECT dt."DeckId",
                                             1 AS match_priority,
                                             pgroonga_score(dt.tableoid, dt.ctid) AS score,
                                             dt."TitleType",
                                             LENGTH(dt."Title") AS title_length
                                      FROM jiten."DeckTitles" dt
                                      WHERE dt."Title" &@~ {{normalizedFilter}}
                                        AND dt."DeckId" NOT IN (SELECT "DeckId" FROM exact_matches)
                                  ),
                                  fuzzy_nospace_matches AS (
                                      SELECT dt."DeckId",
                                             2 AS match_priority,
                                             pgroonga_score(dt.tableoid, dt.ctid) AS score,
                                             dt."TitleType",
                                             LENGTH(dt."TitleNoSpaces") AS title_length
                                      FROM jiten."DeckTitles" dt
                                      WHERE dt."TitleType" IN (1, 3)
                                        AND dt."TitleNoSpaces" &@~ {{filterNoSpaces}}
                                        AND dt."DeckId" NOT IN (SELECT "DeckId" FROM exact_matches)
                                        AND dt."DeckId" NOT IN (SELECT "DeckId" FROM fuzzy_title_matches)
                                  ),
                                  all_matches AS (
                                      SELECT * FROM exact_matches
                                      UNION ALL
                                      SELECT * FROM fuzzy_title_matches
                                      UNION ALL
                                      SELECT * FROM fuzzy_nospace_matches
                                  ),
                                  ranked AS (
                                      SELECT "DeckId",
                                             MIN(match_priority) AS best_match,
                                             MIN(CASE "TitleType"
                                                 WHEN 0 THEN 1
                                                 WHEN 1 THEN 2
                                                 WHEN 2 THEN 3
                                                 ELSE 4
                                             END) AS best_type,
                                             MAX(score) AS best_score,
                                             {{queryLength}}::float / NULLIF(MIN(title_length), 0)::float AS length_ratio
                                      FROM all_matches
                                      GROUP BY "DeckId"
                                  )
                                  SELECT r."DeckId"
                                  FROM ranked r
                                  JOIN jiten."Decks" d ON r."DeckId" = d."DeckId"
                                  WHERE d."ParentDeckId" IS NULL
                                  ORDER BY r.best_match ASC, r.length_ratio DESC, r.best_type ASC, r.best_score DESC
                                  """;

        var allMatchingDeckIds = await context.Database.SqlQuery<int>(sql).ToListAsync();
        var totalCount = allMatchingDeckIds.Count;

        if (totalCount == 0)
            return Ok(new MediaSuggestionsResponse());

        var orderedDeckIds = allMatchingDeckIds.Take(limit).ToList();

        var decks = await context.Decks
            .AsNoTracking()
            .Where(d => orderedDeckIds.Contains(d.DeckId))
            .Select(d => new MediaSuggestionDto
            {
                DeckId = d.DeckId,
                OriginalTitle = d.OriginalTitle,
                RomajiTitle = d.RomajiTitle,
                EnglishTitle = d.EnglishTitle,
                MediaType = d.MediaType,
                CoverName = d.CoverName
            })
            .ToListAsync();

        // Preserve PGroonga ordering
        var deckMap = decks.ToDictionary(d => d.DeckId);
        var suggestions = orderedDeckIds
            .Where(id => deckMap.ContainsKey(id))
            .Select(id => deckMap[id])
            .ToList();

        return Ok(new MediaSuggestionsResponse
        {
            Suggestions = suggestions,
            TotalCount = totalCount
        });
    }

    /// <summary>
    /// Returns media decks with optional filtering, sorting and pagination.
    /// </summary>
    /// <param name="offset">Page offset (multiple of 50).</param>
    /// <param name="mediaType">Restrict to a specific media type.</param>
    /// <param name="wordId">If set, only decks containing this word are returned.</param>
    /// <param name="readingIndex">Reading index associated with wordId.</param>
    /// <param name="titleFilter">Full‑text filter on title (supports romaji/english/japanese).</param>
    /// <param name="sortBy">Sort field (title, difficulty, charCount, wordCount, sentenceLength, dialoguePercentage, uKanji, uWordCount, uKanjiOnce, filter, releaseDate, coverage, uCoverage, etc.).</param>
    /// <param name="sortOrder">Ascending or Descending.</param>
    /// <param name="status">Status (none, fav, ignore, planning, ongoing, completed, dropped)</param>
    /// <param name="charCountMin"></param>
    /// <param name="charCountMax"></param>
    /// <param name="difficultyMin"></param>
    /// <param name="difficultyMax"></param>
    /// <param name="releaseYearMin"></param>
    /// <param name="releaseYearMax"></param>
    /// <param name="uniqueKanjiMin"></param>
    /// <param name="uniqueKanjiMax"></param>
    /// <param name="subdeckCountMin"></param>
    /// <param name="subdeckCountMax"></param>
    /// <param name="extRatingMin"></param>
    /// <param name="extRatingMax"></param>
    /// <returns>Paginated list of decks.</returns>
    [HttpGet("get-media-decks")]
    [ResponseCache(Duration = 300, VaryByHeader = "Authorization",
                   VaryByQueryKeys =
                   [
                       "offset", "mediaType", "wordId", "readingIndex", "titleFilter", "sortBy", "sortOrder", "status",
                       "charCountMin", "charCountMax", "difficultyMin", "difficultyMax", "releaseYearMin", "releaseYearMax", "uniqueKanjiMin",
                       "uniqueKanjiMax", "subdeckCountMin", "subdeckCountMax", "extRatingMin", "extRatingMax", "genres",
                       "excludeGenres", "tags", "excludeTags", "coverageMin", "coverageMax", "uniqueCoverageMin",
                       "uniqueCoverageMax"
                   ])]
    [SwaggerOperation(Summary = "List media decks",
                      Description =
                          "Returns a paginated list of decks with optional filters, sorting and user coverage when authenticated.")]
    [ProducesResponseType(typeof(PaginatedResponse<List<DeckDto>>), StatusCodes.Status200OK)]
    public async Task<PaginatedResponse<List<DeckDto>>> GetMediaDecks(int? offset = 0, MediaType? mediaType = null,
                                                                      int wordId = 0, int readingIndex = 0, string? titleFilter = "",
                                                                      string? sortBy = "",
                                                                      SortOrder sortOrder = SortOrder.Ascending,
                                                                      string status = "",
                                                                      int? charCountMin = null, int? charCountMax = null,
                                                                      float? difficultyMin = null, float? difficultyMax = null,
                                                                      int? releaseYearMin = null, int? releaseYearMax = null,
                                                                      int? uniqueKanjiMin = null, int? uniqueKanjiMax = null,
                                                                      int? subdeckCountMin = null, int? subdeckCountMax = null,
                                                                      int? extRatingMin = null, int? extRatingMax = null,
                                                                      string? genres = null, string? excludeGenres = null,
                                                                      string? tags = null, string? excludeTags = null,
                                                                      float? coverageMin = null, float? coverageMax = null,
                                                                      float? uniqueCoverageMin = null, float? uniqueCoverageMax = null,
                                                                      bool? excludeSequels = null)
    {
        // Disable response caching for authenticated users
        if (currentUserService.IsAuthenticated)
        {
            Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
        }

        int pageSize = 50;
        var query = context.Decks.AsNoTracking();

        // Use "Search then Load" pattern to preserve PGroonga ordering
        List<int>? orderedDeckIds = null;

        if (!string.IsNullOrEmpty(titleFilter))
        {
            var normalizedFilter = titleFilter.Trim();
            var filterNoSpaces = normalizedFilter.Replace(" ", "");
            var queryLength = normalizedFilter.Length;

            // CTE-based query that prioritises exact matches, then uses pgroonga for fuzzy
            // Each CTE uses a single index for efficient pgroonga_score calculation
            // Length ratio ensures shorter titles matching the query rank higher
            FormattableString sql = $$"""
                                      WITH exact_matches AS (
                                          SELECT DISTINCT dt."DeckId",
                                                 0 AS match_priority,
                                                 100.0 AS score,
                                                 dt."TitleType",
                                                 LENGTH(dt."Title") AS title_length
                                          FROM jiten."DeckTitles" dt
                                          WHERE LOWER(dt."Title") = LOWER({{normalizedFilter}})
                                             OR LOWER(dt."TitleNoSpaces") = LOWER({{filterNoSpaces}})
                                      ),
                                      fuzzy_title_matches AS (
                                          SELECT dt."DeckId",
                                                 1 AS match_priority,
                                                 pgroonga_score(dt.tableoid, dt.ctid) AS score,
                                                 dt."TitleType",
                                                 LENGTH(dt."Title") AS title_length
                                          FROM jiten."DeckTitles" dt
                                          WHERE dt."Title" &@~ {{normalizedFilter}}
                                            AND dt."DeckId" NOT IN (SELECT "DeckId" FROM exact_matches)
                                      ),
                                      fuzzy_nospace_matches AS (
                                          SELECT dt."DeckId",
                                                 2 AS match_priority,
                                                 pgroonga_score(dt.tableoid, dt.ctid) AS score,
                                                 dt."TitleType",
                                                 LENGTH(dt."TitleNoSpaces") AS title_length
                                          FROM jiten."DeckTitles" dt
                                          WHERE dt."TitleType" IN (1, 3)
                                            AND dt."TitleNoSpaces" &@~ {{filterNoSpaces}}
                                            AND dt."DeckId" NOT IN (SELECT "DeckId" FROM exact_matches)
                                            AND dt."DeckId" NOT IN (SELECT "DeckId" FROM fuzzy_title_matches)
                                      ),
                                      all_matches AS (
                                          SELECT * FROM exact_matches
                                          UNION ALL
                                          SELECT * FROM fuzzy_title_matches
                                          UNION ALL
                                          SELECT * FROM fuzzy_nospace_matches
                                      ),
                                      ranked AS (
                                          SELECT "DeckId",
                                                 MIN(match_priority) AS best_match,
                                                 MIN(CASE "TitleType"
                                                     WHEN 0 THEN 1
                                                     WHEN 1 THEN 2
                                                     WHEN 2 THEN 3
                                                     ELSE 4
                                                 END) AS best_type,
                                                 MAX(score) AS best_score,
                                                 {{queryLength}}::float / NULLIF(MIN(title_length), 0)::float AS length_ratio
                                          FROM all_matches
                                          GROUP BY "DeckId"
                                      )
                                      SELECT r."DeckId"
                                      FROM ranked r
                                      JOIN jiten."Decks" d ON r."DeckId" = d."DeckId"
                                      WHERE d."ParentDeckId" IS NULL
                                      ORDER BY r.best_match ASC, r.length_ratio DESC, r.best_type ASC, r.best_score DESC
                                      """;

            orderedDeckIds = await context.Database.SqlQuery<int>(sql).ToListAsync();
            query = query.Where(d => orderedDeckIds.Contains(d.DeckId));
        }
        else
        {
            query = query.Where(d => d.ParentDeckId == null);
        }

        if (mediaType != null)
            query = query.Where(d => d.MediaType == mediaType);

        // Advanced filters
        if (charCountMin != null)
            query = query.Where(d => d.CharacterCount >= charCountMin);

        if (charCountMax != null)
            query = query.Where(d => d.CharacterCount <= charCountMax);

        if (difficultyMin != null)
            query = query.Where(d => (d.DifficultyOverride > -1 ? d.DifficultyOverride : d.Difficulty) >= difficultyMin);

        if (difficultyMax != null)
            query = query.Where(d => (d.DifficultyOverride > -1 ? d.DifficultyOverride : d.Difficulty) <= difficultyMax);

        if (releaseYearMin != null)
            query = query.Where(d => d.ReleaseDate.Year >= releaseYearMin);

        if (releaseYearMax != null)
            query = query.Where(d => d.ReleaseDate.Year <= releaseYearMax);

        if (uniqueKanjiMin != null)
            query = query.Where(d => d.UniqueKanjiCount >= uniqueKanjiMin);

        if (uniqueKanjiMax != null)
            query = query.Where(d => d.UniqueKanjiCount <= uniqueKanjiMax);

        if (subdeckCountMin != null)
            query = query.Where(d => d.Children.Count >= subdeckCountMin);

        if (subdeckCountMax != null)
            query = query.Where(d => d.Children.Count <= subdeckCountMax);

        if (extRatingMin != null)
            query = query.Where(d => d.ExternalRating >= extRatingMin);

        if (extRatingMax != null)
            query = query.Where(d => d.ExternalRating <= extRatingMax);

        // Genre filters
        if (!string.IsNullOrEmpty(genres))
        {
            var genreIds = genres.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                 .Select(g => int.TryParse(g, out var genreId) ? (Genre?)genreId : null)
                                 .Where(g => g.HasValue)
                                 .Select(g => g!.Value)
                                 .ToList();

            if (genreIds.Any())
            {
                foreach (var genreId in genreIds)
                {
                    query = query.Where(d => d.DeckGenres.Any(dg => dg.Genre == genreId));
                }
            }
        }

        if (!string.IsNullOrEmpty(excludeGenres))
        {
            var excludeGenreIds = excludeGenres.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                               .Select(g => int.TryParse(g, out var genreId) ? (Genre?)genreId : null)
                                               .Where(g => g.HasValue)
                                               .Select(g => g!.Value)
                                               .ToList();

            if (excludeGenreIds.Any())
            {
                query = query.Where(d => !d.DeckGenres.Any(dg => excludeGenreIds.Contains(dg.Genre)));
            }
        }

        // Tag filters
        if (!string.IsNullOrEmpty(tags))
        {
            var tagIds = tags.Split(',', StringSplitOptions.RemoveEmptyEntries)
                             .Select(t => int.TryParse(t, out var tagId) ? (int?)tagId : null)
                             .Where(t => t.HasValue)
                             .Select(t => t!.Value)
                             .ToList();

            if (tagIds.Any())
            {
                foreach (var tagId in tagIds)
                {
                    query = query.Where(d => d.DeckTags.Any(dt => dt.TagId == tagId));
                }
            }
        }

        if (!string.IsNullOrEmpty(excludeTags))
        {
            var excludeTagIds = excludeTags.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                           .Select(t => int.TryParse(t, out var tagId) ? (int?)tagId : null)
                                           .Where(t => t.HasValue)
                                           .Select(t => t!.Value)
                                           .ToList();

            if (excludeTagIds.Any())
            {
                query = query.Where(d => !d.DeckTags.Any(dt => excludeTagIds.Contains(dt.TagId)));
            }
        }

        // Exclude sequels and fandiscs
        if (excludeSequels == true)
        {
            query = query.Where(d =>
                !d.RelationshipsAsSource.Any(r =>
                    r.RelationshipType == DeckRelationshipType.Sequel ||
                    r.RelationshipType == DeckRelationshipType.Fandisc));
        }

        // Word filter
        if (wordId != 0)
        {
            query = query.Where(d => context.DeckWords
                                            .Any(dw => dw.DeckId == d.DeckId && dw.WordId == wordId && dw.ReadingIndex == readingIndex));
        }

        // User preferences
        Dictionary<int, UserDeckPreference> allUserPrefs = new();
        HashSet<int> favDeckIds = new();
        HashSet<int> ignoredDeckIds = new();

        if (currentUserService.IsAuthenticated)
        {
            var userId = currentUserService.UserId!;
            var prefsList = await userContext.UserDeckPreferences
                                             .AsNoTracking()
                                             .Where(p => p.UserId == userId)
                                             .ToListAsync();

            allUserPrefs = prefsList.ToDictionary(p => p.DeckId);
            favDeckIds = prefsList.Where(p => p.IsFavourite).Select(p => p.DeckId).ToHashSet();
            ignoredDeckIds = prefsList.Where(p => p.IsIgnored).Select(p => p.DeckId).ToHashSet();
        }

        if (currentUserService.IsAuthenticated && !string.IsNullOrEmpty(status))
        {
            var normalizedStatus = status.ToLowerInvariant();

            if (normalizedStatus == "fav")
            {
                query = query.Where(d => favDeckIds.Contains(d.DeckId));
            }
            else if (normalizedStatus == "ignore")
            {
                query = query.Where(d => ignoredDeckIds.Contains(d.DeckId));
            }
            else if (normalizedStatus != "none")
            {
                DeckStatus? deckStatus = normalizedStatus switch
                {
                    "planning" => DeckStatus.Planning,
                    "ongoing" => DeckStatus.Ongoing,
                    "completed" => DeckStatus.Completed,
                    "dropped" => DeckStatus.Dropped,
                    _ => null
                };

                if (deckStatus.HasValue)
                {
                    var statusDeckIds = allUserPrefs
                                        .Where(p => p.Value.Status == deckStatus.Value)
                                        .Select(p => p.Key)
                                        .ToHashSet();
                    query = query.Where(d => statusDeckIds.Contains(d.DeckId));
                }
            }
        }

        // Exclude ignored decks
        if (currentUserService.IsAuthenticated && status?.ToLowerInvariant() != "ignore")
        {
            query = query.Where(d => !ignoredDeckIds.Contains(d.DeckId));
        }

        query = query.Include(d => d.Children)
                     .Include(d => d.Links)
                     .Include(d => d.Titles)
                     .Include(d => d.DeckGenres)
                     .Include(d => d.DeckTags)
                     .ThenInclude(dt => dt.Tag)
                     .Include(d => d.RelationshipsAsSource)
                     .ThenInclude(r => r.TargetDeck)
                     .Include(d => d.RelationshipsAsTarget)
                     .ThenInclude(r => r.SourceDeck);


        // Create projected query for word-based searches
        IQueryable<DeckWithOccurrences>? projectedQuery = null;
        if (wordId != 0)
        {
            projectedQuery = query.Select(d => new DeckWithOccurrences
                                               {
                                                   Deck = d, Occurrences = d.DeckWords
                                                                            .Where(dw => dw.WordId == wordId &&
                                                                                         dw.ReadingIndex == readingIndex)
                                                                            .Select(dw => (int?)dw.Occurrences)
                                                                            .FirstOrDefault() ?? 0
                                               });
        }

        if (string.IsNullOrEmpty(sortBy))
            sortBy = string.IsNullOrEmpty(titleFilter) ? "title" : "filter";

        Dictionary<int, float> coverageDict = new();
        Dictionary<int, float> uniqueCoverageDict = new();

        if (currentUserService.IsAuthenticated)
        {
            var allDeckIds = await query.Select(d => d.DeckId).ToListAsync();
            var userId = currentUserService.UserId!;
            var coverageList = await userContext.UserCoverages
                                                .AsNoTracking()
                                                .Where(uc => uc.UserId == userId && allDeckIds.Contains(uc.DeckId))
                                                .Select(uc => new { uc.DeckId, uc.Coverage, uc.UniqueCoverage })
                                                .ToListAsync();
            coverageDict = coverageList.ToDictionary(x => x.DeckId, x => (float)x.Coverage);
            uniqueCoverageDict = coverageList.ToDictionary(x => x.DeckId, x => (float)x.UniqueCoverage);

            // Apply coverage filters
            if (coverageMin != null || coverageMax != null)
            {
                var matchingIds = coverageDict
                                  .Where(kvp => (coverageMin == null || kvp.Value >= coverageMin) &&
                                                (coverageMax == null || kvp.Value <= coverageMax))
                                  .Select(kvp => kvp.Key)
                                  .ToHashSet();
                query = query.Where(d => matchingIds.Contains(d.DeckId));
            }

            if (uniqueCoverageMin != null || uniqueCoverageMax != null)
            {
                var matchingIds = uniqueCoverageDict
                                  .Where(kvp => (uniqueCoverageMin == null || kvp.Value >= uniqueCoverageMin) &&
                                                (uniqueCoverageMax == null || kvp.Value <= uniqueCoverageMax))
                                  .Select(kvp => kvp.Key)
                                  .ToHashSet();
                query = query.Where(d => matchingIds.Contains(d.DeckId));
            }

            // Reuse allUserPrefs from earlier instead of querying again

            if ((sortBy is "coverage" or "uCoverage"))
            {
                bool sortByUnique = sortBy == "uCoverage";
                return await HandleCoverageSorting(query, projectedQuery, sortOrder, offset ?? 0, pageSize, coverageDict,
                                                   uniqueCoverageDict, sortByUnique, allUserPrefs);
            }
        }

        if (wordId != 0)
        {
            return await HandleWordBasedQuery(projectedQuery!, wordId, readingIndex, sortBy, sortOrder, offset ?? 0, pageSize, coverageDict,
                                              uniqueCoverageDict, allUserPrefs, orderedDeckIds);
        }

        // Handle regular queries
        query = ApplySorting(query, sortBy, sortOrder);
        var totalCount = await query.CountAsync();

        // Fetch unordered decks
        var unorderedDecks = await query
                                   .Skip(offset ?? 0)
                                   .Take(pageSize)
                                   .AsSplitQuery()
                                   .ToListAsync();

        // If we have a title filter, re-sort in memory to match the PGroonga ordering
        List<Deck> paginatedDecks;
        if (orderedDeckIds is { Count: > 0 } && sortBy == "filter")
        {
            // Create lookup dictionary for O(1) access
            var deckLookup = unorderedDecks.ToDictionary(d => d.DeckId);

            // Re-apply the order from PGroonga, but only for the paginated subset
            var paginatedIds = orderedDeckIds.Skip(offset ?? 0).Take(pageSize).ToList();
            paginatedDecks = paginatedIds
                             .Where(id => deckLookup.ContainsKey(id))
                             .Select(id => deckLookup[id])
                             .ToList();
        }
        else
        {
            // No title filter, order is already correct from ApplySorting
            paginatedDecks = unorderedDecks;
        }

        var dtos = paginatedDecks.Select(deck => new DeckDto(deck)).ToList();

        foreach (var (dto, deck) in dtos.Zip(paginatedDecks))
            dto.Relationships = DeckRelationshipDto.FromDeck(deck.RelationshipsAsSource, deck.RelationshipsAsTarget);

        if (currentUserService.IsAuthenticated)
        {
            foreach (var dto in dtos)
            {
                if (coverageDict.TryGetValue(dto.DeckId, out var c)) dto.Coverage = c;
                if (uniqueCoverageDict.TryGetValue(dto.DeckId, out var uc)) dto.UniqueCoverage = uc;
                if (allUserPrefs.TryGetValue(dto.DeckId, out var pref))
                {
                    dto.Status = pref.Status;
                    dto.IsFavourite = pref.IsFavourite;
                    dto.IsIgnored = pref.IsIgnored;
                }
            }
        }

        return new PaginatedResponse<List<DeckDto>>(dtos, totalCount, pageSize, offset ?? 0);
    }

    private async Task<PaginatedResponse<List<DeckDto>>> HandleCoverageSorting(
        IQueryable<Deck> query,
        IQueryable<DeckWithOccurrences>? projectedQuery,
        SortOrder sortOrder,
        int offset,
        int pageSize,
        Dictionary<int, float> coverageDict,
        Dictionary<int, float> uniqueCoverageDict,
        bool sortByUnique,
        Dictionary<int, UserDeckPreference> preferencesDict)
    {
        var totalCount = await query.CountAsync();
        var allDeckIds = await query.Select(d => d.DeckId).ToListAsync();

        var selectedDict = sortByUnique ? uniqueCoverageDict : coverageDict;
        var idsWithCoverage = allDeckIds.Where(id => selectedDict.ContainsKey(id)).ToList();
        var idsWithoutCoverage = allDeckIds.Where(id => !selectedDict.ContainsKey(id)).ToList();

        IEnumerable<int> orderedWithCoverage = sortOrder == SortOrder.Ascending
            ? idsWithCoverage.OrderBy(id => selectedDict[id])
            : idsWithCoverage.OrderByDescending(id => selectedDict[id]);

        var orderedIds = orderedWithCoverage.Concat(idsWithoutCoverage).ToList();
        var pagedIds = orderedIds.Skip(offset).Take(pageSize).ToList();

        // Use projectedQuery if it's word based
        if (projectedQuery != null)
        {
            var paginatedProjections = await projectedQuery
                                         .Where(p => pagedIds.Contains(p.Deck.DeckId))
                                         .ToListAsync();

            var deckIdsToHydrate = paginatedProjections.Select(p => p.Deck.DeckId).ToList();

            var fullDecks = await context.Decks.AsNoTracking()
                                         .Where(d => deckIdsToHydrate.Contains(d.DeckId))
                                         .Include(d => d.Children)
                                         .Include(d => d.Links)
                                         .Include(d => d.Titles)
                                         .Include(d => d.DeckGenres)
                                         .Include(d => d.DeckTags)
                                         .ThenInclude(dt => dt.Tag)
                                         .Include(d => d.RelationshipsAsSource)
                                         .ThenInclude(r => r.TargetDeck)
                                         .Include(d => d.RelationshipsAsTarget)
                                         .ThenInclude(r => r.SourceDeck)
                                         .AsSplitQuery()
                                         .ToListAsync();
            
            var fullDeckMap = fullDecks.ToDictionary(d => d.DeckId);

            var orderIndex = pagedIds.Select((id, idx) => new { id, idx }).ToDictionary(k => k.id, v => v.idx);
            var paginatedResults = paginatedProjections
                               .Where(p => fullDeckMap.ContainsKey(p.Deck.DeckId))
                               .Select(p => new DeckWithOccurrences 
                                            { 
                                                Deck = fullDeckMap[p.Deck.DeckId], 
                                                Occurrences = p.Occurrences 
                                            })
                               .OrderBy(r => orderIndex[r.Deck.DeckId])
                               .ToList();

            var dtos = paginatedResults.Select(r => new DeckDto(r.Deck, r.Occurrences)).ToList();

            foreach (var (dto, result) in dtos.Zip(paginatedResults))
                dto.Relationships = DeckRelationshipDto.FromDeck(result.Deck.RelationshipsAsSource, result.Deck.RelationshipsAsTarget);

            foreach (var dto in dtos)
            {
                if (currentUserService.IsAuthenticated)
                {
                    if (coverageDict.TryGetValue(dto.DeckId, out var c)) dto.Coverage = c;
                    if (uniqueCoverageDict.TryGetValue(dto.DeckId, out var uc)) dto.UniqueCoverage = uc;
                    if (preferencesDict.TryGetValue(dto.DeckId, out var pref))
                    {
                        dto.Status = pref.Status;
                        dto.IsFavourite = pref.IsFavourite;
                        dto.IsIgnored = pref.IsIgnored;
                    }
                }

                if (coverageDict.TryGetValue(dto.DeckId, out var cov)) dto.Coverage = cov;
                if (uniqueCoverageDict.TryGetValue(dto.DeckId, out var uCov)) dto.UniqueCoverage = uCov;
            }


            return new PaginatedResponse<List<DeckDto>>(dtos, totalCount, pageSize, offset);
        }
        else
        {
            var pagedDecks = await query
                                   .Where(d => pagedIds.Contains(d.DeckId))
                                   .ToListAsync();

            var orderIndex = pagedIds.Select((id, idx) => new { id, idx }).ToDictionary(k => k.id, v => v.idx);
            pagedDecks = pagedDecks.OrderBy(d => orderIndex[d.DeckId]).ToList();

            var dtos = pagedDecks.Select(deck => new DeckDto(deck)).ToList();

            foreach (var (dto, deck) in dtos.Zip(pagedDecks))
                dto.Relationships = DeckRelationshipDto.FromDeck(deck.RelationshipsAsSource, deck.RelationshipsAsTarget);

            foreach (var dto in dtos)
            {
                if (currentUserService.IsAuthenticated)
                {
                    if (coverageDict.TryGetValue(dto.DeckId, out var c)) dto.Coverage = c;
                    if (uniqueCoverageDict.TryGetValue(dto.DeckId, out var uc)) dto.UniqueCoverage = uc;
                    if (preferencesDict.TryGetValue(dto.DeckId, out var pref))
                    {
                        dto.Status = pref.Status;
                        dto.IsFavourite = pref.IsFavourite;
                        dto.IsIgnored = pref.IsIgnored;
                    }
                }

                if (coverageDict.TryGetValue(dto.DeckId, out var cov)) dto.Coverage = cov;
                if (uniqueCoverageDict.TryGetValue(dto.DeckId, out var uCov)) dto.UniqueCoverage = uCov;
            }

            return new PaginatedResponse<List<DeckDto>>(dtos, totalCount, pageSize, offset);
        }
    }

    private IQueryable<Deck> ApplySorting(IQueryable<Deck> query, string sortBy, SortOrder sortOrder)
    {
        return sortBy switch
        {
            "difficulty" => sortOrder == SortOrder.Ascending
                ? query.Where(d => d.Difficulty > -1)
                       .OrderBy(d => d.DifficultyOverride > -1 ? d.DifficultyOverride : d.Difficulty)
                : query.Where(d => d.Difficulty > -1)
                       .OrderByDescending(d => d.DifficultyOverride > -1 ? d.DifficultyOverride : d.Difficulty),
            "charCount" => sortOrder == SortOrder.Ascending
                ? query.OrderBy(d => d.CharacterCount)
                : query.OrderByDescending(d => d.CharacterCount),
            "sentenceLength" => sortOrder == SortOrder.Ascending
                ? query.OrderBy(d => d.CharacterCount / (d.SentenceCount + 1)).Where(d => d.SentenceCount != 0)
                : query.OrderByDescending(d => d.CharacterCount / (d.SentenceCount + 1)).Where(d => d.SentenceCount != 0),
            "dialoguePercentage" => sortOrder == SortOrder.Ascending
                ? query.OrderBy(d => d.DialoguePercentage)
                       .Where(d => !d.HideDialoguePercentage && d.DialoguePercentage != 0 && d.DialoguePercentage != 100)
                : query.OrderByDescending(d => d.DialoguePercentage)
                       .Where(d => !d.HideDialoguePercentage && d.DialoguePercentage != 0 && d.DialoguePercentage != 100),
            "wordCount" => sortOrder == SortOrder.Ascending
                ? query.OrderBy(d => d.WordCount)
                : query.OrderByDescending(d => d.WordCount),
            "uKanji" => sortOrder == SortOrder.Ascending
                ? query.OrderBy(d => d.UniqueKanjiCount)
                : query.OrderByDescending(d => d.UniqueKanjiCount),
            "uWordCount" => sortOrder == SortOrder.Ascending
                ? query.OrderBy(d => d.UniqueWordCount)
                : query.OrderByDescending(d => d.UniqueWordCount),
            "uKanjiOnce" => sortOrder == SortOrder.Ascending
                ? query.OrderBy(d => d.UniqueKanjiUsedOnceCount)
                : query.OrderByDescending(d => d.UniqueKanjiUsedOnceCount),
            "filter" => query.OrderBy(_ => 1), // Dummy ordering for pgroonga_score
            "releaseDate" => sortOrder == SortOrder.Ascending
                ? query.OrderBy(d => d.ReleaseDate)
                : query.OrderByDescending(d => d.ReleaseDate),
            "addedDate" => sortOrder == SortOrder.Ascending
                ? query.OrderBy(d => d.CreationDate)
                : query.OrderByDescending(d => d.CreationDate),
            "subdeckCount" => sortOrder == SortOrder.Ascending
                ? query.OrderBy(d => d.Children.Count)
                : query.OrderByDescending(d => d.Children.Count),
            "extRating" => sortOrder == SortOrder.Ascending
                ? query.OrderBy(d => d.ExternalRating)
                       .Where(d => d.ExternalRating != 0)
                : query.OrderByDescending(d => d.ExternalRating)
                       .Where(d => d.ExternalRating != 0),
            _ => sortOrder == SortOrder.Ascending
                ? query.OrderBy(d => d.RomajiTitle)
                : query.OrderByDescending(d => d.RomajiTitle),
        };
    }

    private IQueryable<DeckWithOccurrences> ApplySorting(IQueryable<DeckWithOccurrences> query, string sortBy, SortOrder sortOrder)
    {
        return sortBy switch
        {
            "occurrences" => sortOrder == SortOrder.Ascending
                ? query.OrderBy(p => p.Occurrences)
                : query.OrderByDescending(p => p.Occurrences),
            "difficulty" => sortOrder == SortOrder.Ascending
                ? query.Where(p => p.Deck.Difficulty > -1)
                       .OrderBy(p => p.Deck.DifficultyOverride > -1 ? p.Deck.DifficultyOverride : p.Deck.Difficulty)
                : query.Where(p => p.Deck.Difficulty > -1)
                       .OrderByDescending(p => p.Deck.DifficultyOverride > -1 ? p.Deck.DifficultyOverride : p.Deck.Difficulty),
            "charCount" => sortOrder == SortOrder.Ascending
                ? query.OrderBy(p => p.Deck.CharacterCount)
                : query.OrderByDescending(p => p.Deck.CharacterCount),
            "sentenceLength" => sortOrder == SortOrder.Ascending
                ? query.OrderBy(p => p.Deck.CharacterCount / (p.Deck.SentenceCount + 1)).Where(p => p.Deck.SentenceCount != 0)
                : query.OrderByDescending(p => p.Deck.CharacterCount / (p.Deck.SentenceCount + 1)).Where(p => p.Deck.SentenceCount != 0),
            "dialoguePercentage" => sortOrder == SortOrder.Ascending
                ? query.OrderBy(p => p.Deck.DialoguePercentage)
                       .Where(p => p.Deck.DialoguePercentage != 0 && p.Deck.DialoguePercentage != 100)
                : query.OrderByDescending(p => p.Deck.DialoguePercentage)
                       .Where(p => p.Deck.DialoguePercentage != 0 && p.Deck.DialoguePercentage != 100),
            "wordCount" => sortOrder == SortOrder.Ascending
                ? query.OrderBy(p => p.Deck.WordCount)
                : query.OrderByDescending(p => p.Deck.WordCount),
            "uKanji" => sortOrder == SortOrder.Ascending
                ? query.OrderBy(p => p.Deck.UniqueKanjiCount)
                : query.OrderByDescending(p => p.Deck.UniqueKanjiCount),
            "uWordCount" => sortOrder == SortOrder.Ascending
                ? query.OrderBy(p => p.Deck.UniqueWordCount)
                : query.OrderByDescending(p => p.Deck.UniqueWordCount),
            "uKanjiOnce" => sortOrder == SortOrder.Ascending
                ? query.OrderBy(p => p.Deck.UniqueKanjiUsedOnceCount)
                : query.OrderByDescending(p => p.Deck.UniqueKanjiUsedOnceCount),
            "filter" => query.OrderBy(_ => 1), // Dummy ordering for pgroonga_score
            "releaseDate" => sortOrder == SortOrder.Ascending
                ? query.OrderBy(p => p.Deck.ReleaseDate)
                : query.OrderByDescending(p => p.Deck.ReleaseDate),
            _ => sortOrder == SortOrder.Ascending
                ? query.OrderBy(p => p.Deck.RomajiTitle)
                : query.OrderByDescending(p => p.Deck.RomajiTitle),
        };
    }

    private async Task<PaginatedResponse<List<DeckDto>>> HandleWordBasedQuery(
        IQueryable<DeckWithOccurrences> projectedQuery, int wordId, int readingIndex, string sortBy, SortOrder sortOrder, int offset,
        int pageSize, Dictionary<int, float> coverageDict, Dictionary<int, float> uniqueCoverageDict,
        Dictionary<int, UserDeckPreference> preferencesDict, List<int>? orderedDeckIds)
    {
        // Apply sorting to the projected query
        projectedQuery = ApplySorting(projectedQuery, sortBy, sortOrder);


        var totalCount = await projectedQuery.CountAsync();
        var paginatedProjections = await projectedQuery
                                     .Skip(offset)
                                     .Take(pageSize)
                                     .AsSplitQuery()
                                     .ToListAsync();

        var deckIdsToHydrate = paginatedProjections.Select(p => p.Deck.DeckId).ToList();
        var fullDecks = await context.Decks.AsNoTracking()
                                     .Where(d => deckIdsToHydrate.Contains(d.DeckId))
                                     .Include(d => d.Children)
                                     .Include(d => d.Links)
                                     .Include(d => d.Titles)
                                     .Include(d => d.DeckGenres)
                                     .Include(d => d.DeckTags)
                                     .ThenInclude(dt => dt.Tag)
                                     .Include(d => d.RelationshipsAsSource)
                                     .ThenInclude(r => r.TargetDeck)
                                     .Include(d => d.RelationshipsAsTarget)
                                     .ThenInclude(r => r.SourceDeck)
                                     .AsSplitQuery()
                                     .ToListAsync();
        
        var fullDeckMap = fullDecks.ToDictionary(d => d.DeckId);
        var paginatedResults = paginatedProjections
                               .Where(p => fullDeckMap.ContainsKey(p.Deck.DeckId))
                               .Select(p => new DeckWithOccurrences 
                                            { 
                                                Deck = fullDeckMap[p.Deck.DeckId], 
                                                Occurrences = p.Occurrences 
                                            })
                               .ToList();

        
        // If we have a title filter, re-sort in memory to match the PGroonga ordering
        if (orderedDeckIds is { Count: > 0 } && sortBy == "filter")
        {
            var deckLookup = paginatedResults.ToDictionary(d => d.Deck.DeckId);
            var paginatedIds = orderedDeckIds.Skip(offset).Take(pageSize).ToList();
            paginatedResults = paginatedIds
                               .Where(id => deckLookup.ContainsKey(id))
                               .Select(id => deckLookup[id])
                               .ToList();
        }
        
        var targetDeckIds = paginatedResults.Select(r => r.Deck.DeckId).ToList();

        var minimalExamples = await context.ExampleSentences
                                           .AsNoTracking()
                                           .Join(context.Decks.AsNoTracking(),
                                                 es => es.DeckId,
                                                 d => d.DeckId,
                                                 (es, d) => new { es, d })
                                           .Where(x => targetDeckIds.Contains(x.d.ParentDeckId ?? x.d.DeckId))
                                           .Select(x => new
                                                        {
                                                            EffectiveDeckId = x.d.ParentDeckId ?? x.d.DeckId, x.es.Text, Match = x.es.Words
                                                                .Where(w => w.WordId == wordId && w.ReadingIndex == readingIndex)
                                                                .Select(w => new { w.Position, w.Length })
                                                                .FirstOrDefault()
                                                        })
                                           .Where(x => x.Match != null)
                                           .GroupBy(x => x.EffectiveDeckId)
                                           .Select(g => g.First())
                                           .ToListAsync();

        // Create dictionary for O(1) lookup instead of O(n) per deck
        var exampleSentencesByDeck = minimalExamples
            .ToDictionary(
                          x => x.EffectiveDeckId,
                          x => new ExampleSentenceDto { Text = x.Text, WordPosition = x.Match!.Position, WordLength = x.Match!.Length });

        var dtos = paginatedResults
                   .Select(r => new DeckDto(
                                            r.Deck,
                                            r.Occurrences,
                                            exampleSentencesByDeck.GetValueOrDefault(r.Deck.DeckId)))
                   .ToList();

        foreach (var (dto, result) in dtos.Zip(paginatedResults))
            dto.Relationships = DeckRelationshipDto.FromDeck(result.Deck.RelationshipsAsSource, result.Deck.RelationshipsAsTarget);

        // Populate user coverage if authenticated
        if (currentUserService.IsAuthenticated)
        {
            foreach (var dto in dtos)
            {
                if (coverageDict.TryGetValue(dto.DeckId, out var c)) dto.Coverage = c;
                if (uniqueCoverageDict.TryGetValue(dto.DeckId, out var uc)) dto.UniqueCoverage = uc;
                if (preferencesDict.TryGetValue(dto.DeckId, out var pref))
                {
                    dto.Status = pref.Status;
                    dto.IsFavourite = pref.IsFavourite;
                    dto.IsIgnored = pref.IsIgnored;
                }
            }
        }

        return new PaginatedResponse<List<DeckDto>>(dtos, totalCount, pageSize, offset);
    }

    /// <summary>
    /// Returns vocabulary entries for a given deck with sorting and pagination.
    /// </summary>
    /// <param name="id">Deck identifier.</param>
    /// <param name="sortBy">Sort by globalFreq | deckFreq | chrono.</param>
    /// <param name="sortOrder">Ascending or Descending.</param>
    /// <param name="offset">Pagination offset.</param>
    /// <param name="displayFilter">When authenticated: all | known | young | mature | mastered | blacklisted | unknown.</param>
    /// <returns>Paginated deck vocabulary list.</returns>
    [HttpGet("{id}/vocabulary")]
    // [ResponseCache(Duration = 600, VaryByQueryKeys = ["id", "sortBy", "sortOrder", "offset"])]
    [SwaggerOperation(Summary = "Get deck vocabulary")]
    [ProducesResponseType(typeof(PaginatedResponse<DeckVocabularyListDto?>), StatusCodes.Status200OK)]
    public async Task<PaginatedResponse<DeckVocabularyListDto?>> GetVocabulary(int id, string? sortBy = "",
                                                                               SortOrder sortOrder = SortOrder.Ascending,
                                                                               int? offset = 0, string displayFilter = "all")
    {
        int pageSize = 100;

        var deck = context.Decks.AsNoTracking().FirstOrDefault(d => d.DeckId == id);

        if (deck == null)
            return new PaginatedResponse<DeckVocabularyListDto?>(null, 0, pageSize, offset ?? 0);

        var parentDeck = context.Decks.AsNoTracking().FirstOrDefault(d => d.DeckId == deck.ParentDeckId);
        var parentDeckDto = parentDeck != null ? new DeckDto(parentDeck) : null;

        var query = context.DeckWords.AsNoTracking().Where(dw => dw.DeckId == id);

        if (currentUserService.IsAuthenticated && !string.IsNullOrEmpty(displayFilter) && displayFilter != "all")
        {
            var userId = currentUserService.UserId!;

            var allDeckWords = await query.ToListAsync();
            var deckWordKeys = allDeckWords.Select(dw => (dw.WordId, dw.ReadingIndex)).ToList();

            var knownStates = await currentUserService.GetKnownWordsState(deckWordKeys);

            var distinctWordIds = deckWordKeys.Select(k => k.WordId).Distinct().ToList();
            var fsrsStates = await userContext.FsrsCards
                                              .AsNoTracking()
                                              .Where(uk => uk.UserId == userId && distinctWordIds.Contains(uk.WordId))
                                              .Select(uk => new { uk.WordId, uk.ReadingIndex, uk.State })
                                              .ToDictionaryAsync(uk => (uk.WordId, uk.ReadingIndex), uk => uk.State);

            query = allDeckWords.AsQueryable();

            query = query.AsEnumerable().Where(dw =>
            {
                var key = (dw.WordId, dw.ReadingIndex);
                var knownState = knownStates.GetValueOrDefault(key, [KnownState.New]);
                var fsrsState = fsrsStates.GetValueOrDefault(key);

                return displayFilter switch
                {
                    "known" => !knownState.Contains(KnownState.New) && fsrsStates.ContainsKey(key),
                    "young" => knownState.Contains(KnownState.Young),
                    "mature" => knownState.Contains(KnownState.Mature),
                    "mastered" => knownState.Contains(KnownState.Mastered),
                    "blacklisted" => knownState.Contains(KnownState.Blacklisted),
                    "unknown" => !fsrsStates.ContainsKey(key),
                    _ => true
                };
            }).AsQueryable();
        }

        query = sortBy switch
        {
            "globalFreq" => sortOrder == SortOrder.Ascending
                ? query.OrderBy(d => context.JmDictWordFrequencies
                                            .Where(f => f.WordId == d.WordId)
                                            .Select(f => f.ReadingsFrequencyRank[d.ReadingIndex])
                                            .FirstOrDefault()).ThenBy(d => d.DeckWordId)
                : query.OrderByDescending(d => context.JmDictWordFrequencies
                                                      .Where(f => f.WordId == d.WordId)
                                                      .Select(f => f.ReadingsFrequencyRank[d.ReadingIndex])
                                                      .FirstOrDefault()).ThenBy(d => d.DeckWordId),
            "deckFreq" => sortOrder == SortOrder.Ascending
                ? query.OrderByDescending(d => d.Occurrences).ThenBy(d => d.DeckWordId)
                : query.OrderBy(d => d.Occurrences).ThenBy(d => d.DeckWordId),
            "chrono" or _ => sortOrder == SortOrder.Ascending
                ? query.OrderBy(d => d.DeckWordId)
                : query.OrderByDescending(d => d.DeckWordId),
        };

        int totalCount = query.Count(dw => dw.DeckId == id);

        var deckWordsList = query.Skip(offset ?? 0)
                                 .Take(pageSize)
                                 .ToList();

        var wordIds = deckWordsList.Select(dw => dw.WordId).ToList();

        var jmdictWords = context.JMDictWords.AsNoTracking()
                                 .Where(w => wordIds.Contains(w.WordId))
                                 .Include(w => w.Definitions)
                                 .ToList();

        var words = deckWordsList.Select(dw => new { dw, jmDictWord = jmdictWords.FirstOrDefault(w => w.WordId == dw.WordId) })
                                 .OrderBy(dw => wordIds.IndexOf(dw.dw.WordId))
                                 .ToList();

        var frequencies = context.JmDictWordFrequencies.AsNoTracking().Where(f => wordIds.Contains(f.WordId)).ToList();

        DeckVocabularyListDto dto = new() { ParentDeck = parentDeckDto, Deck = deck, Words = new() };

        var knownWords = await currentUserService.GetKnownWordsState(words.Select(dw => (dw.dw.WordId, dw.dw.ReadingIndex)).ToList());

        foreach (var word in words)
        {
            if (word.jmDictWord == null)
            {
                continue;
            }

            var reading = word.jmDictWord.ReadingsFurigana[word.dw.ReadingIndex];

            List<ReadingDto> alternativeReadings = word.jmDictWord.ReadingsFurigana
                                                       .Select((r, i) => new ReadingDto
                                                                         {
                                                                             Text = r, ReadingIndex = (byte)i,
                                                                             ReadingType = word.jmDictWord.ReadingTypes[i], FrequencyRank =
                                                                                 frequencies.First(f => f.WordId == word.dw.WordId)
                                                                                            .ReadingsFrequencyRank[i],
                                                                             FrequencyPercentage =
                                                                                 frequencies.First(f => f.WordId == word.dw.WordId)
                                                                                            .ReadingsFrequencyPercentage[i]
                                                                         })
                                                       .ToList();

            // Remove current
            alternativeReadings.RemoveAt(word.dw.ReadingIndex);

            var frequency = frequencies.First(f => f.WordId == word.dw.WordId);

            var mainReading = new ReadingDto()
                              {
                                  Text = reading, ReadingIndex = word.dw.ReadingIndex,
                                  ReadingType = word.jmDictWord.ReadingTypes[word.dw.ReadingIndex],
                                  FrequencyRank = frequency.ReadingsFrequencyRank[word.dw.ReadingIndex],
                                  FrequencyPercentage = frequency.ReadingsFrequencyPercentage[word.dw.ReadingIndex]
                              };

            var wordDto = new WordDto
                          {
                              WordId = word.jmDictWord.WordId, MainReading = mainReading, AlternativeReadings = alternativeReadings,
                              PartsOfSpeech = word.jmDictWord.PartsOfSpeech.ToHumanReadablePartsOfSpeech(),
                              Definitions = word.jmDictWord.Definitions.ToDefinitionDtos(), Occurrences = word.dw.Occurrences,
                              PitchAccents = word.jmDictWord.PitchAccents
                          };

            dto.Words.Add(wordDto);
        }

        dto.Words.ApplyKnownWordsState(knownWords);

        return new PaginatedResponse<DeckVocabularyListDto?>(dto, totalCount, pageSize, offset ?? 0);
    }

    /// <summary>
    /// Returns details for a media deck including parent and subdecks.
    /// </summary>
    /// <param name="id">Deck identifier.</param>
    /// <param name="offset">Pagination offset for subdecks.</param>
    /// <returns>Deck detail with subdecks.</returns>
    [HttpGet("{id}/detail")]
    // [ResponseCache(Duration = 600, VaryByQueryKeys = ["id", "offset"])]
    [SwaggerOperation(Summary = "Get deck details")]
    [ProducesResponseType(typeof(PaginatedResponse<DeckDetailDto?>), StatusCodes.Status200OK)]
    public PaginatedResponse<DeckDetailDto?> GetMediaDeckDetail(int id, int? offset = 0)
    {
        int pageSize = 25;

        var deck = context.Decks.AsNoTracking()
                          .Include(d => d.Children)
                          .Include(d => d.Links)
                          .Include(d => d.DeckGenres)
                          .Include(d => d.DeckTags)
                          .ThenInclude(dt => dt.Tag)
                          .Include(d => d.RelationshipsAsSource)
                          .ThenInclude(r => r.TargetDeck)
                          .Include(d => d.RelationshipsAsTarget)
                          .ThenInclude(r => r.SourceDeck)
                          .FirstOrDefault(d => d.DeckId == id);

        if (deck == null)
            return new PaginatedResponse<DeckDetailDto?>(null, 0, pageSize, offset ?? 0);

        var parentDeck = context.Decks.AsNoTracking().Include(d => d.DeckGenres).Include(d => d.DeckTags).ThenInclude(dt => dt.Tag)
                                .FirstOrDefault(d => d.DeckId == deck.ParentDeckId);
        var subDecks = context.Decks.AsNoTracking().Include(d => d.DeckGenres).Include(d => d.DeckTags).ThenInclude(dt => dt.Tag)
                              .Where(d => d.ParentDeckId == id);
        int totalCount = subDecks.Count();

        subDecks = subDecks
                   .OrderBy(dw => dw.DeckOrder)
                   .Skip(offset ?? 0)
                   .Take(pageSize);

        var mainDeckDto = new DeckDto(deck);
        mainDeckDto.Relationships = DeckRelationshipDto.FromDeck(deck.RelationshipsAsSource, deck.RelationshipsAsTarget);
        List<DeckDto> subdeckDtos = [];

        var subDeckList = subDecks.ToList();
        foreach (var subDeck in subDeckList)
            subdeckDtos.Add(new DeckDto(subDeck));

        if (currentUserService.IsAuthenticated)
        {
            var userId = currentUserService.UserId!;
            var ids = new List<int> { mainDeckDto.DeckId };
            ids.AddRange(subdeckDtos.Select(d => d.DeckId));
            var coverages = userContext.UserCoverages.AsNoTracking()
                                       .Where(uc => uc.UserId == userId && ids.Contains(uc.DeckId))
                                       .Select(uc => new { uc.DeckId, uc.Coverage, uc.UniqueCoverage })
                                       .ToList();
            var coverageDict = coverages.ToDictionary(x => x.DeckId, x => (float)x.Coverage);
            var uCoverageDict = coverages.ToDictionary(x => x.DeckId, x => (float)x.UniqueCoverage);

            var preferences = userContext.UserDeckPreferences.AsNoTracking()
                                         .Where(p => p.UserId == userId && ids.Contains(p.DeckId))
                                         .ToList();
            var preferencesDict = preferences.ToDictionary(p => p.DeckId);

            if (coverageDict.TryGetValue(mainDeckDto.DeckId, out var mc)) mainDeckDto.Coverage = mc;
            if (uCoverageDict.TryGetValue(mainDeckDto.DeckId, out var muc)) mainDeckDto.UniqueCoverage = muc;
            if (preferencesDict.TryGetValue(mainDeckDto.DeckId, out var mpref))
            {
                mainDeckDto.Status = mpref.Status;
                mainDeckDto.IsFavourite = mpref.IsFavourite;
                mainDeckDto.IsIgnored = mpref.IsIgnored;
            }

            foreach (var subdeckDto in subdeckDtos)
            {
                if (coverageDict.TryGetValue(subdeckDto.DeckId, out var c)) subdeckDto.Coverage = c;
                if (uCoverageDict.TryGetValue(subdeckDto.DeckId, out var uc)) subdeckDto.UniqueCoverage = uc;
                if (preferencesDict.TryGetValue(subdeckDto.DeckId, out var pref))
                {
                    subdeckDto.Status = pref.Status;
                    subdeckDto.IsFavourite = pref.IsFavourite;
                    subdeckDto.IsIgnored = pref.IsIgnored;
                }
            }
        }

        var parentDeckDto = parentDeck != null ? new DeckDto(parentDeck) : null;
        var dto = new DeckDetailDto { ParentDeck = parentDeckDto, MainDeck = mainDeckDto, SubDecks = subdeckDtos };

        return new PaginatedResponse<DeckDetailDto?>(dto, totalCount, pageSize, offset ?? 0);
    }

    /// <summary>
    /// Downloads a deck in the requested format and order. Supports filtering and excluding known words.
    /// </summary>
    /// <param name="id">Deck identifier.</param>
    /// <param name="request">Download options.</param>
    /// <returns>File content result with the generated deck.</returns>
    [HttpPost("{id}/download")]
    [EnableRateLimiting("download")]
    [SwaggerOperation(Summary = "Download a deck",
                      Description = "Generate a deck file (Anki, CSV, TXT, Yomitan) with optional filters and ordering.")]
    [Produces("application/x-binary", "text/csv", "text/plain", "application/zip")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IResult> DownloadDeck(int id, [FromBody] DeckDownloadRequest request)
    {
        var deck = await context.Decks
                                .AsNoTracking()
                                .Include(d => d.Children)
                                .FirstOrDefaultAsync(d => d.DeckId == id);

        if (deck == null)
        {
            return Results.NotFound();
        }

        IQueryable<DeckWord> deckWordsQuery = context.DeckWords.AsNoTracking().Where(dw => dw.DeckId == id);

        if (request.Format == DeckFormat.Yomitan)
        {
            var yomitanBytes = await YomitanHelper.GenerateYomitanFrequencyDeckFromDeck(contextFactory, deck);
            return Results.File(yomitanBytes, "application/zip", $"freq_{deck.OriginalTitle}.zip");
        }

        List<DeckWord>? deckWordsRaw = null;

        switch (request.DownloadType)
        {
            case DeckDownloadType.Full:
                break;

            case DeckDownloadType.TopGlobalFrequency:
                // Get the words between frequency rank minFrequency and maxFrequency
                deckWordsQuery = deckWordsQuery.Where(dw => context.JmDictWordFrequencies
                                                                   .Any(f => f.WordId == dw.WordId &&
                                                                             f.ReadingsFrequencyRank[dw.ReadingIndex] >=
                                                                             request.MinFrequency &&
                                                                             f.ReadingsFrequencyRank[dw.ReadingIndex] <=
                                                                             request.MaxFrequency));
                break;

            case DeckDownloadType.TopDeckFrequency:
                deckWordsQuery = deckWordsQuery
                                 .OrderByDescending(dw => dw.Occurrences)
                                 .Skip(request.MinFrequency)
                                 .Take(request.MaxFrequency - request.MinFrequency);
                break;

            case DeckDownloadType.TopChronological:
                deckWordsQuery = deckWordsQuery
                                 .OrderBy(dw => dw.DeckWordId)
                                 .Skip(request.MinFrequency)
                                 .Take(request.MaxFrequency - request.MinFrequency);
                break;

            case DeckDownloadType.TargetCoverage:
                if (!currentUserService.IsAuthenticated)
                    return Results.Unauthorized();

                if (request.TargetPercentage is null or < 1 or > 100)
                    return Results.BadRequest("Target percentage must be between 1 and 100");

                // Get user known words
                var knownCards = await userContext.FsrsCards
                    .Where(kw => kw.UserId == currentUserService.UserId! &&
                                 (kw.State == FsrsState.Blacklisted ||
                                  kw.State == FsrsState.Mastered ||
                                  (kw.LastReview != null &&
                                   (kw.Due - kw.LastReview.Value).TotalDays >= 21)))
                    .Select(kw => new { kw.WordId, kw.ReadingIndex })
                    .ToListAsync();

                var knownKeysSet = knownCards
                    .Select(kw => ((long)kw.WordId << 32) | (uint)kw.ReadingIndex)
                    .ToHashSet();

                // Get all deck words ordered by occurrences (most frequent first)
                var allDeckWords = await deckWordsQuery
                    .OrderByDescending(dw => dw.Occurrences)
                    .ToListAsync();

                // Calculate current coverage from known words
                int totalOccurrences = deck.WordCount;
                int knownOccurrences = allDeckWords
                    .Where(dw => knownKeysSet.Contains(((long)dw.WordId << 32) | (uint)dw.ReadingIndex))
                    .Sum(dw => dw.Occurrences);

                double targetCoverage = request.TargetPercentage.Value;

                // Add unknown words (by frequency) until target coverage is reached
                var resultWords = new List<DeckWord>();
                int cumulativeOccurrences = knownOccurrences;

                foreach (var dw in allDeckWords)
                {
                    var key = ((long)dw.WordId << 32) | (uint)dw.ReadingIndex;
                    if (knownKeysSet.Contains(key))
                        continue;

                    resultWords.Add(dw);
                    cumulativeOccurrences += dw.Occurrences;

                    double newCoverage = (double)cumulativeOccurrences / totalOccurrences * 100;
                    if (newCoverage >= targetCoverage)
                        break;
                }

                deckWordsRaw = resultWords;
                break;

            default:
                return Results.BadRequest();
        }

        // Apply ordering and execute query for non-TargetCoverage types
        if (deckWordsRaw == null)
        {
            switch (request.Order)
            {
                case DeckOrder.Chronological:
                    deckWordsQuery = deckWordsQuery.OrderBy(dw => dw.DeckWordId);
                    break;

                case DeckOrder.GlobalFrequency:
                    deckWordsQuery = deckWordsQuery.OrderBy(dw => context.JmDictWordFrequencies
                                                                         .Where(f => f.WordId == dw.WordId)
                                                                         .Select(f => f.ReadingsFrequencyRank[dw.ReadingIndex])
                                                                         .FirstOrDefault()
                                                           );
                    break;

                case DeckOrder.DeckFrequency:
                    deckWordsQuery = deckWordsQuery.OrderByDescending(dw => dw.Occurrences);
                    break;
                default:
                    return Results.BadRequest();
            }

            deckWordsRaw = await deckWordsQuery.ToListAsync();
        }

        if (request.ExcludeMatureMasteredBlacklisted && currentUserService.IsAuthenticated)
        {
            var userCards = await userContext.FsrsCards
                                             .Where(kw => kw.UserId == currentUserService.UserId!)
                                             .Select(kw => new { kw.WordId, kw.ReadingIndex, kw.State, kw.LastReview, kw.Due })
                                             .ToListAsync();

            var matureKeys = userCards
                             .Where(kw => kw.State == FsrsState.Mastered ||
                                          kw.State == FsrsState.Blacklisted ||
                                          (kw.LastReview != null && (kw.Due - kw.LastReview.Value).TotalDays >= 21))
                             .Select(kw => ((long)kw.WordId << 32) | (uint)kw.ReadingIndex)
                             .ToHashSet();

            deckWordsRaw = deckWordsRaw
                           .Where(dw =>
                           {
                               var key = ((long)dw.WordId << 32) | (uint)dw.ReadingIndex;
                               return !matureKeys.Contains(key);
                           })
                           .ToList();
        }

        if (request.ExcludeAllTrackedWords && currentUserService.IsAuthenticated)
        {
            var trackedWordIds = await userContext.FsrsCards
                                                  .Where(kw => kw.UserId == currentUserService.UserId!)
                                                  .Select(kw => new { kw.WordId, kw.ReadingIndex })
                                                  .ToListAsync();

            var trackedKeys = trackedWordIds
                              .Select(kw => ((long)kw.WordId << 32) | (uint)kw.ReadingIndex)
                              .ToHashSet();

            deckWordsRaw = deckWordsRaw
                           .Where(dw =>
                           {
                               var key = ((long)dw.WordId << 32) | (uint)dw.ReadingIndex;
                               return !trackedKeys.Contains(key);
                           })
                           .ToList();
        }

        var wordIds = deckWordsRaw.Select(dw => (long)dw.WordId).ToList();

        List<(int WordId, byte ReadingIndex, int Occurrences)> deckWords = deckWordsRaw
                                                                           .Select(dw => new ValueTuple<int, byte, int>(dw.WordId,
                                                                                       dw.ReadingIndex, dw.Occurrences))
                                                                           .ToList();

        var bytes = await GenerateDeckDownload(id, request, wordIds, deck, deckWords);

        if (bytes == null)
            return Results.BadRequest();

        logger.LogInformation(
                              "User downloaded deck: DeckId={DeckId}, DeckTitle={DeckTitle}, Format={Format}, DownloadType={DownloadType}, WordCount={WordCount}, ExcludeMature={ExcludeMature}, ExcludeAllTracked={ExcludeAllTracked}",
                              id, deck.OriginalTitle, request.Format, request.DownloadType, deckWordsRaw.Count, request.ExcludeMatureMasteredBlacklisted, request.ExcludeAllTrackedWords);

        return request.Format switch
        {
            DeckFormat.Anki => Results.File(bytes, "application/x-binary", $"{deck.OriginalTitle}.apkg"),
            DeckFormat.Csv => Results.File(bytes, "text/csv", $"{deck.OriginalTitle}.csv"),
            DeckFormat.Txt or DeckFormat.TxtRepeated => Results.File(bytes, "text/plain", $"{deck.OriginalTitle}.txt"),
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    /// <summary>
    /// Parses a custom text into a temporary deck and returns the generated Anki package as base64.
    /// </summary>
    /// <param name="request">Text to parse.</param>
    /// <returns>JSON containing deck metadata and a base64-encoded file.</returns>
    [HttpPost("parse-custom-deck")]
    [EnableRateLimiting("download")]
    [RequestSizeLimit(5_000_000)]
    [SwaggerOperation(Summary = "Parse custom deck text")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IResult> ParseCustomDeck([FromBody] ParseCustomDeckRequest request)
    {
        if (request.Text.Length > 200000)
            return Results.BadRequest();

        var deck = await Parser.Parser.ParseTextToDeck(contextFactory, storeRawText: true, text: request.Text);
        deck.OriginalTitle = "Custom deck";
        var deckDownloadRequest = new DeckDownloadRequest() { DownloadType = DeckDownloadType.Full, Format = DeckFormat.Anki };
        var deckWords = deck.DeckWords.Select(dw => new ValueTuple<int, byte, int>(dw.WordId, dw.ReadingIndex, dw.Occurrences)).ToList();
        var wordIds = deck.DeckWords.Select(dw => (long)dw.WordId).ToList();

        var fileResult = await GenerateDeckDownload(0, deckDownloadRequest, wordIds, deck, deckWords);
        var deckDto = new DeckDto(deck);
        var fileBase64 = Convert.ToBase64String(fileResult);

        logger.LogInformation(
                              "User parsed custom deck: CharacterCount={CharacterCount}, WordCount={WordCount}, UniqueWordCount={UniqueWordCount}",
                              deck.CharacterCount, deck.WordCount, deck.UniqueWordCount);

        var result = new
                     {
                         Deck = deckDto, File = new
                                                {
                                                    ContentBase64 = fileBase64, ContentType = "application/x-binary", // Mime type for .apkg
                                                    FileName = $"{deck.OriginalTitle}.apkg"
                                                }
                     };
        return Results.Json(result);
    }

    private async Task<byte[]?> GenerateDeckDownload(int id, DeckDownloadRequest request, List<long> wordIds, Deck deck,
                                                     List<(int WordId, byte ReadingIndex, int Occurrences)> deckWords)
    {
        var jmdictWords = await context.JMDictWords.AsNoTracking()
                                       .Include(w => w.Definitions)
                                       .Where(w => wordIds.Contains(w.WordId))
                                       .ToDictionaryAsync(w => w.WordId);
        var frequencies = context.JmDictWordFrequencies.Where(f => wordIds.Contains(f.WordId))
                                 .ToDictionary(f => f.WordId, f => f);


        var deckIds = new List<int> { id };

        // If this deck has children, use sentences from the children instead
        if (deck.Children.Count != 0)
            deckIds = deck.Children.Select(c => c.DeckId).ToList();

        var exampleSentences = await context.ExampleSentences
                                            .AsNoTracking()
                                            .Where(es => deckIds.Contains(es.DeckId))
                                            .Include(es => es.Words.Where(w => wordIds.Contains(w.WordId)))
                                            .ToListAsync();

        var wordToSentencesMap = new Dictionary<(int WordId, byte ReadingIndex), List<(string Text, byte Position, byte Length)>>();

        foreach (var sentence in exampleSentences)
        {
            foreach (var word in sentence.Words.Where(w => wordIds.Contains(w.WordId)))
            {
                var key = (word.WordId, word.ReadingIndex);
                if (!wordToSentencesMap.ContainsKey(key))
                    wordToSentencesMap[key] = new List<(string, byte, byte)>();

                // If this word already has a sentence and we're only collecting one per word, skip
                if (wordToSentencesMap[key].Count > 0)
                    continue;

                wordToSentencesMap[key].Add((sentence.Text, word.Position, word.Length));
            }
        }

        switch (request.Format)
        {
            case DeckFormat.Anki:

                // Lapis template from https://github.com/donkuri/lapis/tree/main
                var templatePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "resources", "lapis.apkg");
                var template = await AnkiFileReader.ReadFromFileAsync(templatePath);
                var noteTypeTemplate = template.NoteTypes.First();

                var collection = new AnkiCollection();
                var noteTypeId = collection.CreateNoteType(noteTypeTemplate);
                var deckId = collection.CreateDeck(deck.OriginalTitle);

                foreach (var word in deckWords)
                {
                    string expression = jmdictWords[word.WordId].Readings[word.ReadingIndex];

                    if (request.ExcludeKana && WanaKana.IsKana(expression))
                        continue;


                    // Need a space before the kanjis for lapis
                    string kanjiPatternPart = @"\p{IsCJKUnifiedIdeographs}";
                    string lookaheadPattern = $@"(?=(?:{kanjiPatternPart})*\[.*?\])";
                    string precedingKanjiLookbehind = $@"\p{{IsCJKUnifiedIdeographs}}{lookaheadPattern}";
                    string pattern = $"(?<!\\])(?<!{precedingKanjiLookbehind})({kanjiPatternPart}){lookaheadPattern}";
                    string expressionFurigana = Regex.Replace(jmdictWords[word.WordId].ReadingsFurigana[word.ReadingIndex], pattern, " $1");
                    // Very unoptimized, might have to rework
                    string expressionReading = string.Join("", jmdictWords[word.WordId].ReadingsFurigana[word.ReadingIndex]
                                                                                       .Where(c => WanaKana.IsKana(c.ToString()))
                                                                                       .Select(c => c.ToString()));
                    string expressionAudio = "";
                    string selectionText = "";


                    var definitions = jmdictWords[word.WordId].Definitions;
                    var definitionBuilder = new StringBuilder();
                    List<string>? previousPos = null;

                    for (var i = 0; i < definitions.Count; i++)
                    {
                        JmDictDefinition? definition = definitions[i];
                        bool isDifferentPartOfSpeech = previousPos == null || !previousPos.SequenceEqual(definition.PartsOfSpeech);
                        if (isDifferentPartOfSpeech)
                        {
                            if (i != 0)
                                definitionBuilder.Append("</ul>");
                            definitionBuilder.Append("<ul>");

                            previousPos = definition.PartsOfSpeech?.ToList() ?? [];

                            if (previousPos.Count > 0)
                            {
                                definitionBuilder.Append("<div class=\"definition-pos\">");
                                definitionBuilder.Append(string.Join(" ",
                                                                     previousPos.Select(p =>
                                                                                            $"<span class=\"pos\" title=\"{JmDictHelper.ToHumanReadablePartsOfSpeech([p])[0]}\">{System.Net.WebUtility.HtmlEncode(p)}</span>")));
                                definitionBuilder.Append("</div>");
                            }
                        }

                        // Meanings for this definition
                        for (var j = 0; j < definition.EnglishMeanings.Count; j++)
                        {
                            string? meaning = definition.EnglishMeanings[j];
                            if (j == 0)
                                definitionBuilder.Append("<li>");
                            if (j != 0)
                                definitionBuilder.Append(" ; ");
                            definitionBuilder.Append(System.Net.WebUtility.HtmlEncode(meaning));
                            if (j == definitions.Count - 1)
                                definitionBuilder.Append("</li>");
                        }
                    }

                    definitionBuilder.Append("</ul>");

                    string css = """
                                 <style>
                                    .pos {
                                         background-color: rgb(168, 85, 247);
                                         border-radius: 0.35em;
                                         padding: 0.2em 0.4em;
                                         color: white;
                                         word-break: keep-all;
                                    }
                                 </style>
                                 """;
                    definitionBuilder.Append(css);
                    string mainDefinition = definitionBuilder.ToString();


                    string definitionPicture = "";
                    string sentence = "";

                    if (!request.ExcludeExampleSentences &&
                        wordToSentencesMap.TryGetValue((word.WordId, word.ReadingIndex), out var sentences) && sentences.Count > 0)
                    {
                        var exampleSentence = sentences.First();
                        int position = exampleSentence.Position;
                        int length = exampleSentence.Length;

                        string originalText = exampleSentence.Text;
                        if (position >= 0 && position + length <= originalText.Length)
                        {
                            sentence = originalText.Substring(0, position) +
                                       "<span style=\"font-weight: 700; color: rgb(168, 85, 247);\">" +
                                       originalText.Substring(position, length) + "</span>" +
                                       originalText.Substring(position + length);
                        }
                    }

                    string sentenceFurigana = "";
                    string sentenceAudio = "";
                    string picture = "";
                    // This is where to add extra definitions, such as J-J
                    string glossary = "";
                    string hint = "";
                    string isWordAndSentenceCard = "";
                    string isClickCard = "";
                    string isSentenceCard = "";
                    string pitchPosition = "";
                    string pitchCategories = "";
                    string frequency =
                        $"<ul><li>Jiten: {word.Occurrences} occurrences ; #{frequencies[word.WordId].ReadingsFrequencyRank[word.ReadingIndex]} global rank</li></ul>";
                    string freqSort = $"{frequencies[word.WordId].ReadingsFrequencyRank[word.ReadingIndex]}";
                    string occurrences = $"{word.Occurrences}";
                    string miscInfo = $"From {deck.OriginalTitle} - generated by Jiten.moe";

                    if (jmdictWords[word.WordId].PitchAccents != null)
                        pitchPosition = string.Join(",", jmdictWords[word.WordId].PitchAccents!.Select(p => p.ToString()));

                    collection.CreateNote(deckId, noteTypeId,
                                          expression, expressionFurigana,
                                          expressionReading, expressionAudio, selectionText, mainDefinition, definitionPicture,
                                          sentence, sentenceFurigana,
                                          sentenceAudio, picture, glossary, hint,
                                          isWordAndSentenceCard, isClickCard, isSentenceCard,
                                          pitchPosition, pitchCategories,
                                          frequency, freqSort, occurrences,
                                          miscInfo
                                         );
                }


                var stream = new MemoryStream();

                await AnkiFileWriter.WriteToStreamAsync(stream, collection);
                var bytes = stream.ToArray();

                return bytes;

            case DeckFormat.Csv:
                StringBuilder sb = new StringBuilder();

                sb.AppendLine($"\"Word\",\"ReadingFurigana\",\"ReadingKana\",\"Occurences\",\"ReadingFrequency\",\"PitchPositions\",\"Definitions\",\"ExampleSentence\",\"JmDictWordId\"");

                foreach (var word in deckWords)
                {
                    string reading = jmdictWords[word.WordId].Readings[word.ReadingIndex];

                    if (request.ExcludeKana && WanaKana.IsKana(reading))
                        continue;

                    string readingFurigana = jmdictWords[word.WordId].ReadingsFurigana[word.ReadingIndex];
                    string pitchPositions = "";

                    if (jmdictWords[word.WordId].PitchAccents != null)
                        pitchPositions = string.Join(",", jmdictWords[word.WordId].PitchAccents!.Select(p => p.ToString()));

                    // Very unoptimized, might have to rework
                    string readingKana = string.Join("", jmdictWords[word.WordId].ReadingsFurigana[word.ReadingIndex]
                                                                                 .Where(c => WanaKana.IsKana(c.ToString()))
                                                                                 .Select(c => c.ToString()));
                    string definitions = string.Join(",", jmdictWords[word.WordId].Definitions
                                                                                  .SelectMany(d => d.EnglishMeanings)
                                                                                  .Select(m => m.Replace("\"", "\"\"")));
                    var occurrences = word.Occurrences;
                    var readingFrequency = frequencies[word.WordId].ReadingsFrequencyRank[word.ReadingIndex];

                    string exampleSentence = "";
                    if (!request.ExcludeExampleSentences &&
                        wordToSentencesMap.TryGetValue((word.WordId, word.ReadingIndex), out var sentences) && sentences.Count > 0)
                    {
                        var sentence = sentences.First();
                        int position = sentence.Position;
                        int length = sentence.Length;

                        string originalText = sentence.Text;
                        if (position >= 0 && position + length <= originalText.Length)
                        {
                            exampleSentence = originalText.Substring(0, position) +
                                              "**" +
                                              originalText.Substring(position, length) + "**" +
                                              originalText.Substring(position + length);
                        }
                    }

                    sb.AppendLine($"\"{reading}\",\"{readingFurigana}\",\"{readingKana}\",\"{occurrences}\",\"{readingFrequency}\",\"{pitchPositions}\",\"{definitions}\",\"{exampleSentence}\",\"{word.WordId}\"");
                }

                return Encoding.UTF8.GetBytes(sb.ToString());
            case DeckFormat.Txt:
                StringBuilder txtSb = new StringBuilder();
                foreach (var word in deckWords)
                {
                    string reading = jmdictWords[word.WordId].Readings[word.ReadingIndex];
                    if (request.ExcludeKana && WanaKana.IsKana(reading))
                        continue;

                    txtSb.AppendLine(reading);
                }

                return Encoding.UTF8.GetBytes(txtSb.ToString());

            case DeckFormat.TxtRepeated:
                StringBuilder txtRepeatedSb = new StringBuilder();
                foreach (var word in deckWords)
                {
                    string reading = jmdictWords[word.WordId].Readings[word.ReadingIndex];
                    if (request.ExcludeKana && WanaKana.IsKana(reading))
                        continue;

                    for (int i = 0; i < word.Occurrences; i++)
                        txtRepeatedSb.AppendLine(reading);
                }

                return Encoding.UTF8.GetBytes(txtRepeatedSb.ToString());

            default:
                return null;
        }
    }

    /// <summary>
    /// Returns the count of top-level decks per media type.
    /// </summary>
    [HttpGet("decks-count")]
    [ResponseCache(Duration = 600)]
    [SwaggerOperation(Summary = "Get deck counts by media type")]
    [ProducesResponseType(typeof(Dictionary<int, int>), StatusCodes.Status200OK)]
    public IResult GetDecksCountByMediaType()
    {
        Dictionary<int, int> decksCount = context.Decks.AsNoTracking()
                                                 .Where(d => d.ParentDeckId == null)
                                                 .GroupBy(d => d.MediaType)
                                                 .ToDictionary(g => (int)g.Key, g => g.Count());

        return Results.Ok(decksCount);
    }

    /// <summary>
    /// Returns the number of vocabulary items in a deck between global frequency ranks.
    /// </summary>
    /// <param name="id">Deck identifier.</param>
    /// <param name="minFrequency">Minimum global frequency rank (inclusive).</param>
    /// <param name="maxFrequency">Maximum global frequency rank (inclusive).</param>
    [HttpGet("{id}/vocabulary-count-frequency")]
    [SwaggerOperation(Summary = "Count vocabulary in frequency range")]
    [ProducesResponseType(typeof(int), StatusCodes.Status200OK)]
    public IResult GetVocabularyCountByMediaFrequencyRange(int id, int minFrequency, int maxFrequency)
    {
        var query = context.DeckWords.AsNoTracking()
                           .Where(dw => dw.DeckId == id &&
                                        context.JmDictWordFrequencies
                                               .Any(f => f.WordId == dw.WordId &&
                                                         f.ReadingsFrequencyRank[dw.ReadingIndex] >= minFrequency &&
                                                         f.ReadingsFrequencyRank[dw.ReadingIndex] <= maxFrequency));

        var count = query.Count();

        return Results.Ok(count);
    }

    /// <summary>
    /// Gets decks from sliding 30-day windows based on offset for display in the update log
    /// </summary>
    /// <param name="offset">Window offset: 0 = last 30 days, 1 = days 30-60 ago, 2 = days 60-90 ago, etc.</param>
    /// <returns>Deck information for the specified 30-day window</returns>
    [HttpGet("media-update-log")]
    [ResponseCache(Duration = 60 * 10, VaryByQueryKeys = ["offset"])]
    [SwaggerOperation(Summary = "Get decks for update log")]
    [ProducesResponseType(typeof(PaginatedResponse<List<DeckDto>>), StatusCodes.Status200OK)]
    public async Task<PaginatedResponse<List<DeckDto>>> GetDecksForUpdateLog(int? offset = 0)
    {
        int offsetValue = offset ?? 0;
        var endDate = DateTimeOffset.UtcNow.AddDays(-30 * offsetValue);
        var startDate = endDate.AddDays(-30);

        var query = context.Decks.AsNoTracking()
                           .Where(d => d.ParentDeckId == null &&
                                       d.CreationDate >= startDate &&
                                       d.CreationDate < endDate)
                           .OrderByDescending(d => d.CreationDate);

        int totalCount = await query.CountAsync();

        var decks = await query.ToListAsync();

        var dtos = decks.Select(d => new DeckDto
                                     {
                                         DeckId = d.DeckId, CreationDate = d.CreationDate, OriginalTitle = d.OriginalTitle,
                                         RomajiTitle = d.RomajiTitle!, EnglishTitle = d.EnglishTitle!, MediaType = d.MediaType
                                     }).ToList();

        return new PaginatedResponse<List<DeckDto>>(dtos, totalCount, decks.Count, offsetValue);
    }

    /// <summary>
    /// Returns deck IDs that have a link of the specified type whose trailing URL segment matches the provided id.
    /// </summary>
    /// <param name="linkType">External link type.</param>
    /// <param name="id">Trailing identifier from the link URL.</param>
    [HttpGet("by-link-id/{linkType}/{id}")]
    [ResponseCache(Duration = 600, VaryByQueryKeys = ["id"])]
    [SwaggerOperation(Summary = "Find decks by external link id")]
    [ProducesResponseType(typeof(List<int>), StatusCodes.Status200OK)]
    public async Task<List<int>> GetMediaDeckIdsByLinkId(LinkType linkType, string id)
    {
        var links = await context.Decks
                                 .Include(d => d.Links)
                                 .Where(d => d.Links.Any(l => l.LinkType == linkType))
                                 .SelectMany(d => d.Links.Where(l => l.LinkType == linkType)
                                                   .Select(l => new { DeckId = d.DeckId, Url = l.Url }))
                                 .ToListAsync();

        var result = new List<int>();

        foreach (var link in links)
        {
            // Remove trailing slash if present
            var url = link.Url.TrimEnd('/');

            // Get the last part of the URL (after the last slash)
            var lastSlashIndex = url.LastIndexOf('/');
            if (lastSlashIndex == -1)
                continue;

            var urlId = url.Substring(lastSlashIndex + 1);

            // If the extracted ID matches the provided ID, add the deck ID to the result
            if (urlId.Equals(id, StringComparison.OrdinalIgnoreCase))
            {
                result.Add(link.DeckId);
            }
        }

        return result;
    }

    /// <summary>
    /// Returns all available tags for filtering (only tags with at least one associated media).
    /// </summary>
    [HttpGet("tags")]
    [ResponseCache(Duration = 3600)]
    [SwaggerOperation(Summary = "Get all tags")]
    [ProducesResponseType(typeof(List<TagDto>), StatusCodes.Status200OK)]
    public async Task<List<TagDto>> GetAllTags()
    {
        return await context.Tags
                            .AsNoTracking()
                            .Where(t => t.DeckTags.Any())
                            .OrderBy(t => t.Name)
                            .Select(t => new TagDto { TagId = t.TagId, Name = t.Name })
                            .ToListAsync();
    }

    /// <summary>
    /// Report an issue with a deck
    /// </summary>
    /// <param name="request">Issue type and comment.</param>
    /// <returns>Did the report get sent successfully.</returns>
    [HttpPost("report")]
    [EnableRateLimiting("download")]
    [SwaggerOperation(Summary = "Report an issue with a deck")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ReportIssue([FromBody] ReportIssueRequest request)
    {
        if (!currentUserService.IsAuthenticated)
            return BadRequest("You are not logged in");

        if (string.IsNullOrEmpty(request.IssueType) || string.IsNullOrEmpty(request.Comment) || request.IssueType.Length > 30 ||
            request.Comment.Length > 1000)
            return BadRequest();

        var deck = await context.Decks.FirstOrDefaultAsync(d => d.DeckId == request.DeckId);

        if (deck == null)
            return BadRequest("Deck not found");

        var safeComment = SanitizeForDiscord(request.Comment);
        var safeIssueType = SanitizeForDiscord(request.IssueType);

        var discordPayload = new
                             {
                                 content = $"A new report from user ID `{currentUserService.UserId}` came in.\n", tts = false, embeds =
                                     new[]
                                     {
                                         new
                                         {
                                             id = 652627557, title = safeIssueType, description =
                                                 $"[{deck.OriginalTitle}](https://jiten.moe/decks/media/{deck.DeckId}/detail)\n\nComment:\n{safeComment}",
                                             color = 8266731, fields = Array.Empty<object>()
                                         }
                                     },
                                 components = Array.Empty<object>(), actions = new { }, flags = 0, username = "IssueReporter"
                             };
        var embedJson = JsonSerializer.Serialize(discordPayload);
        var webhook = configuration["DiscordWebhook"];
        using var httpClient = new HttpClient();
        var content = new StringContent(embedJson, Encoding.UTF8, "application/json");
        var result = await httpClient.PostAsync(webhook, content);

        if (result.IsSuccessStatusCode)
        {
            logger.LogInformation("User reported deck issue: DeckId={DeckId}, IssueType={IssueType}",
                                  request.DeckId, request.IssueType);
            return Ok();
        }

        logger.LogWarning("Failed to send deck issue report: DeckId={DeckId}, IssueType={IssueType}",
                          request.DeckId, request.IssueType);
        return BadRequest("Failed to send report");

        string SanitizeForDiscord(string input)
        {
            if (string.IsNullOrEmpty(input))
                return "";

            // Detect URLs (http/https)
            var urlRegex = new Regex(@"(https?:\/\/[^\s)]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

            var sb = new StringBuilder();
            int lastIndex = 0;

            foreach (Match match in urlRegex.Matches(input))
            {
                // Escape text before the URL
                if (match.Index > lastIndex)
                {
                    var textPart = input.Substring(lastIndex, match.Index - lastIndex);
                    sb.Append(EscapeMarkdown(textPart));
                }

                // Add URL unescaped
                sb.Append(match.Value);

                lastIndex = match.Index + match.Length;
            }

            // Escape any remaining text after last URL
            if (lastIndex < input.Length)
            {
                sb.Append(EscapeMarkdown(input.Substring(lastIndex)));
            }

            return sb.ToString();
        }

        string EscapeMarkdown(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            var sb = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                // Escape markdown/meta characters but leave slashes and colons for URLs
                if (c is '*' or '_' or '~' or '`' or '>' or '|' or '[' or ']' or '(' or ')' or '@' or '#' or ':' or '"')
                {
                    sb.Append('\\');
                }

                sb.Append(c);
            }

            return sb.ToString();
        }
    }

    /// <summary>
    /// Get advanced stats for a deck such as coverage
    /// </summary>
    /// <param name="id">Deck ID</param>
    /// <returns>Advanced stats</returns>
    [HttpGet("{id}/stats")]
    [ResponseCache(Duration = 3600)]
    [SwaggerOperation(Summary = "Get advanced stats for a deck",
                      Description =
                          "Returns advanced deck statistics such as parametric coverage showing how many of the most frequent words are needed for various coverage percentages")]
    [ProducesResponseType(typeof(DeckStatsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DeckStatsDto>> GetCoverageStats(int id)
    {
        var deckStats = await context.DeckStats
                                     .AsNoTracking()
                                     .FirstOrDefaultAsync(ds => ds.DeckId == id);

        if (deckStats == null)
        {
            return NotFound(new { message = "Missing deck stats" });
        }

        var milestones = deckStats.GetMilestones();

        return Ok(new DeckStatsDto()
                  {
                      DeckId = id, TotalUniqueWords = deckStats.TotalUniqueWords ?? 0, ComputedAt = deckStats.ComputedAt,
                      RSquared = deckStats.RSquared ?? 0,
                      Milestones = new Dictionary<string, int>
                                   {
                                       { "80%", milestones.TryGetValue(80, out var v80) ? v80 : 0 },
                                       { "85%", milestones.TryGetValue(85, out var v85) ? v85 : 0 },
                                       { "90%", milestones.TryGetValue(90, out var v90) ? v90 : 0 },
                                       { "95%", milestones.TryGetValue(95, out var v95) ? v95 : 0 },
                                       { "98%", milestones.TryGetValue(98, out var v98) ? v98 : 0 },
                                       { "99%", milestones.TryGetValue(99, out var v99) ? v99 : 0 }
                                   }
                  });
    }

    /// <summary>
    /// Get full coverage curve data for charting
    /// </summary>
    /// <param name="id">Deck ID</param>
    /// <param name="points">Number of data points (ignored if sampled data exists)</param>
    /// <returns>List of (rank, coverage) pairs - sampled at 1% intervals (0-99%), 0.1% intervals (99-100%)</returns>
    [HttpGet("{id}/coverage-curve")]
    [ResponseCache(Duration = 3600)]
    [SwaggerOperation(Summary = "Get full coverage curve for charting",
                      Description = "Returns sampled coverage data points for interactive visualisation (~108 points)")]
    [ProducesResponseType(typeof(List<CurveDatumDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<CurveDatumDto>>> GetCoverageCurve(int id, [FromQuery] int points = 50)
    {
        var deckStats = await context.DeckStats
                                     .AsNoTracking()
                                     .FirstOrDefaultAsync(ds => ds.DeckId == id);

        if (deckStats == null)
        {
            return NotFound(new { message = "Coverage statistics not yet computed for this deck" });
        }

        // If sampled data exists, 'points' parameter is ignored
        var curvePoints = deckStats.GenerateCurvePoints(points);

        return Ok(curvePoints.Select(p => new CurveDatumDto
                                          {
                                              Rank = p.rank,
                                              // Round to whole number before 99%, keep 2 decimals at 99%+
                                              Coverage = p.coverage < 99.0 ? Math.Round(p.coverage, 0) : Math.Round(p.coverage, 2)
                                          }).ToList());
    }
}