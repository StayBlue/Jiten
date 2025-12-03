using Jiten.Core.Services;
using System.Text.Json;

namespace Jiten.Core.Data;

/// <summary>
/// Extension methods for DeckStats entity
/// </summary>
public static class DeckStatsExtensions
{
    /// <summary>
    /// Get coverage percentage at a specific word rank
    /// </summary>
    /// <param name="stats">DeckStats instance</param>
    /// <param name="rank">Word rank (1-based)</param>
    /// <returns>Coverage percentage (0-100)</returns>
    public static double GetCoverageAtRank(this DeckStats stats, int rank)
    {
        if (!stats.ParameterA.HasValue || !stats.ParameterB.HasValue || !stats.ParameterC.HasValue)
            return 0;
        return CoverageCurveFitter.GetCoverageAtRank(rank, stats.ParameterA.Value, stats.ParameterB.Value, stats.ParameterC.Value);
    }

    /// <summary>
    /// Get word rank needed for target coverage percentage
    /// </summary>
    /// <param name="stats">DeckStats instance</param>
    /// <param name="coveragePercent">Target coverage percentage (0-100)</param>
    /// <returns>Word rank needed</returns>
    public static int GetRankForCoverage(this DeckStats stats, double coveragePercent)
    {
        // Prefer sampled data if available (100% accurate)
        if (!string.IsNullOrEmpty(stats.CoverageSamplesJson))
        {
            return GetRankForCoverageFromSamples(stats, coveragePercent);
        }

        // Fallback to fitted curve (less accurate)
        if (!stats.ParameterA.HasValue || !stats.ParameterB.HasValue || !stats.ParameterC.HasValue || !stats.TotalUniqueWords.HasValue)
            return 0;
        return CoverageCurveFitter.GetRankForCoverage(
            coveragePercent,
            stats.ParameterA.Value,
            stats.ParameterB.Value,
            stats.ParameterC.Value,
            stats.TotalUniqueWords.Value
        );
    }

    /// <summary>
    /// Get word rank needed for target coverage using sampled data with logarithmic interpolation
    /// </summary>
    /// <param name="stats">DeckStats instance</param>
    /// <param name="targetPercent">Target coverage percentage (0-100)</param>
    /// <returns>Word rank needed</returns>
    private static int GetRankForCoverageFromSamples(DeckStats stats, double targetPercent)
    {
        var samples = JsonSerializer.Deserialize<List<double[]>>(stats.CoverageSamplesJson!);
        if (samples == null || samples.Count == 0) return 0;

        // Boundary checks
        if (targetPercent <= samples[0][1]) return (int)samples[0][0];
        if (targetPercent >= samples[^1][1]) return (int)samples[^1][0];

        // Find bounding points and use logarithmic interpolation
        for (int i = 0; i < samples.Count - 1; i++)
        {
            var p1 = samples[i];     // [Rank, Coverage]
            var p2 = samples[i + 1];

            double cov1 = p1[1];
            double cov2 = p2[1];

            if (targetPercent >= cov1 && targetPercent <= cov2)
            {
                double rank1 = p1[0];
                double rank2 = p2[0];

                // Calculate interpolation parameter t (0.0 to 1.0)
                double t = (targetPercent - cov1) / (cov2 - cov1);

                // Logarithmic interpolation (models Zipf's law)
                double logRank1 = Math.Log(rank1);
                double logRank2 = Math.Log(rank2);
                double interpolatedLogRank = logRank1 + t * (logRank2 - logRank1);

                return (int)Math.Round(Math.Exp(interpolatedLogRank));
            }
        }

        return (int)samples[^1][0];
    }

    /// <summary>
    /// Get all coverage milestones (80%, 85%, 90%, 95%, 98%, 99%)
    /// </summary>
    /// <param name="stats">DeckStats instance</param>
    /// <returns>Dictionary of coverage percentage → word rank</returns>
    public static Dictionary<int, int> GetMilestones(this DeckStats stats)
    {
        return new Dictionary<int, int>
        {
            { 80, stats.GetRankForCoverage(80) },
            { 85, stats.GetRankForCoverage(85) },
            { 90, stats.GetRankForCoverage(90) },
            { 95, stats.GetRankForCoverage(95) },
            { 98, stats.GetRankForCoverage(98) },
            { 99, stats.GetRankForCoverage(99) }
        };
    }

    /// <summary>
    /// Generate full coverage curve data for charting (logarithmic spacing)
    /// </summary>
    /// <param name="stats">DeckStats instance</param>
    /// <param name="pointCount">Number of points to generate (ignored if sampled data exists)</param>
    /// <returns>List of (rank, coverage) pairs</returns>
    public static List<(int rank, double coverage)> GenerateCurvePoints(this DeckStats stats, int pointCount = 50)
    {
        // If sampled data exists, return it directly for hoverable points
        if (!string.IsNullOrEmpty(stats.CoverageSamplesJson))
        {
            var samples = JsonSerializer.Deserialize<List<double[]>>(stats.CoverageSamplesJson);
            if (samples != null && samples.Count > 0)
            {
                return samples.Select(s => ((int)s[0], s[1])).ToList();
            }
        }

        // Fallback: use fitted curve with logarithmic spacing
        if (!stats.TotalUniqueWords.HasValue)
            return new List<(int, double)>();

        var points = new List<(int, double)>();
        double logMax = Math.Log(stats.TotalUniqueWords.Value);

        for (int i = 0; i <= pointCount; i++)
        {
            double logRank = (i * logMax) / pointCount;
            int rank = (int)Math.Round(Math.Exp(logRank));
            rank = Math.Max(1, Math.Min(rank, stats.TotalUniqueWords.Value));

            double coverage = stats.GetCoverageAtRank(rank);
            points.Add((rank, coverage));
        }

        return points;
    }
}
