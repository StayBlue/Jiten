using System.Text;
using Jiten.Core;
using Jiten.Core.Data;

namespace Jiten.Cli.ML;

public static class MLHelper
{
    private static int[] LOGICAL_CONNECTORS =
    [
        1254730, // 結局
        1007040, // それに
        1006730, // そして
        1254690, // 結果
        1406090, // そこで
        1004200, // けど
        2853889, // けれども
        1612900, // にも関わらず
        2454160, // それにもかかわらず
        1009720, // に加えて
        1505990, // しかし
        2055530, // だが
        1166510, // 一方
        1007310, // だから
        1009970, // ので 
        1335210, // 従って
        1009410, // なぜなら
        1279310, // さらに
        1506050, // しかも
        1349300, // なお
        1387240, // まず
        2600340, // 次に
        1610430, // つまり
        1343110, // ところで
    ];

    private static int[] MODAL_MARKERS =
    [
        1928670, // だろう
        1008420, // でしょう
        1002970, // かもしれない
        2143350, // かも
        1476430, // はず
        1610740, // 違いない
        1013240, // らしい
        2409190, // ようだ
        2016410, // みたい
        1006650, // そうだ
        2083720, // っぽい
        1221540, // 気がする
        1589350, // 思う
        1002940, // かな
    ];

    private static int[] RELATIVE_CLAUSE_MARKERS =
    [
        1922760, // という
        2540200, // といった
        1009660,// による
        1009780, // について
        1008590, // として
        1215790,// 関する
        1009810,// に対する
        1342050,// めぐる
        1432880,// 通じた
        1404100,// すなわち
    ];

    private static int[] METAPHOR_MARKERS =
    [
        1010030, // のように
        2016410, // みたい
        1216280, // まるで
        1208190, // あたかも
        2409180, // ような
        2826769, // かのように
        2409190, // ようだ
        
    ];

    public static void ExtractCharacterCounts(string text, ExtractedFeatures features)
    {
        features.TotalCount = text.Length;
        if (features.TotalCount == 0) return;

        foreach (Rune rune in text.EnumerateRunes())
        {
            int codePoint = rune.Value;

            if (JapaneseTextHelper.IsKanji(rune))
            {
                features.KanjiCount++;
            }
            else if (codePoint is >= 0x3040 and <= 0x309F) // Hiragana
            {
                features.HiraganaCount++;
            }
            else if (codePoint is >= 0x30A0 and <= 0x30FF) // Katakana
            {
                features.KatakanaCount++;
            }
            else
            {
                features.OtherCount++;
            }
        }

        features.KanjiRatio = (double)features.KanjiCount / features.TotalCount;
        features.HiraganaRatio = (double)features.HiraganaCount / features.TotalCount;
        features.KatakanaRatio = (double)features.KatakanaCount / features.TotalCount;
        features.OtherRatio = (double)features.OtherCount / features.TotalCount;
        double kanaAndOther = features.HiraganaCount + features.KatakanaCount + features.OtherCount;
        features.KanjiToKanaRatio = kanaAndOther > 0 ? (double)features.KanjiCount / kanaAndOther : 0;
    }

