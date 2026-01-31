using Jiten.Core;
using Jiten.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace Jiten.Api.Helpers;

public static class UserCoverageChunkHelper
{
    private const int COVERAGE_CHUNK_SIZE = 1024;

    public sealed class CoverageDictionaries
    {
        public Dictionary<int, float> MatureCoverage { get; } = new();
        public Dictionary<int, float> MatureUniqueCoverage { get; } = new();
        public Dictionary<int, float> YoungCoverage { get; } = new();
        public Dictionary<int, float> YoungUniqueCoverage { get; } = new();
    }

    public static async Task<CoverageDictionaries> GetCoverage(UserDbContext userContext, string userId, IReadOnlyCollection<int> deckIds)
    {
        var result = new CoverageDictionaries();
        if (deckIds.Count == 0)
            return result;

        var chunkIndices = deckIds.Select(id => id / COVERAGE_CHUNK_SIZE).Distinct().ToList();
        short[] metrics =
        [
            (short)UserCoverageMetric.MatureCoverage,
            (short)UserCoverageMetric.MatureUniqueCoverage,
            (short)UserCoverageMetric.YoungCoverage,
            (short)UserCoverageMetric.YoungUniqueCoverage
        ];

        var chunks = await userContext.UserCoverageChunks
            .AsNoTracking()
            .Where(c => c.UserId == userId && chunkIndices.Contains(c.ChunkIndex) && metrics.Contains(c.Metric))
            .ToListAsync();

        var chunkMap = chunks.ToDictionary(c => (c.Metric, c.ChunkIndex), c => c.Values);

        foreach (var deckId in deckIds)
        {
            var chunkIndex = deckId / COVERAGE_CHUNK_SIZE;
            var offset = deckId % COVERAGE_CHUNK_SIZE;

            SetValue(result.MatureCoverage, UserCoverageMetric.MatureCoverage);
            SetValue(result.MatureUniqueCoverage, UserCoverageMetric.MatureUniqueCoverage);
            SetValue(result.YoungCoverage, UserCoverageMetric.YoungCoverage);
            SetValue(result.YoungUniqueCoverage, UserCoverageMetric.YoungUniqueCoverage);

            void SetValue(Dictionary<int, float> dict, UserCoverageMetric metric)
            {
                short basisPoints = 0;
                if (!chunkMap.TryGetValue(((short)metric, chunkIndex), out var values))
                {
                    dict[deckId] = 0;
                    return;
                }

                if ((uint)offset < (uint)values.Length)
                    basisPoints = values[offset];

                dict[deckId] = basisPoints / 100f;
            }
        }

        return result;
    }
}
