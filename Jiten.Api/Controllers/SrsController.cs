using Jiten.Api.Dtos.Requests;
using Jiten.Api.Services;
using Jiten.Core;
using Jiten.Core.Data.FSRS;
using Jiten.Core.Data.JMDict;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Swashbuckle.AspNetCore.Annotations;

namespace Jiten.Api.Controllers;

[ApiController]
[Route("api/srs")]
[Authorize]
public class SrsController(
    JitenDbContext context,
    UserDbContext userContext,
    ICurrentUserService currentUserService,
    ISrsService srsService,
    ISrsDebounceService debounceService,
    ILogger<SrsController> logger) : ControllerBase
{
    /// <summary>
    /// Rate (review) a word using the FSRS scheduler.
    /// </summary>
    /// <param name="request">A request containing the word to review and a rating</param>
    /// <returns>Status.</returns>
    [HttpPost("review")]
    [SwaggerOperation(Summary = "Review a FSRS card",
                      Description = "Rate (review) a word using the FSRS scheduler.")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IResult> Review(SrsReviewRequest request)
    {
        if (!debounceService.TryAcquire(currentUserService.UserId!, request.WordId, request.ReadingIndex))
        {
            return Results.StatusCode(StatusCodes.Status429TooManyRequests);
        }

        var card = await userContext.FsrsCards.FirstOrDefaultAsync(c => c.UserId == currentUserService.UserId &&
                                                                        c.WordId == request.WordId &&
                                                                        c.ReadingIndex == request.ReadingIndex);

        // TODO: customize the scheduler to the user
        var scheduler = new FsrsScheduler();
        if (card == null)
        {
            card = new FsrsCard(currentUserService.UserId!, request.WordId, request.ReadingIndex);
        }

        var cardAndLog = scheduler.ReviewCard(card, request.Rating, DateTime.UtcNow);

        if (card.CardId == 0)
        {
            await userContext.FsrsCards.AddAsync(cardAndLog.UpdatedCard);
            await userContext.SaveChangesAsync();

            cardAndLog.ReviewLog.CardId = cardAndLog.UpdatedCard.CardId;
        }
        else
        {
            userContext.Entry(card).CurrentValues.SetValues(cardAndLog.UpdatedCard);
            cardAndLog.ReviewLog.CardId = card.CardId;
        }

        await userContext.FsrsReviewLogs.AddAsync(cardAndLog.ReviewLog);
        await userContext.SaveChangesAsync();

        // Sync kana reading if this is a kanji reading card
        await SyncKanaReadingCard(request.WordId, request.ReadingIndex, cardAndLog.UpdatedCard, DateTime.UtcNow);

        logger.LogInformation("User reviewed SRS card: WordId={WordId}, ReadingIndex={ReadingIndex}, Rating={Rating}, NewState={NewState}",
                              request.WordId, request.ReadingIndex, request.Rating, cardAndLog.UpdatedCard.State);
        return Results.Json(new { success = true });
    }

    [HttpPost("set-vocabulary-state")]
    [SwaggerOperation(Summary = "Set vocabulary state",
                      Description = "Set vocabulary state to neverForget or blacklist.")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IResult> SetVocabularyState(SetVocabularyStateRequest request)
    {
        var userId = currentUserService.UserId;

        if (!debounceService.TryAcquire(userId!, request.WordId, request.ReadingIndex))
        {
            return Results.StatusCode(StatusCodes.Status429TooManyRequests);
        }

        var card = await userContext.FsrsCards.FirstOrDefaultAsync(c =>
                                                                       c.UserId == userId &&
                                                                       c.WordId == request.WordId &&
                                                                       c.ReadingIndex == request.ReadingIndex);

        switch (request.State)
        {
            case "neverForget-add":
                if (card == null)
                {
                    card = new FsrsCard(userId!, request.WordId, request.ReadingIndex,
                                        due: DateTime.UtcNow,
                                        lastReview: DateTime.UtcNow,
                                        state: FsrsState.Mastered);
                    await userContext.FsrsCards.AddAsync(card);
                }
                else
                {
                    card.State = FsrsState.Mastered;
                }

                break;

            case "neverForget-remove":
                if (card != null && card.State == FsrsState.Mastered)
                    RestoreCardState(card);
                break;

            case "blacklist-add":
                if (card == null)
                {
                    card = new FsrsCard(userId!, request.WordId, request.ReadingIndex,
                                        state: FsrsState.Blacklisted);
                    await userContext.FsrsCards.AddAsync(card);
                }
                else
                {
                    card.State = FsrsState.Blacklisted;
                }

                break;

            case "blacklist-remove":
                if (card != null && card.State == FsrsState.Blacklisted)
                    RestoreCardState(card);
                break;

            case "forget-add":
                if (card != null)
                {
                    userContext.FsrsCards.Remove(card);
                }

                break;

            default:
                return Results.BadRequest($"Invalid state: {request.State}");
        }

        await userContext.SaveChangesAsync();

        // Sync kana reading for vocabulary state changes
        await SyncVocabularyStateToKana(request.WordId, request.ReadingIndex, request.State, card);

        logger.LogInformation("User set vocabulary state: WordId={WordId}, ReadingIndex={ReadingIndex}, State={State}",
                              request.WordId, request.ReadingIndex, request.State);
        return Results.Json(new { success = true });

        void RestoreCardState(FsrsCard card)
        {
            if (card.Stability is > 0)
            {
                card.State = FsrsState.Review;
            }
            else
            {
                card.State = FsrsState.Learning;
                card.Step = 0;
            }
        }
    }

    /// <summary>
    /// Syncs FSRS state from a kanji reading card to its corresponding kana reading card.
    /// Only syncs if the source card is a Reading type and a KanaReading variant exists.
    /// </summary>
    private async Task SyncKanaReadingCard(int wordId, byte readingIndex, FsrsCard sourceCard, DateTime reviewDateTime)
    {
        await srsService.SyncKanaReading(currentUserService.UserId!, wordId, readingIndex, sourceCard, reviewDateTime);
    }

    /// <summary>
    /// Syncs vocabulary state changes to the corresponding kana reading.
    /// Handles neverForget and blacklist operations.
    /// </summary>
    private async Task SyncVocabularyStateToKana(int wordId, byte readingIndex, string state, FsrsCard? sourceCard)
    {
        // Fetch word reading types
        var jmdictWord = await context.JMDictWords
                                      .AsNoTracking()
                                      .Where(w => w.WordId == wordId)
                                      .Select(w => new { w.WordId, w.ReadingTypes })
                                      .FirstOrDefaultAsync();

        if (jmdictWord == null) return;
        if (readingIndex >= jmdictWord.ReadingTypes.Count) return;

        // Only sync from Reading (kanji) to KanaReading
        if (jmdictWord.ReadingTypes[readingIndex] != JmDictReadingType.Reading) return;

        // Find kana reading index
        var kanaIndex = jmdictWord.ReadingTypes.FindIndex(t => t == JmDictReadingType.KanaReading);
        if (kanaIndex < 0) return; // No kana variant exists

        var userId = currentUserService.UserId;

        switch (state)
        {
            case "neverForget-add":
                if (sourceCard != null)
                {
                    await SyncKanaReadingCard(wordId, readingIndex, sourceCard, DateTime.UtcNow);
                }

                break;

            case "neverForget-remove":
                // Remove kana card if it exists
                var kanaCardToRemove = await userContext.FsrsCards
                                                        .FirstOrDefaultAsync(c => c.UserId == userId &&
                                                                                  c.WordId == wordId &&
                                                                                  c.ReadingIndex == (byte)kanaIndex);
                if (kanaCardToRemove != null)
                {
                    userContext.FsrsCards.Remove(kanaCardToRemove);
                    await userContext.SaveChangesAsync();
                    logger.LogInformation("Removed synced kana reading: WordId={WordId}, KanaIndex={KanaIndex}",
                                          wordId, kanaIndex);
                }

                break;

            case "blacklist-add":
                if (sourceCard != null)
                {
                    await SyncKanaReadingCard(wordId, readingIndex, sourceCard, DateTime.UtcNow);
                }

                break;

            case "blacklist-remove":
                // Update kana card to Review state if it exists
                var kanaCardToUpdate = await userContext.FsrsCards
                                                        .FirstOrDefaultAsync(c => c.UserId == userId &&
                                                                                  c.WordId == wordId &&
                                                                                  c.ReadingIndex == (byte)kanaIndex);
                if (kanaCardToUpdate != null)
                {
                    kanaCardToUpdate.State = FsrsState.Review;
                    await userContext.SaveChangesAsync();
                    logger.LogInformation("Updated synced kana reading to Review: WordId={WordId}, KanaIndex={KanaIndex}",
                                          wordId, kanaIndex);
                }

                break;

            case "forget-add":
                var kanaCardToForget = await userContext.FsrsCards
                                                        .FirstOrDefaultAsync(c => c.UserId == userId &&
                                                                                  c.WordId == wordId &&
                                                                                  c.ReadingIndex == (byte)kanaIndex);
                if (kanaCardToForget != null)
                {
                    userContext.FsrsCards.Remove(kanaCardToForget);
                    await userContext.SaveChangesAsync();
                    logger.LogInformation("Removed synced kana reading (forget): WordId={WordId}, KanaIndex={KanaIndex}",
                                          wordId, kanaIndex);
                }

                break;
        }
    }
}