    public static async Task ExtractFrequencyStats(JitenDbContext context, List<DeckWord> deckWords, ExtractedFeatures features)
    {
        if (!deckWords.Any()) return;

        var wordIdsToExclude = context.JMDictWords.Where(w => w.Priorities != null && w.Priorities.Contains("name")).Select(w => w.WordId)
                                      .ToList();
        var wordIds = deckWords.Select(dw => dw.WordId).Where(w => !wordIdsToExclude.Contains(w)).Distinct();


        if (!wordIds.Any()) return;

        var freqDataMap = context.JmDictWordFrequencies
                                 .Where(f => wordIds.Contains(f.WordId))
                                 .ToDictionary(f => f.WordId);

        List<double> freqRanks = new List<double>();
        List<double> obsFreqs = new List<double>();
        List<double> readingFreqRanks = new List<double>();
        List<double> readingObsFreqs = new List<double>();
        List<double> readingFreqPercentages = new List<double>();
        List<double> customWordScores = new List<double>();

        long totalWordOccurrences = 0;
        long lowFreqRankCount = 0;
        long lowFreqObsCount = 0;
        long customScoreAboveSoftcapStartCount = 0;
        double scoreAtSoftcapStartThreshold = CalculateCustomRankScore(MLConfig.RankSoftcapStart);
        double totalCustomScoreSum = 0;

        foreach (var dw in deckWords)
        {
            totalWordOccurrences += dw.Occurrences;

            if (!freqDataMap.TryGetValue(dw.WordId, out var freqData)) continue;

            double customScore = CalculateCustomRankScore(freqData.FrequencyRank);
            if (!double.IsNaN(customScore))
            {
                customWordScores.AddRange(Enumerable.Repeat(customScore, dw.Occurrences));
                totalCustomScoreSum += customScore * dw.Occurrences;
                if (customScore >= scoreAtSoftcapStartThreshold)
                    customScoreAboveSoftcapStartCount += dw.Occurrences;
            }

            freqRanks.AddRange(Enumerable.Repeat((double)freqData.FrequencyRank, dw.Occurrences));
            if (freqData.FrequencyRank > MLConfig.LowFreqRankThreshold) lowFreqRankCount += dw.Occurrences;

            obsFreqs.AddRange(Enumerable.Repeat(freqData.ObservedFrequency, dw.Occurrences));
            if (freqData.ObservedFrequency < MLConfig.LowFreqObserverThreshold) lowFreqObsCount += dw.Occurrences;

            int rIdx = dw.ReadingIndex;
            if (rIdx < freqData.ReadingsFrequencyRank.Count)
                readingFreqRanks.AddRange(Enumerable.Repeat((double)freqData.ReadingsFrequencyRank[rIdx], dw.Occurrences));
            if (rIdx < freqData.ReadingsObservedFrequency.Count)
                readingObsFreqs.AddRange(Enumerable.Repeat(freqData.ReadingsObservedFrequency[rIdx], dw.Occurrences));
            if (rIdx < freqData.ReadingsFrequencyPercentage.Count)
                readingFreqPercentages.AddRange(Enumerable.Repeat(freqData.ReadingsFrequencyPercentage[rIdx], dw.Occurrences));
        }

        if (totalWordOccurrences == 0) return;

        // Log transform and calculate stats
        var logFreqRanks = freqRanks.Any() ? freqRanks.Select(r => Math.Log(r + 1)).ToList() : new List<double>();
        var logObsFreqs = obsFreqs.Any() ? obsFreqs.Select(f => Math.Log(f + 1)).ToList() : new List<double>();
        // Npgsql might return int[] for rank arrays, ensure they are double for log.
        var logReadingFreqRanks = readingFreqRanks.Any() ? readingFreqRanks.Select(r => Math.Log(r + 1)).ToList() : new List<double>();
        var logReadingObsFreqs = readingObsFreqs.Any() ? readingObsFreqs.Select(f => Math.Log(f + 1)).ToList() : new List<double>();


        features.LogSentenceLength = features.AverageSentenceLength != 0 ? Math.Log(features.AverageSentenceLength) : double.NaN;
        features.AvgLogFreqRank = SafeAverage(logFreqRanks);
        features.AvgFreqRank = SafeAverage(freqRanks);
        features.MedianLogFreqRank = SafeMedian(logFreqRanks);
        features.StdLogFreqRank = SafeStdDev(logFreqRanks);
        features.MinFreqRank = freqRanks.Any() ? freqRanks.Min() : double.NaN;
        features.MaxFreqRank = freqRanks.Any() ? freqRanks.Max() : double.NaN;

        features.AvgLogObsFreq = SafeAverage(logObsFreqs);
        features.MedianLogObsFreq = SafeMedian(logObsFreqs);
        features.StdLogObsFreq = SafeStdDev(logObsFreqs);
        features.MinObsFreq = obsFreqs.Any() ? obsFreqs.Min() : double.NaN;
        features.MaxObsFreq = obsFreqs.Any() ? obsFreqs.Max() : double.NaN;

        features.LowFreqRankPerc = (double)lowFreqRankCount / totalWordOccurrences;
        features.LowFreqObsPerc = (double)lowFreqObsCount / totalWordOccurrences;

        features.AvgReadingFreqRank = SafeAverage(logReadingFreqRanks);
        features.MedianReadingFreqRank = SafeMedian(logReadingFreqRanks);
        features.AvgReadingObsFreq = SafeAverage(logReadingObsFreqs);
        features.MedianReadingObsFreq = SafeMedian(logReadingObsFreqs);
        features.AvgReadingFreqPerc = SafeAverage(readingFreqPercentages);
        features.MedianReadingFreqPerc = SafeMedian(readingFreqPercentages);

        features.AvgCustomScorePerWord = totalWordOccurrences > 0 ? totalCustomScoreSum / totalWordOccurrences : double.NaN;
        features.MedianCustomWordScore = SafeMedian(customWordScores);
        features.StdCustomWordScore = SafeStdDev(customWordScores);
        features.MaxCustomWordScore = customWordScores.Any() ? customWordScores.Max() : double.NaN;
        features.PercCustomScoreAboveSoftcapStart = (double)customScoreAboveSoftcapStartCount / totalWordOccurrences;
    }

