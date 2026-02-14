using Hangfire;
using Jiten.Core;
using Jiten.Core.Data;
using Jiten.Core.Services;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text.Json;

namespace Jiten.Api.Jobs;

public class StatsComputationJob(IDbContextFactory<JitenDbContext> contextFactory)
{
    /// <summary>
    /// Compute coverage statistics for a single deck
    /// </summary>
    /// <param name="deckId">Deck ID</param>
    [Queue("stats")]
    public async Task ComputeDeckCoverageStats(int deckId)
    {
        await using var context = await contextFactory.CreateDbContextAsync();

        // Fetch all DeckWords for this deck, grouped by (WordId, ReadingIndex)
        var deckWords = await context.DeckWords
            .Where(dw => dw.DeckId == deckId)
            .GroupBy(dw => new { dw.WordId, dw.ReadingIndex })
            .Select(g => new
            {
                g.Key.WordId,
                g.Key.ReadingIndex,
                TotalOccurrences = g.Sum(dw => dw.Occurrences)
            })
            .OrderByDescending(w => w.TotalOccurrences)
            .ThenBy(w => w.WordId)
            .ToListAsync();

        if (deckWords.Count == 0)
        {
            Console.WriteLine($"No words found for deck {deckId}, skipping coverage stats");
            return;
        }

        // Compute cumulative occurrences
        int totalOccurrences = deckWords.Sum(w => w.TotalOccurrences);
        int cumulativeOccurrences = 0;

        var rankOccurrencePairs = new List<(int rank, int cumulativeOccurrences)>();
        var samples = new List<double[]>();

        double nextMilestone = 0.0; // Start at 0%

        for (int i = 0; i < deckWords.Count; i++)
        {
            cumulativeOccurrences += deckWords[i].TotalOccurrences;
            int currentRank = i + 1;
            double currentCoverage = (double)cumulativeOccurrences / totalOccurrences * 100.0;

            rankOccurrencePairs.Add((currentRank, cumulativeOccurrences));

            // Determine sampling resolution based on coverage region
            double step = currentCoverage >= 99.0 ? 0.1 : 1.0;

            // Capture sample points at milestones
            if (currentCoverage >= nextMilestone)
            {
                samples.Add([currentRank, currentCoverage]);

                // Advance to next milestone (handle cases where one word jumps multiple %)
                while (nextMilestone <= currentCoverage)
                {
                    nextMilestone += step;

                    // Switch to finer resolution at 99%
                    if (nextMilestone >= 99.0 && step == 1.0)
                    {
                        step = 0.1;
                        nextMilestone = 99.0; // Reset to start of fine-grained region
                    }
                }
            }
        }

        // Always ensure 100% is the final point
        if (samples.Count == 0 || samples[^1][1] < 100.0)
        {
            samples.Add(new double[] { deckWords.Count, 100.0 });
        }

        // Fit power law curve
        var (A, B, C, rSquared, rmse) = CoverageCurveFitter.FitPowerLaw(rankOccurrencePairs, totalOccurrences);

        // Format as CSV string: "A,B,C,RSquared,RMSE,TotalWords"
        var coverageCurveString = string.Format(
            CultureInfo.InvariantCulture,
            "{0},{1},{2},{3},{4},{5}",
            A, B, C, rSquared, rmse, deckWords.Count
        );

        // Serialise sampled coverage data
        var coverageSamplesJson = JsonSerializer.Serialize(samples);

        // Upsert DeckStats
        var existingStats = await context.DeckStats.FindAsync(deckId);

        if (existingStats != null)
        {
            // Update existing
            existingStats.CoverageCurve = coverageCurveString;
            existingStats.CoverageSamplesJson = coverageSamplesJson;
            existingStats.ComputedAt = DateTimeOffset.UtcNow;
        }
        else
        {
            // Create new
            var newStats = new DeckStats
            {
                DeckId = deckId,
                CoverageCurve = coverageCurveString,
                CoverageSamplesJson = coverageSamplesJson,
                ComputedAt = DateTimeOffset.UtcNow
            };

            await context.DeckStats.AddAsync(newStats);
        }

        await context.SaveChangesAsync();

        Console.WriteLine($"Computed coverage stats for deck {deckId}: R²={rSquared:F4}, RMSE={rmse:F2}, Samples={samples.Count}, Total={deckWords.Count} words");
    }
}
