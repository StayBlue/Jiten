using Hangfire;
using Jiten.Api.Dtos;
using Jiten.Api.Dtos.Requests;
using Jiten.Api.Helpers;
using Jiten.Api.Jobs;
using Jiten.Api.Services;
using Jiten.Core;
using Jiten.Core.Data.FSRS;
using Jiten.Core.Data.JMDict;
using Jiten.Core.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using System.Text;
using System.Text.Json;
using WanaKanaShaapu;

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
    ISrsService srsService,
    IConfiguration configuration,
    IConnectionMultiplexer redis,
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

        var fsrsCards = await userContext.FsrsCards
                                         .AsNoTracking()
                                         .Where(uk => uk.UserId == userId)
                                         .Select(uk => new { uk.WordId, uk.ReadingIndex, uk.State, uk.Due, uk.LastReview })
                                         .ToListAsync();

        var now = DateTime.UtcNow;

        // Build effective state per (WordId, ReadingIndex) form.
        // FSRS cards take precedence over WordSet membership per form.
        var effectiveForms = new Dictionary<(int WordId, int ReadingIndex), KnownState>();
        foreach (var c in fsrsCards)
        {
            effectiveForms[(c.WordId, c.ReadingIndex)] =
                ComputeEffectiveCategory(c.State, c.Due, c.LastReview, now) ?? KnownState.New;
        }

        // Count FSRS-only forms and words
        int youngForms = 0, matureForms = 0, masteredForms = 0, blacklistedForms = 0;
        foreach (var state in effectiveForms.Values)
        {
            switch (state)
            {
                case KnownState.Young: youngForms++; break;
                case KnownState.Mature: matureForms++; break;
                case KnownState.Mastered: masteredForms++; break;
                case KnownState.Blacklisted: blacklistedForms++; break;
            }
        }

        int youngWords = 0, matureWords = 0, masteredWords = 0, blacklistedWords = 0;
        foreach (var wordGroup in effectiveForms.GroupBy(kvp => kvp.Key.WordId))
        {
            var best = wordGroup.Max(kvp => kvp.Value);
            switch (best)
            {
                case KnownState.Young: youngWords++; break;
                case KnownState.Mature: matureWords++; break;
                case KnownState.Mastered: masteredWords++; break;
                case KnownState.Blacklisted: blacklistedWords++; break;
            }
        }

        var fsrsWordIds = new HashSet<int>(effectiveForms.Keys.Select(k => k.WordId));

        // Word set contributions (only forms/words not already in FSRS)
        int wsMasteredForms = 0, wsBlacklistedForms = 0;
        var wsMasteredWordIds = new HashSet<int>();
        var wsBlacklistedWordIds = new HashSet<int>();

        var userSetStates = await userContext.UserWordSetStates
            .Where(uwss => uwss.UserId == userId)
            .ToListAsync();

        if (userSetStates.Count > 0)
        {
            var masteredSetIds = userSetStates
                .Where(s => s.State == WordSetStateType.Mastered).Select(s => s.SetId).ToList();
            var blacklistedSetIds = userSetStates
                .Where(s => s.State == WordSetStateType.Blacklisted).Select(s => s.SetId).ToList();

            if (masteredSetIds.Count > 0)
            {
                var masteredSetForms = await jitenContext.WordSetMembers
                    .Where(wsm => masteredSetIds.Contains(wsm.SetId))
                    .Select(wsm => new { wsm.WordId, ReadingIndex = (int)wsm.ReadingIndex })
                    .Distinct()
                    .ToListAsync();

                foreach (var m in masteredSetForms)
                {
                    var key = (m.WordId, m.ReadingIndex);
                    if (!effectiveForms.ContainsKey(key))
                    {
                        effectiveForms[key] = KnownState.Mastered;
                        wsMasteredForms++;
                        if (!fsrsWordIds.Contains(m.WordId))
                            wsMasteredWordIds.Add(m.WordId);
                    }
                }
            }

            if (blacklistedSetIds.Count > 0)
            {
                var blacklistedSetForms = await jitenContext.WordSetMembers
                    .Where(wsm => blacklistedSetIds.Contains(wsm.SetId))
                    .Select(wsm => new { wsm.WordId, ReadingIndex = (int)wsm.ReadingIndex })
                    .Distinct()
                    .ToListAsync();

                foreach (var m in blacklistedSetForms)
                {
                    var key = (m.WordId, m.ReadingIndex);
                    if (!effectiveForms.ContainsKey(key))
                    {
                        effectiveForms[key] = KnownState.Blacklisted;
                        wsBlacklistedForms++;
                        if (!fsrsWordIds.Contains(m.WordId))
                            wsBlacklistedWordIds.Add(m.WordId);
                    }
                }
            }
        }

        return Results.Ok(new KnownWordAmountDto
                          {
                              Young = youngWords,
                              YoungForm = youngForms,
                              Mature = matureWords,
                              MatureForm = matureForms,
                              Mastered = masteredWords,
                              MasteredForm = masteredForms,
                              Blacklisted = blacklistedWords,
                              BlacklistedForm = blacklistedForms,
                              WordSetMastered = wsMasteredWordIds.Count,
                              WordSetMasteredForm = wsMasteredForms,
                              WordSetBlacklisted = wsBlacklistedWordIds.Count,
                              WordSetBlacklistedForm = wsBlacklistedForms
                          });
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
                                   .Select(uk => new { uk.WordId, uk.ReadingIndex })
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

        var cards = await userContext.FsrsCards
                                     .Where(uk => uk.UserId == userId)
                                     .ToListAsync();
        var cardIds = cards.Select(c => c.CardId).ToList();
        var reviewLogs = await userContext.FsrsReviewLogs
                                          .Where(rl => cardIds.Contains(rl.CardId))
                                          .ToListAsync();
        if (cards.Count == 0) return Results.Ok(new { removed = 0 });

        userContext.FsrsReviewLogs.RemoveRange(reviewLogs);
        userContext.FsrsCards.RemoveRange(cards);
        await userContext.SaveChangesAsync();

        await CoverageDirtyHelper.MarkCoverageDirty(userContext, userId);
        await userContext.SaveChangesAsync();
        backgroundJobs.Enqueue<ComputationJob>(job => job.ComputeUserCoverage(userId));

        logger.LogInformation("User cleared all known words: UserId={UserId}, RemovedCount={RemovedCount}, RemovedLogsCount={RemovedLogsCount}",
                              userId, cards.Count, reviewLogs.Count);
        return Results.Ok(new { removed = cards.Count, removedLogs = reviewLogs.Count });
    }

    /// <summary>
    /// Add known words for the current user by JMdict word IDs, filtered by reading frequency.
    /// </summary>
    [HttpPost("vocabulary/import-from-ids")]
    public async Task<IResult> ImportWordsFromIds([FromBody] ImportFromIdsRequest request)
    {
        var userId = userService.UserId;
        if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

        if (request.WordIds == null || request.WordIds.Count == 0) return Results.BadRequest("No word IDs provided");

        var distinctIds = request.WordIds.Where(id => id > 0).Distinct().ToList();
        if (distinctIds.Count == 0) return Results.BadRequest("No valid words found");

        var jmdictWords = await jitenContext.JMDictWords
                                            .AsNoTracking()
                                            .Where(w => distinctIds.Contains(w.WordId))
                                            .ToListAsync();

        if (jmdictWords.Count == 0) return Results.BadRequest("Invalid words provided");

        var jmdictWordIds = jmdictWords.Select(w => w.WordId).ToList();

        var frequencies = await jitenContext.JmDictWordFrequencies
                                            .AsNoTracking()
                                            .Where(f => jmdictWordIds.Contains(f.WordId))
                                            .ToDictionaryAsync(f => f.WordId);

        var alreadyKnown = await userContext.FsrsCards
                                            .AsNoTracking()
                                            .Where(uk => uk.UserId == userId && jmdictWordIds.Contains(uk.WordId))
                                            .ToListAsync();

        List<FsrsCard> toInsert = new();
        var alreadyKnownSet = alreadyKnown.Select(uk => (uk.WordId, (int)uk.ReadingIndex)).ToHashSet();

        foreach (var word in jmdictWords)
        {
            var readingIndicesToImport = GetReadingIndicesToImport(word, frequencies, request.FrequencyThreshold);

            foreach (var i in readingIndicesToImport)
            {
                if (alreadyKnownSet.Contains((word.WordId, i)))
                    continue;

                toInsert.Add(new FsrsCard(userId, word.WordId, (byte)i, due: DateTime.UtcNow, lastReview: DateTime.UtcNow,
                                          state: FsrsState.Mastered));
            }
        }

        if (toInsert.Count > 0)
        {
            await userContext.FsrsCards.AddRangeAsync(toInsert);
            await userContext.SaveChangesAsync();
        }

        await CoverageDirtyHelper.MarkCoverageDirty(userContext, userId);
        await userContext.SaveChangesAsync();
        backgroundJobs.Enqueue<ComputationJob>(job => job.ComputeUserCoverage(userId));

        logger.LogInformation("User imported words from IDs: UserId={UserId}, AddedCount={AddedCount}, SkippedCount={SkippedCount}",
                              userId, toInsert.Count, alreadyKnown.Count);
        return Results.Ok(new { added = toInsert.Count, skipped = alreadyKnown.Count });
    }

    private static List<int> GetReadingIndicesToImport(
        JmDictWord word,
        Dictionary<int, JmDictWordFrequency> frequencies,
        int? threshold)
    {
        if (word.Readings.Count <= 1)
            return [0];

        if (!frequencies.TryGetValue(word.WordId, out var freq) || freq.ReadingsFrequencyRank.Count == 0)
            return [0];

        var ranks = freq.ReadingsFrequencyRank;
        var bestRank = int.MaxValue;
        var bestIndex = 0;

        for (var i = 0; i < ranks.Count && i < word.Readings.Count; i++)
        {
            if (ranks[i] > 0 && ranks[i] < bestRank)
            {
                bestRank = ranks[i];
                bestIndex = i;
            }
        }

        if (bestRank == int.MaxValue)
            return [0];

        var result = new List<int> { bestIndex };

        if (threshold == null)
            return result;

        for (var i = 0; i < ranks.Count && i < word.Readings.Count; i++)
        {
            if (i == bestIndex)
                continue;

            if (ranks[i] > 0 && ranks[i] <= bestRank + threshold.Value)
                result.Add(i);
        }

        return result;
    }

    /// <summary>
    /// Parse an Anki-exported TXT file and add all parsed words as known for the current user.
    /// </summary>
    [HttpPost("vocabulary/import-from-anki-txt")]
    [Consumes("multipart/form-data")]
    public async Task<IResult> AddKnownFromAnkiTxt(IFormFile? file, [FromQuery] bool parseWords = false)
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
        var parsedWords = parseWords
            ? await Parser.Parser.ParseText(contextFactory, combinedText)
            : await Parser.Parser.GetWordsDirectLookup(contextFactory, validWords);
        var added = await userService.AddKnownWords(parsedWords);

        await CoverageDirtyHelper.MarkCoverageDirty(userContext, userId);
        await userContext.SaveChangesAsync();
        backgroundJobs.Enqueue<ComputationJob>(job => job.ComputeUserCoverage(userId));

        logger.LogInformation("User imported words from Anki TXT: UserId={UserId}, ParsedCount={ParsedCount}, AddedCount={AddedCount}",
                              userId, parsedWords.Count, added);
        return Results.Ok(new { parsed = parsedWords.Count, added });
    }

    /// <summary>
    /// Parse a JSON coming from AnkiConnect
    /// </summary>
    [HttpPost("vocabulary/import-from-anki")]
    [Consumes("text/json", "application/json")]
    public async Task<IResult> ImportFromAnki([FromBody] AnkiImportRequest request)
    {
        var userId = userService.UserId;
        if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

        if (request?.Cards == null || request.Cards.Count == 0)
            return Results.BadRequest("No cards provided.");

        // Step 1: Parse all unique words to get WordId + ReadingIndex
        var uniqueWords = request.Cards
                                 .Select(c => c.Card.Word)
                                 .Where(w => !string.IsNullOrWhiteSpace(w))
                                 .Distinct()
                                 .ToList();

        if (uniqueWords.Count == 0)
            return Results.BadRequest("No valid words found.");

        // Track skipped words (no reviews)
        List<string> skippedWordsNoReviews = new List<string>();
        int skippedCountNoReviews = 0;

        if (!request.ForceImportCardsWithNoReviews)
        {
            for (int i = request.Cards.Count - 1; i >= 0; i--)
            {
                if (request.Cards[i].Card.LastReview != null)
                    continue;

                skippedWordsNoReviews.Add(new string(request.Cards[i].Card.Word));
                request.Cards.RemoveAt(i);
                skippedCountNoReviews++;
            }
        }

        var combinedText = string.Join(Environment.NewLine, uniqueWords);
        var parsedWords = request.ParseWords
            ? await Parser.Parser.ParseText(contextFactory, combinedText)
            : await Parser.Parser.GetWordsDirectLookup(contextFactory, uniqueWords);

        // Create lookup: original text → (WordId, ReadingIndex)
        var wordLookup = new Dictionary<string, (int WordId, byte ReadingIndex)>();
        foreach (var parsed in parsedWords)
        {
            if (!wordLookup.ContainsKey(parsed.OriginalText))
            {
                wordLookup[parsed.OriginalText] = (parsed.WordId, parsed.ReadingIndex);
            }
        }

        // Step 2: Get existing cards
        var parsedWordIds = wordLookup.Values.Select(v => v.WordId).Distinct().ToList();
        var existingCards = await userContext.FsrsCards
                                             .Include(c => c.ReviewLogs)
                                             .Where(c => c.UserId == userId && parsedWordIds.Contains(c.WordId))
                                             .ToListAsync();

        var existingCardsMap = existingCards
            .ToDictionary(c => (c.WordId, c.ReadingIndex));

        // Step 3: Process cards - separate new cards vs updates
        var cardsToAdd = new List<FsrsCard>();
        var cardsToUpdate = new List<FsrsCard>();
        var cardToAnkiMap = new Dictionary<FsrsCard, AnkiCardWrapper>();

        var processedPairs = new Dictionary<(int WordId, byte ReadingIndex), (FsrsCard Card, List<AnkiReviewLogImport> AllReviewLogs)>();

        // Track skipped words (not in dictionary)
        int skippedCount = 0;
        var skippedWords = new List<string>();

        foreach (var wrapper in request.Cards)
        {
            var word = wrapper.Card.Word?.Trim();
            if (string.IsNullOrWhiteSpace(word))
            {
                skippedCount++;
                continue;
            }

            // Lookup parsed word
            if (!wordLookup.TryGetValue(word, out var wordInfo))
            {
                skippedWords.Add(word);
                skippedCount++;
                continue;
            }

            var key = (wordInfo.WordId, wordInfo.ReadingIndex);

            // Check for duplicates
            if (processedPairs.TryGetValue(key, out var existing))
            {
                logger.LogWarning("Duplicate word detected in import request: {Word} (WordId={WordId}, ReadingIndex={ReadingIndex})",
                                  word, wordInfo.WordId, wordInfo.ReadingIndex);
                skippedCount++;
                continue;
            }

            // Validate and clamp values
            var stability = Math.Max(0, wrapper.Card.Stability ?? 0);
            var difficulty = Math.Clamp(wrapper.Card.Difficulty ?? 5.0, 1.0, 10.0);
            var state = wrapper.Card.State;

            // Check if card already exists
            if (existingCardsMap.TryGetValue(key, out var existingCard))
            {
                if (!request.Overwrite)
                {
                    skippedCount++;
                    continue;
                }

                // Update existing card
                existingCard.State = state;
                existingCard.Stability = stability;
                existingCard.Difficulty = difficulty;
                existingCard.Due = wrapper.Card.Due;
                existingCard.LastReview = wrapper.Card.LastReview;
                existingCard.Step = state == FsrsState.Learning ? (byte?)0 : null;

                cardsToUpdate.Add(existingCard);
                cardToAnkiMap[existingCard] = wrapper;
                processedPairs[key] = (existingCard, new List<AnkiReviewLogImport>(wrapper.ReviewLogs));
            }
            else
            {
                // Create new card
                var fsrsCard = new FsrsCard(
                                            userId,
                                            wordInfo.WordId,
                                            wordInfo.ReadingIndex,
                                            state: state,
                                            due: wrapper.Card.Due,
                                            lastReview: wrapper.Card.LastReview
                                           )
                               {
                                   Stability = stability, Difficulty = difficulty, Step = state == FsrsState.Learning ? (byte?)0 : null,
                               };

                cardsToAdd.Add(fsrsCard);
                cardToAnkiMap[fsrsCard] = wrapper;
                processedPairs[key] = (fsrsCard, new List<AnkiReviewLogImport>(wrapper.ReviewLogs));
            }
        }

        // Step 4: Bulk insert/update with transaction
        if (cardsToAdd.Count == 0 && cardsToUpdate.Count == 0)
        {
            return Results.Ok(new
                              {
                                  imported = 0, updated = 0, skipped = skippedCount, reviewLogs = 0,
                                  skippedWords = skippedWords.Take(50).ToList()
                              });
        }

        await using var transaction = await userContext.Database.BeginTransactionAsync();
        try
        {
            // Insert new cards
            if (cardsToAdd.Count > 0)
            {
                await userContext.FsrsCards.AddRangeAsync(cardsToAdd);
                await userContext.SaveChangesAsync();
            }

            // Handle review logs
            var logsToAdd = new List<FsrsReviewLog>();
            var allCards = cardsToAdd.Concat(cardsToUpdate).ToList();

            foreach (var card in allCards)
            {
                var key = (card.WordId, card.ReadingIndex);

                // For updated cards, remove old review logs
                if (cardsToUpdate.Contains(card))
                {
                    userContext.FsrsReviewLogs.RemoveRange(card.ReviewLogs);
                }

                // Get ALL review logs for this card (including from duplicates)
                if (!processedPairs.TryGetValue(key, out var processed))
                    continue;

                foreach (var log in processed.AllReviewLogs)
                {
                    // Validate rating
                    if (log.Rating is < FsrsRating.Again or > FsrsRating.Easy)
                        continue;

                    logsToAdd.Add(new FsrsReviewLog
                                  {
                                      CardId = card.CardId, Rating = log.Rating, ReviewDateTime = log.ReviewDateTime,
                                      ReviewDuration = log.ReviewDuration,
                                  });
                }
            }

            if (logsToAdd.Count > 0)
            {
                await userContext.FsrsReviewLogs.AddRangeAsync(logsToAdd);
            }

            await userContext.SaveChangesAsync();

            // Sync kana readings for imported/updated kanji cards
            var cardsToSync = new Dictionary<int, (int WordId, byte ReadingIndex, FsrsCard SourceCard, bool Overwrite)>();

            // Process newly added cards
            foreach (var card in cardsToAdd)
            {
                if (!cardsToSync.ContainsKey(card.WordId))
                {
                    cardsToSync[card.WordId] = (card.WordId, card.ReadingIndex, card, request.Overwrite);
                }
            }

            // Process updated cards
            foreach (var card in cardsToUpdate)
            {
                if (!cardsToSync.ContainsKey(card.WordId))
                {
                    cardsToSync[card.WordId] = (card.WordId, card.ReadingIndex, card, request.Overwrite);
                }
            }

            if (cardsToSync.Count > 0)
            {
                await srsService.SyncKanaReadingBatch(userId, cardsToSync.Values, DateTime.UtcNow);
            }

            await transaction.CommitAsync();

            await CoverageDirtyHelper.MarkCoverageDirty(userContext, userId);
            await userContext.SaveChangesAsync();
            backgroundJobs.Enqueue<ComputationJob>(job => job.ComputeUserCoverage(userId));

            logger.LogInformation(
                                  "Anki import completed: UserId={UserId}, Imported={Imported}, Updated={Updated}, Skipped={Skipped}, Logs={Logs}, NotFound={NotFound}",
                                  userId, cardsToAdd.Count, cardsToUpdate.Count, skippedCount, logsToAdd.Count, skippedWords.Count
                                 );

            return Results.Ok(new
                              {
                                  imported = cardsToAdd.Count, updated = cardsToUpdate.Count, skipped = skippedCount,
                                  reviewLogs = logsToAdd.Count, skippedWords = skippedWords,
                                  skippedWordsNoReviews = skippedWordsNoReviews.ToList(), skippedCountNoReviews = skippedCountNoReviews
                              });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            logger.LogError(ex, "Anki import failed");
            return Results.Problem("Import failed: " + ex.Message);
        }
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
    /// Only readings whose per-reading frequency rank falls within the range are imported.
    /// </summary>
    [HttpPost("vocabulary/import-from-frequency/{minFrequency:int}/{maxFrequency:int}")]
    public async Task<IResult> ImportWordsFromFrequency(int minFrequency, int maxFrequency)
    {
        var userId = userService.UserId;
        if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

        if (minFrequency < 0 || maxFrequency < minFrequency || maxFrequency > 10000)
            return Results.BadRequest("Invalid frequency range");

        // Use raw SQL with PostgreSQL unnest to efficiently filter per-reading frequency ranks
        var targetReadings = await jitenContext.Database
            .SqlQuery<ReadingFrequencyResult>($"""
                SELECT f."WordId", (idx - 1)::smallint AS "ReadingIndex", rank AS "FrequencyRank"
                FROM jmdict."WordFrequencies" f,
                     unnest(f."ReadingsFrequencyRank") WITH ORDINALITY AS t(rank, idx)
                WHERE rank >= {minFrequency} AND rank <= {maxFrequency} AND rank > 0
                ORDER BY rank
                """)
            .ToListAsync();

        if (targetReadings.Count == 0)
            return Results.BadRequest("No readings found for the requested frequency range");

        var targetWordIds = targetReadings.Select(r => r.WordId).Distinct().ToList();

        // Determine which entries are already known (by word + reading index)
        var alreadyKnown = await userContext.FsrsCards
                                            .AsNoTracking()
                                            .Where(uk => uk.UserId == userId && targetWordIds.Contains(uk.WordId))
                                            .ToListAsync();

        var alreadyKnownSet = alreadyKnown.Select(uk => (uk.WordId, (short)uk.ReadingIndex)).ToHashSet();

        List<FsrsCard> toInsert = new();

        foreach (var target in targetReadings)
        {
            if (alreadyKnownSet.Contains((target.WordId, target.ReadingIndex)))
                continue;

            toInsert.Add(new FsrsCard(userId, target.WordId, (byte)target.ReadingIndex,
                                      due: DateTime.UtcNow, lastReview: DateTime.UtcNow,
                                      state: FsrsState.Mastered));
        }

        var kanaSyncCount = 0;
        if (toInsert.Count > 0)
        {
            await userContext.FsrsCards.AddRangeAsync(toInsert);
            await userContext.SaveChangesAsync();

            // Sync kana readings for imported kanji cards
            var cardsToSync = toInsert
                .GroupBy(c => c.WordId)
                .Select(g => g.First())
                .Select(c => (c.WordId, c.ReadingIndex, c, false))
                .ToList();

            if (cardsToSync.Count > 0)
            {
                kanaSyncCount = await srsService.SyncKanaReadingBatch(userId, cardsToSync, DateTime.UtcNow);
            }
        }

        await CoverageDirtyHelper.MarkCoverageDirty(userContext, userId);
        await userContext.SaveChangesAsync();
        backgroundJobs.Enqueue<ComputationJob>(job => job.ComputeUserCoverage(userId));

        var uniqueWords = toInsert.Select(c => c.WordId).Distinct().Count();
        var totalForms = toInsert.Count + kanaSyncCount;
        logger.LogInformation("User imported words from frequency range: UserId={UserId}, MinFreq={MinFrequency}, MaxFreq={MaxFrequency}, WordCount={WordCount}, FormCount={FormCount}",
                              userId, minFrequency, maxFrequency, uniqueWords, totalForms);
        return Results.Ok(new { words = uniqueWords, forms = totalForms });
    }


    /// <summary>
    /// Get user metadata
    /// </summary>
    [HttpGet("metadata")]
    public async Task<IResult> GetMetadata()
    {
        var userId = userService.UserId;
        if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

        var metadata = await userContext.UserMetadatas.SingleOrDefaultAsync(m => m.UserId == userId);
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

        var cooldownSeconds = 90;
        var lockKey = $"jiten:coverage-refresh:{userId}";

        try
        {
            var redisDb = redis.GetDatabase();
            var lockAcquired = await redisDb.StringSetAsync(
                lockKey,
                DateTime.UtcNow.ToString("O"),
                TimeSpan.FromSeconds(cooldownSeconds),
                When.NotExists);

            if (!lockAcquired)
            {
                var ttl = await redisDb.KeyTimeToLiveAsync(lockKey);
                var retryAfter = ttl.HasValue ? (int)Math.Ceiling(ttl.Value.TotalSeconds) : cooldownSeconds;
                logger.LogInformation("Coverage refresh already in progress for user {UserId}, retry after {RetryAfter}s", userId, retryAfter);
                return Results.Json(new { status = "already_in_progress", retryAfterSeconds = retryAfter });
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Redis unavailable for coverage refresh lock, proceeding without deduplication");
        }

        await CoverageDirtyHelper.MarkCoverageDirty(userContext, userId);
        await userContext.SaveChangesAsync();
        backgroundJobs.Enqueue<ComputationJob>(job => job.ComputeUserCoverage(userId));

        return Results.Json(new { status = "queued" });
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
            return Results.Ok(new { deckId, status = DeckStatus.None, isFavourite = false, isIgnored = false });

        return Results.Ok(new { preference.DeckId, preference.Status, preference.IsFavourite, preference.IsIgnored });
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

        return Results.Ok(new { preference.DeckId, preference.Status, preference.IsFavourite, preference.IsIgnored });
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

        return Results.Ok(new { preference.DeckId, preference.Status, preference.IsFavourite, preference.IsIgnored });
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

        var previousStatus = preference?.Status ?? DeckStatus.None;

        if (preference == null)
        {
            preference = new UserDeckPreference { UserId = userId, DeckId = deckId };
            userContext.UserDeckPreferences.Add(preference);
        }

        preference.Status = request.Status;
        await userContext.SaveChangesAsync();

        // Trigger accomplishment recomputation if status changed to/from Completed
        if (previousStatus != request.Status &&
            (previousStatus == DeckStatus.Completed || request.Status == DeckStatus.Completed))
        {
            backgroundJobs.Enqueue<ComputationJob>(job => job.ComputeUserAccomplishments(userId));
        }

        return Results.Ok(new { preference.DeckId, preference.Status, preference.IsFavourite, preference.IsIgnored });
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

    /// <summary>
    /// Export all FSRS cards and review logs for the current user as JSON.
    /// </summary>
    [HttpGet("vocabulary/export")]
    public async Task<IResult> ExportVocabulary()
    {
        var userId = userService.UserId;
        if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

        var cards = await userContext.FsrsCards
                                     .AsNoTracking()
                                     .Include(c => c.ReviewLogs)
                                     .Where(c => c.UserId == userId)
                                     .OrderBy(c => c.WordId)
                                     .ThenBy(c => c.ReadingIndex)
                                     .ToListAsync();

        var exportDto = new FsrsExportDto
                        {
                            ExportDate = DateTime.UtcNow, UserId = userId, TotalCards = cards.Count,
                            TotalReviews = cards.Sum(c => c.ReviewLogs.Count), Cards = cards.Select(c => new FsrsCardExportDto
                                {
                                    WordId = c.WordId, ReadingIndex = c.ReadingIndex, State = c.State, Step = c.Step,
                                    Stability = EnsureValidNumber(c.Stability), Difficulty = EnsureValidNumber(c.Difficulty),
                                    Due = new DateTimeOffset(c.Due).ToUnixTimeSeconds(), LastReview = c.LastReview.HasValue
                                        ? new DateTimeOffset(c.LastReview.Value).ToUnixTimeSeconds()
                                        : null,
                                    ReviewLogs = c.ReviewLogs.OrderBy(r => r.ReviewDateTime)
                                                  .Select(r => new FsrsReviewLogExportDto
                                                               {
                                                                   Rating = r.Rating, ReviewDateTime =
                                                                       new DateTimeOffset(r.ReviewDateTime)
                                                                           .ToUnixTimeSeconds(),
                                                                   ReviewDuration = r.ReviewDuration
                                                               }).ToList()
                                }).ToList()
                        };

        logger.LogInformation("User exported vocabulary: UserId={UserId}, CardCount={CardCount}, ReviewCount={ReviewCount}",
                              userId, exportDto.TotalCards, exportDto.TotalReviews);

        return Results.Ok(exportDto);

        double? EnsureValidNumber(double? value)
        {
            if (!value.HasValue) return null;
            if (double.IsNaN(value.Value) || double.IsInfinity(value.Value)) return null;
            return value;
        }
    }

    /// <summary>
    /// Export user's vocabulary as a TXT organised by learning state.
    /// </summary>
    [HttpGet("vocabulary/export-words")]
    [Produces("text/plain")]
    public async Task<IResult> ExportWords(
        [FromQuery] bool exportKanaOnly = false,
        [FromQuery] bool exportMastered = true,
        [FromQuery] bool exportMature = true,
        [FromQuery] bool exportYoung = true,
        [FromQuery] bool exportBlacklisted = true)
    {
        var userId = userService.UserId;
        if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

        if (!exportMastered && !exportMature && !exportYoung && !exportBlacklisted)
            return Results.BadRequest("At least one category must be selected");

        var cards = await userContext.FsrsCards
                                     .AsNoTracking()
                                     .Where(c => c.UserId == userId)
                                     .OrderBy(c => c.WordId)
                                     .ThenBy(c => c.ReadingIndex)
                                     .ToListAsync();

        if (cards.Count == 0)
        {
            return Results.NoContent();
        }

        var masteredCards = new List<FsrsCard>();
        var matureCards = new List<FsrsCard>();
        var youngCards = new List<FsrsCard>();
        var blacklistedCards = new List<FsrsCard>();

        var knownStates = await userService.GetKnownWordsState(cards.Select(c => (c.WordId, c.ReadingIndex)));

        foreach (var card in cards)
        {
            if (knownStates[(card.WordId, card.ReadingIndex)].Contains(KnownState.Mastered))
            {
                masteredCards.Add(card);
            }

            if (knownStates[(card.WordId, card.ReadingIndex)].Contains(KnownState.Blacklisted))
            {
                blacklistedCards.Add(card);
            }

            if (knownStates[(card.WordId, card.ReadingIndex)].Contains(KnownState.Mature))
            {
                matureCards.Add(card);
            }

            if (knownStates[(card.WordId, card.ReadingIndex)].Contains(KnownState.Young))
            {
                youngCards.Add(card);
            }
        }

        var allCards = masteredCards.Concat(matureCards).Concat(youngCards).Concat(blacklistedCards).ToList();
        var wordIds = allCards.Select(c => c.WordId).Distinct().ToList();

        var jmdictWords = await jitenContext.JMDictWords
                                            .AsNoTracking()
                                            .Where(w => wordIds.Contains(w.WordId))
                                            .Select(w => new { w.WordId, w.Readings })
                                            .ToDictionaryAsync(w => w.WordId);

        var txt = new StringBuilder();

        AppendSection("MASTERED", masteredCards);
        AppendSection("MATURE", matureCards);
        AppendSection("YOUNG", youngCards);
        AppendSection("BLACKLISTED", blacklistedCards);

        var txtBytes = Encoding.UTF8.GetBytes(txt.ToString());

        logger.LogInformation(
                              "User exported words: UserId={UserId}, TotalCards={TotalCards}, Mastered={Mastered}, Mature={Mature}, Young={Young}, Blacklisted={Blacklisted}",
                              userId, allCards.Count, masteredCards.Count, matureCards.Count, youngCards.Count, blacklistedCards.Count);

        var dateStr = DateTime.UtcNow.ToString("yyyy-MM-dd");
        return Results.File(txtBytes, "text/plain", $"jiten-vocabulary-export-{dateStr}.txt");

        void AppendSection(string sectionName, List<FsrsCard> sectionCards)
        {
            if (sectionCards.Count == 0) return;

            txt.AppendLine($"=== {sectionName} ===");

            foreach (var card in sectionCards)
            {
                if (!jmdictWords.TryGetValue(card.WordId, out var word)) continue;
                if (card.ReadingIndex >= word.Readings.Count) continue;

                var reading = word.Readings[card.ReadingIndex];

                if (!exportKanaOnly && WanaKana.IsKana(reading)) continue;

                txt.AppendLine(reading);
            }
        }
    }

    /// <summary>
    /// Get all FSRS cards for the current user with word information.
    /// </summary>
    [HttpGet("vocabulary/cards")]
    public async Task<IResult> GetCards()
    {
        var userId = userService.UserId;
        if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

        var cards = await userContext.FsrsCards
                                     .AsNoTracking()
                                     .Where(c => c.UserId == userId)
                                     .OrderBy(c => c.Due)
                                     .ToListAsync();

        var wordIds = cards.Select(c => c.WordId).Distinct().ToList();

        var words = await jitenContext.JMDictWords
                                      .AsNoTracking()
                                      .Where(w => wordIds.Contains(w.WordId))
                                      .Select(w => new { w.WordId, w.Readings, w.ReadingTypes })
                                      .ToListAsync();

        var wordLookup = words.ToDictionary(w => w.WordId);

        var frequencies = await jitenContext.JmDictWordFrequencies
                                            .AsNoTracking()
                                            .Where(f => wordIds.Contains(f.WordId))
                                            .ToDictionaryAsync(f => f.WordId);

        var result = cards.Select(c =>
        {
            var word = wordLookup.GetValueOrDefault(c.WordId);
            var hasValidReading = word != null && c.ReadingIndex < word.Readings.Count;
            var freq = frequencies.GetValueOrDefault(c.WordId);
            var frequencyRank = freq != null && c.ReadingIndex < freq.ReadingsFrequencyRank.Count
                ? freq.ReadingsFrequencyRank[c.ReadingIndex]
                : 0;

            return new FsrsCardWithWordDto
                   {
                       CardId = c.CardId, WordId = c.WordId, ReadingIndex = c.ReadingIndex, State = c.State, Step = c.Step,
                       Stability = EnsureValidNumber(c.Stability), Difficulty = EnsureValidNumber(c.Difficulty), Due = c.Due,
                       LastReview = c.LastReview, WordText = hasValidReading ? word!.Readings[c.ReadingIndex] : "",
                       ReadingType = hasValidReading ? word!.ReadingTypes[c.ReadingIndex] : JmDictReadingType.Reading,
                       FrequencyRank = frequencyRank
                   };
        }).ToList();

        logger.LogInformation("User fetched cards view: UserId={UserId}, CardCount={CardCount}",
                              userId, result.Count);

        return Results.Ok(result);

        double? EnsureValidNumber(double? value)
        {
            if (!value.HasValue) return null;
            if (double.IsNaN(value.Value) || double.IsInfinity(value.Value)) return null;
            return value;
        }
    }

    /// <summary>
    /// Import FSRS cards and review logs from JSON export.
    /// </summary>
    [HttpPost("vocabulary/import")]
    public async Task<IResult> ImportVocabulary([FromBody] FsrsExportDto exportDto, [FromQuery] bool overwrite = false)
    {
        var userId = userService.UserId;
        if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

        if (exportDto?.Cards == null || exportDto.Cards.Count == 0)
            return Results.BadRequest("Invalid export data");

        var result = new FsrsImportResultDto
                     {
                         ValidationErrors = [], CardsImported = 0, CardsSkipped = 0, CardsUpdated = 0, ReviewLogsImported = 0
                     };

        var distinctWordIds = exportDto.Cards.Select(c => c.WordId).Distinct().ToList();

        var wordValidationMap = await jitenContext.JMDictWords
                                                  .AsNoTracking()
                                                  .Where(w => distinctWordIds.Contains(w.WordId))
                                                  .Select(w => new { w.WordId, ReadingCount = w.Readings.Count })
                                                  .ToDictionaryAsync(w => w.WordId);

        var validCards = new List<FsrsCardExportDto>(exportDto.Cards.Count);

        foreach (var card in exportDto.Cards)
        {
            if (!wordValidationMap.TryGetValue(card.WordId, out var wordInfo))
            {
                result.ValidationErrors.Add($"WordId {card.WordId} does not exist in JMDict");
                continue;
            }

            if (card.ReadingIndex >= wordInfo.ReadingCount)
            {
                result.ValidationErrors.Add($"WordId {card.WordId} ReadingIndex {card.ReadingIndex} is invalid");
                continue;
            }

            validCards.Add(card);
        }

        if (validCards.Count == 0)
        {
            return Results.Ok(result);
        }

        await using var transaction = await userContext.Database.BeginTransactionAsync();
        try
        {
            var existingCardsMap = await userContext.FsrsCards
                                                    .Include(c => c.ReviewLogs)
                                                    .Where(c => c.UserId == userId)
                                                    .ToDictionaryAsync(c => (c.WordId, c.ReadingIndex));

            var cardsToAdd = new List<FsrsCard>();

            foreach (var cardDto in validCards)
            {
                var key = (cardDto.WordId, cardDto.ReadingIndex);

                var uniqueIncomingLogs = cardDto.ReviewLogs
                                                .DistinctBy(l => l.ReviewDateTime)
                                                .ToList();

                if (existingCardsMap.TryGetValue(key, out var existingCard))
                {
                    if (!overwrite)
                    {
                        result.CardsSkipped++;
                        continue;
                    }

                    existingCard.State = cardDto.State;
                    existingCard.Step = cardDto.Step;
                    existingCard.Stability = cardDto.Stability;
                    existingCard.Difficulty = cardDto.Difficulty;
                    existingCard.Due = DateTimeOffset.FromUnixTimeSeconds(cardDto.Due).UtcDateTime;
                    existingCard.LastReview = cardDto.LastReview.HasValue
                        ? DateTimeOffset.FromUnixTimeSeconds(cardDto.LastReview.Value).UtcDateTime
                        : null;

                    // Replace logs: Clear old ones, Add new ones
                    userContext.FsrsReviewLogs.RemoveRange(existingCard.ReviewLogs);

                    foreach (var logDto in uniqueIncomingLogs)
                    {
                        existingCard.ReviewLogs.Add(new FsrsReviewLog
                                                    {
                                                        Rating = logDto.Rating, ReviewDateTime = DateTimeOffset
                                                            .FromUnixTimeSeconds(logDto.ReviewDateTime)
                                                            .UtcDateTime,
                                                        ReviewDuration = logDto.ReviewDuration,
                                                        // EF Core handles the FK association automatically here
                                                    });
                    }

                    result.CardsUpdated++;
                    result.ReviewLogsImported += cardDto.ReviewLogs.Count;
                }
                else
                {
                    var newCard = new FsrsCard(userId, cardDto.WordId, cardDto.ReadingIndex)
                                  {
                                      State = cardDto.State, Step = cardDto.Step, Stability = cardDto.Stability,
                                      Difficulty = cardDto.Difficulty, Due = DateTimeOffset.FromUnixTimeSeconds(cardDto.Due).UtcDateTime,
                                      LastReview = cardDto.LastReview.HasValue
                                          ? DateTimeOffset.FromUnixTimeSeconds(cardDto.LastReview.Value).UtcDateTime
                                          : null,
                                      ReviewLogs = uniqueIncomingLogs.Select(l => new FsrsReviewLog
                                                                                  {
                                                                                      Rating = l.Rating, ReviewDateTime = DateTimeOffset
                                                                                          .FromUnixTimeSeconds(l.ReviewDateTime)
                                                                                          .UtcDateTime,
                                                                                      ReviewDuration = l.ReviewDuration
                                                                                  }).ToList()
                                  };

                    cardsToAdd.Add(newCard);
                    result.CardsImported++;
                    result.ReviewLogsImported += cardDto.ReviewLogs.Count;
                }
            }

            if (cardsToAdd.Count > 0)
            {
                await userContext.FsrsCards.AddRangeAsync(cardsToAdd);
            }

            await userContext.SaveChangesAsync();
            await transaction.CommitAsync();

            await CoverageDirtyHelper.MarkCoverageDirty(userContext, userId);
            await userContext.SaveChangesAsync();
            backgroundJobs.Enqueue<ComputationJob>(job => job.ComputeUserCoverage(userId));

            logger.LogInformation("Import stats: Imported={Imported}, Updated={Updated}, Skipped={Skipped}",
                                  result.CardsImported, result.CardsUpdated, result.CardsSkipped);

            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            logger.LogError(ex, "Import failed");
            return Results.Problem("Import failed: " + ex.Message);
        }
    }

    #region Accomplishments

    /// <summary>
    /// Get accomplishments for a user. Requires own profile or public profile.
    /// </summary>
    [HttpGet("user/{targetUserId}/accomplishments")]
    [AllowAnonymous]
    public async Task<IResult> GetUserAccomplishments(string targetUserId)
    {
        var currentUserId = userService.UserId;
        var isOwnProfile = currentUserId == targetUserId;

        if (!isOwnProfile)
        {
            var profile = await userContext.UserProfiles
                                           .AsNoTracking()
                                           .FirstOrDefaultAsync(p => p.UserId == targetUserId);

            if (profile == null || !profile.IsPublic)
                return Results.Forbid();
        }

        var accomplishments = await userContext.UserAccomplishments
                                               .AsNoTracking()
                                               .Where(ua => ua.UserId == targetUserId)
                                               .OrderBy(ua => ua.MediaType)
                                               .ToListAsync();

        return Results.Ok(accomplishments);
    }

    /// <summary>
    /// Get accomplishment for a specific MediaType. Use 'global' for all types combined.
    /// Requires own profile or public profile.
    /// </summary>
    [HttpGet("user/{targetUserId}/accomplishments/global")]
    [HttpGet("user/{targetUserId}/accomplishments/{mediaType}")]
    [AllowAnonymous]
    public async Task<IResult> GetUserAccomplishment(string targetUserId, Jiten.Core.Data.MediaType? mediaType = null)
    {
        var currentUserId = userService.UserId;
        var isOwnProfile = currentUserId == targetUserId;

        if (!isOwnProfile)
        {
            var profile = await userContext.UserProfiles
                                           .AsNoTracking()
                                           .FirstOrDefaultAsync(p => p.UserId == targetUserId);

            if (profile == null || !profile.IsPublic)
                return Results.Forbid();
        }

        var accomplishment = await userContext.UserAccomplishments
                                              .AsNoTracking()
                                              .FirstOrDefaultAsync(ua => ua.UserId == targetUserId && ua.MediaType == mediaType);

        if (accomplishment == null)
            return Results.NotFound();

        return Results.Ok(accomplishment);
    }

    /// <summary>
    /// Manually trigger accomplishment recomputation.
    /// </summary>
    [HttpPost("accomplishments/refresh")]
    public IResult RefreshAccomplishments()
    {
        var userId = userService.UserId;
        if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

        backgroundJobs.Enqueue<ComputationJob>(job => job.ComputeUserAccomplishments(userId));

        return Results.Accepted();
    }

    /// <summary>
    /// Get aggregated vocabulary across all completed decks.
    /// Requires own profile or public profile.
    /// </summary>
    [HttpGet("user/{targetUserId}/accomplishments/vocabulary")]
    [AllowAnonymous]
    public async Task<ActionResult<PaginatedResponse<AccomplishmentVocabularyDto>>> GetAccomplishmentVocabulary(
        string targetUserId,
        [FromQuery] Jiten.Core.Data.MediaType? mediaType = null,
        [FromQuery] int offset = 0,
        [FromQuery] int pageSize = 100,
        [FromQuery] string sortBy = "occurrences",
        [FromQuery] bool descending = true)
    {
        var currentUserId = userService.UserId;
        var isOwnProfile = currentUserId == targetUserId;

        if (!isOwnProfile)
        {
            var profile = await userContext.UserProfiles
                                           .AsNoTracking()
                                           .FirstOrDefaultAsync(p => p.UserId == targetUserId);

            if (profile == null || !profile.IsPublic)
                return Forbid();
        }

        pageSize = Math.Clamp(pageSize, 1, 100);

        // Get all completed deck IDs for the user
        var userCompletedDeckIds = await userContext.UserDeckPreferences
                                                    .AsNoTracking()
                                                    .Where(udp => udp.UserId == targetUserId && udp.Status == DeckStatus.Completed)
                                                    .Select(udp => udp.DeckId)
                                                    .ToListAsync();

        // Load completed decks with parent relationship
        var allCompletedDecks = await jitenContext.Decks
                                                  .AsNoTracking()
                                                  .Where(d => userCompletedDeckIds.Contains(d.DeckId))
                                                  .Select(d => new { d.DeckId, d.ParentDeckId, d.MediaType })
                                                  .ToListAsync();

        // Build effective deck set: include parents, and children only if their parent is NOT completed
        var completedParentIds = allCompletedDecks
                                 .Where(d => d.ParentDeckId == null)
                                 .Select(d => d.DeckId)
                                 .ToHashSet();

        var effectiveDecks = allCompletedDecks
                             .Where(d => d.ParentDeckId == null || !completedParentIds.Contains(d.ParentDeckId.Value))
                             .ToList();

        // Apply media type filter if provided
        if (mediaType.HasValue)
        {
            effectiveDecks = effectiveDecks.Where(d => d.MediaType == mediaType.Value).ToList();
        }

        var completedDeckIds = effectiveDecks.Select(d => d.DeckId).ToList();

        if (completedDeckIds.Count == 0)
        {
            return Ok(new PaginatedResponse<AccomplishmentVocabularyDto>(
                                                                         new AccomplishmentVocabularyDto { Words = [] }, 0, pageSize,
                                                                         offset));
        }

        // Aggregate vocabulary across completed decks
        var aggregatedWordsQuery = jitenContext.DeckWords
                                               .AsNoTracking()
                                               .Where(dw => completedDeckIds.Contains(dw.DeckId))
                                               .GroupBy(dw => new { dw.WordId, dw.ReadingIndex })
                                               .Select(g => new AggregatedWord
                                                            {
                                                                WordId = g.Key.WordId, ReadingIndex = g.Key.ReadingIndex,
                                                                TotalOccurrences = g.Sum(dw => dw.Occurrences)
                                                            });

        int totalCount = await aggregatedWordsQuery.CountAsync();

        // Apply sorting and pagination
        List<AggregatedWord> pagedWords;
        if (sortBy.Equals("alphabetical", StringComparison.OrdinalIgnoreCase))
        {
            // For alphabetical sorting, we need to join with JMDict to get reading text
            var sortedQuery = from aw in aggregatedWordsQuery
                              join jw in jitenContext.JMDictWords on aw.WordId equals jw.WordId
                              select new { aw, ReadingText = jw.Readings[aw.ReadingIndex] };

            var orderedQuery = descending
                ? sortedQuery.OrderByDescending(x => x.ReadingText)
                : sortedQuery.OrderBy(x => x.ReadingText);

            pagedWords = await orderedQuery
                               .Skip(offset)
                               .Take(pageSize)
                               .Select(x => x.aw)
                               .ToListAsync();
        }
        else
        {
            // Default: sort by occurrences
            var orderedQuery = descending
                ? aggregatedWordsQuery.OrderByDescending(w => w.TotalOccurrences)
                : aggregatedWordsQuery.OrderBy(w => w.TotalOccurrences);

            pagedWords = await orderedQuery
                               .Skip(offset)
                               .Take(pageSize)
                               .ToListAsync();
        }

        var wordIds = pagedWords.Select(w => w.WordId).ToList();

        // Fetch JMDict words
        var jmdictWords = await jitenContext.JMDictWords
                                            .AsNoTracking()
                                            .Where(w => wordIds.Contains(w.WordId))
                                            .Include(w => w.Definitions)
                                            .ToListAsync();

        var jmdictLookup = jmdictWords.ToDictionary(w => w.WordId);

        // Fetch frequencies
        var frequencies = await jitenContext.JmDictWordFrequencies
                                            .AsNoTracking()
                                            .Where(f => wordIds.Contains(f.WordId))
                                            .ToListAsync();
        var freqLookup = frequencies.ToDictionary(f => f.WordId);

        // Build WordDtos - preserve the order from pagedWords
        var wordDtos = new List<WordDto>();
        foreach (var pw in pagedWords)
        {
            if (!jmdictLookup.TryGetValue(pw.WordId, out var jmWord))
                continue;

            if (pw.ReadingIndex >= jmWord.ReadingsFurigana.Count)
                continue;

            var reading = jmWord.ReadingsFurigana[pw.ReadingIndex];
            freqLookup.TryGetValue(pw.WordId, out var freq);

            var alternativeReadings = jmWord.ReadingsFurigana
                                            .Select((r, i) => new ReadingDto
                                                              {
                                                                  Text = r, ReadingIndex = (byte)i, ReadingType = jmWord.ReadingTypes[i],
                                                                  FrequencyRank = freq?.ReadingsFrequencyRank[i] ?? 0,
                                                                  FrequencyPercentage = freq?.ReadingsFrequencyPercentage[i] ?? 0
                                                              })
                                            .ToList();
            alternativeReadings.RemoveAt(pw.ReadingIndex);

            var mainReading = new ReadingDto
                              {
                                  Text = reading, ReadingIndex = pw.ReadingIndex, ReadingType = jmWord.ReadingTypes[pw.ReadingIndex],
                                  FrequencyRank = freq?.ReadingsFrequencyRank[pw.ReadingIndex] ?? 0,
                                  FrequencyPercentage = freq?.ReadingsFrequencyPercentage[pw.ReadingIndex] ?? 0
                              };

            wordDtos.Add(new WordDto
                         {
                             WordId = jmWord.WordId, MainReading = mainReading, AlternativeReadings = alternativeReadings,
                             PartsOfSpeech = jmWord.PartsOfSpeech.ToHumanReadablePartsOfSpeech(),
                             Definitions = jmWord.Definitions.ToDefinitionDtos(), Occurrences = pw.TotalOccurrences,
                             PitchAccents = jmWord.PitchAccents
                         });
        }

        // Apply known states
        var knownStates = await userService.GetKnownWordsState(
                                                               wordDtos.Select(w => (w.WordId, w.MainReading.ReadingIndex)).ToList());
        wordDtos.ApplyKnownWordsState(knownStates);

        var dto = new AccomplishmentVocabularyDto { Words = wordDtos };

        return Ok(new PaginatedResponse<AccomplishmentVocabularyDto>(dto, totalCount, pageSize, offset));
    }

    #endregion

    #region Profile

    /// <summary>
    /// Get current user's profile.
    /// </summary>
    [HttpGet("profile")]
    public async Task<IResult> GetProfile()
    {
        var userId = userService.UserId;
        if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

        var profile = await userContext.UserProfiles
                                       .AsNoTracking()
                                       .FirstOrDefaultAsync(p => p.UserId == userId);

        if (profile == null)
        {
            // Return default profile
            return Results.Ok(new UserProfile { UserId = userId, IsPublic = false });
        }

        return Results.Ok(profile);
    }

    /// <summary>
    /// Update current user's profile.
    /// </summary>
    [HttpPatch("profile")]
    public async Task<IResult> UpdateProfile([FromBody] UpdateProfileRequest request)
    {
        var userId = userService.UserId;
        if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

        var profile = await userContext.UserProfiles
                                       .FirstOrDefaultAsync(p => p.UserId == userId);

        if (profile == null)
        {
            profile = new UserProfile { UserId = userId };
            userContext.UserProfiles.Add(profile);
        }

        if (request.IsPublic.HasValue)
            profile.IsPublic = request.IsPublic.Value;

        await userContext.SaveChangesAsync();

        return Results.Ok(profile);
    }

    /// <summary>
    /// Get another user's profile by user ID (limited info if not public).
    /// </summary>
    [HttpGet("user/{targetUserId}/profile")]
    [AllowAnonymous]
    public async Task<IResult> GetUserProfile(string targetUserId)
    {
        // Check user exists
        var user = await userContext.Users
                                    .AsNoTracking()
                                    .FirstOrDefaultAsync(u => u.Id == targetUserId);

        if (user == null)
            return Results.NotFound(new { message = "User not found" });

        var currentUserId = userService.UserId;
        var isOwnProfile = currentUserId == targetUserId;

        var profile = await userContext.UserProfiles
                                       .AsNoTracking()
                                       .FirstOrDefaultAsync(p => p.UserId == targetUserId);

        if (isOwnProfile)
        {
            return Results.Ok(new UserProfileResponse
                              {
                                  UserId = user.Id, Username = user.UserName ?? string.Empty, IsPublic = profile?.IsPublic ?? false
                              });
        }

        if (profile == null || !profile.IsPublic)
        {
            // Return minimal info for non-public profiles
            return Results.Ok(new UserProfileResponse { UserId = user.Id, Username = user.UserName ?? string.Empty, IsPublic = false });
        }

        return Results.Ok(new UserProfileResponse
                          {
                              UserId = user.Id, Username = user.UserName ?? string.Empty, IsPublic = profile.IsPublic
                          });
    }

    /// <summary>
    /// Get a user's profile by username (case-insensitive).
    /// Returns 404 if user doesn't exist or profile is private (to prevent username enumeration).
    /// </summary>
    [HttpGet("profile/{username}")]
    [AllowAnonymous]
    public async Task<IResult> GetUserProfileByUsername(string username)
    {
        var user = await userContext.Users
                                    .AsNoTracking()
                                    .FirstOrDefaultAsync(u => u.NormalizedUserName == username.ToUpperInvariant());

        if (user == null)
            return Results.NotFound(new { message = "Profile not found" });

        var currentUserId = userService.UserId;
        var isOwnProfile = currentUserId == user.Id;

        var profile = await userContext.UserProfiles
                                       .AsNoTracking()
                                       .FirstOrDefaultAsync(p => p.UserId == user.Id);

        // Return 404 for private profiles (same as non-existent to prevent enumeration)
        if (!isOwnProfile && (profile == null || !profile.IsPublic))
            return Results.NotFound(new { message = "Profile not found" });

        return Results.Ok(new UserProfileResponse
                          {
                              UserId = user.Id, Username = user.UserName ?? string.Empty, IsPublic = profile?.IsPublic ?? false
                          });
    }

    /// <summary>
    /// Get accomplishments for a user by username. Requires own profile or public profile.
    /// Returns 404 if user doesn't exist or profile is private.
    /// </summary>
    [HttpGet("profile/{username}/accomplishments")]
    [AllowAnonymous]
    public async Task<IResult> GetUserAccomplishmentsByUsername(string username)
    {
        var user = await userContext.Users
                                    .AsNoTracking()
                                    .FirstOrDefaultAsync(u => u.NormalizedUserName == username.ToUpperInvariant());

        if (user == null)
            return Results.NotFound(new { message = "Profile not found" });

        var currentUserId = userService.UserId;
        var isOwnProfile = currentUserId == user.Id;

        if (!isOwnProfile)
        {
            var profile = await userContext.UserProfiles
                                           .AsNoTracking()
                                           .FirstOrDefaultAsync(p => p.UserId == user.Id);

            if (profile == null || !profile.IsPublic)
                return Results.NotFound(new { message = "Profile not found" });
        }

        var accomplishments = await userContext.UserAccomplishments
                                               .AsNoTracking()
                                               .Where(ua => ua.UserId == user.Id)
                                               .OrderBy(ua => ua.MediaType)
                                               .ToListAsync();

        return Results.Ok(accomplishments);
    }

    /// <summary>
    /// Get aggregated vocabulary across all completed decks by username.
    /// Requires own profile or public profile.
    /// Returns 404 if user doesn't exist or profile is private.
    /// </summary>
    [HttpGet("profile/{username}/accomplishments/vocabulary")]
    [AllowAnonymous]
    public async Task<ActionResult<PaginatedResponse<AccomplishmentVocabularyDto>>> GetAccomplishmentVocabularyByUsername(
        string username,
        [FromQuery] Jiten.Core.Data.MediaType? mediaType = null,
        [FromQuery] int offset = 0,
        [FromQuery] int pageSize = 100,
        [FromQuery] string sortBy = "occurrences",
        [FromQuery] bool descending = true,
        [FromQuery] string displayFilter = "all")
    {
        var user = await userContext.Users
                                    .AsNoTracking()
                                    .FirstOrDefaultAsync(u => u.NormalizedUserName == username.ToUpperInvariant());

        if (user == null)
            return NotFound(new { message = "Profile not found" });

        var targetUserId = user.Id;
        var currentUserId = userService.UserId;
        var isOwnProfile = currentUserId == targetUserId;

        if (!isOwnProfile)
        {
            var profile = await userContext.UserProfiles
                                           .AsNoTracking()
                                           .FirstOrDefaultAsync(p => p.UserId == targetUserId);

            if (profile == null || !profile.IsPublic)
                return NotFound(new { message = "Profile not found" });
        }

        pageSize = Math.Clamp(pageSize, 1, 100);

        // Get all completed deck IDs for the user
        var userCompletedDeckIds = await userContext.UserDeckPreferences
                                                    .AsNoTracking()
                                                    .Where(udp => udp.UserId == targetUserId && udp.Status == DeckStatus.Completed)
                                                    .Select(udp => udp.DeckId)
                                                    .ToListAsync();

        // Load completed decks with parent relationship
        var allCompletedDecks = await jitenContext.Decks
                                                  .AsNoTracking()
                                                  .Where(d => userCompletedDeckIds.Contains(d.DeckId))
                                                  .Select(d => new { d.DeckId, d.ParentDeckId, d.MediaType })
                                                  .ToListAsync();

        // Build effective deck set: include parents, and children only if their parent is NOT completed
        var completedParentIds = allCompletedDecks
                                 .Where(d => d.ParentDeckId == null)
                                 .Select(d => d.DeckId)
                                 .ToHashSet();

        var effectiveDecks = allCompletedDecks
                             .Where(d => d.ParentDeckId == null || !completedParentIds.Contains(d.ParentDeckId.Value))
                             .ToList();

        // Apply media type filter if provided
        if (mediaType.HasValue)
        {
            effectiveDecks = effectiveDecks.Where(d => d.MediaType == mediaType.Value).ToList();
        }

        var completedDeckIds = effectiveDecks.Select(d => d.DeckId).ToList();

        if (completedDeckIds.Count == 0)
        {
            return Ok(new PaginatedResponse<AccomplishmentVocabularyDto>(
                                                                         new AccomplishmentVocabularyDto { Words = [] }, 0, pageSize,
                                                                         offset));
        }

        // Aggregate vocabulary across completed decks
        var aggregatedWordsQuery = jitenContext.DeckWords
                                               .AsNoTracking()
                                               .Where(dw => completedDeckIds.Contains(dw.DeckId))
                                               .GroupBy(dw => new { dw.WordId, dw.ReadingIndex })
                                               .Select(g => new AggregatedWord
                                                            {
                                                                WordId = g.Key.WordId, ReadingIndex = g.Key.ReadingIndex,
                                                                TotalOccurrences = g.Sum(dw => dw.Occurrences)
                                                            });

        // Materialise the aggregated words for filtering and sorting
        var allAggregatedWords = await aggregatedWordsQuery.ToListAsync();

        // Apply displayFilter if authenticated and filter is not "all"
        if (userService.IsAuthenticated && !string.IsNullOrEmpty(displayFilter) && displayFilter != "all")
        {
            var wordKeys = allAggregatedWords.Select(aw => (aw.WordId, aw.ReadingIndex)).ToList();
            var filterKnownStates = await userService.GetKnownWordsState(wordKeys);

            var distinctWordIds = wordKeys.Select(k => k.WordId).Distinct().ToList();
            var fsrsStates = await userContext.FsrsCards
                                              .AsNoTracking()
                                              .Where(uk => uk.UserId == userService.UserId && distinctWordIds.Contains(uk.WordId))
                                              .Select(uk => new { uk.WordId, uk.ReadingIndex, uk.State })
                                              .ToDictionaryAsync(uk => (uk.WordId, uk.ReadingIndex), uk => uk.State);

            allAggregatedWords = allAggregatedWords.Where(aw =>
            {
                var key = (aw.WordId, aw.ReadingIndex);
                var knownState = filterKnownStates.GetValueOrDefault(key, [KnownState.New]);
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
            }).ToList();
        }

        int totalCount = allAggregatedWords.Count;

        // Apply sorting
        IEnumerable<AggregatedWord> sortedWords;
        if (sortBy.Equals("globalFreq", StringComparison.OrdinalIgnoreCase))
        {
            var freqWordIds = allAggregatedWords.Select(aw => aw.WordId).Distinct().ToList();
            var sortFrequencies = await jitenContext.JmDictWordFrequencies
                                                    .AsNoTracking()
                                                    .Where(f => freqWordIds.Contains(f.WordId))
                                                    .ToDictionaryAsync(f => f.WordId);

            sortedWords = descending
                ? allAggregatedWords.OrderByDescending(aw =>
                                                           sortFrequencies.TryGetValue(aw.WordId, out var f) &&
                                                           aw.ReadingIndex < f.ReadingsFrequencyRank.Count
                                                               ? f.ReadingsFrequencyRank[aw.ReadingIndex]
                                                               : 0)
                : allAggregatedWords.OrderBy(aw =>
                                                 sortFrequencies.TryGetValue(aw.WordId, out var f) &&
                                                 aw.ReadingIndex < f.ReadingsFrequencyRank.Count
                                                     ? f.ReadingsFrequencyRank[aw.ReadingIndex]
                                                     : int.MaxValue);
        }
        else
        {
            // Default to occurrences
            sortedWords = descending
                ? allAggregatedWords.OrderByDescending(w => w.TotalOccurrences)
                : allAggregatedWords.OrderBy(w => w.TotalOccurrences);
        }

        // Apply pagination
        var pagedWords = sortedWords.Skip(offset).Take(pageSize).ToList();

        var wordIds = pagedWords.Select(w => w.WordId).ToList();

        var jmdictWords = await jitenContext.JMDictWords
                                            .AsNoTracking()
                                            .Where(w => wordIds.Contains(w.WordId))
                                            .Include(w => w.Definitions)
                                            .ToListAsync();

        var jmdictLookup = jmdictWords.ToDictionary(w => w.WordId);

        var frequencies = await jitenContext.JmDictWordFrequencies
                                            .AsNoTracking()
                                            .Where(f => wordIds.Contains(f.WordId))
                                            .ToListAsync();
        var freqLookup = frequencies.ToDictionary(f => f.WordId);

        var wordDtos = new List<WordDto>();
        foreach (var pw in pagedWords)
        {
            if (!jmdictLookup.TryGetValue(pw.WordId, out var jmWord))
                continue;

            if (pw.ReadingIndex >= jmWord.ReadingsFurigana.Count)
                continue;

            var reading = jmWord.ReadingsFurigana[pw.ReadingIndex];
            freqLookup.TryGetValue(pw.WordId, out var freq);

            var alternativeReadings = jmWord.ReadingsFurigana
                                            .Select((r, i) => new ReadingDto
                                                              {
                                                                  Text = r, ReadingIndex = (byte)i, ReadingType = jmWord.ReadingTypes[i],
                                                                  FrequencyRank = freq?.ReadingsFrequencyRank[i] ?? 0,
                                                                  FrequencyPercentage = freq?.ReadingsFrequencyPercentage[i] ?? 0
                                                              })
                                            .ToList();
            alternativeReadings.RemoveAt(pw.ReadingIndex);

            var mainReading = new ReadingDto
                              {
                                  Text = reading, ReadingIndex = pw.ReadingIndex, ReadingType = jmWord.ReadingTypes[pw.ReadingIndex],
                                  FrequencyRank = freq?.ReadingsFrequencyRank[pw.ReadingIndex] ?? 0,
                                  FrequencyPercentage = freq?.ReadingsFrequencyPercentage[pw.ReadingIndex] ?? 0
                              };

            wordDtos.Add(new WordDto
                         {
                             WordId = jmWord.WordId, MainReading = mainReading, AlternativeReadings = alternativeReadings,
                             PartsOfSpeech = jmWord.PartsOfSpeech.ToHumanReadablePartsOfSpeech(),
                             Definitions = jmWord.Definitions.ToDefinitionDtos(), Occurrences = pw.TotalOccurrences,
                             PitchAccents = jmWord.PitchAccents
                         });
        }

        var knownStates = await userService.GetKnownWordsState(
                                                               wordDtos.Select(w => (w.WordId, w.MainReading.ReadingIndex)).ToList());
        wordDtos.ApplyKnownWordsState(knownStates);

        var dto = new AccomplishmentVocabularyDto { Words = wordDtos };

        return Ok(new PaginatedResponse<AccomplishmentVocabularyDto>(dto, totalCount, pageSize, offset));
    }

    #endregion

    #region Kanji Grid

    /// <summary>
    /// Get kanji grid data for a user profile.
    /// Returns all kanji ordered by frequency with user's scores.
    /// </summary>
    [HttpGet("profile/{username}/kanji-grid")]
    [AllowAnonymous]
    public async Task<IResult> GetKanjiGridByUsername(
        string username,
        [FromQuery] bool onlySeen = false)
    {
        var user = await userContext.Users
                                    .AsNoTracking()
                                    .FirstOrDefaultAsync(u => u.NormalizedUserName == username.ToUpperInvariant());

        if (user == null)
            return Results.NotFound(new { message = "Profile not found" });

        var targetUserId = user.Id;
        var currentUserId = userService.UserId;
        var isOwnProfile = currentUserId == targetUserId;

        if (!isOwnProfile)
        {
            var profile = await userContext.UserProfiles
                                           .AsNoTracking()
                                           .FirstOrDefaultAsync(p => p.UserId == targetUserId);

            if (profile is not { IsPublic: true })
                return Results.NotFound(new { message = "Profile not found" });
        }

        // Get all kanji ordered by frequency
        var redisDb = redis.GetDatabase();
        const string cacheKey = "jiten:kanji-grid:all-kanji";

        var cached = await redisDb.StringGetAsync(cacheKey);
        List<CachedKanjiInfo>? allKanji;

        if (!cached.IsNullOrEmpty)
        {
            allKanji = JsonSerializer.Deserialize<List<CachedKanjiInfo>>(cached!);
        }
        else
        {
            allKanji = await jitenContext.Kanjis
                                         .AsNoTracking()
                                         .OrderBy(k => k.FrequencyRank ?? int.MaxValue)
                                         .Select(k => new CachedKanjiInfo(k.Character, k.FrequencyRank, k.JlptLevel))
                                         .ToListAsync();

            var json = JsonSerializer.Serialize(allKanji);
            await redisDb.StringSetAsync(cacheKey, json, expiry: TimeSpan.FromHours(1));
        }

        var userGrid = await userContext.UserKanjiGrids
                                        .AsNoTracking()
                                        .FirstOrDefaultAsync(ukg => ukg.UserId == targetUserId);

        var userScores = userGrid?.GetKanjiScoresOnce() ?? new Dictionary<string, double[]>();
        var maxThreshold = double.Parse(configuration["KanjiGrid:MaxScoreThreshold"] ?? "10.0");

        var kanjiData = allKanji!
                        .Where(k => !onlySeen || userScores.ContainsKey(k.Character))
                        .Select(k =>
                        {
                            userScores.TryGetValue(k.Character, out var scoreData);
                            return new KanjiGridItemDto
                                   {
                                       Character = k.Character, FrequencyRank = k.FrequencyRank, JlptLevel = k.JlptLevel,
                                       Score = scoreData?[0] ?? 0, WordCount = (int)(scoreData?[1] ?? 0)
                                   };
                        })
                        .ToList();

        return Results.Ok(new KanjiGridResponseDto
                          {
                              Kanji = kanjiData, MaxScoreThreshold = maxThreshold, TotalKanjiCount = allKanji!.Count,
                              SeenKanjiCount = userScores.Count, LastComputedAt = userGrid?.LastComputedAt
                          });
    }

    private record CachedKanjiInfo(string Character, int? FrequencyRank, short? JlptLevel);
    private record ReadingFrequencyResult(int WordId, short ReadingIndex, int FrequencyRank);

    private static KnownState? ComputeEffectiveCategory(FsrsState state, DateTime due, DateTime? lastReview, DateTime now)
    {
        switch (state)
        {
            case FsrsState.Mastered: return KnownState.Mastered;
            case FsrsState.Blacklisted: return KnownState.Blacklisted;
            case FsrsState.New: return null;
        }

        if (lastReview == null)
            return null;

        var interval = (due - lastReview.Value).TotalDays;
        return interval < 21 ? KnownState.Young : KnownState.Mature;
    }

    #endregion
}