    public static void ExtractConjugationStats(List<DeckWord> deckWords, ExtractedFeatures features)
    {
        if (!deckWords.Any()) return;

        var categoryCounts = new Dictionary<string, int>();
        foreach (var cat in MLConfig.ConjugationCategories.Keys)
        {
            categoryCounts[cat] = 0;
        }

        int totalConjugations = 0;

        var posCounts = new Dictionary<string, int>();
        foreach (var cat in MLConfig.PosCategories.Keys)
        {
            posCounts[cat] = 0;
        }

        foreach (var dw in deckWords)
        {
            if (dw.Conjugations.Count == 0) continue;
            foreach (var conjDetail in dw.Conjugations)
            {
                totalConjugations += dw.Occurrences;
                string category = MLConfig.ConjugationDetailToCategory.GetValueOrDefault(conjDetail, "other");
                categoryCounts[category] += dw.Occurrences;
            }

            foreach (var pos in dw.PartsOfSpeech)
            {
                string category = MLConfig.PosDetailToCategory.GetValueOrDefault(pos, "other");
                posCounts[category] += dw.Occurrences;
            }
        }

        features.TotalConjugations = totalConjugations;
        foreach (var cat in MLConfig.ConjugationCategories.Keys)
        {
            string countKey = $"conj_{cat.Replace("/", "_")}_count";
            string ratioKey = $"ratio_{cat.Replace("/", "_")}_conj";

            features.ConjugationCategoryCounts[countKey] = categoryCounts.GetValueOrDefault(cat, 0);
            features.ConjugationCategoryRatios[ratioKey] =
                features.WordCount > 0 ? (double)features.ConjugationCategoryCounts[countKey] / features.WordCount : 0;
        }

        foreach (var cat in MLConfig.PosCategories.Keys)
        {
            string countKey = $"pos_{cat.Replace("/", "_")}_count";
            string ratioKey = $"ratio_{cat.Replace("/", "_")}_pos";

            features.PosCategoryCounts[countKey] = posCounts.GetValueOrDefault(cat, 0);
            features.PosCategoryRatios[ratioKey] =
                features.WordCount > 0 ? (double)features.PosCategoryCounts[countKey] / features.WordCount : 0;
        }

        if (features.WordCount > 0)
        {
            features.RatioConjugations = (double)totalConjugations / features.WordCount;
        }
        else
        {
            features.RatioConjugations = 0;
        }
    }

    private static double SafeAverage(List<double> list) => list.Any() ? list.Average() : double.NaN;

    private static double SafeMedian(List<double> list)
    {
        if (!list.Any()) return double.NaN;
        var sorted = list.OrderBy(x => x).ToList();
        int mid = sorted.Count / 2;
        return (sorted.Count % 2 != 0) ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2.0;
    }

    private static double SafeStdDev(List<double> list)
    {
        if (list.Count < 2) return double.NaN; // StdDev not meaningful for < 2 items
        double avg = list.Average();
        double sumOfSquares = list.Sum(val => (val - avg) * (val - avg));
        return Math.Sqrt(sumOfSquares / (list.Count - 1)); // Sample StDev
    }

