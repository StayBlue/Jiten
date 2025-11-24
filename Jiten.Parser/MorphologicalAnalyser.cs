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
        ["そ", "ー", "る", "ま", "ふ", "ち", "ほ", "す", "じ", "なさ", "い", "ぴ", "ふあ", "ぷ", "ちゅ", "にっ", "じら"];

    public async Task<List<SentenceInfo>> Parse(string text, bool morphemesOnly = false)
    {
        var configuration = new ConfigurationBuilder()
                            .SetBasePath(Directory.GetCurrentDirectory())
                            .AddJsonFile(Path.Combine(Environment.CurrentDirectory, "..", "Shared", "sharedsettings.json"), optional: true)
                            .AddJsonFile("sharedsettings.json", optional: true)
                            .AddJsonFile("appsettings.json", optional: true)
                            .AddEnvironmentVariables()
                            .Build();

        // Build dictionary  sudachi ubuild Y:\CODE\Jiten\Shared\resources\user_dic.xml -s S:\Jiten\sudachi.rs\resources\system_full.dic -o "Y:\CODE\Jiten\Shared\resources\user_dic.dic"

        // Preprocess the text to remove invalid characters
        PreprocessText(ref text);

        // Custom stuff in the user dictionary interferes with the mode A morpheme parsing
        var configPath =
            morphemesOnly
                ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "resources", "sudachi_nouserdic.json")
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "resources", "sudachi.json");
        var dic = configuration.GetValue<string>("DictionaryPath");

        var output = SudachiInterop.ProcessText(configPath, text, dic, mode: morphemesOnly ? 'A' : 'C').Split("\n");

        text = text.Replace(" ", "");

        List<WordInfo> wordInfos = new();

        foreach (var line in output)
        {
            if (line == "EOS") continue;

            var wi = new WordInfo(line);
            if (!wi.IsInvalid)
                wordInfos.Add(wi);
        }

        if (morphemesOnly)
            return [new SentenceInfo("") { Words = wordInfos.Select(w => (w, (byte)0, (byte)0)).ToList() }];

        wordInfos = ProcessSpecialCases(wordInfos);

        // Disabled this, seems like it's doing more harm than good
        // wordInfos = CombinePrefixes(wordInfos);

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

        return SplitIntoSentences(text, wordInfos);
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

    private void PreprocessText(ref string text)
    {
        text = text.Replace("<", " ");
        text = text.Replace(">", " ");
        text = text.ToFullWidthDigits();
        text = Regex.Replace(text,
                             "[^\u3040-\u309F\u30A0-\u30FF\u4E00-\u9FAF\uFF21-\uFF3A\uFF41-\uFF5A\uFF10-\uFF19\u3005\u3001-\u3003\u3008-\u3011\u3014-\u301F\uFF01-\uFF0F\uFF1A-\uFF1F\uFF3B-\uFF3F\uFF5B-\uFF60\uFF62-\uFF65．\\n…\u3000―\u2500()。！？「」）]",
                             "");

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

            if (w1.Text == "見降ろし")
            {
                var mi = new WordInfo
                         {
                             Text = "見", DictionaryForm = "見", PartOfSpeech = PartOfSpeech.Noun,
                             PartOfSpeechSection1 = PartOfSpeechSection.None, Reading = "見"
                         };
                var oroshi = new WordInfo
                             {
                                 Text = "降ろし", DictionaryForm = "降ろす", PartOfSpeech = PartOfSpeech.Verb,
                                 PartOfSpeechSection1 = PartOfSpeechSection.None, Reading = "降ろし"
                             };

                newList.Add(mi);
                newList.Add(oroshi);
                i++;
                continue;
            }
            
            if (w1.Text == "斬り裂い")
            {
                var kiru = new WordInfo
                         {
                             Text = "斬り", DictionaryForm = "斬る", PartOfSpeech = PartOfSpeech.Verb,
                             PartOfSpeechSection1 = PartOfSpeechSection.None, Reading = "斬り"
                         };
                var saku = new WordInfo
                             {
                                 Text = "裂い", DictionaryForm = "裂く", PartOfSpeech = PartOfSpeech.Verb,
                                 PartOfSpeechSection1 = PartOfSpeechSection.None, Reading = "裂い"
                             };

                newList.Add(kiru);
                newList.Add(saku);
                i++;
                continue;
            }
            
            if (w1.Text == "斬り裂く")
            {
                var kiru = new WordInfo
                           {
                               Text = "斬り", DictionaryForm = "斬る", PartOfSpeech = PartOfSpeech.Verb,
                               PartOfSpeechSection1 = PartOfSpeechSection.None, Reading = "斬り"
                           };
                var saku = new WordInfo
                           {
                               Text = "裂く", DictionaryForm = "裂く", PartOfSpeech = PartOfSpeech.Verb,
                               PartOfSpeechSection1 = PartOfSpeechSection.None, Reading = "裂く"
                           };

                newList.Add(kiru);
                newList.Add(saku);
                i++;
                continue;
            }
            
            if (w1.Text == "砕き割れ")
            {
                var kudaku = new WordInfo
                           {
                               Text = "砕き", DictionaryForm = "砕く", PartOfSpeech = PartOfSpeech.Verb,
                               PartOfSpeechSection1 = PartOfSpeechSection.None, Reading = "砕き"
                           };
                var ware = new WordInfo
                           {
                               Text = "割れ", DictionaryForm = "割れる", PartOfSpeech = PartOfSpeech.Verb,
                               PartOfSpeechSection1 = PartOfSpeechSection.None, Reading = "割れ"
                           };

                newList.Add(kudaku);
                newList.Add(ware);
                i++;
                continue;
            }
            
            // TODO: prune from dictionary
            if (w1.NormalizedForm == "囁き合う")
            {
                int kiIndex = w1.Text.IndexOf('き');
                if (kiIndex > 0)
                {
                    var sasayaki = new WordInfo
                                   {
                                       Text = w1.Text[..(kiIndex + 1)], DictionaryForm = "囁く", PartOfSpeech = PartOfSpeech.Verb,
                                       Reading = "ささやき"
                                   };
                    var au = new WordInfo
                             {
                                 Text = w1.Text[(kiIndex + 1)..], DictionaryForm = "合う", PartOfSpeech = PartOfSpeech.Verb, Reading = "あう"
                             };

                    newList.Add(sasayaki);
                    newList.Add(au);
                    i++;
                    continue;
                }
            }

            if (w1.NormalizedForm.StartsWith("垣間見"))
            {
                int miIndex = w1.Text.IndexOf('見');
                if (miIndex > 0)
                {
                    var kakima = new WordInfo
                                 {
                                     Text = w1.Text[..(miIndex)], DictionaryForm = "垣間", PartOfSpeech = PartOfSpeech.Noun,
                                     Reading = "垣間"
                                 };
                    var mi = new WordInfo
                             {
                                 Text = w1.Text[(miIndex)..], DictionaryForm = "見える", PartOfSpeech = PartOfSpeech.Verb,
                                 Reading = w1.Text[(miIndex)..]
                             };
                    newList.Add(kakima);
                    newList.Add(mi);
                    i++;
                    continue;
                }
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
                && currentWord.DictionaryForm != "べし"
                && currentWord.DictionaryForm != "ようだ"
                && currentWord.DictionaryForm != "やがる"
                && currentWord.DictionaryForm != "たり"
                && currentWord.Text != "だろう"
                && currentWord.Text != "で"
                && currentWord.Text != "や"
                && currentWord.Text != "やろ"
                && currentWord.Text != "し"
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
                    || ((wordInfos[i].DictionaryForm == "ら") &&
                        wordInfos[i - 1].PartOfSpeech == PartOfSpeech.Pronoun)))
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