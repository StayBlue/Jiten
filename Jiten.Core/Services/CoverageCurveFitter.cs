using MathNet.Numerics;

namespace Jiten.Core.Services;

/// <summary>
/// Fits parametric curves to word coverage data
/// </summary>
public static class CoverageCurveFitter
{
    /// <summary>
    /// Fits a power law curve to coverage data using non-linear regression
    /// Model: Coverage(rank) = A × (rank + B)^C
    /// </summary>
    /// <param name="rankOccurrencePairs">List of (rank, cumulative_occurrences)</param>
    /// <param name="totalOccurrences">Total word occurrences in deck</param>
    /// <returns>Fitted parameters (A, B, C) and goodness of fit (R², RMSE)</returns>
    public static (double A, double B, double C, double RSquared, double RMSE)
        FitPowerLaw(List<(int rank, int cumulativeOccurrences)> rankOccurrencePairs, int totalOccurrences)
    {
        if (rankOccurrencePairs.Count == 0)
            throw new ArgumentException("No data to fit", nameof(rankOccurrencePairs));

        // Convert to coverage percentages
        var xData = rankOccurrencePairs.Select(p => (double)p.rank).ToArray();
        var yData = rankOccurrencePairs.Select(p => 100.0 * p.cumulativeOccurrences / totalOccurrences).ToArray();

        double bestB = 0;
        double bestRSquared = 0;
        double bestA = 0, bestC = 0, bestRMSE = double.MaxValue;

        // Grid search for optimal B (offset parameter)
        for (double B = 0; B <= 10; B += 0.5)
        {
            try
            {
                // Transform to log-log space: log(y) = log(A) + C*log(x + B)
                var xTransformed = xData.Select(x => Math.Log(x + B)).ToArray();
                var yTransformed = yData.Select(y => Math.Log(Math.Max(y, 1e-10))).ToArray();

                // Linear regression in log-log space
                var (intercept, slope) = Fit.Line(xTransformed, yTransformed);

                double A = Math.Exp(intercept);
                double C = slope;

                // Compute R² and RMSE in original space
                var yPredicted = xData.Select(x => Math.Min(100.0, A * Math.Pow(x + B, C))).ToArray();
                double rSquared = GoodnessOfFit.RSquared(yPredicted, yData);
                double rmse = Math.Sqrt(yData.Zip(yPredicted, (y, yPred) => Math.Pow(y - yPred, 2)).Average());

                if (rSquared > bestRSquared)
                {
                    bestRSquared = rSquared;
                    bestA = A;
                    bestB = B;
                    bestC = C;
                    bestRMSE = rmse;
                }
            }
            catch
            {
                // Skip invalid B values
                continue;
            }
        }

        return (bestA, bestB, bestC, bestRSquared, bestRMSE);
    }

    /// <summary>
    /// Computes coverage percentage at a given word rank
    /// </summary>
    /// <param name="rank">Word rank (1-based)</param>
    /// <param name="A">Amplitude parameter</param>
    /// <param name="B">Offset parameter</param>
    /// <param name="C">Exponent parameter</param>
    /// <returns>Coverage percentage (0-100)</returns>
    public static double GetCoverageAtRank(int rank, double A, double B, double C)
    {
        if (rank <= 0) return 0.0;
        return Math.Min(100.0, A * Math.Pow(rank + B, C));
    }

    /// <summary>
    /// Computes word rank needed for a target coverage percentage (inverse function)
    /// </summary>
    /// <param name="targetCoverage">Target coverage percentage (0-100)</param>
    /// <param name="A">Amplitude parameter</param>
    /// <param name="B">Offset parameter</param>
    /// <param name="C">Exponent parameter</param>
    /// <param name="maxWords">Maximum word count</param>
    /// <returns>Word rank needed</returns>
    public static int GetRankForCoverage(double targetCoverage, double A, double B, double C, int maxWords)
    {
        if (targetCoverage <= 0) return 0;
        if (targetCoverage >= 100) return maxWords;

        // Analytical inverse if C ≠ 0
        if (Math.Abs(C) > 1e-10)
        {
            double rank = Math.Pow(targetCoverage / A, 1.0 / C) - B;
            return (int)Math.Round(Math.Max(1, Math.Min(rank, maxWords)));
        }

        // Fallback: binary search
        int low = 1, high = maxWords;
        while (low < high)
        {
            int mid = (low + high) / 2;
            double coverage = GetCoverageAtRank(mid, A, B, C);

            if (coverage < targetCoverage)
                low = mid + 1;
            else
                high = mid;
        }

        return low;
    }
}