    private static double CalculateCustomRankScore(int? rankNullable)
    {
        if (!rankNullable.HasValue)
        {
            return double.NaN;
        }

        int rank = rankNullable.Value;

        if (rank <= MLConfig.RankBase) return (double)rank;
        if (rank >= MLConfig.RankHardcap) return MLConfig.ScoreHardcapValue;

        double score = MLConfig.RankBase;
        int currentRankThreshold = MLConfig.RankBase;
        double multiplier = 1.0;

        while (currentRankThreshold < MLConfig.RankSoftcapStart && currentRankThreshold < rank)
        {
            multiplier *= 2;
            int nextThreshold = currentRankThreshold + MLConfig.RankExpBase;
            int rankInThisStep = Math.Min(rank, nextThreshold) - currentRankThreshold;
            score += rankInThisStep * multiplier;
            currentRankThreshold = nextThreshold;
            if (rank <= currentRankThreshold) return score;
        }

        double multiplierAtSoftcap = multiplier;
        while (currentRankThreshold < MLConfig.RankHardcap && currentRankThreshold < rank)
        {
            int nextThreshold = currentRankThreshold + MLConfig.RankSoftcapStep;
            int rankInThisStep = Math.Min(rank, nextThreshold) - currentRankThreshold;
            score += rankInThisStep * multiplierAtSoftcap;
            currentRankThreshold = nextThreshold;
            if (rank <= currentRankThreshold) return score;
        }

        return score;
    }

    /// <summary>
    /// Implementation of https://github.com/joshdavham/jreadability/tree/main
    /// </summary>
    /// <param name="deckWords"></param>
    /// <param name="features"></param>
    public static void ExtractReadabilityScore(List<DeckWord> deckWords, ExtractedFeatures features)
    {
        var wordCount = features.WordCount;
        var kangoOccurrenceSum = deckWords.Where(dw => dw.Origin == WordOrigin.Kango).Sum(dw => dw.Occurrences);
        var wagoOccurrenceSum = deckWords.Where(dw => dw.Origin == WordOrigin.Wago).Sum(dw => dw.Occurrences);
        var gairaigoOccurrenceSum = deckWords.Where(dw => dw.Origin == WordOrigin.Gairaigo).Sum(dw => dw.Occurrences);
        var verbOccurrenceSum = deckWords.Where(dw => dw.PartsOfSpeech.Contains(PartOfSpeech.Verb)).Sum(dw => dw.Occurrences);
        var particleOccurrenceSum = deckWords.Where(dw => dw.PartsOfSpeech.Contains(PartOfSpeech.Particle)).Sum(dw => dw.Occurrences);

        var kangoPercentage = 100d * kangoOccurrenceSum / wordCount;
        var wagoPercentage = 100d * wagoOccurrenceSum / wordCount;
        var gairaigoPercentage = 100d * gairaigoOccurrenceSum / wordCount;
        var verbPercentage = 100d * verbOccurrenceSum / wordCount;
        var particlePercentage = particleOccurrenceSum / wordCount;
        var avgWordPerSentence = features.SentenceCount > 0 ? wordCount / features.SentenceCount : 5;
        var readabilityScore = avgWordPerSentence * -0.056 + kangoPercentage * -0.126 + wagoPercentage * -0.042 + verbPercentage * -0.145 +
                               particlePercentage * -0.044 + 11.724;

        features.KangoPercentage = kangoPercentage;
        features.WagoPercentage = wagoPercentage;
        features.GairaigoPercentage = gairaigoPercentage;
        features.VerbPercentage = verbPercentage;
        features.ParticlePercentage = particlePercentage;
        features.AvgWordPerSentence = avgWordPerSentence;
        features.ReadabilityScore = readabilityScore;
    }

    public static void ExtractSemanticComplexity(List<DeckWord> deckWords, ExtractedFeatures features)
    {
        var logicalConnectorsCount = deckWords.Where(dw => LOGICAL_CONNECTORS.Contains(dw.WordId)).Sum(x => x.Occurrences);
        var modalMarkers = deckWords.Where(dw => MODAL_MARKERS.Contains(dw.WordId)).Sum(x => x.Occurrences);
        var relativeClauseMarkers = deckWords.Where(dw => RELATIVE_CLAUSE_MARKERS.Contains(dw.WordId)).Sum(x => x.Occurrences);
        var metaphorMarkers = deckWords.Where(dw => METAPHOR_MARKERS.Contains(dw.WordId)).Sum(x => x.Occurrences);
        
        features.LogicalConnectorRatio = logicalConnectorsCount / features.WordCount;
        features.ModalMarkerRatio = modalMarkers / features.WordCount;
        features.RelativeClauseMarkerRatio = relativeClauseMarkers / features.WordCount;
        features.MetaphorMarkerRatio = metaphorMarkers / features.WordCount;
    }
}