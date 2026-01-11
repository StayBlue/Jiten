namespace Jiten.Core.Data.FSRS;

public class FsrsScheduler
{
    /// <summary>
    /// FSRS algorithm parameters
    /// </summary>
    public double[] Parameters { get; }

    /// <summary>
    /// Target retention rate (0-1)
    /// </summary>
    public double DesiredRetention { get; }

    /// <summary>
    /// Learning step intervals for new cards
    /// </summary>
    public TimeSpan[] LearningSteps { get; }

    /// <summary>
    /// Relearning step intervals for forgotten cards
    /// </summary>
    public TimeSpan[] RelearningSteps { get; }

    /// <summary>
    /// Maximum review interval in days
    /// </summary>
    public int MaximumInterval { get; }

    /// <summary>
    /// Whether to apply interval fuzzing
    /// </summary>
    public bool EnableFuzzing { get; }


    /// <summary>
    /// Creates a new FSRS scheduler with specified configuration
    /// </summary>
    /// <param name="parameters">Custom FSRS parameters (uses defaults if null)</param>
    /// <param name="desiredRetention">Target retention rate (default: 0.9)</param>
    /// <param name="learningSteps">Learning intervals (default: 1min, 10min)</param>
    /// <param name="relearningSteps">Relearning intervals (default: 10min)</param>
    /// <param name="maximumInterval">Max interval in days (default: 36500)</param>
    /// <param name="enableFuzzing">Enable interval randomization (default: true)</param>
    public FsrsScheduler(
        double desiredRetention = 0.9,
        double[]? parameters = null,
        TimeSpan[]? learningSteps = null,
        TimeSpan[]? relearningSteps = null,
        int maximumInterval = 36500,
        bool enableFuzzing = true)
    {
        Parameters = parameters ?? FsrsConstants.DefaultParameters;
        DesiredRetention = desiredRetention;
        LearningSteps = learningSteps ?? [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(10)];
        RelearningSteps = relearningSteps ?? [TimeSpan.FromMinutes(10)];
        MaximumInterval = maximumInterval;
        EnableFuzzing = enableFuzzing;
    }

    /// <summary>
    /// Gets the current retrievability (recall probability) of a card
    /// </summary>
    /// <param name="card">Card to check</param>
    /// <param name="currentDateTime">Current time (optional)</param>
    /// <returns>Retrievability between 0 and 1</returns>
    public double GetCardRetrievability(FsrsCard card, DateTime? currentDateTime = null)
    {
        return FsrsHelper.CalculateRetrievability(card, currentDateTime, Parameters);
    }

