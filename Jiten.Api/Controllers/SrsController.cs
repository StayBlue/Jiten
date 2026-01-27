using System.Globalization;
using Jiten.Api.Dtos;
using Jiten.Api.Dtos.Requests;
using Jiten.Api.Jobs;
using Jiten.Api.Services;
using Jiten.Core;
using Jiten.Core.Data.FSRS;
using Jiten.Core.Data.JMDict;
using Jiten.Core.Data.User;
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
    SrsRecomputeJob recomputeJob,
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

        var userSettings = await LoadUserSettings(currentUserService.UserId!);
        var parameters = GetParameters(userSettings);
        var desiredRetention = GetDesiredRetention(userSettings);
        var scheduler = new FsrsScheduler(desiredRetention: desiredRetention, parameters: parameters);
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

    [HttpGet("settings")]
    [SwaggerOperation(Summary = "Get FSRS settings",
                      Description = "Get the user's FSRS settings.")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IResult> GetParameters()
    {
        var userId = currentUserService.UserId;
        if (userId == null)
        {
            return Results.Unauthorized();
        }

        var userSettings = await LoadUserSettings(userId);
        var parameters = GetParameters(userSettings);
        var desiredRetention = GetDesiredRetention(userSettings);
        var isDefault = IsSettingsDefault(parameters, desiredRetention);
        var response = new FsrsParametersResponse
        {
            Parameters = SerializeParametersCsv(parameters),
            IsDefault = isDefault,
            DesiredRetention = desiredRetention
        };

        return Results.Ok(response);
    }

    [HttpPut("settings")]
    [SwaggerOperation(Summary = "Update FSRS settings",
                      Description = "Update the user's FSRS settings.")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IResult> UpdateParameters(UpdateFsrsParametersRequest request)
    {
        var userId = currentUserService.UserId;
        if (userId == null)
        {
            return Results.Unauthorized();
        }

        var settings = await userContext.UserFsrsSettings.FirstOrDefaultAsync(s => s.UserId == userId);
        var currentParameters = GetParameters(settings);
        var currentDesiredRetention = GetDesiredRetention(settings);

        var nextParameters = currentParameters;
        var nextDesiredRetention = currentDesiredRetention;

        if (request.Parameters != null)
        {
            if (string.IsNullOrWhiteSpace(request.Parameters))
            {
                nextParameters = FsrsConstants.DefaultParameters;
            }
            else if (!TryParseParametersCsv(request.Parameters, out var parsedParameters, out var error))
            {
                return Results.BadRequest(error);
            }
            else
            {
                nextParameters = parsedParameters;
            }
        }

        if (request.DesiredRetention.HasValue)
        {
            if (!IsDesiredRetentionValid(request.DesiredRetention.Value))
            {
                return Results.BadRequest("Desired retention must be between 0 and 1.");
            }

            nextDesiredRetention = request.DesiredRetention.Value;
        }

        var parametersAreDefault = AreParametersDefault(nextParameters);
        var desiredRetentionIsDefault = IsDesiredRetentionDefault(nextDesiredRetention);
        var shouldRemoveSettings = parametersAreDefault && desiredRetentionIsDefault;

        if (shouldRemoveSettings)
        {
            if (settings != null)
            {
                userContext.UserFsrsSettings.Remove(settings);
                await userContext.SaveChangesAsync();
            }
        }
        else
        {
            if (settings == null)
            {
                settings = new UserFsrsSettings { UserId = userId };
                userContext.UserFsrsSettings.Add(settings);
            }

            settings.Parameters = nextParameters;
            settings.DesiredRetention = desiredRetentionIsDefault ? null : nextDesiredRetention;
            await userContext.SaveChangesAsync();
        }

        return Results.Ok(new FsrsParametersResponse
        {
            Parameters = SerializeParametersCsv(nextParameters),
            IsDefault = shouldRemoveSettings,
            DesiredRetention = nextDesiredRetention
        });
    }

    [HttpPost("settings/recompute")]
    [SwaggerOperation(Summary = "Recompute FSRS scheduling",
                      Description = "Recompute scheduling for all cards using the stored settings (or defaults).")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IResult> RecomputeParameters()
    {
        var userId = currentUserService.UserId;
        if (userId == null)
        {
            return Results.Unauthorized();
        }

        var userSettings = await LoadUserSettings(userId);
        var parameters = GetParameters(userSettings);
        var desiredRetention = GetDesiredRetention(userSettings);
        await recomputeJob.RecomputeUserSrs(userId, parameters, desiredRetention);

        return Results.Ok(new { success = true });
    }

    [HttpPost("settings/recompute-batch")]
    [SwaggerOperation(Summary = "Recompute FSRS scheduling batch",
                      Description = "Recompute scheduling in batches for progress tracking.")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IResult> RecomputeParametersBatch([FromQuery] long lastCardId = 0, [FromQuery] int batchSize = 500)
    {
        var userId = currentUserService.UserId;
        if (userId == null)
        {
            return Results.Unauthorized();
        }

        if (batchSize < 1 || batchSize > 2000)
        {
            batchSize = 500;
        }

        var userSettings = await LoadUserSettings(userId);
        var parameters = GetParameters(userSettings);
        var desiredRetention = GetDesiredRetention(userSettings);
        var result = await recomputeJob.RecomputeUserSrsBatch(userId, parameters, desiredRetention, lastCardId, batchSize);

        return Results.Ok(result);
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

    private async Task<UserFsrsSettings?> LoadUserSettings(string userId)
    {
        return await userContext.UserFsrsSettings.AsNoTracking()
                                                 .FirstOrDefaultAsync(s => s.UserId == userId);
    }

    private static bool AreParametersDefault(double[] parameters)
    {
        if (parameters.Length != FsrsConstants.DefaultParameters.Length)
        {
            return false;
        }

        for (var i = 0; i < parameters.Length; i++)
        {
            if (Math.Abs(parameters[i] - FsrsConstants.DefaultParameters[i]) > 1e-6)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsDesiredRetentionDefault(double desiredRetention)
    {
        return Math.Abs(desiredRetention - FsrsConstants.DefaultDesiredRetention) < 1e-6;
    }

    private static bool IsSettingsDefault(double[] parameters, double desiredRetention)
    {
        return AreParametersDefault(parameters) && IsDesiredRetentionDefault(desiredRetention);
    }

    private static bool IsDesiredRetentionValid(double desiredRetention)
    {
        return desiredRetention > 0 && desiredRetention < 1 && !double.IsNaN(desiredRetention) && !double.IsInfinity(desiredRetention);
    }

    private static double[] GetParameters(UserFsrsSettings? settings)
    {
        return TryGetStoredParameters(settings, out var parameters) ? parameters : FsrsConstants.DefaultParameters;
    }

    private static double GetDesiredRetention(UserFsrsSettings? settings)
    {
        if (settings?.DesiredRetention is double desiredRetention && IsDesiredRetentionValid(desiredRetention))
        {
            return desiredRetention;
        }

        return FsrsConstants.DefaultDesiredRetention;
    }

    private static bool TryGetStoredParameters(UserFsrsSettings? settings, out double[] parameters)
    {
        parameters = Array.Empty<double>();
        if (settings == null)
        {
            return false;
        }

        var stored = settings.GetParametersOnce();
        if (stored.Length != FsrsConstants.DefaultParameters.Length)
        {
            return false;
        }

        if (stored.Any(value => double.IsNaN(value) || double.IsInfinity(value)))
        {
            return false;
        }

        parameters = stored;
        return true;
    }

    private static bool TryParseParametersCsv(string? csv, out double[] parameters, out string error)
    {
        parameters = Array.Empty<double>();
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(csv))
        {
            error = "Parameters are required.";
            return false;
        }

        var parts = csv.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var expectedCount = FsrsConstants.DefaultParameters.Length;
        if (parts.Length != expectedCount)
        {
            error = $"Parameters must contain {expectedCount} comma-separated values.";
            return false;
        }

        var parsed = new double[expectedCount];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
                double.IsNaN(value) || double.IsInfinity(value))
            {
                error = $"Invalid parameter at position {i + 1}.";
                return false;
            }

            parsed[i] = value;
        }

        parameters = parsed;
        return true;
    }

    private static string SerializeParametersCsv(double[] parameters)
    {
        return string.Join(", ", parameters.Select(value => value.ToString("0.####", CultureInfo.InvariantCulture)));
    }
}
