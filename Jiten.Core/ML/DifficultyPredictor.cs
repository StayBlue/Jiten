using Microsoft.ML.Data;

namespace Jiten.Cli.ML;

using Core;
using Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.ML;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

public class DifficultyPredictor
{
    private readonly MLContext _mlContext;
    private readonly PredictionEngine<PredictorInput, PredictorOutput> _predictionEngine;
    private readonly IDbContextFactory<JitenDbContext> _contextFactory;

    private readonly List<string> _featureOrder;

    public DifficultyPredictor(
        IDbContextFactory<JitenDbContext> contextFactory,
        string modelPath)
    {
        _contextFactory = contextFactory;

        _mlContext = new MLContext(seed: 0);

        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException($"LightGBM model file not found: {modelPath}");
        }

        var emptyInputDataView = _mlContext.Data.LoadFromEnumerable(new List<PredictorInput>());

        // Load the model
        var onnxScoringEstimator = _mlContext.Transforms.ApplyOnnxModel(
                                                                        outputColumnNames: ["variable"],
                                                                        inputColumnNames: ["float_input"],
                                                                        modelFile: modelPath,
                                                                        fallbackToCpu: true
                                                                       );

        ITransformer trainedModel = onnxScoringEstimator.Fit(emptyInputDataView);
        _predictionEngine = _mlContext.Model.CreatePredictionEngine<PredictorInput, PredictorOutput>(trainedModel);

