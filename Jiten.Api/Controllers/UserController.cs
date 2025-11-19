using Hangfire;
using Jiten.Api.Dtos.Requests;
using Jiten.Api.Jobs;
using Jiten.Api.Services;
using Jiten.Core;
using Jiten.Core.Data.FSRS;
using Jiten.Core.Data.User;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Jiten.Api.Controllers;

[ApiController]
[Route("api/user")]
[ApiExplorerSettings(IgnoreApi = true)]
[Authorize]
public class UserController(
    ICurrentUserService userService,
    JitenDbContext jitenContext,
    IDbContextFactory<JitenDbContext> contextFactory,
    UserDbContext userContext,
    IBackgroundJobClient backgroundJobs,
    ILogger<UserController> logger) : ControllerBase
{
    /// <summary>
    /// Get all known JMdict word IDs for the current user.
    /// </summary>
    [HttpGet("vocabulary/known-ids/amount")]
    public async Task<IResult> GetKnownWordAmount()
    {
        var userId = userService.UserId;
        if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

        var uniqueWordCount = await userContext.FsrsCards
                                               .AsNoTracking()
                                               .Where(uk => uk.UserId == userId)
                                               .Select(uk => uk.WordId)
                                               .Distinct()
                                               .CountAsync();

        var totalFormsCount = await userContext.FsrsCards
                                               .AsNoTracking()
                                               .Where(uk => uk.UserId == userId)
                                               .Select(uk => new { uk.WordId, uk.ReadingIndex })
                                               .Distinct()
                                               .CountAsync();

        return Results.Ok(new { words = uniqueWordCount, forms = totalFormsCount });
    }

    /// <summary>
    /// Get all known JMdict word IDs for the current user.
    /// </summary>
    [HttpGet("vocabulary/known-ids")]
    public async Task<IResult> GetKnownWordIds()
    {
        var userId = userService.UserId;
        if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

        var ids = await userContext.FsrsCards
                                   .AsNoTracking()
                                   .Where(uk => uk.UserId == userId)
                                   .Select(uk => uk.WordId)
                                   .Distinct()
                                   .ToListAsync();

        return Results.Ok(ids);
    }

    /// <summary>
    /// Remove all known words for the current user.
    /// </summary>
    [HttpDelete("vocabulary/known-ids/clear")]
    public async Task<IResult> ClearKnownWordIds()
    {
        var userId = userService.UserId;
        if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

        var entries = await userContext.FsrsCards
                                       .Where(uk => uk.UserId == userId)
                                       .ToListAsync();
        if (entries.Count == 0) return Results.Ok(new { removed = 0 });

        userContext.FsrsCards.RemoveRange(entries);
        await userContext.SaveChangesAsync();

        backgroundJobs.Enqueue<ComputationJob>(job => job.ComputeUserCoverage(userId));

        logger.LogInformation("User cleared all known words: UserId={UserId}, RemovedCount={RemovedCount}", userId, entries.Count);
        return Results.Ok(new { removed = entries.Count });
    }

    /// <summary>
    /// Add known words for the current user by JMdict word IDs. ReadingIndex defaults to 0.
    /// </summary>
    [HttpPost("vocabulary/import-from-ids")]
    public async Task<IResult> ImportWordsFromIds([FromBody] List<long> wordIds)
    {
        var userId = userService.UserId;
        if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

        if (wordIds == null || wordIds.Count == 0) return Results.BadRequest("No word IDs provided");

        var distinctIds = wordIds.Where(id => id > 0).Distinct().ToList();
        if (distinctIds.Count == 0) return Results.BadRequest("No valid words found");

        var jmdictWords = await jitenContext.JMDictWords
                                            .AsNoTracking()
                                            .Where(w => distinctIds.Contains(w.WordId))
                                            .ToListAsync();

        if (jmdictWords.Count == 0) return Results.BadRequest("Invalid words provided");

        var jmdictWordIds = jmdictWords.Select(w => w.WordId).ToList();

        var alreadyKnown = await userContext.FsrsCards
                                            .AsNoTracking()
                                            .Where(uk => uk.UserId == userId && jmdictWordIds.Contains(uk.WordId))
                                            .ToListAsync();

        List<FsrsCard> toInsert = new();

        foreach (var word in jmdictWords)
        {
            for (var i = 0; i < word.Readings.Count; i++)
            {
                if (alreadyKnown.Any(uk => uk.WordId == word.WordId && uk.ReadingIndex == i))
                    continue;

                toInsert.Add(new FsrsCard(userId, word.WordId, (byte)i, due: DateTime.UtcNow.AddYears(100), lastReview: DateTime.UtcNow,
                                          state: FsrsState.Review));
            }
        }

        if (toInsert.Count > 0)
        {
            await userContext.FsrsCards.AddRangeAsync(toInsert);
            await userContext.SaveChangesAsync();
        }

        backgroundJobs.Enqueue<ComputationJob>(job => job.ComputeUserCoverage(userId));

        logger.LogInformation("User imported words from IDs: UserId={UserId}, AddedCount={AddedCount}, SkippedCount={SkippedCount}",
            userId, toInsert.Count, alreadyKnown.Count);
        return Results.Ok(new { added = toInsert.Count, skipped = alreadyKnown.Count });
    }

    /// <summary>
    /// Parse an Anki-exported TXT file and add all parsed words as known for the current user.
    /// </summary>
    [HttpPost("vocabulary/import-from-anki-txt")]
    [Consumes("multipart/form-data")]
    public async Task<IResult> AddKnownFromAnkiTxt(IFormFile? file)
    {
        var userId = userService.UserId;
        if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

        if (file == null || file.Length == 0 || file.Length > 10 * 1024 * 1024)
            return Results.BadRequest("File is empty, too big or not provided");

        using var reader = new StreamReader(file.OpenReadStream());
        var lineCount = 0;
        var validWords = new List<string>();

        while (await reader.ReadLineAsync() is { } line)
        {
            lineCount++;
            if (lineCount > 50000)
                return Results.BadRequest("File has more than 50,000 lines");
            if (line.StartsWith("#"))
                continue;

            var tabIndex = line.IndexOf('\t');
            if (tabIndex <= 0)
                tabIndex = line.IndexOf(',');
            if (tabIndex <= 0)
                tabIndex = line.Length;

            var word = line.Substring(0, tabIndex);
            if (word.Length <= 25)
                validWords.Add(word);
        }

        if (validWords.Count == 0)
            return Results.BadRequest("No valid words found in file");

        var combinedText = string.Join(Environment.NewLine, validWords);
        var parsedWords = await Parser.Parser.ParseText(contextFactory, combinedText);
        var added = await userService.AddKnownWords(parsedWords);

        backgroundJobs.Enqueue<ComputationJob>(job => job.ComputeUserCoverage(userId));

        logger.LogInformation("User imported words from Anki TXT: UserId={UserId}, ParsedCount={ParsedCount}, AddedCount={AddedCount}",
            userId, parsedWords.Count, added);
        return Results.Ok(new { parsed = parsedWords.Count, added });
    }

    /// <summary>
    /// Add a single word for the current user.
    /// </summary>
    /// <returns></returns>
    [HttpPost("vocabulary/add/{wordId}/{readingIndex}")]
    public async Task<IResult> AddKnownWord(int wordId, byte readingIndex)
    {
        var userId = userService.UserId;
        if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

        await userService.AddKnownWord(wordId, readingIndex);
        return Results.Ok();
    }

    /// <summary>
    /// Remove a single word for the current user.
    /// </summary>
    /// <returns></returns>
    [HttpPost("vocabulary/remove/{wordId}/{readingIndex}")]
    public async Task<IResult> RemoveKnownWord(int wordId, byte readingIndex)
    {
        var userId = userService.UserId;
        if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

        await userService.RemoveKnownWord(wordId, readingIndex);
        return Results.Ok();
    }

    /// <summary>
    /// Add known words for the current user by frequency rank range (inclusive).
    /// For each JMdict word in the range, all its readings are added as Known if not already present.
    /// </summary>
    [HttpPost("vocabulary/import-from-frequency/{minFrequency:int}/{maxFrequency:int}")]
    public async Task<IResult> ImportWordsFromFrequency(int minFrequency, int maxFrequency)
    {
        var userId = userService.UserId;
        if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

        if (minFrequency < 0 || maxFrequency < minFrequency || maxFrequency > 10000)
            return Results.BadRequest("Invalid frequency range");

        // Fetch candidate word IDs by frequency range
        var wordIds = await jitenContext.JmDictWordFrequencies
                                        .AsNoTracking()
                                        .Where(f => f.FrequencyRank >= minFrequency && f.FrequencyRank <= maxFrequency)
                                        .OrderBy(f => f.FrequencyRank)
                                        .Select(f => f.WordId)
                                        .Distinct()
                                        .ToListAsync();

        if (wordIds.Count == 0)
            return Results.BadRequest("No words found for the requested frequency range");

        // Load JMdict words for the selected IDs
        var jmdictWords = await jitenContext.JMDictWords
                                            .AsNoTracking()
                                            .Where(w => wordIds.Contains(w.WordId))
                                            .ToListAsync();

        if (jmdictWords.Count == 0)
            return Results.BadRequest("No valid JMdict words found for the requested frequency range");

        var jmdictWordIds = jmdictWords.Select(w => w.WordId).ToList();

        // Determine which entries are already known (by word + reading index)
        var alreadyKnown = await userContext.FsrsCards
                                            .AsNoTracking()
                                            .Where(uk => uk.UserId == userId && jmdictWordIds.Contains(uk.WordId))
                                            .ToListAsync();

        List<FsrsCard> toInsert = new();

        foreach (var word in jmdictWords)
        {
            for (var i = 0; i < word.Readings.Count; i++)
            {
                if (alreadyKnown.Any(uk => uk.WordId == word.WordId && uk.ReadingIndex == i))
                    continue;

                toInsert.Add(new FsrsCard(userId, word.WordId, (byte)i, due: DateTime.UtcNow.AddYears(100), lastReview: DateTime.UtcNow,
                                          state: FsrsState.Review));
            }
        }

        if (toInsert.Count > 0)
        {
            await userContext.FsrsCards.AddRangeAsync(toInsert);
            await userContext.SaveChangesAsync();
        }

        backgroundJobs.Enqueue<ComputationJob>(job => job.ComputeUserCoverage(userId));

        logger.LogInformation("User imported words from frequency range: UserId={UserId}, MinFreq={MinFrequency}, MaxFreq={MaxFrequency}, WordCount={WordCount}, FormCount={FormCount}",
            userId, minFrequency, maxFrequency, jmdictWords.Count, toInsert.Count);
        return Results.Ok(new { words = jmdictWords.Count, forms = toInsert.Count });
    }


    /// <summary>
    /// Get user metadata
    /// </summary>
    [HttpGet("metadata")]
    public async Task<IResult> GetMetadata()
    {
        var userId = userService.UserId;
        if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

        var metadata = userContext.UserMetadatas.SingleOrDefault(m => m.UserId == userId);
        if (metadata == null)
            return Results.Ok(new UserMetadata());

        return Results.Ok(metadata);
    }

    /// <summary>
    /// Queue a coverage refresh
    /// </summary>
    [HttpPost("coverage/refresh")]
    public async Task<IResult> RefreshCoverage()
    {
        var userId = userService.UserId;
        if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

        backgroundJobs.Enqueue<ComputationJob>(job => job.ComputeUserCoverage(userId));

        return Results.Ok();
    }

    /// <summary>
    /// Get deck preference for a specific deck
    /// </summary>
    [HttpGet("deck-preferences/{deckId}")]
    public async Task<IResult> GetDeckPreference(int deckId)
    {
        var userId = userService.UserId;
        if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

        var preference = await userContext.UserDeckPreferences
                                          .AsNoTracking()
                                          .FirstOrDefaultAsync(p => p.UserId == userId && p.DeckId == deckId);

        if (preference == null)
            return Results.Ok(new
            {
                deckId,
                status = DeckStatus.None,
                isFavourite = false,
                isIgnored = false
            });

        return Results.Ok(new
        {
            preference.DeckId,
            preference.Status,
            preference.IsFavourite,
            preference.IsIgnored
        });
    }

    /// <summary>
    /// Set favourite status for a deck
    /// </summary>
    [HttpPost("deck-preferences/{deckId}/favourite")]
    public async Task<IResult> SetFavourite(int deckId, [FromBody] SetFavouriteRequest request)
    {
        var userId = userService.UserId;
        if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

        var preference = await userContext.UserDeckPreferences
                                          .FirstOrDefaultAsync(p => p.UserId == userId && p.DeckId == deckId);

        if (preference == null)
        {
            preference = new UserDeckPreference { UserId = userId, DeckId = deckId };
            userContext.UserDeckPreferences.Add(preference);
        }

        if (request.IsFavourite && preference.IsIgnored)
            return Results.BadRequest("A deck cannot be both favourited and ignored");

        preference.IsFavourite = request.IsFavourite;
        await userContext.SaveChangesAsync();

        return Results.Ok(new
        {
            preference.DeckId,
            preference.Status,
            preference.IsFavourite,
            preference.IsIgnored
        });
    }

    /// <summary>
    /// Set ignore status for a deck
    /// </summary>
    [HttpPost("deck-preferences/{deckId}/ignore")]
    public async Task<IResult> SetIgnore(int deckId, [FromBody] SetIgnoreRequest request)
    {
        var userId = userService.UserId;
        if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

        var preference = await userContext.UserDeckPreferences
                                          .FirstOrDefaultAsync(p => p.UserId == userId && p.DeckId == deckId);

        if (preference == null)
        {
            preference = new UserDeckPreference { UserId = userId, DeckId = deckId };
            userContext.UserDeckPreferences.Add(preference);
        }

        if (request.IsIgnored && preference.IsFavourite)
            return Results.BadRequest("A deck cannot be both favourited and ignored");

        preference.IsIgnored = request.IsIgnored;
        await userContext.SaveChangesAsync();

        return Results.Ok(new
        {
            preference.DeckId,
            preference.Status,
            preference.IsFavourite,
            preference.IsIgnored
        });
    }

    /// <summary>
    /// Set status for a deck
    /// </summary>
    [HttpPost("deck-preferences/{deckId}/status")]
    public async Task<IResult> SetDeckStatus(int deckId, [FromBody] SetDeckStatusRequest request)
    {
        var userId = userService.UserId;
        if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

        var preference = await userContext.UserDeckPreferences
                                          .FirstOrDefaultAsync(p => p.UserId == userId && p.DeckId == deckId);

        if (preference == null)
        {
            preference = new UserDeckPreference { UserId = userId, DeckId = deckId };
            userContext.UserDeckPreferences.Add(preference);
        }

        preference.Status = request.Status;
        await userContext.SaveChangesAsync();

        return Results.Ok(new
        {
            preference.DeckId,
            preference.Status,
            preference.IsFavourite,
            preference.IsIgnored
        });
    }

    /// <summary>
    /// Delete all preferences for a deck
    /// </summary>
    [HttpDelete("deck-preferences/{deckId}")]
    public async Task<IResult> DeleteDeckPreference(int deckId)
    {
        var userId = userService.UserId;
        if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

        var preference = await userContext.UserDeckPreferences
                                          .FirstOrDefaultAsync(p => p.UserId == userId && p.DeckId == deckId);

        if (preference == null)
            return Results.Ok(new { deleted = false });

        userContext.UserDeckPreferences.Remove(preference);
        await userContext.SaveChangesAsync();

        return Results.Ok(new { deleted = true });
    }
}