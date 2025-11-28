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
    ISrsService srsService,
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

        backgroundJobs.Enqueue<ComputationJob>(job => job.ComputeUserCoverage(userId));

        logger.LogInformation("User cleared all known words: UserId={UserId}, RemovedCount={RemovedCount}, RemovedLogsCount={RemovedLogsCount}",
                              userId, cards.Count, reviewLogs.Count);
        return Results.Ok(new { removed = cards.Count, removedLogs = reviewLogs.Count });
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

        var combinedText = string.Join(Environment.NewLine, uniqueWords);
        var parsedWords = await Parser.Parser.ParseText(contextFactory, combinedText);

        // Create lookup: original text → (WordId, ReadingIndex)
        var wordLookup = new Dictionary<string, (int WordId, byte ReadingIndex)>();
        foreach (var parsed in parsedWords)
        {
            if (!wordLookup.ContainsKey(parsed.OriginalText))
            {
                wordLookup[parsed.OriginalText] = (parsed.WordId, parsed.ReadingIndex);
            }
        }

        // Track skipped words for user feedback
        var skippedWords = new List<string>();

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
        int skippedCount = 0;

        var processedPairs = new Dictionary<(int WordId, byte ReadingIndex), (FsrsCard Card, List<AnkiReviewLogImport> AllReviewLogs)>();

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

            backgroundJobs.Enqueue<ComputationJob>(job => job.ComputeUserCoverage(userId));

            logger.LogInformation(
                                  "Anki import completed: UserId={UserId}, Imported={Imported}, Updated={Updated}, Skipped={Skipped}, Logs={Logs}, NotFound={NotFound}",
                                  userId, cardsToAdd.Count, cardsToUpdate.Count, skippedCount, logsToAdd.Count, skippedWords.Count
                                 );

            return Results.Ok(new
                              {
                                  imported = cardsToAdd.Count, updated = cardsToUpdate.Count, skipped = skippedCount,
                                  reviewLogs = logsToAdd.Count, skippedWords = skippedWords.Take(50).ToList()
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

        if (preference == null)
        {
            preference = new UserDeckPreference { UserId = userId, DeckId = deckId };
            userContext.UserDeckPreferences.Add(preference);
        }

        preference.Status = request.Status;
        await userContext.SaveChangesAsync();

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
                                    Stability = c.Stability, Difficulty = c.Difficulty,
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

                    foreach (var logDto in uniqueIncomingLogs )
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
}