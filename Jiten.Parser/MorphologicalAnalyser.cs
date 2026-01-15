using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Jiten.Core.Data;
using Jiten.Core.Utils;
using Jiten.Parser.Diagnostics;
using Microsoft.Extensions.Configuration;
using WanaKanaShaapu;

namespace Jiten.Parser;

public class MorphologicalAnalyser
{
    private static HashSet<(string, string, string, PartOfSpeech?)> SpecialCases3 =
    [
        ("な", "の", "で", PartOfSpeech.Expression),
        ("で", "は", "ない", PartOfSpeech.Expression),
        ("それ", "で", "も", PartOfSpeech.Conjunction),
        ("なく", "なっ", "た", PartOfSpeech.Verb),
        ("さ", "せ", "て", PartOfSpeech.Verb),
        ("ほう", "が", "いい", PartOfSpeech.Expression),
        ("に", "とっ", "て", PartOfSpeech.Expression),
    ];

    // Auxiliary verbs (補助動詞) that attach to te-form or masu-stem but should remain separate for vocabulary learning
    // These add grammatical meaning (aspect, direction, etc.) rather than forming true compound verbs
    private static readonly HashSet<string> AuxiliaryVerbs =
    [
        "続ける",   // continue doing
        "始める",   // start doing
        "終わる",   // finish doing
        "終える",   // finish doing (transitive)
        "出す",     // start (suddenly)
        "かける",   // be about to / half-way
    ];

    // Mapping from auxiliary verb dictionary form to its stem for splitting compound tokens
    // Used to split Sudachi tokens like し終わっ (dict: し終わる) → し + 終わっ
    private static readonly Dictionary<string, string> AuxiliaryVerbStems = new()
    {
        { "続ける", "続け" },
        { "始める", "始め" },
        { "終わる", "終わ" },
        { "終える", "終え" },
        { "出す", "出" },
        { "かける", "かけ" },
    };

    private static HashSet<(string, string, PartOfSpeech?)> SpecialCases2 =
    [
        ("じゃ", "ない", PartOfSpeech.Expression),
        ("に", "しろ", PartOfSpeech.Expression),
        ("だ", "けど", PartOfSpeech.Conjunction),
        ("だ", "が", PartOfSpeech.Conjunction),
        ("で", "さえ", PartOfSpeech.Expression),
        ("で", "すら", PartOfSpeech.Expression),
        ("と", "いう", PartOfSpeech.Expression),
        ("と", "か", PartOfSpeech.Conjunction),
        ("だ", "から", PartOfSpeech.Conjunction),
        ("これ", "まで", PartOfSpeech.Expression),
        ("それ", "も", PartOfSpeech.Conjunction),
        ("それ", "だけ", PartOfSpeech.Noun),
        ("くせ", "に", PartOfSpeech.Conjunction),
        ("の", "で", PartOfSpeech.Particle),
        ("誰", "も", PartOfSpeech.Expression),
        ("誰", "か", PartOfSpeech.Expression),
        ("すぐ", "に", PartOfSpeech.Adverb),
        ("なん", "か", PartOfSpeech.Particle),
        ("だっ", "た", PartOfSpeech.Expression),
        ("だっ", "たら", PartOfSpeech.Conjunction),
        ("よう", "に", PartOfSpeech.Expression),
        ("ん", "です", PartOfSpeech.Expression),
        ("ん", "だ", PartOfSpeech.Expression),
        ("です", "か", PartOfSpeech.Expression),
        ("し", "て", PartOfSpeech.Verb),
        ("し", "ちゃ", PartOfSpeech.Verb),
        ("何", "の", PartOfSpeech.Pronoun),
        ("カッコ", "いい", PartOfSpeech.IAdjective),
        ("か", "な", PartOfSpeech.Particle),
        ("よう", "です", PartOfSpeech.Expression),
        ("何も", "かも", PartOfSpeech.Expression),
        ("に", "とって", PartOfSpeech.Expression),
        ("何と", "も", PartOfSpeech.Adverb),
    ];

    private static readonly List<string> HonorificsSuffixes = ["さん", "ちゃん", "くん"];

    private readonly HashSet<char> _sentenceEnders = ['。', '！', '？', '」'];

    private static readonly HashSet<string> MisparsesRemove =
    [
        "そ", "ー", "る", "ま", "ふ", "ち", "ほ", "す", "じ", "なさ", "い", "ぴ", "ふあ", "ぷ", "ちゅ", "にっ", "じら", "タ", "け", "イ", "イッ", "ほっ",
        "ウー", "うー", "ううう", "うう", "ウウウウ", "ウウ", "ううっ", "かー", "ぐわー", "違"
    ];

    // Token to separate some words in sudachi
    private static readonly string _stopToken = "|";

    // Delimiter for batch processing multiple texts in a single Sudachi call
    private static readonly string _batchDelimiter = "|||";

    // Auxiliary/copula patterns that can follow ん (to be split: んだ → ん + だ)
    private static readonly HashSet<string> NCompoundSuffixes =
        ["だ", "です", "じゃ", "なら", "ても", "でも", "だろ", "だろう", "だって", "だけど", "だけ", "だが", "だし", "だから"];

    // Conjunction patterns that can follow だ when preceded by ん (to be split: だけど → だ + けど)
    // Only conjunctions, not sentence-final particles (よ, ね, な, etc. should keep んだ together)
    private static readonly HashSet<string> DaCompoundSuffixes =
        ["が", "けど", "けれど", "けれども", "から", "し", "って"];

    /// <summary>
    /// Parses the given text into a list of SentenceInfo objects by performing morphological analysis.
    /// Delegates to ParseBatch for a single codepath.
    /// </summary>
    /// <param name="text">The input text to be analyzed.</param>
    /// <param name="morphemesOnly">A boolean indicating whether the parsing should output only morphemes. When true, parsing will use mode 'A' for morpheme parsing.</param>
    /// <param name="preserveStopToken">A boolean indicating whether the stop token should be preserved in the processed text. Used in the ReaderController</param>
    /// <param name="diagnostics">Optional diagnostics container for verbose debug output.</param>
    /// <returns>A list of SentenceInfo objects representing the parsed output.</returns>
    public async Task<List<SentenceInfo>> Parse(string text, bool morphemesOnly = false, bool preserveStopToken = false, ParserDiagnostics? diagnostics = null)
    {
        var results = await ParseBatch([text], morphemesOnly, preserveStopToken, diagnostics);
        return results.Count > 0 ? results[0] : [];
    }