        _featureOrder = GetOrderedFeatureNames();
    }

    private List<string> GetOrderedFeatureNames()
    {
        var orderedNames = new List<string>
                           {
                               "Ttr", "AverageSentenceLength", "DialoguePercentage", "KanjiRatio",
                               "KangoPercentage", "WagoPercentage", "GairaigoPercentage", "VerbPercentage", "ParticlePercentage",
                               "AvgWordPerSentence", "ReadabilityScore",
                               "LogicalConnectorRatio", "ModalMarkerRatio", "RelativeClauseMarkerRatio", "MetaphorMarkerRatio",
                               "AvgLogFreqRank", "StdLogFreqRank", "AvgLogObsFreq", "StdLogObsFreq",
                               "LowFreqRankPerc", "AvgReadingFreqRank", "MedianReadingFreqRank", "AvgReadingObsFreq",
                               "MedianReadingObsFreq", "AvgReadingFreqPerc", "AvgCustomScorePerWord",
                                "StdCustomWordScore", "PercCustomScoreAboveSoftcapStart",
                               "ratio_negative_conj", "ratio_polite_conj", "ratio_conditional_conj", "ratio_passive_causative_conj",
                               "ratio_potential_conj", "ratio_volitional_conj", "ratio_imperative_conj", "ratio_te_form_conj",
                               "ratio_past_conj", "ratio_stem_conj", "ratio_garu_conj", "ratio_seemingness_conj", "ratio_shimau_conj",
                               "ratio_contracted_conj", "ratio_other_conj", "RatioConjugations", "ratio_noun_pos", "ratio_verb_pos",
                               "ratio_adj_pos", "ratio_adv_pos", "ratio_conjunc_pos", "ratio_aux_pos", "ratio_inter_pos",
                               "ratio_fix_pos",  "ratio_pn_pos", "ratio_exp_pos", "ratio_other_pos"
                           };
        return orderedNames;
    }


    public async Task<float> PredictDifficulty(Deck deck, MediaType mediaType)
    {
        // 1. Create a temporary MLInputData (some fields might be dummies for live prediction)
        var mlInputData = new MLInputData
                          {
                              DifficultyScore = 0, // Not used for prediction, can be dummy
                              TextFilePath = deck.OriginalTitle, // Or some identifier
                              MediaType = (int)mediaType, OriginalFileName = deck.OriginalTitle
                          };

        // 2. Extract features using your existing FeatureExtractor logic
        ExtractedFeatures extractedFeatures;
        await using (var context = await _contextFactory.CreateDbContextAsync())
        {
            if (deck == null) throw new InvalidOperationException("Parser returned null deck.");

            // Populate a dummy MLInputData since ProcessFileAsync expects it
            // Manually construct ExtractedFeatures from the deck, then call other extraction methods.
            // This is like inlining parts of ProcessFileAsync.
            extractedFeatures = new ExtractedFeatures
                                {
                                    Filename = mlInputData.OriginalFileName, DifficultyRating = mlInputData.DifficultyScore
                                };

            if (deck.CharacterCount == 0 || deck.WordCount == 0)
                throw new Exception($"Received empty deck metrics from parser for: {deck.OriginalTitle}");

            deck.MediaType = (MediaType)mlInputData.MediaType;

            extractedFeatures.CharacterCount = deck.CharacterCount;
            extractedFeatures.WordCount = deck.WordCount;
            extractedFeatures.UniqueWordCount = deck.UniqueWordCount;
            extractedFeatures.UniqueWordOnceCount = deck.UniqueWordUsedOnceCount;
            extractedFeatures.UniqueKanjiCount = deck.UniqueKanjiCount;
            extractedFeatures.UniqueKanjiOnceCount = deck.UniqueKanjiUsedOnceCount;
            extractedFeatures.SentenceCount = deck.SentenceCount;
            extractedFeatures.AverageSentenceLength = deck.AverageSentenceLength;
            extractedFeatures.DialoguePercentage = deck.DialoguePercentage;
            if (extractedFeatures.WordCount > 0)
                extractedFeatures.Ttr = extractedFeatures.UniqueWordCount / extractedFeatures.WordCount;
            else extractedFeatures.Ttr = 0;


            var deckWords = deck.DeckWords.ToList();

            MLHelper.ExtractCharacterCounts(deck.RawText!.RawText, extractedFeatures);
            await MLHelper.ExtractFrequencyStats(context, deckWords, extractedFeatures);
            MLHelper.ExtractConjugationStats(deckWords, extractedFeatures);
            MLHelper.ExtractReadabilityScore(deckWords, extractedFeatures);
            MLHelper.ExtractSemanticComplexity(deckWords, extractedFeatures);
        }

        // 3. Convert ExtractedFeatures to float[] in the correct order
        var featureVector = new float[_featureOrder.Count];
        var featureMap = GetFeatureMap(extractedFeatures);

        for (int i = 0; i < _featureOrder.Count; i++)
        {
            string featureName = _featureOrder[i];
            if (featureMap.TryGetValue(featureName, out double value))
            {
                if (double.IsNaN(value))
                {
                    featureVector[i] = float.NaN;
                }
                else if (double.IsPositiveInfinity(value))
                {
                    featureVector[i] = float.PositiveInfinity;
                }
                else if (double.IsNegativeInfinity(value))
                {
                    featureVector[i] = float.NegativeInfinity;
                }
                else
                {
                    featureVector[i] = (float)value;
                }
            }
            else
            {
                Console.WriteLine($"Warning: Feature '{featureName}' not found in extracted features map. Using 0.0f.");
                featureVector[i] = float.NaN;
            }
        }

        var predictorInput = new PredictorInput { Features = featureVector };

        // 4. Predict
        var prediction = _predictionEngine.Predict(predictorInput);
        var predictedDifficulty = prediction.PredictedDifficulty![0];

        // Console.WriteLine("Predicted difficulty (not rounded): " + predictedDifficulty + "");
        // Console.WriteLine("Predicted difficulty (rounded): " + (float)Math.Clamp(Math.Round(predictedDifficulty), 0, 5) + "");

        return predictedDifficulty;
    }

    private Dictionary<string, double> GetFeatureMap(ExtractedFeatures features)
    {
        var map = new Dictionary<string, double>
                  {
                      { "CharacterCount", features.CharacterCount }, { "WordCount", features.WordCount },
                      { "UniqueWordCount", features.UniqueWordCount }, { "UniqueWordOnceCount", features.UniqueWordOnceCount },
                      { "UniqueKanjiCount", features.UniqueKanjiCount }, { "UniqueKanjiOnceCount", features.UniqueKanjiOnceCount },
                      { "SentenceCount", features.SentenceCount }, { "Ttr", features.Ttr },
                      { "AverageSentenceLength", features.AverageSentenceLength },
                      { "DialoguePercentage", features.DialoguePercentage }, { "TotalCount", features.TotalCount },
                      { "KanjiCount", features.KanjiCount }, { "HiraganaCount", features.HiraganaCount },
                      { "KatakanaCount", features.KatakanaCount }, { "OtherCount", features.OtherCount },
                      { "KanjiRatio", features.KanjiRatio }, { "HiraganaRatio", features.HiraganaRatio },
                      { "KatakanaRatio", features.KatakanaRatio }, { "OtherRatio", features.OtherRatio },
                      { "KangoPercentage", features.KangoPercentage },
                      { "WagoPercentage", features.WagoPercentage }, { "GairaigoPercentage", features.GairaigoPercentage },
                      { "VerbPercentage", features.VerbPercentage }, { "ParticlePercentage", features.ParticlePercentage },
                      { "AvgWordPerSentence", features.AvgWordPerSentence }, { "ReadabilityScore", features.ReadabilityScore },
                      { "LogicalConnectorRatio", features.LogicalConnectorRatio }, { "ModalMarkerRatio", features.ModalMarkerRatio },
                      { "RelativeClauseMarkerRatio", features.RelativeClauseMarkerRatio },
                      { "MetaphorMarkerRatio", features.MetaphorMarkerRatio }, { "AvgLogFreqRank", features.AvgLogFreqRank },
                      { "StdLogFreqRank", features.StdLogFreqRank }, { "MinFreqRank", features.MinFreqRank },
                      { "AvgLogObsFreq", features.AvgLogObsFreq },
                      { "StdLogObsFreq", features.StdLogObsFreq },
                      { "MaxObsFreq", features.MaxObsFreq },
                      { "LowFreqRankPerc", features.LowFreqRankPerc }, /* { "LowFreqObsPerc", features.LowFreqObsPerc },*/
                      { "AvgReadingFreqRank", features.AvgReadingFreqRank }, { "MedianReadingFreqRank", features.MedianReadingFreqRank },
                      { "AvgReadingObsFreq", features.AvgReadingObsFreq }, { "MedianReadingObsFreq", features.MedianReadingObsFreq },
                      { "AvgReadingFreqPerc", features.AvgReadingFreqPerc }, 
                      { "AvgCustomScorePerWord", features.AvgCustomScorePerWord },
                      { "StdCustomWordScore", features.StdCustomWordScore },
                     { "PercCustomScoreAboveSoftcapStart", features.PercCustomScoreAboveSoftcapStart },
                      { "TotalConjugations", features.TotalConjugations }
                  };

        foreach (var kvp in features.ConjugationCategoryCounts)
        {
            map[kvp.Key] = kvp.Value;
        }

        foreach (var kvp in features.ConjugationCategoryRatios)
        {
            map[kvp.Key] = kvp.Value;
        }

        map["RatioConjugations"] = features.RatioConjugations;

        foreach (var kvp in features.PosCategoryCounts)
        {
            map[kvp.Key] = kvp.Value;
        }

        foreach (var kvp in features.PosCategoryRatios)
        {
            map[kvp.Key] = kvp.Value;
        }

        return map;
    }
}