using Jiten.Api.Dtos.Requests;
using Jiten.Api.Services;
using Jiten.Core;
using Jiten.Core.Data.FSRS;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Swashbuckle.AspNetCore.Annotations;

namespace Jiten.Api.Controllers;

[ApiController]
[Route("api/srs")]
[Authorize]
public class SrsController(JitenDbContext context, UserDbContext userContext, ICurrentUserService currentUserService, ILogger<SrsController> logger) : ControllerBase
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
    public async Task<IResult> Review(SrsReviewRequest request)
    {
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
    public async Task<IResult> SetVocabularyState(SetVocabularyStateRequest request)
    {
        var userId = currentUserService.UserId;
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
                                        due: DateTime.UtcNow.AddYears(100),
                                        lastReview: DateTime.UtcNow,
                                        state: FsrsState.Review);
                    await userContext.FsrsCards.AddAsync(card);
                }
                else
                {
                    // Fallback remove, TODO get better logic
                    if (card.Due > DateTime.UtcNow.AddYears(90))
                    {
                        userContext.FsrsCards.Remove(card);
                        break;
                    }

                    card.Due = DateTime.UtcNow.AddYears(100);
                    card.State = FsrsState.Review;
                }

                break;

            case "neverForget-remove":
                if (card != null)
                    userContext.FsrsCards.Remove(card);
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
                    // Fallback remove TODO get better logic
                    if (card.State == FsrsState.Blacklisted)
                    {
                        card.State = FsrsState.Review;
                        break;
                    }
                    
                    card.State = FsrsState.Blacklisted;
                }

                break;

            case "blacklist-remove":
                if (card != null)
                    card.State = FsrsState.Review;
                break;

            default:
                return Results.BadRequest($"Invalid state: {request.State}");
        }

        await userContext.SaveChangesAsync();
        logger.LogInformation("User set vocabulary state: WordId={WordId}, ReadingIndex={ReadingIndex}, State={State}",
            request.WordId, request.ReadingIndex, request.State);
        return Results.Json(new { success = true });
    }
}