    /// <summary>
    /// Parses multiple texts in a single Sudachi call for efficiency.
    /// This is the main implementation - Parse() delegates here.
    /// </summary>
    /// <param name="texts">List of texts to parse.</param>
    /// <param name="morphemesOnly">A boolean indicating whether the parsing should output only morphemes.</param>
    /// <param name="preserveStopToken">A boolean indicating whether the stop token should be preserved.</param>
    /// <param name="diagnostics">Optional diagnostics container for verbose debug output.</param>
    /// <returns>List of SentenceInfo lists, one per input text.</returns>
    public async Task<List<List<SentenceInfo>>> ParseBatch(List<string> texts, bool morphemesOnly = false, bool preserveStopToken = false, ParserDiagnostics? diagnostics = null)
    {
        if (texts.Count == 0) return [];

        diagnostics?.TokenStages.Clear();

        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var configuration = new ConfigurationBuilder()
                            .SetBasePath(Directory.GetCurrentDirectory())
                            .AddJsonFile(Path.Combine(Environment.CurrentDirectory, "..", "Shared", "sharedsettings.json"), optional: true)
                            .AddJsonFile(Path.Combine(baseDir, "sharedsettings.json"), optional: true)
                            .AddJsonFile("sharedsettings.json", optional: true)
                            .AddJsonFile("appsettings.json", optional: true)
                            .AddEnvironmentVariables()
                            .Build();

        // Preprocess each text separately (preserves transformations per-text)
        var processedTexts = new List<string>(texts.Count);
        var originalTexts = new List<string>(texts.Count);
        foreach (var text in texts)
        {
            var copy = text;
            PreprocessText(ref copy, preserveStopToken);
            processedTexts.Add(copy);

            var cleanedOriginal = copy.Replace(" ", "");
            if (!preserveStopToken)
                cleanedOriginal = cleanedOriginal.Replace(_stopToken, "");
            originalTexts.Add(cleanedOriginal);
        }

        // Join with batch delimiter (only if multiple texts)
        var combinedText = texts.Count == 1
            ? processedTexts[0]
            : string.Join($" {_batchDelimiter} ", processedTexts);

        // Single Sudachi call
        var configPath = morphemesOnly
            ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "resources", "sudachi_nouserdic.json")
            : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "resources", "sudachi.json");
        // Note: DictionaryPath from config causes issues - Sudachi uses the systemDict path from sudachi.json
        // which is relative to the config file. Passing null lets Sudachi handle dictionary resolution itself.
        string? dic = null;

        var sudachiStopwatch = diagnostics != null ? Stopwatch.StartNew() : null;
        var rawOutput = SudachiInterop.ProcessText(configPath, combinedText, dic, mode: morphemesOnly ? 'A' : 'C');
        sudachiStopwatch?.Stop();

        if (diagnostics != null)
        {
            diagnostics.Sudachi = new SudachiDiagnostics
            {
                ElapsedMs = sudachiStopwatch!.Elapsed.TotalMilliseconds,
                RawOutput = rawOutput,
                Tokens = ParseSudachiOutputToDiagnosticTokens(rawOutput)
            };
        }

        var output = rawOutput.Split("\n");

        // Parse all WordInfo objects
        var allWordInfos = new List<WordInfo>();
        foreach (var line in output)
        {
            if (line == "EOS") continue;
            var wi = new WordInfo(line);
            if (!wi.IsInvalid) allWordInfos.Add(wi);
        }

        // Split by delimiter tokens (if batch)
        var batches = new List<List<WordInfo>>();
        if (texts.Count == 1)
        {
            batches.Add(allWordInfos);
        }
        else
        {
            var currentBatch = new List<WordInfo>();
            for (int j = 0; j < allWordInfos.Count; j++)
            {
                var wi = allWordInfos[j];

                // Sudachi tokenizes ||| as three separate | tokens
                if (wi.Text == _stopToken &&
                    j + 2 < allWordInfos.Count &&
                    allWordInfos[j + 1].Text == _stopToken &&
                    allWordInfos[j + 2].Text == _stopToken)
                {
                    batches.Add(currentBatch);
                    currentBatch = new List<WordInfo>();
                    j += 2;
                }
                else
                {
                    currentBatch.Add(wi);
                }
            }

            batches.Add(currentBatch); // Last batch
        }

        // Process each batch through normal pipeline
        var results = new List<List<SentenceInfo>>();
        for (int i = 0; i < batches.Count && i < originalTexts.Count; i++)
        {
            var wordInfos = batches[i];

            if (morphemesOnly)
            {
                results.Add([new SentenceInfo("") { Words = wordInfos.Select(w => (w, (byte)0, (byte)0)).ToList() }]);
                continue;
            }

            wordInfos = TrackStage(diagnostics, "SplitCompoundAuxiliaryVerbs", wordInfos, SplitCompoundAuxiliaryVerbs);
            wordInfos = TrackStage(diagnostics, "RepairNTokenisation", wordInfos, RepairNTokenisation);
            wordInfos = TrackStage(diagnostics, "ProcessSpecialCases", wordInfos, ProcessSpecialCases);
            wordInfos = TrackStage(diagnostics, "CombineInflections", wordInfos, CombineInflections);
            wordInfos = TrackStage(diagnostics, "CombineAmounts", wordInfos, CombineAmounts);
            wordInfos = TrackStage(diagnostics, "CombineTte", wordInfos, CombineTte);
            wordInfos = TrackStage(diagnostics, "CombineAuxiliaryVerbStem", wordInfos, CombineAuxiliaryVerbStem);
            wordInfos = TrackStage(diagnostics, "CombineAdverbialParticle", wordInfos, CombineAdverbialParticle);
            wordInfos = TrackStage(diagnostics, "CombineSuffix", wordInfos, CombineSuffix);
            wordInfos = TrackStage(diagnostics, "CombineConjunctiveParticle", wordInfos, CombineConjunctiveParticle);
            wordInfos = TrackStage(diagnostics, "CombineAuxiliary", wordInfos, CombineAuxiliary);
            wordInfos = TrackStage(diagnostics, "CombineVerbDependant", wordInfos, CombineVerbDependant);
            wordInfos = TrackStage(diagnostics, "CombineParticles", wordInfos, CombineParticles);
            wordInfos = TrackStage(diagnostics, "CombineHiraganaElongation", wordInfos, CombineHiraganaElongation);
            wordInfos = TrackStage(diagnostics, "CombineFinal", wordInfos, CombineFinal);
            wordInfos = TrackStage(diagnostics, "RepairTankaToTaNKa", wordInfos, RepairTankaToTaNKa);
            wordInfos = TrackStage(diagnostics, "FilterMisparse", wordInfos, FilterMisparse);

            results.Add(SplitIntoSentences(originalTexts[i], wordInfos));
        }

        return results;
    }

    /// <summary>
    /// Remove common misparses
    /// </summary>
    /// <param name="wordInfos"></param>
    /// <returns></returns>
    /// <exception cref="NotImplementedException"></exception>
    private List<WordInfo> FilterMisparse(List<WordInfo> wordInfos)
    {
        for (int i = wordInfos.Count - 1; i >= 0; i--)
        {
            var word = wordInfos[i];
            if (word.Text is "なん" or "フン" or "ふん")
                word.PartOfSpeech = PartOfSpeech.Prefix;

            if (word.Text == "そう")
                word.PartOfSpeech = PartOfSpeech.Adverb;

            if (word.Text == "おい")
                word.PartOfSpeech = PartOfSpeech.Interjection;

            if (word is { Text: "つ", PartOfSpeech: PartOfSpeech.Suffix })
                word.PartOfSpeech = PartOfSpeech.Counter;

            if (word.Text is "だー" or "だあ")
            {
                word.Text = "だ";
                word.DictionaryForm = "です";
                word.PartOfSpeech = PartOfSpeech.Auxiliary;
            }

            if (MisparsesRemove.Contains(word.Text) ||
                word.PartOfSpeech == PartOfSpeech.Noun && (
                    (word.Text.Length == 1 && WanaKana.IsKana(word.Text)) ||
                    word.Text.Length == 2 && WanaKana.IsKana(word.Text[0].ToString()) && word.Text[1] == 'ー' && word.Text != "バー"
                    || word.Text is "エナ" or "えな"
                ))
            {
                wordInfos.RemoveAt(i);
                continue;
            }
        }

        return wordInfos;
    }

    /// <summary>
    /// Splits compound verb tokens that Sudachi outputs as single tokens when they contain auxiliary verbs.
    /// For example: し終わっ (dict: し終わる) → し + 終わっ
    /// This is necessary because compound verbs like し終わる don't exist in JMDict, but their components do.
    /// </summary>
    private List<WordInfo> SplitCompoundAuxiliaryVerbs(List<WordInfo> wordInfos)
    {
        var result = new List<WordInfo>(wordInfos.Count + 4);

        foreach (var word in wordInfos)
        {
            // Only process verb tokens with dictionary forms
            if (word.PartOfSpeech != PartOfSpeech.Verb ||
                string.IsNullOrEmpty(word.DictionaryForm) ||
                word.DictionaryForm.Length < 3)
            {
                result.Add(word);
                continue;
            }

            // Check if dictionary form ends with any auxiliary verb
            string? matchedAux = null;
            foreach (var aux in AuxiliaryVerbs)
            {
                if (word.DictionaryForm.EndsWith(aux) && word.DictionaryForm.Length > aux.Length)
                {
                    matchedAux = aux;
                    break;
                }
            }

            if (matchedAux == null)
            {
                result.Add(word);
                continue;
            }

            // Calculate the main verb prefix length from dictionary form
            int mainVerbDictLen = word.DictionaryForm.Length - matchedAux.Length;
            string mainVerbDict = word.DictionaryForm[..mainVerbDictLen];

            // The surface form should have the same prefix length for the main verb
            // e.g., し終わっ → し (1 char) + 終わっ (3 chars)
            if (word.Text.Length <= mainVerbDictLen)
            {
                result.Add(word);
                continue;
            }

            string mainVerbSurface = word.Text[..mainVerbDictLen];
            string auxVerbSurface = word.Text[mainVerbDictLen..];

            // Verify the auxiliary surface starts with the auxiliary stem
            if (!AuxiliaryVerbStems.TryGetValue(matchedAux, out var auxStem) ||
                !auxVerbSurface.StartsWith(auxStem))
            {
                result.Add(word);
                continue;
            }

            // Create the main verb token
            var mainVerb = new WordInfo
            {
                Text = mainVerbSurface,
                DictionaryForm = mainVerbDict,
                NormalizedForm = mainVerbDict,
                PartOfSpeech = PartOfSpeech.Verb,
                Reading = WanaKana.ToHiragana(mainVerbSurface)
            };

            // Create the auxiliary verb token
            var auxVerb = new WordInfo
            {
                Text = auxVerbSurface,
                DictionaryForm = matchedAux,
                NormalizedForm = matchedAux,
                PartOfSpeech = PartOfSpeech.Verb,
                PartOfSpeechSection1 = PartOfSpeechSection.PossibleDependant,
                Reading = WanaKana.ToHiragana(auxVerbSurface)
            };

            result.Add(mainVerb);
            result.Add(auxVerb);
        }

        return result;
    }

    /// <summary>
    /// Repairs たんか misparsed as noun (啖呵/短歌) when it should be た + ん + か in Kansai dialect.
    /// Patterns that indicate Kansai dialect question (not the noun):
    /// - Verb + たんか: 云うたんか, してたんか (verb past + んか question)
    /// - Adverb もう + たんか: てもうたんか (てしまった Kansai form + んか)
    /// - Particle/conjunction し + も + たんか: てしもたんか (てしまった variant)
    /// Does NOT split when:
    /// - Preceded by を (indicates object: たんかを吐く)
    /// - Followed by を (same reason)
    /// - Preceded by の (possessive: お島の方のたんか)
    /// - At sentence start with no preceding verb context
    /// </summary>
    private List<WordInfo> RepairTankaToTaNKa(List<WordInfo> wordInfos)
    {
        var result = new List<WordInfo>(wordInfos.Count + 4);
        var deconj = Deconjugator.Instance;

        for (int i = 0; i < wordInfos.Count; i++)
        {
            var word = wordInfos[i];

            // Only process たんか noun tokens
            if (word.PartOfSpeech != PartOfSpeech.Noun || word.Text != "たんか")
            {
                result.Add(word);
                continue;
            }

            // Don't split if followed by を (object marker - indicates real noun usage like たんかを吐く)
            if (i + 1 < wordInfos.Count && wordInfos[i + 1].Text == "を")
            {
                result.Add(word);
                continue;
            }

            // Don't split if preceded by を (indicates real noun)
            if (result.Count > 0 && result[^1].Text == "を")
            {
                result.Add(word);
                continue;
            }

            // Don't split if preceded by の (possessive - indicates real noun like お島の方のたんか)
            if (result.Count > 0 && result[^1].Text == "の")
            {
                result.Add(word);
                continue;
            }

            // Helper to find the last meaningful token (skip punctuation)
            WordInfo? GetPrevToken(int offset = 1)
            {
                int count = 0;
                for (int j = result.Count - 1; j >= 0; j--)
                {
                    if (result[j].PartOfSpeech == PartOfSpeech.SupplementarySymbol) continue;
                    count++;
                    if (count == offset) return result[j];
                }
                return null;
            }

            int GetPrevTokenIndex(int offset = 1)
            {
                int count = 0;
                for (int j = result.Count - 1; j >= 0; j--)
                {
                    if (result[j].PartOfSpeech == PartOfSpeech.SupplementarySymbol) continue;
                    count++;
                    if (count == offset) return j;
                }
                return -1;
            }

            // Check if splitting would create a valid verb conjugation
            bool shouldSplit = false;
            var prev = GetPrevToken(1);

            if (prev != null)
            {
                // Pattern 1: Verb/Adjective + たんか → Verb/Adjective + た + ん + か
                // e.g., 云う + たんか → 云うた + ん + か (valid past tense)
                // e.g., 怖がって + たんか → 怖がってた + ん + か (te-form + ta)
                if (prev.PartOfSpeech == PartOfSpeech.Verb)
                {
                    var candidateText = prev.Text + "た";
                    var forms = deconj.Deconjugate(NormalizeToHiragana(candidateText));
                    if (forms.Any(f => f.Tags.Any(t => t.StartsWith("v"))))
                        shouldSplit = true;
                }

                // Pattern 1b: Te-form ending + たんか → combine with た
                // Handles cases like 怖がって + たんか where 怖がって is classified as IAdjective
                // If prev ends with て/で, adding た creates てた/でた (past progressive/resultative)
                if (!shouldSplit && (prev.Text.EndsWith("て") || prev.Text.EndsWith("で")))
                {
                    // This is likely a te-form that should combine with た from たんか
                    // e.g., 怖がって + た → 怖がってた (was scared)
                    shouldSplit = true;
                }

                // Pattern 2: Adverb もう + たんか → もう is part of てもうた (Kansai てしまった)
                // e.g., ハズレて + もう + たんか → ハズレてもうた + ん + か
                // Check by text "もう" since POS might vary
                if (prev.Text == "もう")
                {
                    var verbBefore = GetPrevToken(2);
                    if (verbBefore != null && (verbBefore.Text.EndsWith("て") || verbBefore.Text.EndsWith("で")))
                    {
                        // Combine: verbて + もう + た → verbてもうた
                        var combinedText = verbBefore.Text + "もうた";
                        var prevIdx = GetPrevTokenIndex(1);
                        var verbIdx = GetPrevTokenIndex(2);
                        // Remove in descending order to keep indices valid
                        if (prevIdx >= 0 && verbIdx >= 0)
                        {
                            if (prevIdx > verbIdx)
                            {
                                result.RemoveAt(prevIdx);
                                result.RemoveAt(verbIdx);
                            }
                            else
                            {
                                result.RemoveAt(verbIdx);
                                result.RemoveAt(prevIdx);
                            }
                        }
                        result.Add(new WordInfo(verbBefore) { Text = combinedText, PartOfSpeech = PartOfSpeech.Verb });
                        result.Add(CreateNToken());
                        result.Add(new WordInfo { Text = "か", DictionaryForm = "か", PartOfSpeech = PartOfSpeech.Particle, Reading = "か" });
                        continue;
                    }
                }

                // Pattern 3: も + たんか after し (conjunction) → part of てしもた (Kansai てしまった)
                // e.g., 言うて + し + も + たんか → 言うてしもた + ん + か
                if (prev.Text == "も")
                {
                    var shiToken = GetPrevToken(2);
                    if (shiToken != null && shiToken.Text == "し")
                    {
                        var verbBefore = GetPrevToken(3);
                        if (verbBefore != null && (verbBefore.Text.EndsWith("て") || verbBefore.Text.EndsWith("で") ||
                            verbBefore.PartOfSpeech == PartOfSpeech.Expression))
                        {
                            // Combine: verb + し + も + た → verbしもた
                            var combinedText = verbBefore.Text + "しもた";
                            var moIdx = GetPrevTokenIndex(1);
                            var shiIdx = GetPrevTokenIndex(2);
                            var verbIdx = GetPrevTokenIndex(3);
                            // Remove in descending index order
                            var indices = new[] { moIdx, shiIdx, verbIdx }.Where(x => x >= 0).OrderByDescending(x => x).ToList();
                            foreach (var idx in indices) result.RemoveAt(idx);
                            result.Add(new WordInfo(verbBefore) { Text = combinedText, PartOfSpeech = PartOfSpeech.Verb });
                            result.Add(CreateNToken());
                            result.Add(new WordInfo { Text = "か", DictionaryForm = "か", PartOfSpeech = PartOfSpeech.Particle, Reading = "か" });
                            continue;
                        }
                    }
                }
            }

            if (shouldSplit && prev != null)
            {
                // Modify previous verb to include た
                var prevIdx = GetPrevTokenIndex(1);
                if (prevIdx >= 0)
                {
                    result[prevIdx] = new WordInfo(prev) { Text = prev.Text + "た", PartOfSpeech = PartOfSpeech.Verb };
                }
                result.Add(CreateNToken());
                result.Add(new WordInfo { Text = "か", DictionaryForm = "か", PartOfSpeech = PartOfSpeech.Particle, Reading = "か" });
            }
            else
            {
                result.Add(word);
            }
        }

        return result;
    }

    #region RepairNTokenisation Helpers

    private static string NormalizeToHiragana(string text) =>
        KanaNormalizer.Normalize(WanaKana.ToHiragana(text));

    private static bool IsNdaVerbForm(HashSet<DeconjugationForm> forms) =>
        forms.Any(f => f.Tags.Count > 0 &&
            ((f.Tags.Any(t => t == "v5m") && f.Text.EndsWith("む")) ||
             (f.Tags.Any(t => t == "v5n") && f.Text.EndsWith("ぬ")) ||
             (f.Tags.Any(t => t == "v5b") && f.Text.EndsWith("ぶ")) ||
             (f.Tags.Any(t => t == "v5g") && f.Text.EndsWith("ぐ"))));

    private static bool IsAnyVerbForm(HashSet<DeconjugationForm> forms) =>
        forms.Any(f => f.Tags.Count > 0 && f.Tags.Any(t => t.StartsWith("v")));

    private static bool IsMasenVerbForm(HashSet<DeconjugationForm> forms) =>
        forms.Any(f => f.Tags.Count > 0 && f.Tags.Any(t => t.StartsWith("v")) && !f.Text.EndsWith("ます"));

    private static WordInfo CreateNToken() => new()
    {
        Text = "ん", DictionaryForm = "ん", NormalizedForm = "ん", Reading = "ん",
        PartOfSpeech = PartOfSpeech.Auxiliary, PartOfSpeechSection1 = PartOfSpeechSection.None
    };

    private static WordInfo CreateDaToken() => new()
    {
        Text = "だ", DictionaryForm = "だ", NormalizedForm = "だ", Reading = "だ",
        PartOfSpeech = PartOfSpeech.Auxiliary, PartOfSpeechSection1 = PartOfSpeechSection.None
    };

    private static string BuildCandidateText(List<WordInfo> words, int lookback, string suffix)
    {
        var sb = new StringBuilder();
        for (int j = words.Count - lookback; j < words.Count; j++)
            sb.Append(words[j].Text);
        sb.Append(suffix);
        return sb.ToString();
    }

    private static void RemoveLastN(List<WordInfo> list, int n)
    {
        for (int j = 0; j < n; j++)
            list.RemoveAt(list.Count - 1);
    }

    private static bool TryCombineWithLookback(
        List<WordInfo> result,
        string suffix,
        Deconjugator deconj,
        Func<HashSet<DeconjugationForm>, bool> validator,
        out WordInfo? combined)
    {
        combined = null;

        // Skip na-adjective + な pattern entirely - the な belongs to the adjective, not to んだ
        // e.g., 好きなんだ should be 好きな + んだ, not 好き + なんだ or 好きなんだ
        if (result.Count >= 2)
        {
            var lastToken = result[^1];
            if (lastToken is { Text: "な", DictionaryForm: "だ" } &&
                IsNaAdjectiveToken(result[^2]))
            {
                return false;
            }
        }

        // Skip i-adjective + んだ pattern - the んだ is explanatory, not verb conjugation
        // e.g., いいんだ should be いい + んだ, not combined as verb form
        if (result.Count >= 1 && suffix.StartsWith("ん"))
        {
            var lastToken = result[^1];
            if (lastToken.PartOfSpeech == PartOfSpeech.IAdjective)
            {
                return false;
            }
        }

        for (int lookback = 1; lookback <= Math.Min(3, result.Count); lookback++)
        {
            bool hasBlankSpace = false;
            for (int j = result.Count - lookback; j < result.Count; j++)
            {
                if (result[j].PartOfSpeech != PartOfSpeech.BlankSpace)
                    continue;

                hasBlankSpace = true;
                break;
            }

            // Skip blank spaces since they will deconjugate to a correct form and break the parser
            if (hasBlankSpace) continue;

            var candidateText = BuildCandidateText(result, lookback, suffix);
            var forms = deconj.Deconjugate(NormalizeToHiragana(candidateText));

            if (validator(forms))
            {
                var baseWord = result[^lookback];
                RemoveLastN(result, lookback);
                combined = new WordInfo(baseWord) { Text = candidateText, PartOfSpeech = PartOfSpeech.Verb };
                return true;
            }
        }
        return false;
    }

    private static bool IsNaAdjectiveToken(WordInfo word) =>
        word.PartOfSpeech == PartOfSpeech.NaAdjective ||
        word.HasPartOfSpeechSection(PartOfSpeechSection.PossibleNaAdjective) ||
        word.HasPartOfSpeechSection(PartOfSpeechSection.NaAdjectiveLike);

    #endregion

    /// <summary>
    /// Repairs incorrect tokenisation of ん-forms by Sudachi.
    /// Phase 1: Split compound tokens (んだ → ん + だ, だが → だ + が when preceded by ん)
    /// Phase 2: Recombine verb stems with ん using deconjugator validation
    /// </summary>
    private List<WordInfo> RepairNTokenisation(List<WordInfo> wordInfos)
    {
        if (wordInfos.Count < 2) return wordInfos;

        // Phase 1: Split compound tokens that Sudachi incorrectly grouped
        var split = new List<WordInfo>(wordInfos.Count + 4);
        foreach (var word in wordInfos)
        {
            // Split tokens starting with ん (e.g., んだ → ん + だ)
            if (word.Text.Length > 1 && word.Text[0] == 'ん')
            {
                var remainder = word.Text[1..];
                if (NCompoundSuffixes.Contains(remainder) || NCompoundSuffixes.Any(s => remainder.StartsWith(s)))
                {
                    split.Add(CreateNToken());
                    split.Add(new WordInfo(word) { Text = remainder });
                    continue;
                }
            }

            // Split tokens starting with だ when preceded by ん (e.g., だが → だ + が)
            if (word.Text.Length > 1 && word.Text[0] == 'だ' &&
                split.Count > 0 && (split[^1].Text == "ん" || split[^1].Text.EndsWith("ん")))
            {
                var remainder = word.Text[1..];
                if (DaCompoundSuffixes.Contains(remainder))
                {
                    split.Add(CreateDaToken());
                    split.Add(new WordInfo(word) { Text = remainder, DictionaryForm = remainder });
                    continue;
                }
            }

            // Split そうだ → そう + だ (appearance/hearsay pattern should be split for combining logic)
            if (word is { Text: "そうだ", PartOfSpeech: PartOfSpeech.Adverb })
            {
                split.Add(new WordInfo(word)
                {
                    Text = "そう",
                    DictionaryForm = "そう",
                    PartOfSpeech = PartOfSpeech.Auxiliary,
                    PartOfSpeechSection1 = PartOfSpeechSection.AuxiliaryVerbStem
                });
                split.Add(CreateDaToken());
                continue;
            }

            split.Add(word);
        }

        // Phase 2: Recombine verb stems with ん using deconjugator validation
        var result = new List<WordInfo>(split.Count);
        var deconj = Deconjugator.Instance;

        for (int i = 0; i < split.Count; i++)
        {
            var current = split[i];

            // Case: Token already ends with ん (e.g., 飲ん) and next is だ/で - combine as past/te-form
            // Skip na-adjectives (e.g., たくさん + で should NOT combine - で is the copula, not verb conjugation)
            if (current.Text.EndsWith("ん") && current.Text.Length > 1 && current.Text != "ん" &&
                !IsNaAdjectiveToken(current) &&
                i + 1 < split.Count && split[i + 1].Text is "だ" or "で")
            {
                var candidateText = current.Text + split[i + 1].Text;
                if (IsNdaVerbForm(deconj.Deconjugate(NormalizeToHiragana(candidateText))))
                {
                    result.Add(new WordInfo(current) { Text = candidateText, PartOfSpeech = PartOfSpeech.Verb });
                    i++;
                    continue;
                }
            }

            // Case: Standalone ん - try to combine with preceding verb stem
            if (current.Text == "ん" && result.Count > 0)
            {
                bool combined = false;

                // Try んだ/んで pattern (past/te-form) - only for verb conjugation, not explanatory ん
                // Skip when ん is explanatory particle (DictionaryForm = "の" or "ん") or negative auxiliary (DictionaryForm = "ぬ")
                if (i + 1 < split.Count && split[i + 1].Text is "だ" or "で" &&
                    current.DictionaryForm is not "ぬ" and not "の" and not "ん")
                {
                    var suffix = "ん" + split[i + 1].Text;
                    if (TryCombineWithLookback(result, suffix, deconj, IsNdaVerbForm, out var combinedWord))
                    {
                        result.Add(combinedWord!);
                        combined = true;
                        i++;
                    }
                }

                // If んだ/んで didn't match, try negative ん contraction (ませ+ん→ません)
                // Only for actual negative auxiliary (DictionaryForm = "ぬ"), not explanatory ん
                if (!combined && current.DictionaryForm == "ぬ" &&
                    TryCombineWithLookback(result, "ん", deconj, IsAnyVerbForm, out var negativeWord))
                {
                    // After combining ませ+ん→ません, try to combine preceding verb stem with ません
                    // e.g., [し, ませ] + ん → [しません]
                    if (negativeWord!.Text.EndsWith("ません") && result.Count > 0)
                    {
                        var verbStem = result[^1];
                        var candidateText = verbStem.Text + negativeWord.Text;
                        var candidateHiragana = NormalizeToHiragana(candidateText);
                        var forms = deconj.Deconjugate(candidateHiragana);
                        if (IsMasenVerbForm(forms))
                        {
                            result.RemoveAt(result.Count - 1);
                            negativeWord.Text = candidateText;
                            negativeWord.DictionaryForm = verbStem.DictionaryForm;
                        }
                    }
                    result.Add(negativeWord);
                    combined = true;
                }

                if (!combined)
                    result.Add(current);
                continue;
            }

            result.Add(current);
        }

        return result;
    }

    private void PreprocessText(ref string text, bool preserveStopToken)
    {
        text = text.Replace("<", " ");
        text = text.Replace(">", " ");
        text = text.ToFullWidthDigits();
        text = Regex.Replace(text,
                             "[^\u3040-\u309F\u30A0-\u30FF\u4E00-\u9FAF\uFF21-\uFF3A\uFF41-\uFF5A\uFF10-\uFF19\u3005\u3001-\u3003\u3008-\u3011\u3014-\u301F\uFF01-\uFF0F\uFF1A-\uFF1F\uFF3B-\uFF3F\uFF5B-\uFF60\uFF62-\uFF65．\\n…\u3000―\u2500()。！？「」）|]",
                             "");

        if (!preserveStopToken)
            text = text.Replace("|", "");

        // Force spaces and line breaks with some characters so sudachi doesn't try to include them as part of a word
        text = Regex.Replace(text, "「", "\n「 ");
        text = Regex.Replace(text, "」", " 」\n");
        text = Regex.Replace(text, "〈", " \n〈 ");
        text = Regex.Replace(text, "〉", " 〉\n");
        text = Regex.Replace(text, "《", " \n《 ");
        text = Regex.Replace(text, "》", " 》\n");
        text = Regex.Replace(text, "“", " \n“ ");
        text = Regex.Replace(text, "”", " ”\n");
        text = Regex.Replace(text, "―", " ― ");
        text = Regex.Replace(text, "。", " 。\n");
        text = Regex.Replace(text, "！", " ！\n");
        text = Regex.Replace(text, "？", " ？\n");

        // Strip trailing ー from kanji words to prevent Sudachi misparses
        // e.g., 休憩ー → 休憩, 敵襲ーー → 敵襲, 吉森ーー → 吉森
        text = Regex.Replace(text, @"([\u4E00-\u9FAF])ー+", "$1");
        // Strip trailing ー from hiragana when preceded by kanji (e.g., お疲れー → お疲れ)
        // This preserves standalone particles like わー, ねー which aren't preceded by kanji
        text = Regex.Replace(text, @"(?<=[\u4E00-\u9FAF])([\u3040-\u309F])ー+", "$1");

        // Split up words that are parsed together in sudachi when they don't exist in jmdict
        text = Regex.Replace(text, "囁き合", $"囁き{_stopToken}合");
        text = Regex.Replace(text, "見降", $"見{_stopToken}降");
        text = Regex.Replace(text, "斬(.)裂", $"斬$1{_stopToken}裂");
        text = Regex.Replace(text, "砕(.)割", $"砕$1{_stopToken}割");
        text = Regex.Replace(text, "垣間見", $"垣間{_stopToken}見");
        text = Regex.Replace(text, "摺(.)寄", $"摺$1{_stopToken}寄");
        text = Regex.Replace(text, "勃(.)上", $"勃$1{_stopToken}上");
        text = Regex.Replace(text, "滴(.)流", $"滴$1{_stopToken}流");
        text = Regex.Replace(text, "蹴(.)倒", $"蹴$1{_stopToken}倒");
        text = Regex.Replace(text, "善がり狂", $"善がり{_stopToken}狂");
        text = Regex.Replace(text, "いい知", $"いい{_stopToken}知");
        text = Regex.Replace(text, "伝(.)流", $"伝$1{_stopToken}流");
        text = Regex.Replace(text, "歩(.)出", $"歩$1{_stopToken}出");
        text = Regex.Replace(text, "持(.)得", $"持$1{_stopToken}得");
        text = Regex.Replace(text, "笑(.)崩", $"笑$1{_stopToken}崩");
        text = Regex.Replace(text, "揚(.)だ", $"揚$1{_stopToken}だ");
        text = Regex.Replace(text, "考(.)直", $"考$1{_stopToken}直");
        text = Regex.Replace(text, "漏(.)出", $"漏$1{_stopToken}出");
        text = Regex.Replace(text, "返(.)忘", $"返$1{_stopToken}忘");
        text = Regex.Replace(text, "書(.)合", $"書$1{_stopToken}合");
        text = Regex.Replace(text, "はやめ", $"は$1{_stopToken}やめ");
        text = Regex.Replace(text, "もやる", $"も{_stopToken}やる");
        text = Regex.Replace(text, "べや", $"べ{_stopToken}や");
        text = Regex.Replace(text, "はいい", $"は{_stopToken}いい");
        text = Regex.Replace(text, "女子高校生", $"女子{_stopToken}高校生");

        // Replace line ending ellipsis with a sentence ender to be able to flatten later
        text = text.Replace("…\r", "。\r").Replace("…\n", "。\n");
    }

    /// <summary>
    /// Handle special cases that could not be covered by the other rules
    /// </summary>
    /// <param name="wordInfos"></param>
    /// <returns></returns>
    private List<WordInfo> ProcessSpecialCases(List<WordInfo> wordInfos)
    {
        if (wordInfos.Count == 0)
            return wordInfos;

        List<WordInfo> newList = new List<WordInfo>(wordInfos.Count);


        for (int i = 0; i < wordInfos.Count;)
        {
            WordInfo w1 = wordInfos[i];

            if (w1 is { PartOfSpeech: PartOfSpeech.Conjunction or PartOfSpeech.Auxiliary, Text: "で" })
            {
                w1.PartOfSpeech = PartOfSpeech.Particle;
                newList.Add(w1);
                i++;
                continue;
            }

            if (w1 is { PartOfSpeech: PartOfSpeech.Prefix, Text: "今" })
            {
                w1.PartOfSpeech = PartOfSpeech.Adverb;
                newList.Add(w1);
                i++;
                continue;
            }

            if (i < wordInfos.Count - 2)
            {
                WordInfo w2 = wordInfos[i + 1];
                WordInfo w3 = wordInfos[i + 2];

                bool found = false;
                foreach (var sc in SpecialCases3)
                {
                    if (w1.Text == sc.Item1 && w2.Text == sc.Item2 && w3.Text == sc.Item3)
                    {
                        var newWord = new WordInfo(w1);
                        newWord.Text = w1.Text + w2.Text + w3.Text;

                        if (sc.Item4 != null)
                        {
                            newWord.PartOfSpeech = sc.Item4.Value;
                        }

                        newList.Add(newWord);
                        i += 3;
                        found = true;
                        break;
                    }
                }

                if (found)
                    continue;

                // Special case: な + ん + だ should become なんだ (explanatory)
                // BUT only when not preceded by AuxiliaryVerbStem (like そう in 泣きそうな)
                // or NaAdjective (like 好き in 好きなんだ)
                if (w1.Text == "な" && w2.Text == "ん" && w3.Text == "だ")
                {
                    bool prevIsAuxVerbStem = i > 0 &&
                        wordInfos[i - 1].HasPartOfSpeechSection(PartOfSpeechSection.AuxiliaryVerbStem);
                    bool prevIsNaAdjective = i > 0 &&
                        wordInfos[i - 1].PartOfSpeech == PartOfSpeech.NaAdjective;
                    if (!prevIsAuxVerbStem && !prevIsNaAdjective)
                    {
                        var newWord = new WordInfo(w1);
                        newWord.Text = "なんだ";
                        newWord.PartOfSpeech = PartOfSpeech.Expression;
                        newList.Add(newWord);
                        i += 3;
                        continue;
                    }
                }
            }

            if (i < wordInfos.Count - 1)
            {
                WordInfo w2 = wordInfos[i + 1];

                // Special case: ん + だ + DaCompoundSuffix should become ん + だ[suffix]
                // e.g., 飲んだけど → 飲ん + だけど (verb ん)
                // BUT: そうなんだけど → そう + なんだ + けど (explanatory ん - 準体助詞)
                // Only apply this for non-explanatory ん (not a 準体助詞 particle)
                bool isExplanatoryN = w1.PartOfSpeech == PartOfSpeech.Particle &&
                                      w1.HasPartOfSpeechSection(PartOfSpeechSection.Juntaijoushi);
                if (w1.Text == "ん" && w2.Text == "だ" && i + 2 < wordInfos.Count &&
                    DaCompoundSuffixes.Contains(wordInfos[i + 2].Text) &&
                    !isExplanatoryN)
                {
                    var w3 = wordInfos[i + 2];
                    newList.Add(w1); // Keep ん separate
                    var daSuffix = new WordInfo(w2) { Text = w2.Text + w3.Text };
                    daSuffix.PartOfSpeech = PartOfSpeech.Conjunction;
                    newList.Add(daSuffix);
                    i += 3;
                    continue;
                }

                bool found = false;
                foreach (var sc in SpecialCases2)
                {
                    if (w1.Text == sc.Item1 && w2.Text == sc.Item2)
                    {
                        var newWord = new WordInfo(w1);
                        newWord.Text = w1.Text + w2.Text;

                        if (sc.Item3 != null)
                        {
                            newWord.PartOfSpeech = sc.Item3.Value;
                        }

                        newList.Add(newWord);
                        i += 2;
                        found = true;
                        break;
                    }
                }

                if (found)
                    continue;
            }

            // This word is (sometimes?) parsed as auxiliary for some reason
            if (w1.Text == "でしょう")
            {
                var newWord = new WordInfo(w1);
                newWord.PartOfSpeech = PartOfSpeech.Expression;
                newWord.PartOfSpeechSection1 = PartOfSpeechSection.None;

                newList.Add(newWord);
                i++;
                continue;
            }


            if (w1.Text == "だし")
            {
                var da = new WordInfo
                         {
                             Text = "だ", DictionaryForm = "だ", PartOfSpeech = PartOfSpeech.Auxiliary,
                             PartOfSpeechSection1 = PartOfSpeechSection.None, Reading = "だ"
                         };
                var shi = new WordInfo
                          {
                              Text = "し", DictionaryForm = "し", PartOfSpeech = PartOfSpeech.Conjunction,
                              PartOfSpeechSection1 = PartOfSpeechSection.None, Reading = "し"
                          };

                newList.Add(da);
                newList.Add(shi);
                i++;
                continue;
            }

            // Handle な based on context
            if (w1 is { Text: "な", DictionaryForm: "だ" })
            {
                // If previous token is na-adjective followed by explanatory ん, combine them
                // e.g., 好き + な + んだ → 好きな + んだ
                if (newList.Count > 0 && IsNaAdjectiveToken(newList[^1]) &&
                    i + 1 < wordInfos.Count && wordInfos[i + 1].Text.StartsWith("ん"))
                {
                    newList[^1].Text += w1.Text;
                    i++;
                    continue;
                }
                // Otherwise, treat as particle (not the vegetable 菜)
                w1.PartOfSpeech = PartOfSpeech.Particle;
            }
            // Always process に as the particle and not the baggage
            else if (w1.Text == "に")
                w1.PartOfSpeech = PartOfSpeech.Particle;

            // Always process よう as the noun
            if (w1.Text is "よう")
                w1.PartOfSpeech = PartOfSpeech.Noun;

            if (w1.Text is "十五")
                w1.PartOfSpeech = PartOfSpeech.Numeral;

            if (w1.Text is "オレ")
                w1.PartOfSpeech = PartOfSpeech.Pronoun;

            newList.Add(w1);
            i++;
        }

        return newList;
    }

    private static readonly HashSet<PartOfSpeech> InflectableBasePOS =
    [
        PartOfSpeech.Verb,
        PartOfSpeech.IAdjective,
        PartOfSpeech.NaAdjective
    ];

    private static readonly HashSet<PartOfSpeech> InflectionPartPOS =
    [
        PartOfSpeech.Auxiliary,
        PartOfSpeech.Suffix,
        PartOfSpeech.Particle
    ];

    /// <summary>
    /// Combines inflected verb/adjective forms by verifying with the Deconjugator.
    /// Uses a forward-rolling strategy to handle suffixes that change the dictionary base (e.g. わかる + かね → わかりかねる).
    /// </summary>
    private List<WordInfo> CombineInflections(List<WordInfo> wordInfos)
    {
        if (wordInfos.Count < 2) return wordInfos;

        var deconjugator = Deconjugator.Instance;
        var result = new List<WordInfo>(wordInfos.Count);

        for (int i = 0; i < wordInfos.Count; i++)
        {
            var currentWord = new WordInfo(wordInfos[i]);

            // Check if potential base for inflection
            // Exclude AuxiliaryVerbStem words (みたい, そう, etc.) as they are grammatical suffixes, not standalone inflectable words
            bool isBase = (InflectableBasePOS.Contains(currentWord.PartOfSpeech) ||
                           currentWord.HasPartOfSpeechSection(PartOfSpeechSection.PossibleSuru))
                          && currentWord.NormalizedForm != "物"
                          && !currentWord.HasPartOfSpeechSection(PartOfSpeechSection.AuxiliaryVerbStem);

            if (!isBase)
            {
                result.Add(currentWord);
                continue;
            }

            // Track current target dictionary form
            var currentDictForm = currentWord.DictionaryForm;
            var currentPOS = currentWord.PartOfSpeech;

            // Iteratively try to merge subsequent tokens
            while (i + 1 < wordInfos.Count)
            {
                var nextWord = wordInfos[i + 1];

                // Safety filter: stop at particles and auxiliary expressions
                if (nextWord.Text is "は" or "な" or "よ" or "し" or "を" or "が" or "ください")
                    break;

                // Don't merge いけ/いけない after ちゃ/じゃ/きゃ/にゃ (obligation/prohibition patterns)
                // e.g., しちゃいけない = "must not do", なきゃいけない = "must do"
                // But allow merging after て (continuation: やっていける = "can get by")
                if (nextWord.DictionaryForm == "いける" &&
                    (currentWord.Text.EndsWith("ちゃ") || currentWord.Text.EndsWith("じゃ") ||
                     currentWord.Text.EndsWith("きゃ") || currentWord.Text.EndsWith("にゃ")))
                    break;

                // Don't merge explanatory ん (DictionaryForm = "の" or "ん") with preceding tokens
                if (nextWord.Text == "ん" && nextWord.DictionaryForm is "の" or "ん")
                    break;

                if (currentWord.Text.EndsWith("ん") && nextWord.Text is "だ" or "です")
                    break;

                // Don't merge na-adjective + copula で (e.g., たくさん + で should stay separate)
                // The で here is the te-form of copula だ, not a verb conjugation
                if (currentPOS == PartOfSpeech.NaAdjective &&
                    nextWord.Text == "で" && nextWord.DictionaryForm == "だ")
                    break;

                // Check if valid inflection part
                bool isValidPart = InflectionPartPOS.Contains(nextWord.PartOfSpeech) ||
                                   nextWord.HasPartOfSpeechSection(PartOfSpeechSection.AuxiliaryVerbStem) ||
                                   nextWord.HasPartOfSpeechSection(PartOfSpeechSection.ConjunctionParticle) ||
                                   nextWord.HasPartOfSpeechSection(PartOfSpeechSection.Dependant) ||
                                   nextWord.HasPartOfSpeechSection(PartOfSpeechSection.PossibleDependant);

                // Greedy steal: handle そうだ/そうか by taking just そう if it forms valid inflection
                // e.g., 新しそうだ → 新しそう + だ, 話そうか → 話そう + か
                if (!isValidPart && nextWord.Text is "そうだ" or "そうか")
                {
                    string stealCandidate = currentWord.Text + "そう";
                    string stealHiragana = KanaNormalizer.Normalize(WanaKana.ToHiragana(stealCandidate));
                    var stealForms = deconjugator.Deconjugate(stealHiragana);

                    string stealTarget = currentPOS == PartOfSpeech.Noun
                        ? KanaNormalizer.Normalize(WanaKana.ToHiragana(currentDictForm)) + "する"
                        : KanaNormalizer.Normalize(WanaKana.ToHiragana(currentDictForm));

                    if (stealForms.Any(f => f.Text == stealTarget))
                    {
                        // Successful steal - merge base + そう
                        currentWord.Text = stealCandidate;
                        if (currentPOS == PartOfSpeech.Noun)
                        {
                            currentWord.DictionaryForm = currentDictForm + "する";
                            currentPOS = PartOfSpeech.Verb;
                        }
                        currentWord.PartOfSpeech = currentPOS;
                        currentDictForm = currentWord.DictionaryForm;

                        // Modify the original token to be just だ or か for subsequent processing
                        string remainder = nextWord.Text == "そうだ" ? "だ" : "か";
                        wordInfos[i + 1] = new WordInfo
                        {
                            Text = remainder,
                            DictionaryForm = remainder,
                            PartOfSpeech = remainder == "だ" ? PartOfSpeech.Auxiliary : PartOfSpeech.Particle,
                            Reading = remainder
                        };
                        // Don't increment i - let the remainder be processed as a new token in the main loop
                        break;
                    }
                }

                if (!isValidPart) break;

                string candidateText = currentWord.Text + nextWord.Text;
                string candidateHiragana = KanaNormalizer.Normalize(WanaKana.ToHiragana(candidateText));
                var forms = deconjugator.Deconjugate(candidateHiragana);

                bool merged = false;
                string? newDictForm = null;

                // Scenario A: Standard inflection - deconjugates to current target
                string targetHiragana = currentPOS == PartOfSpeech.Noun
                    ? KanaNormalizer.Normalize(WanaKana.ToHiragana(currentDictForm)) + "する"
                    : KanaNormalizer.Normalize(WanaKana.ToHiragana(currentDictForm));

                if (forms.Any(f => f.Text == targetHiragana))
                {
                    merged = true;
                    if (currentPOS == PartOfSpeech.Noun)
                    {
                        newDictForm = currentDictForm + "する";
                        currentPOS = PartOfSpeech.Verb;
                    }
                }
                // Scenario B: Suffix transition - creates new compound verb
                // Handle both: Suffix with VerbLike (かねる) and Verb with PossibleDependant (切れる, 合う, etc.)
                // IMPORTANT: Only apply when:
                // 1. Base is a Verb, not a Noun (e.g. 提出+いただき should NOT combine)
                // 2. Current word doesn't end in te-form or auxiliary patterns (these are grammatical constructions, not compounds)
                //    - て/で: te-form (探して+みる is NOT a compound)
                //    - たく/なく: adverbial form of auxiliaries (転げ回りたく+なる is NOT a compound)
                // 3. Next word is not an auxiliary verb (補助動詞) like 続ける, 始める - these add aspect/meaning and should stay separate
                else if (currentPOS == PartOfSpeech.Verb &&
                         !currentWord.Text.EndsWith("て") &&
                         !currentWord.Text.EndsWith("で") &&
                         !currentWord.Text.EndsWith("たく") &&
                         !currentWord.Text.EndsWith("なく") &&
                         !AuxiliaryVerbs.Contains(nextWord.DictionaryForm) &&
                         (nextWord.HasPartOfSpeechSection(PartOfSpeechSection.VerbLike) ||
                          (nextWord.PartOfSpeech == PartOfSpeech.Verb &&
                           nextWord.HasPartOfSpeechSection(PartOfSpeechSection.PossibleDependant))))
                {
                    var suffixDict = KanaNormalizer.Normalize(WanaKana.ToHiragana(nextWord.DictionaryForm));
                    var match = forms.FirstOrDefault(f => f.Text.EndsWith(suffixDict) && f.Text.Length > suffixDict.Length);

                    if (match != null)
                    {
                        merged = true;
                        newDictForm = match.Text;
                        currentPOS = PartOfSpeech.Verb;
                    }
                }

                if (merged)
                {
                    currentWord.Text = candidateText;
                    currentWord.PartOfSpeech = currentPOS;
                    if (newDictForm != null)
                        currentWord.DictionaryForm = newDictForm;
                    currentDictForm = currentWord.DictionaryForm;
                    i++;
                }
                else
                {
                    break;
                }
            }

            result.Add(currentWord);
        }

        return result;
    }

    private List<WordInfo> CombinePrefixes(List<WordInfo> wordInfos)
    {
        if (wordInfos.Count < 2)
            return wordInfos;

        List<WordInfo> newList = new List<WordInfo>(wordInfos.Count);
        var currentWord = new WordInfo(wordInfos[0]);

        for (int i = 1; i < wordInfos.Count; i++)
        {
            var nextWord = wordInfos[i];
            if (currentWord.PartOfSpeech == PartOfSpeech.Prefix && currentWord.NormalizedForm != "御" && currentWord.NormalizedForm != "大" &&
                currentWord.NormalizedForm != "下" && currentWord.NormalizedForm != "約" && currentWord.NormalizedForm != "秋" &&
                currentWord.NormalizedForm != "本" && currentWord.NormalizedForm != "中")
            {
                var newText = currentWord.Text + nextWord.Text;
                currentWord = new WordInfo(nextWord);
                currentWord.Text = newText;
            }
            else
            {
                newList.Add(currentWord);
                currentWord = new WordInfo(nextWord);
            }
        }

        newList.Add(currentWord);

        return newList;
    }

    private List<WordInfo> CombineAmounts(List<WordInfo> wordInfos)
    {
        if (wordInfos.Count < 2)
            return wordInfos;

        List<WordInfo> newList = new List<WordInfo>(wordInfos.Count);
        var currentWord = new WordInfo(wordInfos[0]);
        for (int i = 1; i < wordInfos.Count; i++)
        {
            var nextWord = wordInfos[i];

            if ((currentWord.HasPartOfSpeechSection(PartOfSpeechSection.Amount) ||
                 currentWord.HasPartOfSpeechSection(PartOfSpeechSection.Numeral)) &&
                AmountCombinations.Combinations.Contains((currentWord.Text, nextWord.Text)))
            {
                var text = currentWord.Text + nextWord.Text;
                currentWord = new WordInfo(nextWord);
                currentWord.Text = text;
                currentWord.PartOfSpeech = PartOfSpeech.Noun;
            }
            else
            {
                newList.Add(currentWord);
                currentWord = new WordInfo(nextWord);
            }
        }

        newList.Add(currentWord);

        return newList;
    }

    private List<WordInfo> CombineTte(List<WordInfo> wordInfos)
    {
        if (wordInfos.Count < 2)
            return wordInfos;

        List<WordInfo> newList = new List<WordInfo>(wordInfos.Count);
        var currentWord = new WordInfo(wordInfos[0]);
        for (int i = 1; i < wordInfos.Count; i++)
        {
            WordInfo nextWord = wordInfos[i];

            if (currentWord.Text.EndsWith("っ") && nextWord.Text.StartsWith("て"))
            {
                currentWord.Text += nextWord.Text;
            }
            else
            {
                newList.Add(currentWord);
                currentWord = new WordInfo(nextWord);
            }
        }

        newList.Add(currentWord);

        return newList;
    }

    private List<WordInfo> CombineVerbDependant(List<WordInfo> wordInfos)
    {
        if (wordInfos.Count < 2)
            return wordInfos;

        wordInfos = CombineVerbDependants(wordInfos);
        wordInfos = CombineVerbPossibleDependants(wordInfos);
        wordInfos = CombineVerbDependantsSuru(wordInfos);
        wordInfos = CombineVerbDependantsTeiru(wordInfos);

        return wordInfos;
    }

    private List<WordInfo> CombineVerbDependants(List<WordInfo> wordInfos)
    {
        if (wordInfos.Count < 2)
            return wordInfos;

        List<WordInfo> newList = new List<WordInfo>();
        WordInfo currentWord = new WordInfo(wordInfos[0]);

        for (int i = 1; i < wordInfos.Count; i++)
        {
            WordInfo nextWord = wordInfos[i];

            if (nextWord.HasPartOfSpeechSection(PartOfSpeechSection.Dependant) &&
                currentWord.PartOfSpeech == PartOfSpeech.Verb)
            {
                currentWord.Text += nextWord.Text;
            }
            else
            {
                newList.Add(currentWord);
                currentWord = new WordInfo(nextWord);
            }
        }

        newList.Add(currentWord);
        return newList;
    }

    private List<WordInfo> CombineVerbPossibleDependants(List<WordInfo> wordInfos)
    {
        if (wordInfos.Count < 2)
            return wordInfos;

        List<WordInfo> newList = new List<WordInfo>();
        WordInfo currentWord = new WordInfo(wordInfos[0]);

        for (int i = 1; i < wordInfos.Count; i++)
        {
            WordInfo nextWord = wordInfos[i];

            // Condition uses accumulator (verb) and next word (possible dependant + specific forms)
            // Note: きる is intentionally excluded because it creates compound verbs (e.g., 食べきる)
            // that are often not in JMDict, causing lookup failures. Keep them separate for better parsing.
            if (nextWord.HasPartOfSpeechSection(PartOfSpeechSection.PossibleDependant) &&
                currentWord.PartOfSpeech == PartOfSpeech.Verb && !currentWord.Text.EndsWith("たり") &&
                nextWord.DictionaryForm is "得る" or "する" or "しまう" or "おる" or "こなす" or "いく" or "貰う" or "いる" or "ない")
            {
                currentWord.Text += nextWord.Text;
            }
            else
            {
                newList.Add(currentWord);
                currentWord = new WordInfo(nextWord);
            }
        }

        newList.Add(currentWord);
        return newList;
    }

    private List<WordInfo> CombineVerbDependantsSuru(List<WordInfo> wordInfos)
    {
        if (wordInfos.Count < 2)
            return wordInfos;

        List<WordInfo> newList = new List<WordInfo>();
        int i = 0;
        while (i < wordInfos.Count)
        {
            WordInfo currentWord = wordInfos[i];

            if (i + 1 < wordInfos.Count)
            {
                WordInfo nextWord = wordInfos[i + 1];
                if (currentWord.HasPartOfSpeechSection(PartOfSpeechSection.PossibleSuru) &&
                    nextWord.DictionaryForm == "する" && nextWord.Text != "する" && nextWord.Text != "しない")
                {
                    WordInfo combinedWord = new WordInfo(currentWord);
                    combinedWord.Text += nextWord.Text;
                    // combinedWord.PartOfSpeech = PartOfSpeech.Verb;
                    newList.Add(combinedWord);
                    i += 2;
                    continue;
                }
            }

            newList.Add(new WordInfo(currentWord));
            i++;
        }

        return newList;
    }

    private List<WordInfo> CombineVerbDependantsTeiru(List<WordInfo> wordInfos)
    {
        if (wordInfos.Count < 2)
            return wordInfos;

        List<WordInfo> newList = new List<WordInfo>();
        int i = 0;
        while (i < wordInfos.Count)
        {
            WordInfo currentWord = wordInfos[i];

            // Pattern 1: Verb + て (particle) + いる (3 tokens)
            if (i + 2 < wordInfos.Count)
            {
                WordInfo nextWord1 = wordInfos[i + 1];
                WordInfo nextWord2 = wordInfos[i + 2];

                if (currentWord.PartOfSpeech is PartOfSpeech.Verb &&
                    nextWord1.DictionaryForm == "て" &&
                    nextWord2.DictionaryForm == "いる")
                {
                    WordInfo combinedWord = new WordInfo(currentWord);
                    combinedWord.Text += nextWord1.Text + nextWord2.Text;
                    newList.Add(combinedWord);
                    i += 3;
                    continue;
                }
            }

            // Pattern 2: Word ending in て/で + いる or ない (2 tokens)
            // Handles cases where て is already combined with the verb/adjective (e.g., うらやましがられて + いる, 進んで + ない)
            if (i + 1 < wordInfos.Count)
            {
                WordInfo nextWord = wordInfos[i + 1];

                if ((currentWord.Text.EndsWith("て") || currentWord.Text.EndsWith("で")) &&
                    nextWord.HasPartOfSpeechSection(PartOfSpeechSection.PossibleDependant) &&
                    nextWord.DictionaryForm is "いる" or "ない")
                {
                    WordInfo combinedWord = new WordInfo(currentWord);
                    combinedWord.Text += nextWord.Text;
                    newList.Add(combinedWord);
                    i += 2;
                    continue;
                }
            }

            newList.Add(new WordInfo(currentWord));
            i++;
        }

        return newList;
    }

    private List<WordInfo> CombineAdverbialParticle(List<WordInfo> wordInfos)
    {
        if (wordInfos.Count < 2)
            return wordInfos;

        List<WordInfo> newList = new List<WordInfo>();
        WordInfo currentWord = new WordInfo(wordInfos[0]);

        for (int i = 1; i < wordInfos.Count; i++)
        {
            WordInfo nextWord = wordInfos[i];

            // i.e　だり, たり
            if (nextWord.HasPartOfSpeechSection(PartOfSpeechSection.AdverbialParticle) &&
                (nextWord.DictionaryForm == "だり" || nextWord.DictionaryForm == "たり") &&
                currentWord.PartOfSpeech == PartOfSpeech.Verb)

            {
                currentWord.Text += nextWord.Text;
            }
            else
            {
                newList.Add(currentWord);
                currentWord = new WordInfo(nextWord);
            }
        }

        newList.Add(currentWord);

        return newList;
    }

    private List<WordInfo> CombineConjunctiveParticle(List<WordInfo> wordInfos)
    {
        if (wordInfos.Count < 2)
            return wordInfos;

        List<WordInfo> newList = [wordInfos[0]];

        for (int i = 1; i < wordInfos.Count; i++)
        {
            WordInfo currentWord = wordInfos[i];
            WordInfo previousWord = newList[^1];
            bool combined = false;

            if (currentWord.HasPartOfSpeechSection(PartOfSpeechSection.ConjunctionParticle) &&
                currentWord.Text is "て" or "で" or "ちゃ" or "ば" &&
                previousWord.PartOfSpeech is PartOfSpeech.Verb or PartOfSpeech.IAdjective or PartOfSpeech.Auxiliary)
            {
                previousWord.Text += currentWord.Text;
                combined = true;
            }

            if (!combined)
            {
                newList.Add(currentWord);
            }
        }

        return newList;
    }

    private List<WordInfo> CombineAuxiliary(List<WordInfo> wordInfos)
    {
        if (wordInfos.Count < 2)
            return wordInfos;

        List<WordInfo> newList =
        [
            wordInfos[0]
        ];

        for (int i = 1; i < wordInfos.Count; i++)
        {
            WordInfo currentWord = wordInfos[i];
            WordInfo previousWord = newList[^1];
            bool combined = false;

            if (currentWord.PartOfSpeech != PartOfSpeech.Auxiliary)
            {
                newList.Add(currentWord);
                continue;
            }

            if ((previousWord.PartOfSpeech is PartOfSpeech.Verb or PartOfSpeech.IAdjective or PartOfSpeech.NaAdjective
                     or PartOfSpeech.Auxiliary
                 || previousWord.HasPartOfSpeechSection(PartOfSpeechSection.Adjectival))
                && currentWord.Text != "な"
                && currentWord.Text != "に"
                && (currentWord.DictionaryForm != "です" ||
                    previousWord.PartOfSpeech is PartOfSpeech.Verb && currentWord is { DictionaryForm: "です", Text: "でし" or "でした" })
                && currentWord.DictionaryForm != "らしい"
                && currentWord.Text != "なら"
                && currentWord.Text != "なる"
                && currentWord.DictionaryForm != "べし"
                && currentWord.DictionaryForm != "ようだ"
                && currentWord.DictionaryForm != "やがる"
                && currentWord.DictionaryForm != "たり"
                && currentWord.DictionaryForm != "筈"
                && currentWord.Text != "だろう"
                && currentWord.Text != "で"
                && currentWord.Text != "や"
                && currentWord.Text != "やろ"
                && currentWord.Text != "やしない"
                && currentWord.Text != "し"
                && currentWord.Text != "なのだ"
                && currentWord.Text != "だろ"
                && currentWord.Text != "ハズ"
                && (currentWord.Text != "だ" || currentWord.Text == "だ" && previousWord.Text[^1] == 'ん')
               )
            {
                previousWord.Text += currentWord.Text;
                combined = true;
            }

            if (!combined)
            {
                newList.Add(currentWord);
            }
        }

        return newList;
    }

    private List<WordInfo> CombineAuxiliaryVerbStem(List<WordInfo> wordInfos)
    {
        if (wordInfos.Count < 2)
            return wordInfos;

        List<WordInfo> newList = new List<WordInfo>();
        WordInfo currentWord = new WordInfo(wordInfos[0]);

        for (int i = 1; i < wordInfos.Count; i++)
        {
            var nextWord = wordInfos[i];

            // Combine AuxiliaryVerbStem (そう, etc.) with preceding verb/adjective
            // Also handle adjectival suffixes like やすい, にくい, づらい (their stem forms: やす, にく, づら)
            var isAdjectivalSuffix = wordInfos[i - 1].PartOfSpeech == PartOfSpeech.Suffix &&
                                     wordInfos[i - 1].DictionaryForm.EndsWith("い");
            if (wordInfos[i].HasPartOfSpeechSection(PartOfSpeechSection.AuxiliaryVerbStem) &&
                wordInfos[i].Text != "ように" &&
                wordInfos[i].Text != "よう" &&
                wordInfos[i].Text != "ようです" &&
                wordInfos[i].Text != "みたい" &&
                (wordInfos[i - 1].PartOfSpeech == PartOfSpeech.Verb ||
                 wordInfos[i - 1].PartOfSpeech == PartOfSpeech.IAdjective ||
                 wordInfos[i - 1].PartOfSpeech == PartOfSpeech.Noun ||
                 isAdjectivalSuffix))
            {
                currentWord.Text += nextWord.Text;
            }
            else
            {
                newList.Add(currentWord);
                currentWord = new WordInfo(nextWord);
            }
        }

        newList.Add(currentWord);

        return newList;
    }

    private List<WordInfo> CombineSuffix(List<WordInfo> wordInfos)
    {
        if (wordInfos.Count < 2)
            return wordInfos;

        List<WordInfo> newList = new List<WordInfo>();
        WordInfo currentWord = new WordInfo(wordInfos[0]);

        for (int i = 1; i < wordInfos.Count; i++)
        {
            var nextWord = wordInfos[i];

            if ((wordInfos[i].PartOfSpeech == PartOfSpeech.Suffix || wordInfos[i].HasPartOfSpeechSection(PartOfSpeechSection.Suffix))
                && (wordInfos[i].DictionaryForm == "っこ"
                    || wordInfos[i].DictionaryForm == "さ"
                    || wordInfos[i].DictionaryForm == "がる"
                    || (wordInfos[i].DictionaryForm == "ら" &&
                        wordInfos[i - 1].PartOfSpeech == PartOfSpeech.Pronoun && wordInfos[i - 1].Text != "貴様")))
            {
                currentWord.Text += nextWord.Text;
            }
            else
            {
                newList.Add(currentWord);
                currentWord = new WordInfo(nextWord);
            }
        }

        newList.Add(currentWord);
        return newList;
    }

    private List<WordInfo> CombineParticles(List<WordInfo> wordInfos)
    {
        if (wordInfos.Count < 2)
            return wordInfos;

        List<WordInfo> newList = new List<WordInfo>();
        int i = 0;
        while (i < wordInfos.Count)
        {
            WordInfo currentWord = wordInfos[i];

            // Combine かもしれ* (kamoshirenai, kamoshiremasen, etc.) into single expression
            if (i + 2 < wordInfos.Count &&
                currentWord.Text == "か" &&
                wordInfos[i + 1].Text == "も" &&
                wordInfos[i + 2].Text.StartsWith("しれ"))
            {
                WordInfo combinedWord = new WordInfo(currentWord);
                combinedWord.Text = currentWord.Text + wordInfos[i + 1].Text + wordInfos[i + 2].Text;
                combinedWord.PartOfSpeech = PartOfSpeech.Expression;
                newList.Add(combinedWord);
                i += 3;
                continue;
            }

            if (i + 1 < wordInfos.Count)
            {
                WordInfo nextWord = wordInfos[i + 1];
                string combinedText = "";

                if (currentWord.Text == "に" && nextWord.Text == "は") combinedText = "には";
                else if (currentWord.Text == "と" && nextWord.Text == "は") combinedText = "とは";
                else if (currentWord.Text == "で" && nextWord.Text == "は") combinedText = "では";
                else if (currentWord.Text == "の" && nextWord.Text == "に") combinedText = "のに";

                if (!string.IsNullOrEmpty(combinedText))
                {
                    WordInfo combinedWord = new WordInfo(currentWord);
                    combinedWord.Text = combinedText;
                    newList.Add(combinedWord);
                    i += 2;
                    continue;
                }
            }

            newList.Add(new WordInfo(currentWord));
            i++;
        }

        return newList;
    }

    private List<WordInfo> CombineHiraganaElongation(List<WordInfo> wordInfos)
    {
        if (wordInfos.Count < 2) return wordInfos;

        var newList = new List<WordInfo>(wordInfos.Count);
        var currentWord = new WordInfo(wordInfos[0]);

        for (int i = 1; i < wordInfos.Count; i++)
        {
            var nextWord = wordInfos[i];

            // CHECK: Does the current word end in 'ー'?
            bool endsInBar = currentWord.Text.EndsWith("ー");

            // CHECK: Are we dealing with Hiragana? (Ignore Katakana words like コーヒー)
            // We strip the 'ー' to check if the 'root' is Hiragana.
            bool isHiraganaBase = WanaKana.IsHiragana(currentWord.Text.Replace("ー", ""));
            bool nextIsHiragana = WanaKana.IsHiragana(nextWord.Text.Replace("ー", ""));

            // LOGIC: If current is [Hiragana]ー and next is [Hiragana], they are likely one spoken word.
            // Example: どー (Do-) + いう (Iu) -> どーいう
            // Example: だー (Da-) + かー (Ka-) -> だーかー
            if (endsInBar && isHiraganaBase && nextIsHiragana && nextWord.PartOfSpeech != PartOfSpeech.Particle)
            {
                currentWord.Text += nextWord.Text;
            }
            else
            {
                newList.Add(currentWord);
                currentWord = new WordInfo(nextWord);
            }
        }

        newList.Add(currentWord);
        return newList;
    }

    /// <summary>
    /// Cleanup method / 2nd pass for some cases
    /// </summary>
    /// <param name="wordInfos"></param>
    /// <returns></returns>
    /// <exception cref="NotImplementedException"></exception>
    private List<WordInfo> CombineFinal(List<WordInfo> wordInfos)
    {
        if (wordInfos.Count < 2)
            return wordInfos;

        List<WordInfo> newList = new List<WordInfo>();
        WordInfo currentWord = new WordInfo(wordInfos[0]);

        for (int i = 1; i < wordInfos.Count; i++)
        {
            var nextWord = wordInfos[i];

            if (wordInfos[i].Text == "ば" &&
                wordInfos[i - 1].PartOfSpeech == PartOfSpeech.Verb)
            {
                currentWord.Text += nextWord.Text;
            }
            else
            {
                newList.Add(currentWord);
                currentWord = new WordInfo(nextWord);
            }
        }

        newList.Add(currentWord);

        return newList;
    }

    private List<SentenceInfo> SplitIntoSentences(string text, List<WordInfo> wordInfos)
    {
        var sentences = new List<SentenceInfo>();

        var sb = new StringBuilder();
        bool seenEnder = false;

        // Need a flat text for the sentence to corresponds if they're cut between 2 lines
        text = text.Replace("\r", "").Replace("\n", "");

        for (int i = 0; i < text.Length; i++)
        {
            char current = text[i];
            sb.Append(current);

            // Detect if sentence ender was seen
            if (_sentenceEnders.Contains(current))
            {
                seenEnder = true;
                continue;
            }

            // Handle possible multiple enders in a row
            if (seenEnder)
            {
                if (_sentenceEnders.Contains(current))
                    continue;

                // Flush the sentence and append the character to the next one instead
                var lastCharacter = sb[^1];
                sentences.Add(new SentenceInfo(sb.ToString(0, sb.Length - 1)));
                sb.Clear();
                sb.Append(lastCharacter);
                seenEnder = false;
            }
        }

        // Handle leftover buffer
        if (sb.Length > 0)
        {
            sentences.Add(new SentenceInfo(sb.ToString()));
        }

        int currentSentenceIndex = 0;
        int currentCharIndex = 0;
        foreach (var word in wordInfos)
        {
            if (string.IsNullOrEmpty(word.Text) || word.PartOfSpeech == PartOfSpeech.BlankSpace)
                continue;

            bool wordAssigned = false;
            while (currentSentenceIndex < sentences.Count && !wordAssigned)
            {
                var sentence = sentences[currentSentenceIndex];
                int wordIndex = sentence.Text.IndexOf(word.Text, currentCharIndex, StringComparison.Ordinal);

                if (wordIndex >= 0)
                {
                    currentCharIndex = wordIndex + word.Text.Length;
                    sentences[currentSentenceIndex].Words.Add((word, (byte)wordIndex, (byte)word.Text.Length));
                    wordAssigned = true;
                }
                else
                {
                    // Word not found, check if it spans across to the next sentence
                    if (currentSentenceIndex + 1 < sentences.Count)
                    {
                        var remainingTextInCurrentSentence = currentCharIndex < sentence.Text.Length
                            ? sentence.Text[currentCharIndex..]
                            : string.Empty;
                        var nextSentence = sentences[currentSentenceIndex + 1];

                        for (int i = 1; i < word.Text.Length; i++)
                        {
                            string part1 = word.Text[..i];
                            string part2 = word.Text[i..];
                            if (!remainingTextInCurrentSentence.EndsWith(part1) || !nextSentence.Text.StartsWith(part2)) continue;

                            // Word spans sentences, merge them
                            sentence.Text += nextSentence.Text;
                            sentences.RemoveAt(currentSentenceIndex + 1);

                            // Retry finding the word in the merged sentence
                            wordIndex = sentence.Text.IndexOf(word.Text, currentCharIndex, StringComparison.Ordinal);
                            if (wordIndex >= 0)
                            {
                                currentCharIndex = wordIndex + word.Text.Length;
                                sentence.Words.Add((word, (byte)wordIndex, (byte)word.Text.Length));
                                wordAssigned = true;
                            }

                            break;
                        }
                    }

                    if (wordAssigned) continue;

                    currentSentenceIndex++;
                    currentCharIndex = 0;
                }
            }
        }

        return sentences;
    }

    #region Diagnostics Helpers

    private static List<SudachiToken> ParseSudachiOutputToDiagnosticTokens(string rawOutput)
    {
        var tokens = new List<SudachiToken>();
        var lines = rawOutput.Split('\n');

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line) || line == "EOS") continue;

            var parts = line.Split('\t');
            if (parts.Length < 6) continue;

            var posDetail = parts[1].Split(',');
            tokens.Add(new SudachiToken
            {
                Surface = parts[0],
                PartOfSpeech = posDetail.Length > 0 ? posDetail[0] : "",
                PosDetail = posDetail.Skip(1).ToArray(),
                NormalizedForm = parts.Length > 2 ? parts[2] : "",
                DictionaryForm = parts.Length > 3 ? parts[3] : "",
                Reading = parts.Length > 5 ? parts[5] : ""
            });
        }

        return tokens;
    }

    private static List<WordInfo> TrackStage(
        ParserDiagnostics? diagnostics,
        string stageName,
        List<WordInfo> input,
        Func<List<WordInfo>, List<WordInfo>> processor)
    {
        if (diagnostics == null)
            return processor(input);

        var inputSnapshot = input.Select(w => w.Text).ToList();
        var sw = Stopwatch.StartNew();
        var result = processor(input);
        sw.Stop();

        var stage = new TokenProcessingStage
        {
            StageName = stageName,
            ElapsedMs = sw.Elapsed.TotalMilliseconds,
            InputTokenCount = inputSnapshot.Count,
            OutputTokenCount = result.Count,
            Modifications = DetectModifications(inputSnapshot, result.Select(w => w.Text).ToList())
        };
        diagnostics.TokenStages.Add(stage);

        return result;
    }

    private static List<TokenModification> DetectModifications(List<string> inputTokens, List<string> outputTokens)
    {
        var modifications = new List<TokenModification>();

        // Find tokens that were removed
        var outputSet = new HashSet<string>(outputTokens);
        var inputSet = new HashSet<string>(inputTokens);

        // Detect merges: multiple input tokens → single output token
        int i = 0, j = 0;
        while (i < inputTokens.Count && j < outputTokens.Count)
        {
            if (inputTokens[i] == outputTokens[j])
            {
                i++;
                j++;
                continue;
            }

            // Check if output token is a merge of consecutive input tokens
            var merged = new StringBuilder();
            var mergeStart = i;
            while (i < inputTokens.Count && !merged.ToString().Equals(outputTokens[j]))
            {
                merged.Append(inputTokens[i]);
                i++;

                if (merged.ToString() == outputTokens[j])
                {
                    modifications.Add(new TokenModification
                    {
                        Type = "merge",
                        InputTokens = inputTokens.Skip(mergeStart).Take(i - mergeStart).ToArray(),
                        OutputToken = outputTokens[j],
                        Reason = $"Merged {i - mergeStart} tokens"
                    });
                    j++;
                    break;
                }
            }

            // If no merge found, tokens differ
            if (i == mergeStart)
            {
                i++;
                j++;
            }
        }

        // Remaining input tokens were removed
        while (i < inputTokens.Count)
        {
            modifications.Add(new TokenModification
            {
                Type = "remove",
                InputTokens = [inputTokens[i]],
                OutputToken = null,
                Reason = "Token removed"
            });
            i++;
        }

        return modifications;
    }

    #endregion
}