    /// <summary>
    /// Processes a card review and returns updated card and review log
    /// </summary>
    /// <param name="card">Card being reviewed</param>
    /// <param name="rating">User's performance rating</param>
    /// <param name="reviewDateTime">When the review occurred (optional)</param>
    /// <param name="reviewDuration">How long the review took in ms (optional)</param>
    /// <returns>Tuple of updated card and review log</returns>
    public (FsrsCard UpdatedCard, FsrsReviewLog ReviewLog) ReviewCard(FsrsCard card,
                                                                  FsrsRating rating,
                                                                  DateTime? reviewDateTime = null,
                                                                  int? reviewDuration = null)
    {
        reviewDateTime ??= DateTime.UtcNow;

        if (reviewDateTime.Value.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Review datetime must be UTC");

        var updatedCard = card.Clone();

        var daysSinceLastReview = updatedCard.LastReview != null
            ? (reviewDateTime.Value - updatedCard.LastReview.Value).Days
            : (int?)null;

        ProcessCardReview(updatedCard, rating, reviewDateTime.Value, daysSinceLastReview);

        var reviewLog = new FsrsReviewLog(updatedCard.CardId, rating, reviewDateTime.Value, reviewDuration);

        return (updatedCard, reviewLog);
    }

    private void ProcessCardReview(FsrsCard card, FsrsRating rating, DateTime reviewDateTime, int? daysSinceLastReview)
    {
        UpdateCardParameters(card, rating, reviewDateTime, daysSinceLastReview);
        var nextInterval = CalculateNextInterval(card, rating);

        if (EnableFuzzing && card.State == FsrsState.Review)
        {
            nextInterval = FsrsHelper.ApplyFuzzing(nextInterval, MaximumInterval);
        }

        card.Due = reviewDateTime + nextInterval;
        card.LastReview = reviewDateTime;
    }

    private void UpdateCardParameters(FsrsCard card, FsrsRating rating, DateTime reviewDateTime, int? daysSinceLastReview)
    {
        var stability = card.Stability ?? 1.0d;
        var difficulty = card.Difficulty ?? 1.0d;
        if (card is { State: FsrsState.Learning, Stability: null, Difficulty: null })
        {
            card.Stability = FsrsHelper.CalculateInitialStability(rating, Parameters);
            card.Difficulty = FsrsHelper.CalculateInitialDifficulty(rating, Parameters);
        }
        else if (daysSinceLastReview is < 1)
        {

            
            card.Stability = FsrsHelper.CalculateShortTermStability(stability, rating, Parameters);
            card.Difficulty = FsrsHelper.CalculateNextDifficulty(difficulty, rating, Parameters);
        }
        else
        {
            var retrievability = GetCardRetrievability(card, reviewDateTime);
            card.Stability = FsrsHelper.CalculateNextStability(
                                                               difficulty, stability, retrievability, rating,
                                                               Parameters);
            card.Difficulty = FsrsHelper.CalculateNextDifficulty(difficulty, rating, Parameters);
        }
    }

    private TimeSpan CalculateNextInterval(FsrsCard card, FsrsRating rating)
    {
        return card.State switch
        {
            FsrsState.Learning => CalculateLearningInterval(card, rating),
            FsrsState.Review => CalculateReviewInterval(card, rating),
            FsrsState.Relearning => CalculateRelearningInterval(card, rating),
            FsrsState.Blacklisted => TimeSpan.MaxValue,
            FsrsState.Mastered => TimeSpan.Zero, 
            FsrsState.New => CalculateRelearningInterval(card, rating),
            _ => throw new ArgumentException($"Unknown card state: {card.State}")
        };
    }

    private TimeSpan CalculateLearningInterval(FsrsCard card, FsrsRating rating)
    {
        var stability = card.Stability ?? 1.0d;
        
        if (LearningSteps.Length == 0 || (card.Step >= LearningSteps.Length && rating != FsrsRating.Again))
        {
            card.State = FsrsState.Review;
            card.Step = null;
            var days = FsrsHelper.CalculateNextInterval(stability, DesiredRetention, Parameters, MaximumInterval);
            return TimeSpan.FromDays(days);
        }

        return rating switch
        {
            FsrsRating.Again => HandleAgainRating(card, LearningSteps),
            FsrsRating.Hard => HandleHardRating(card, LearningSteps),
            FsrsRating.Good => HandleGoodRating(card, LearningSteps, FsrsState.Review),
            FsrsRating.Easy => HandleEasyRating(card),
            _ => throw new ArgumentException($"Unknown rating: {rating}")
        };
    }

    private TimeSpan CalculateReviewInterval(FsrsCard card, FsrsRating rating)
    {
        var stability = card.Stability ?? 1.0d;
        
        if (rating == FsrsRating.Again)
        {
            if (RelearningSteps.Length == 0)
            {
                var days = FsrsHelper.CalculateNextInterval(stability, DesiredRetention, Parameters, MaximumInterval);
                return TimeSpan.FromDays(days);
            }

            card.State = FsrsState.Relearning;
            card.Step = 0;
            return RelearningSteps[0];
        }

        var intervalDays = FsrsHelper.CalculateNextInterval(stability, DesiredRetention, Parameters, MaximumInterval);
        return TimeSpan.FromDays(intervalDays);
    }

    private TimeSpan CalculateRelearningInterval(FsrsCard card, FsrsRating rating)
    {
        var stability = card.Stability ?? 1.0d;
        
        if (RelearningSteps.Length == 0 || (card.Step >= RelearningSteps.Length && rating != FsrsRating.Again))
        {
            card.State = FsrsState.Review;
            card.Step = null;
            var days = FsrsHelper.CalculateNextInterval(stability, DesiredRetention, Parameters, MaximumInterval);
            return TimeSpan.FromDays(days);
        }

        return rating switch
        {
            FsrsRating.Again => HandleAgainRating(card, RelearningSteps),
            FsrsRating.Hard => HandleHardRating(card, RelearningSteps),
            FsrsRating.Good => HandleGoodRating(card, RelearningSteps, FsrsState.Review),
            FsrsRating.Easy => HandleEasyRating(card),
            _ => throw new ArgumentException($"Unknown rating: {rating}")
        };
    }

    private TimeSpan HandleAgainRating(FsrsCard card, TimeSpan[] steps)
    {
        card.Step = 0;
        return steps[0];
    }

    private TimeSpan HandleHardRating(FsrsCard card, TimeSpan[] steps)
    {
        if (card.Step == 0 && steps.Length == 1)
            return TimeSpan.FromTicks((long)(steps[0].Ticks * 1.5));

        if (card.Step == 0 && steps.Length >= 2)
            return TimeSpan.FromTicks((steps[0].Ticks + steps[1].Ticks) / 2);

        return steps[card.Step!.Value];
    }

    private TimeSpan HandleGoodRating(FsrsCard card, TimeSpan[] steps, FsrsState nextState)
    {
        var stability = card.Stability ?? 1.0d;
        
        if (card.Step + 1 == steps.Length)
        {
            card.State = nextState;
            card.Step = null;
            var days = FsrsHelper.CalculateNextInterval(stability, DesiredRetention, Parameters, MaximumInterval);

            return TimeSpan.FromDays(days);
        }

        card.Step++;
        return steps[card.Step.Value];
    }

    private TimeSpan HandleEasyRating(FsrsCard card)
    {
        var stability = card.Stability ?? 1.0d;
        
        card.State = FsrsState.Review;
        card.Step = null;
        var days = FsrsHelper.CalculateNextInterval(stability, DesiredRetention, Parameters, MaximumInterval);
        return TimeSpan.FromDays(days);
    }
}