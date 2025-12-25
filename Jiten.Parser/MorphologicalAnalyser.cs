using System.Text;
using System.Text.RegularExpressions;
using Jiten.Core.Data;
using Jiten.Core.Utils;
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
    ];

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
    ];

    private static readonly List<string> HonorificsSuffixes = ["さん", "ちゃん", "くん"];

    private readonly HashSet<char> _sentenceEnders = ['。', '！', '？', '」'];

    private static readonly HashSet<string> MisparsesRemove =
        ["そ", "ー", "る", "ま", "ふ", "ち", "ほ", "す", "じ", "なさ", "い", "ぴ", "ふあ", "ぷ", "ちゅ", "にっ", "じら", "タ", "け", "イ", "イッ", "ほっ",
        "ウー", "うー", "ううう", "うう", "ウウウウ", "ウウ", "ううっ", "かー", "ぐわー"];

    // Token to separate some words in sudachi
    private static readonly string _stopToken = "|";

    // Delimiter for batch processing multiple texts in a single Sudachi call
    private static readonly string _batchDelimiter = "|||";

    /// <summary>
    /// Parses the given text into a list of SentenceInfo objects by performing morphological analysis.
    /// Delegates to ParseBatch for a single codepath.
    /// </summary>
    /// <param name="text">The input text to be analyzed.</param>
    /// <param name="morphemesOnly">A boolean indicating whether the parsing should output only morphemes. When true, parsing will use mode 'A' for morpheme parsing.</param>
    /// <param name="preserveStopToken">A boolean indicating whether the stop token should be preserved in the processed text. Used in the ReaderController</param>
    /// <returns>A list of SentenceInfo objects representing the parsed output.</returns>
    public async Task<List<SentenceInfo>> Parse(string text, bool morphemesOnly = false, bool preserveStopToken = false)
    {
        var results = await ParseBatch([text], morphemesOnly, preserveStopToken);
        return results.Count > 0 ? results[0] : [];
    }

    /// <summary>
    /// Parses multiple texts in a single Sudachi call for efficiency.
    /// This is the main implementation - Parse() delegates here.
    /// </summary>
    /// <param name="texts">List of texts to parse.</param>
    /// <param name="morphemesOnly">A boolean indicating whether the parsing should output only morphemes.</param>
    /// <param name="preserveStopToken">A boolean indicating whether the stop token should be preserved.</param>
    /// <returns>List of SentenceInfo lists, one per input text.</returns>
    public async Task<List<List<SentenceInfo>>> ParseBatch(List<string> texts, bool morphemesOnly = false, bool preserveStopToken = false)
    {
        if (texts.Count == 0) return [];

        var configuration = new ConfigurationBuilder()
                            .SetBasePath(Directory.GetCurrentDirectory())
                            .AddJsonFile(Path.Combine(Environment.CurrentDirectory, "..", "Shared", "sharedsettings.json"), optional: true)
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
        var dic = configuration.GetValue<string>("DictionaryPath");

        var output = SudachiInterop.ProcessText(configPath, combinedText, dic, mode: morphemesOnly ? 'A' : 'C').Split("\n");

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

            // All Combine* methods - SINGLE place to maintain
            wordInfos = ProcessSpecialCases(wordInfos);
            wordInfos = CombineInflections(wordInfos);
            wordInfos = CombineAmounts(wordInfos);
            wordInfos = CombineTte(wordInfos);
            wordInfos = CombineAuxiliaryVerbStem(wordInfos);
            wordInfos = CombineAdverbialParticle(wordInfos);
            wordInfos = CombineSuffix(wordInfos);
            wordInfos = CombineConjunctiveParticle(wordInfos);
            wordInfos = CombineAuxiliary(wordInfos);
            wordInfos = CombineVerbDependant(wordInfos);
            wordInfos = CombineParticles(wordInfos);
            wordInfos = CombineHiraganaElongation(wordInfos);
            wordInfos = CombineFinal(wordInfos);
            wordInfos = FilterMisparse(wordInfos);

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
                    word.Text.Length == 2 && WanaKana.IsKana(word.Text[0].ToString()) && word.Text[1] == 'ー'
                    || word.Text is "エナ" or "えな"
                ))
            {
                wordInfos.RemoveAt(i);
                continue;
            }
        }

        return wordInfos;
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

                // surukudasai
                if (w1.DictionaryForm == "する" && w2.Text == "て" && w3.DictionaryForm == "くださる")
                {
                    var newWord = new WordInfo(w1);
                    newWord.Text = w1.Text + w2.Text + w3.Text;

                    newList.Add(newWord);
                    i += 3;

                    continue;
                }

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
            }

            if (i < wordInfos.Count - 1)
            {
                WordInfo w2 = wordInfos[i + 1];

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
            
            // Always process な as the particle and not the vegetable
            // Always process に as the particle and not the baggage
            if (w1.Text is "な" or "に")
                w1.PartOfSpeech = PartOfSpeech.Particle;

            // Always process よう as the noun
            if (w1.Text is "よう")
                w1.PartOfSpeech = PartOfSpeech.Noun;

            if (w1.Text is "十五")
                w1.PartOfSpeech = PartOfSpeech.Numeral;

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
            bool isBase = (InflectableBasePOS.Contains(currentWord.PartOfSpeech) ||
                           currentWord.HasPartOfSpeechSection(PartOfSpeechSection.PossibleSuru))
                          && currentWord.NormalizedForm != "物";

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

                // Safety filter: stop at particles
                if (nextWord.Text is "は" or "な" or "よ" or "し" or "を" or "が" or "ください")
                    break;

                // Check if valid inflection part
                bool isValidPart = InflectionPartPOS.Contains(nextWord.PartOfSpeech) ||
                                   nextWord.HasPartOfSpeechSection(PartOfSpeechSection.AuxiliaryVerbStem) ||
                                   nextWord.HasPartOfSpeechSection(PartOfSpeechSection.ConjunctionParticle) ||
                                   nextWord.HasPartOfSpeechSection(PartOfSpeechSection.Dependant) ||
                                   nextWord.HasPartOfSpeechSection(PartOfSpeechSection.PossibleDependant);

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
                else if (currentPOS == PartOfSpeech.Verb &&
                         !currentWord.Text.EndsWith("て") &&
                         !currentWord.Text.EndsWith("で") &&
                         !currentWord.Text.EndsWith("たく") &&
                         !currentWord.Text.EndsWith("なく") &&
                         (nextWord.HasPartOfSpeechSection(PartOfSpeechSection.VerbLike) ||
                          (nextWord.PartOfSpeech == PartOfSpeech.Verb && nextWord.HasPartOfSpeechSection(PartOfSpeechSection.PossibleDependant))))
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
            if (nextWord.HasPartOfSpeechSection(PartOfSpeechSection.PossibleDependant) &&
                currentWord.PartOfSpeech == PartOfSpeech.Verb && !currentWord.Text.EndsWith("たり") &&
                nextWord.DictionaryForm is "得る" or "する" or "しまう" or "おる" or "きる" or "こなす" or "いく" or "貰う" or "いる" or "ない")
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
        if (wordInfos.Count < 3)
            return wordInfos;

        List<WordInfo> newList = new List<WordInfo>();
        int i = 0;
        while (i < wordInfos.Count)
        {
            WordInfo currentWord = wordInfos[i];

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

            if (wordInfos[i].HasPartOfSpeechSection(PartOfSpeechSection.AuxiliaryVerbStem) &&
                wordInfos[i].Text != "ように" &&
                wordInfos[i].Text != "よう" &&
                wordInfos[i].Text != "みたい" &&
                (wordInfos[i - 1].PartOfSpeech == PartOfSpeech.Verb || wordInfos[i - 1].PartOfSpeech == PartOfSpeech.IAdjective))
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
                        wordInfos[i - 1].PartOfSpeech == PartOfSpeech.Pronoun && wordInfos[i-1].Text != "貴様")))
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
}