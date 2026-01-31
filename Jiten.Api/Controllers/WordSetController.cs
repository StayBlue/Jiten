using Hangfire;
using Jiten.Api.Dtos;
using Jiten.Api.Dtos.Requests;
using Jiten.Api.Enums;
using Jiten.Api.Helpers;
using Jiten.Api.Jobs;
using Jiten.Api.Services;
using Jiten.Core;
using Jiten.Core.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Swashbuckle.AspNetCore.Annotations;

namespace Jiten.Api.Controllers;

[ApiController]
[Route("api/word-sets")]
[Produces("application/json")]
public class WordSetController(
    JitenDbContext jitenContext,
    UserDbContext userContext,
    ICurrentUserService currentUserService,
    IBackgroundJobClient backgroundJobs,
    ILogger<WordSetController> logger) : ControllerBase
{
    [HttpGet]
    [SwaggerOperation(Summary = "Get all word sets", Description = "Returns all available word sets with word and form counts.")]
    [ProducesResponseType(typeof(List<WordSetDto>), StatusCodes.Status200OK)]
    public async Task<IResult> GetWordSets()
    {
        var sets = await jitenContext.WordSets
            .AsNoTracking()
            .OrderBy(ws => ws.SetId)
            .Select(ws => new WordSetDto
            {
                SetId = ws.SetId,
                Slug = ws.Slug,
                Name = ws.Name,
                Description = ws.Description,
                WordCount = ws.WordCount,
                FormCount = ws.Members.Count
            })
            .ToListAsync();

        return Results.Ok(sets);
    }

    [HttpGet("{slug}")]
    [SwaggerOperation(Summary = "Get word set by slug", Description = "Returns a single word set by its URL-friendly slug.")]
    [ProducesResponseType(typeof(WordSetDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IResult> GetWordSet(string slug)
    {
        var set = await jitenContext.WordSets
            .AsNoTracking()
            .Where(ws => ws.Slug == slug)
            .Select(ws => new WordSetDto
            {
                SetId = ws.SetId,
                Slug = ws.Slug,
                Name = ws.Name,
                Description = ws.Description,
                WordCount = ws.WordCount,
                FormCount = ws.Members.Count
            })
            .FirstOrDefaultAsync();

        if (set == null)
            return Results.NotFound("Word set not found");

        return Results.Ok(set);
    }

    [HttpGet("{slug}/vocabulary")]
    [SwaggerOperation(Summary = "Get word set vocabulary", Description = "Returns paginated vocabulary list for a word set with sorting and filtering.")]
    [ProducesResponseType(typeof(PaginatedResponse<List<WordDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IResult> GetWordSetVocabulary(
        string slug,
        [FromQuery] int offset = 0,
        [FromQuery] int limit = 50,
        [FromQuery] string sortBy = "",
        [FromQuery] SortOrder sortOrder = SortOrder.Ascending,
        [FromQuery] string displayFilter = "all")
    {
        limit = Math.Clamp(limit, 1, 100);

        var set = await jitenContext.WordSets
            .AsNoTracking()
            .FirstOrDefaultAsync(ws => ws.Slug == slug);

        if (set == null)
            return Results.NotFound("Word set not found");

        var baseQuery = jitenContext.WordSetMembers
            .AsNoTracking()
            .Where(wsm => wsm.SetId == set.SetId);

        bool needsKnownFilter = currentUserService.IsAuthenticated
            && !string.IsNullOrEmpty(displayFilter)
            && displayFilter != "all";

        List<WordSetMember> pagedItems;
        int totalCount;

        if (needsKnownFilter)
        {
            (pagedItems, totalCount) = await ExecuteFilteredVocabularyQuery(
                set.SetId, currentUserService.UserId!, displayFilter, sortBy, sortOrder, offset, limit);
        }
        else if (sortBy == "globalFreq")
        {
            totalCount = await baseQuery.CountAsync();

            var sorted = sortOrder == SortOrder.Ascending
                ? baseQuery.OrderBy(m => jitenContext.JmDictWordFrequencies
                    .Where(f => f.WordId == m.WordId)
                    .Select(f => f.ReadingsFrequencyRank[m.ReadingIndex])
                    .FirstOrDefault()).ThenBy(m => m.Position)
                : baseQuery.OrderByDescending(m => jitenContext.JmDictWordFrequencies
                    .Where(f => f.WordId == m.WordId)
                    .Select(f => f.ReadingsFrequencyRank[m.ReadingIndex])
                    .FirstOrDefault()).ThenBy(m => m.Position);

            pagedItems = await sorted.Skip(offset).Take(limit).ToListAsync();
        }
        else
        {
            totalCount = await baseQuery.CountAsync();

            var sorted = sortOrder == SortOrder.Ascending
                ? baseQuery.OrderBy(m => m.Position)
                : baseQuery.OrderByDescending(m => m.Position);

            pagedItems = await sorted.Skip(offset).Take(limit).ToListAsync();
        }

        var pagedWordIds = pagedItems.Select(p => p.WordId).Distinct().ToList();

        var frequencies = await jitenContext.JmDictWordFrequencies
            .AsNoTracking()
            .Where(f => pagedWordIds.Contains(f.WordId))
            .ToDictionaryAsync(f => f.WordId);

        var words = await jitenContext.JMDictWords
            .AsNoTracking()
            .Include(w => w.Definitions)
            .Where(w => pagedWordIds.Contains(w.WordId))
            .ToDictionaryAsync(w => w.WordId);

        var knownWordStates = await currentUserService.GetKnownWordsState(
            pagedItems.Select(p => (p.WordId, (byte)p.ReadingIndex)));

        var vocabulary = pagedItems
            .Where(p => words.ContainsKey(p.WordId))
            .Select(p =>
            {
                var word = words[p.WordId];
                var freq = frequencies.GetValueOrDefault(p.WordId);
                var readingIndex = (byte)p.ReadingIndex;

                var mainReading = new ReadingDto
                {
                    Text = word.ReadingsFurigana.ElementAtOrDefault(readingIndex) ?? word.Readings.ElementAtOrDefault(readingIndex) ?? "",
                    ReadingIndex = readingIndex,
                    ReadingType = word.ReadingTypes.ElementAtOrDefault(readingIndex),
                    FrequencyRank = freq is not null && readingIndex < freq.ReadingsFrequencyRank.Count ? freq.ReadingsFrequencyRank[readingIndex] : 0,
                    FrequencyPercentage = freq is not null && readingIndex < freq.ReadingsFrequencyPercentage.Count ? freq.ReadingsFrequencyPercentage[readingIndex] : 0,
                    UsedInMediaAmount = freq is not null && readingIndex < freq.ReadingsUsedInMediaAmount.Count ? freq.ReadingsUsedInMediaAmount[readingIndex] : 0
                };

                return new WordDto
                {
                    WordId = p.WordId,
                    MainReading = mainReading,
                    AlternativeReadings = [],
                    Definitions = word.Definitions.ToDefinitionDtos(),
                    PartsOfSpeech = word.PartsOfSpeech,
                    PitchAccents = word.PitchAccents,
                    KnownStates = knownWordStates.GetValueOrDefault((p.WordId, readingIndex), [KnownState.New])
                };
            })
            .ToList();

        logger.LogInformation("GetWordSetVocabulary: Slug={Slug}, Offset={Offset}, Limit={Limit}, SortBy={SortBy}, DisplayFilter={DisplayFilter}, ResultCount={ResultCount}",
                              slug, offset, limit, sortBy, displayFilter, vocabulary.Count);

        return Results.Ok(new PaginatedResponse<List<WordDto>>(vocabulary, totalCount, limit, offset));
    }

    [HttpGet("subscriptions")]
    [Authorize]
    [SwaggerOperation(Summary = "Get user's word set subscriptions", Description = "Returns the current user's word set subscriptions with state.")]
    [ProducesResponseType(typeof(List<UserWordSetSubscriptionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IResult> GetSubscriptions()
    {
        var userId = currentUserService.UserId;
        if (string.IsNullOrEmpty(userId))
            return Results.Unauthorized();

        var userStates = await userContext.UserWordSetStates
            .AsNoTracking()
            .Where(uwss => uwss.UserId == userId)
            .ToListAsync();

        if (userStates.Count == 0)
            return Results.Ok(new List<UserWordSetSubscriptionDto>());

        var setIds = userStates.Select(s => s.SetId).ToList();

        var sets = await jitenContext.WordSets
            .AsNoTracking()
            .Where(ws => setIds.Contains(ws.SetId))
            .Select(ws => new { ws.SetId, ws.Slug, ws.Name, ws.Description, ws.WordCount, FormCount = ws.Members.Count })
            .ToDictionaryAsync(ws => ws.SetId);

        var subscriptions = userStates
            .Where(s => sets.ContainsKey(s.SetId))
            .Select(s =>
            {
                var ws = sets[s.SetId];
                return new UserWordSetSubscriptionDto
                {
                    SetId = ws.SetId,
                    Slug = ws.Slug,
                    Name = ws.Name,
                    Description = ws.Description,
                    State = s.State,
                    WordCount = ws.WordCount,
                    FormCount = ws.FormCount,
                    SubscribedAt = s.CreatedAt
                };
            })
            .ToList();

        return Results.Ok(subscriptions);
    }

    [HttpPost("{setId:int}/subscribe")]
    [Authorize]
    [SwaggerOperation(Summary = "Subscribe to a word set", Description = "Subscribe to a word set with the specified state (Blacklisted or Mastered).")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IResult> Subscribe(int setId, [FromBody] WordSetSubscribeRequest request)
    {
        var userId = currentUserService.UserId;
        if (string.IsNullOrEmpty(userId))
            return Results.Unauthorized();

        if (request.State != WordSetStateType.Blacklisted && request.State != WordSetStateType.Mastered)
            return Results.BadRequest("Invalid state. Must be Blacklisted (1) or Mastered (2).");

        var set = await jitenContext.WordSets.AsNoTracking().FirstOrDefaultAsync(ws => ws.SetId == setId);
        if (set == null)
            return Results.NotFound("Word set not found");

        var existing = await userContext.UserWordSetStates
            .FirstOrDefaultAsync(uwss => uwss.UserId == userId && uwss.SetId == setId);

        if (existing != null)
        {
            existing.State = request.State;
        }
        else
        {
            userContext.UserWordSetStates.Add(new UserWordSetState
            {
                UserId = userId,
                SetId = setId,
                State = request.State
            });
        }

        await CoverageDirtyHelper.MarkCoverageDirty(userContext, userId);
        await userContext.SaveChangesAsync();

        backgroundJobs.Enqueue<ComputationJob>(job => job.ComputeUserCoverage(userId));

        logger.LogInformation("User subscribed to word set: UserId={UserId}, SetId={SetId}, State={State}",
                              userId, setId, request.State);

        return Results.Ok(new { success = true });
    }

    [HttpDelete("{setId:int}/subscribe")]
    [Authorize]
    [SwaggerOperation(Summary = "Unsubscribe from a word set", Description = "Remove subscription from a word set.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IResult> Unsubscribe(int setId)
    {
        var userId = currentUserService.UserId;
        if (string.IsNullOrEmpty(userId))
            return Results.Unauthorized();

        var subscription = await userContext.UserWordSetStates
            .FirstOrDefaultAsync(uwss => uwss.UserId == userId && uwss.SetId == setId);

        if (subscription == null)
            return Results.NotFound("Subscription not found");

        userContext.UserWordSetStates.Remove(subscription);
        await CoverageDirtyHelper.MarkCoverageDirty(userContext, userId);
        await userContext.SaveChangesAsync();

        backgroundJobs.Enqueue<ComputationJob>(job => job.ComputeUserCoverage(userId));

        logger.LogInformation("User unsubscribed from word set: UserId={UserId}, SetId={SetId}",
                              userId, setId);

        return Results.Ok(new { success = true });
    }

    private class FilteredMemberResult
    {
        public int WordId { get; set; }
        public short ReadingIndex { get; set; }
        public int Position { get; set; }
        public long TotalCount { get; set; }
    }

    private async Task<(List<WordSetMember> Items, int TotalCount)> ExecuteFilteredVocabularyQuery(
        int setId, string userId, string displayFilter, string sortBy, SortOrder sortOrder, int offset, int limit)
    {
        var userIdGuid = Guid.Parse(userId);

        string filterClause = displayFilter switch
        {
            "known" =>
                @"(f.""WordId"" IS NOT NULL AND f.""State"" != 0) OR " +
                @"(f.""WordId"" IS NULL AND (COALESCE(use_s.has_mastered, FALSE) OR COALESCE(use_s.has_blacklisted, FALSE)))",
            "unknown" =>
                @"(f.""WordId"" IS NULL AND NOT COALESCE(use_s.has_mastered, FALSE) AND NOT COALESCE(use_s.has_blacklisted, FALSE)) OR " +
                @"(f.""WordId"" IS NOT NULL AND f.""State"" = 0)",
            "young" =>
                @"f.""WordId"" IS NOT NULL AND f.""State"" IN (1,2,3) AND f.""LastReview"" IS NOT NULL AND " +
                @"(EXTRACT(EPOCH FROM (f.""Due"" - f.""LastReview"")) / 86400.0) < 21",
            "mature" =>
                @"f.""WordId"" IS NOT NULL AND f.""State"" IN (1,2,3) AND f.""LastReview"" IS NOT NULL AND " +
                @"(EXTRACT(EPOCH FROM (f.""Due"" - f.""LastReview"")) / 86400.0) >= 21",
            "mastered" =>
                @"(f.""WordId"" IS NOT NULL AND f.""State"" = 5) OR " +
                @"(f.""WordId"" IS NULL AND COALESCE(use_s.has_mastered, FALSE))",
            "blacklisted" =>
                @"(f.""WordId"" IS NOT NULL AND f.""State"" = 4) OR " +
                @"(f.""WordId"" IS NULL AND COALESCE(use_s.has_blacklisted, FALSE))",
            _ => "TRUE"
        };

        string freqJoin = sortBy == "globalFreq"
            ? @"LEFT JOIN jmdict.""WordFrequencies"" freq ON m.""WordId"" = freq.""WordId"""
            : "";

        string orderByClause = (sortBy, sortOrder) switch
        {
            ("globalFreq", SortOrder.Ascending) =>
                @"COALESCE(freq.""ReadingsFrequencyRank""[m.""ReadingIndex"" + 1], 2147483647) ASC, m.""Position"" ASC",
            ("globalFreq", _) =>
                @"COALESCE(freq.""ReadingsFrequencyRank""[m.""ReadingIndex"" + 1], 2147483647) DESC, m.""Position"" ASC",
            (_, SortOrder.Ascending) => @"m.""Position"" ASC",
            _ => @"m.""Position"" DESC"
        };

        string sql =
            @"WITH user_fsrs AS (
                SELECT ""WordId"", ""ReadingIndex"", ""State"", ""Due"", ""LastReview""
                FROM ""user"".""FsrsCards""
                WHERE ""UserId"" = {0}
                  AND ""WordId"" IN (SELECT ""WordId"" FROM jiten.""WordSetMembers"" WHERE ""SetId"" = {1})
            ),
            user_set_effective AS (
                SELECT wsm.""WordId"", wsm.""ReadingIndex"",
                       BOOL_OR(uwss.""State"" = 2) AS has_mastered,
                       BOOL_OR(uwss.""State"" = 1) AS has_blacklisted
                FROM ""user"".""UserWordSetStates"" uwss
                INNER JOIN jiten.""WordSetMembers"" wsm ON wsm.""SetId"" = uwss.""SetId""
                WHERE uwss.""UserId"" = {0}
                  AND wsm.""WordId"" IN (SELECT ""WordId"" FROM jiten.""WordSetMembers"" WHERE ""SetId"" = {1})
                GROUP BY wsm.""WordId"", wsm.""ReadingIndex""
            )
            SELECT m.""WordId"", m.""ReadingIndex"", m.""Position"", COUNT(*) OVER() AS ""TotalCount""
            FROM jiten.""WordSetMembers"" m
            LEFT JOIN user_fsrs f ON m.""WordId"" = f.""WordId"" AND m.""ReadingIndex"" = f.""ReadingIndex""
            LEFT JOIN user_set_effective use_s ON m.""WordId"" = use_s.""WordId"" AND m.""ReadingIndex"" = use_s.""ReadingIndex""
            " + freqJoin + @"
            WHERE m.""SetId"" = {1} AND (" + filterClause + @")
            ORDER BY " + orderByClause + @"
            OFFSET {2} LIMIT {3}";

        var results = await jitenContext.Database
            .SqlQueryRaw<FilteredMemberResult>(sql, userIdGuid, setId, offset, limit)
            .ToListAsync();

        int totalCount = (int)(results.FirstOrDefault()?.TotalCount ?? 0);

        var items = results.Select(r => new WordSetMember
        {
            SetId = setId,
            WordId = r.WordId,
            ReadingIndex = r.ReadingIndex,
            Position = r.Position
        }).ToList();

        return (items, totalCount);
    }
}
