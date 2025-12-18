using System.Diagnostics;
using System.Text.RegularExpressions;
using Jiten.Cli.ML;
using Jiten.Core;
using Jiten.Core.Data;
using Jiten.Core.Data.JMDict;
using Jiten.Core.Utils;
using Jiten.Parser.Data.Redis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using StackExchange.Redis;
using WanaKanaShaapu;

namespace Jiten.Parser
{
    public static class Parser
    {
        private static bool _initialized = false;
        private static readonly SemaphoreSlim _initSemaphore = new SemaphoreSlim(1, 1);

        private static readonly bool UseCache = true;
        private static IDeckWordCache DeckWordCache;
        private static IJmDictCache JmDictCache;

        private static IDbContextFactory<JitenDbContext> _contextFactory;
        private static Dictionary<string, List<int>> _lookups;


        private static async Task InitDictionaries()
        {
            var configuration = new ConfigurationBuilder()
                                .SetBasePath(Directory.GetCurrentDirectory())
                                .AddJsonFile(Path.Combine(Environment.CurrentDirectory, "..", "Shared", "sharedsettings.json"),
                                             optional: true,
                                             reloadOnChange: true)
                                .AddJsonFile("sharedsettings.json", optional: true, reloadOnChange: true)
                                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                                .AddEnvironmentVariables()
                                .Build();

            await using var context = await _contextFactory.CreateDbContextAsync();

            var optionsBuilder = new DbContextOptionsBuilder<JitenDbContext>();
            optionsBuilder.UseNpgsql(context.Database.GetConnectionString());

            DeckWordCache = new RedisDeckWordCache(configuration);
            JmDictCache = new RedisJmDictCache(configuration, _contextFactory);

            _lookups = await JmDictHelper.LoadLookupTable(context);

            // Check if cache is already initialized
            if (!await JmDictCache.IsCacheInitializedAsync())
            {
                // Cache not initialized, load from database and populate Redis
                var allWords = await JmDictHelper.LoadAllWords(context);

                const int BATCH_SIZE = 10000;

                // // Store lookups in Redis
                // for (int i = 0; i < _lookups.Count; i += BATCH_SIZE)
                // {
                //     var lookupsBatch = _lookups.Skip(i).Take(BATCH_SIZE).ToDictionary(x => x.Key, x => x.Value);
                //     await JmDictCache.SetLookupIdsAsync(lookupsBatch);
                // }

                // Store words in Redis using batching
                for (int i = 0; i < allWords.Count; i += BATCH_SIZE)
                {
                    var wordsBatch = allWords.Skip(i).Take(BATCH_SIZE)
                                             .ToDictionary(w => w.WordId, w => w);
                    await JmDictCache.SetWordsAsync(wordsBatch);
                }

                // Mark cache as initialized
                await JmDictCache.SetCacheInitializedAsync();
            }
        }

        public static async Task<List<DeckWord>> ParseText(IDbContextFactory<JitenDbContext> contextFactory, string text,
                                                           bool preserveStopToken = false)
        {
            _contextFactory = contextFactory;
            if (!_initialized)
            {
                await _initSemaphore.WaitAsync();
                try
                {
                    if (!_initialized) // Double-check to avoid race conditions
                    {
                        await InitDictionaries();
                        _initialized = true;
                    }
                }
                finally
                {
                    _initSemaphore.Release();
                }
            }

            var parser = new MorphologicalAnalyser();
            var sentences = await parser.Parse(text, preserveStopToken: preserveStopToken);
            var wordInfos = sentences.SelectMany(s => s.Words).Select(w => w.word).ToList();

            // Only keep kanjis, kanas, digits,full width digits, latin characters, full width latin characters
            foreach (WordInfo wi in wordInfos)
            {
                wi.Text = Regex.Replace(wi.Text,
                                        "[^a-zA-Z0-9\u3040-\u309F\u30A0-\u30FF\u4E00-\u9FAF\uFF21-\uFF3A\uFF41-\uFF5A\uFF10-\uFF19\u3005．]",
                                        "");
            }

            // Remove empty lines
            wordInfos.RemoveAll(x => string.IsNullOrWhiteSpace(x.Text));

            // Filter bad lines that cause exceptions
            wordInfos.ForEach(x => x.Text = Regex.Replace(x.Text, "ッー", ""));

            Deconjugator deconjugator = new Deconjugator();

            const int BATCH_SIZE = 1000;
            List<DeckWord> allProcessedWords = new List<DeckWord>();

            for (int i = 0; i < wordInfos.Count; i += BATCH_SIZE)
            {
                var batch = wordInfos.Skip(i).Take(BATCH_SIZE).ToList();
                var processBatch = batch.Select(word => ProcessWord((word, 0), deconjugator)).ToList();
                var batchResults = await Task.WhenAll(processBatch);

                allProcessedWords.AddRange(batchResults.Where(result => result != null).Select(result => result!));
            }

            return allProcessedWords;
        }

        public static async Task<Deck> ParseTextToDeck(IDbContextFactory<JitenDbContext> contextFactory, string text,
                                                       bool storeRawText = false,
                                                       bool predictDifficulty = true,
                                                       MediaType mediatype = MediaType.Novel)
        {
            _contextFactory = contextFactory;
            if (!_initialized)
            {
                await _initSemaphore.WaitAsync();
                try
                {
                    if (!_initialized) // Double-check to avoid race conditions
                    {
                        await InitDictionaries();
                        _initialized = true;
                    }
                }
                finally
                {
                    _initSemaphore.Release();
                }
            }

            var timer = new Stopwatch();
            timer.Start();
            var parser = new MorphologicalAnalyser();
            var sentences = await parser.Parse(text);
            var wordInfos = sentences.SelectMany(s => s.Words).Select(w => w.word).ToList();

            // TODO: support elongated vowels ふ～ -> ふう

            // Only keep kanjis, kanas, digits,full width digits, latin characters, full width latin characters 
            wordInfos.ForEach(x => x.Text =
                                  Regex.Replace(x.Text,
                                                "[^a-zA-Z0-9\u3040-\u309F\u30A0-\u30FF\u4E00-\u9FAF\uFF21-\uFF3A\uFF41-\uFF5A\uFF10-\uFF19\u3005．]",
                                                ""));
            // Remove empty lines
            wordInfos = wordInfos.Where(x => !string.IsNullOrWhiteSpace(x.Text)).ToList();

            // Filter bad lines that cause exceptions
            // wordInfos.RemoveAll(w => w.Text is "ッー");
            wordInfos.ForEach(x => x.Text = Regex.Replace(x.Text, "ッー", ""));

            Deconjugator deconjugator = new Deconjugator();

            var uniqueWords = new List<(WordInfo wordInfo, int occurrences)>();
            var wordCount = new Dictionary<(string, PartOfSpeech), int>();

            foreach (var word in wordInfos)
            {
                if (!wordCount.TryAdd((word.Text, word.PartOfSpeech), 1))
                    wordCount[(word.Text, word.PartOfSpeech)]++;
                else
                    uniqueWords.Add((word, 1));
            }

            for (int i = 0; i < uniqueWords.Count; i++)
            {
                uniqueWords[i] = (uniqueWords[i].wordInfo, wordCount[(uniqueWords[i].wordInfo.Text, uniqueWords[i].wordInfo.PartOfSpeech)]);
            }

            timer.Stop();
            double mecabTime = timer.Elapsed.TotalMilliseconds;

            timer.Restart();

            const int BATCH_SIZE = 1000;
            List<DeckWord> allProcessedWords = new List<DeckWord>();

            for (int i = 0; i < uniqueWords.Count; i += BATCH_SIZE)
            {
                var batch = uniqueWords.Skip(i).Take(BATCH_SIZE).ToList();
                var processBatch = batch.Select(word => ProcessWord(word, deconjugator)).ToList();
                var batchResults = await Task.WhenAll(processBatch);

                allProcessedWords.AddRange(batchResults.Where(result => result != null).Select(result => result!));
            }

            var processedWords = allProcessedWords.ToArray();

            processedWords = processedWords
                             .Select(result => result)
                             .ToArray();

            // Sum occurrences while deduplicating by WordId and ReadingIndex for deconjugated words
            processedWords = processedWords
                             .GroupBy(x => new { x.WordId, x.ReadingIndex })
                             .Select(g =>
                             {
                                 var first = g.First();
                                 first.Occurrences = g.Sum(w => w.Occurrences);
                                 return first;
                             })
                             .ToArray();

            List<ExampleSentence>? exampleSentences = null;

            if (mediatype is MediaType.Novel or MediaType.NonFiction or MediaType.VideoGame or MediaType.VisualNovel or MediaType.WebNovel)
                exampleSentences = ExampleSentenceExtractor.ExtractSentences(sentences, processedWords);

            var totalWordCount = processedWords.Select(w => w.Occurrences).Sum();

            timer.Stop();

            double deconjugationTime = timer.Elapsed.TotalMilliseconds;

            double totalTime = mecabTime + deconjugationTime;

            Console.WriteLine("Total words found : " + wordInfos.Count);

            // Console.WriteLine("Unique words found before deconjugation : " + uniqueWordInfos.Count);
            Console.WriteLine("Unique words found after deconjugation : " + processedWords.Length);

            var characterCount = wordInfos.Sum(x => x.Text.Length);
            Console.WriteLine($"Time elapsed: {totalTime:0.0}ms");
            Console.WriteLine($"Mecab time: {mecabTime:0.0}ms ({(mecabTime / totalTime * 100):0}%), Deconjugation time: {deconjugationTime:0.0}ms ({(deconjugationTime / totalTime * 100):0}%)");

            // Character count
            Console.WriteLine("Character count: " + characterCount);
            // Time for 10000 characters
            Console.WriteLine($"Time per 10000 characters: {(totalTime / characterCount * 10000):0.0}ms");
            // Time for 1million characters
            Console.WriteLine($"Time per 1 million characters: {(totalTime / characterCount * 1000000):0.0}ms");

            var textWithoutDialogues = Regex.Replace(text, @"[「『].{0,200}?[」』]", "", RegexOptions.Singleline);
            textWithoutDialogues = Regex.Replace(textWithoutDialogues,
                                                 "[^a-zA-Z0-9\u3040-\u309F\u30A0-\u30FF\u4E00-\u9FAF\uFF21-\uFF3A\uFF41-\uFF5A\uFF10-\uFF19\u3005．]",
                                                 "");
            var textWithoutPunctuation = Regex.Replace(text,
                                                       "[^a-zA-Z0-9\u3040-\u309F\u30A0-\u30FF\u4E00-\u9FAF\uFF21-\uFF3A\uFF41-\uFF5A\uFF10-\uFF19\u3005．]",
                                                       "");

            int dialogueCharacterCount = textWithoutPunctuation.Length - textWithoutDialogues.Length;
            float dialoguePercentage = (float)dialogueCharacterCount / textWithoutPunctuation.Length * 100f;

            Console.WriteLine($"Dialogue percentage: {dialoguePercentage:0.0}%");

            var deck = new Deck
                       {
                           CharacterCount = characterCount, WordCount = totalWordCount, UniqueWordCount = processedWords.Length,
                           UniqueWordUsedOnceCount = processedWords.Count(x => x.Occurrences == 1),
                           UniqueKanjiCount = wordInfos.SelectMany(w => w.Text).Distinct().Count(c => WanaKana.IsKanji(c.ToString())),
                           UniqueKanjiUsedOnceCount = wordInfos.SelectMany(w => w.Text).GroupBy(c => c)
                                                               .Count(g => g.Count() == 1 && WanaKana.IsKanji(g.Key.ToString())),
                           SentenceCount = sentences.Count, DialoguePercentage = dialoguePercentage, DeckWords = processedWords,
                           RawText = storeRawText ? new DeckRawText(text) : null, ExampleSentences = exampleSentences
                       };

            if (predictDifficulty)
            {
                string model;
                if (mediatype is MediaType.Novel or MediaType.NonFiction or MediaType.VideoGame or MediaType.VisualNovel or MediaType.Manga
                    or MediaType.WebNovel)
                    model = "difficulty_prediction_model_novels.onnx";
                else
                    model = "difficulty_prediction_model_shows.onnx";

                DifficultyPredictor difficultyPredictor =
                    new(_contextFactory, Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "resources", model));
                deck.Difficulty = await difficultyPredictor.PredictDifficulty(deck, mediatype);
                // DifficultyPredictorVae difficultyPredictor =
                //     new(_dbContext.DbOptions,
                //         Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "resources", "vae_prediction.py"));
                // deck.Difficulty = await difficultyPredictor.PredictDifficulty(deck, mediatype);
                Console.WriteLine($"Predicted difficulty: {deck.Difficulty}");
            }


            return deck;
        }

        public static async Task<List<DeckWord?>> ParseMorphenes(IDbContextFactory<JitenDbContext> contextFactory, string text)
        {
            _contextFactory = contextFactory;
            if (!_initialized)
            {
                await _initSemaphore.WaitAsync();
                try
                {
                    if (!_initialized) // Double-check to avoid race conditions
                    {
                        await InitDictionaries();
                        _initialized = true;
                    }
                }
                finally
                {
                    _initSemaphore.Release();
                }
            }

            var parser = new MorphologicalAnalyser();
            var sentences = await parser.Parse(text, morphemesOnly: true);
            var wordInfos = sentences.SelectMany(s => s.Words).Select(w => w.word).ToList();

            // Filter bad lines that cause exceptions
            wordInfos.ForEach(x => x.Text = Regex.Replace(x.Text, "ッー", ""));

            Deconjugator deconjugator = new Deconjugator();

            const int BATCH_SIZE = 5000;
            List<DeckWord?> allProcessedWords = new();

            for (int i = 0; i < wordInfos.Count; i += BATCH_SIZE)
            {
                var batch = wordInfos.Skip(i).Take(BATCH_SIZE).ToList();
                var processBatch = batch.Select(word => ProcessWord((word, 0), deconjugator)).ToList();
                var batchResults = await Task.WhenAll(processBatch);

                allProcessedWords.AddRange(batchResults);
            }

            return allProcessedWords;
        }

        // Limit how many concurrent operations we perform to prevent overwhelming the system
        private static readonly SemaphoreSlim _processSemaphore = new SemaphoreSlim(100, 100);

        private static async Task<DeckWord?> ProcessWord((WordInfo wordInfo, int occurrences) wordData, Deconjugator deconjugator)
        {
            // Try to acquire semaphore with timeout to prevent deadlock
            if (!await _processSemaphore.WaitAsync(TimeSpan.FromSeconds(5)))
            {
                // If we can't get the semaphore in a reasonable time, just return null
                // This is better than hanging indefinitely
                return null;
            }

            try
            {
                var cacheKey = new DeckWordCacheKey(
                                                    wordData.wordInfo.Text,
                                                    wordData.wordInfo.PartOfSpeech,
                                                    wordData.wordInfo.DictionaryForm
                                                   );

                if (UseCache)
                {
                    try
                    {
                        var cachedWord = await DeckWordCache.GetAsync(cacheKey);

                        if (cachedWord != null)
                        {
                            return new DeckWord
                                   {
                                       WordId = cachedWord.WordId, OriginalText = wordData.wordInfo.Text,
                                       ReadingIndex = cachedWord.ReadingIndex, Occurrences = wordData.occurrences,
                                       Conjugations = cachedWord.Conjugations, PartsOfSpeech = cachedWord.PartsOfSpeech,
                                       Origin = cachedWord.Origin
                                   };
                        }
                    }
                    catch (Exception ex)
                    {
                        // Log but continue - we'll just process the word directly
                        Console.WriteLine($"[Warning] Failed to read from DeckWordCache: {ex.Message}");
                    }
                }

                DeckWord? processedWord = null;
                bool isProcessed = false;
                int attemptCount = 0;
                const int maxAttempts = 3; // Limit how many attempts we make to prevent infinite loops

                var baseWord = wordData.wordInfo.Text;
                do
                {
                    attemptCount++;
                    try
                    {
                        if (wordData.wordInfo.PartOfSpeech is PartOfSpeech.Verb or PartOfSpeech.IAdjective or PartOfSpeech.Auxiliary
                                or PartOfSpeech.NaAdjective || wordData.wordInfo.PartOfSpeechSection1 is PartOfSpeechSection.Adjectival)
                        {
                            // Try to deconjugate as verb or adjective
                            var verbResult = await DeconjugateVerbOrAdjective(wordData, deconjugator);
                            if (!verbResult.success || verbResult.word == null)
                            {
                                // The word might be a noun misparsed as a verb/adjective like お祭り
                                var nounResult = await DeconjugateWord(wordData);
                                processedWord = nounResult.word;
                            }
                            else
                            {
                                processedWord = verbResult.word;
                            }
                        }
                        else
                        {
                            var nounResult = await DeconjugateWord(wordData);
                            if (!nounResult.success || nounResult.word == null)
                            {
                                // The word might be a conjugated noun + suru
                                var verbResult = await DeconjugateVerbOrAdjective(wordData, deconjugator);

                                var oldPos = wordData.wordInfo.PartOfSpeech;
                                // The word might be a verb or an adjective misparsed as a noun like らしく
                                if (!verbResult.success || verbResult.word == null)
                                {
                                    wordData.wordInfo.PartOfSpeech = PartOfSpeech.Verb;
                                    verbResult = await DeconjugateVerbOrAdjective(wordData, deconjugator);
                                }

                                if (!verbResult.success || verbResult.word == null)
                                {
                                    wordData.wordInfo.PartOfSpeech = PartOfSpeech.IAdjective;
                                    verbResult = await DeconjugateVerbOrAdjective(wordData, deconjugator);
                                }

                                if (!verbResult.success || verbResult.word == null)
                                {
                                    wordData.wordInfo.PartOfSpeech = PartOfSpeech.NaAdjective;
                                    verbResult = await DeconjugateVerbOrAdjective(wordData, deconjugator);
                                }

                                wordData.wordInfo.PartOfSpeech = oldPos;
                                processedWord = verbResult.word;
                            }
                            else
                            {
                                processedWord = nounResult.word;
                            }
                        }

                        if (processedWord != null) break;

                        // We haven't found a match, let's try to remove the last character if it's a っ, a ー or a duplicate
                        if (wordData.wordInfo.Text.Length > 2 &&
                            (wordData.wordInfo.Text[^1] == 'っ' || wordData.wordInfo.Text[^1] == 'ー' ||
                             wordData.wordInfo.Text[^2] == wordData.wordInfo.Text[^1]))
                        {
                            wordData.wordInfo.Text = wordData.wordInfo.Text[..^1];
                        }
                        // Let's try to remove any honorifics in front of the word
                        else if (wordData.wordInfo.Text.StartsWith("お"))
                        {
                            wordData.wordInfo.Text = wordData.wordInfo.Text[1..];
                        }
                        // Let's try without any long vowel mark
                        else if (wordData.wordInfo.Text.Contains('ー'))
                        {
                            wordData.wordInfo.Text = wordData.wordInfo.Text.Replace("ー", "");
                        }
                        // Let's try without small っ
                        else if (wordData.wordInfo.Text.Contains('っ') || wordData.wordInfo.Text.Contains('ッ'))
                        {
                            wordData.wordInfo.Text = baseWord.Replace("っ", "").Replace("ッ", "");
                        }
                        // Let's try stripping any small kana
                        else if (wordData.wordInfo.Text.Contains('ゃ') || wordData.wordInfo.Text.Contains('ゅ') ||
                                 wordData.wordInfo.Text.Contains('ょ') || wordData.wordInfo.Text.Contains('ぁ') ||
                                 wordData.wordInfo.Text.Contains('ぃ') || wordData.wordInfo.Text.Contains('ぅ') ||
                                 wordData.wordInfo.Text.Contains('ぇ') || wordData.wordInfo.Text.Contains('ぉ'))
                        {
                            wordData.wordInfo.Text = baseWord.Replace("ゃ", "").Replace("ゅ", "")
                                                             .Replace("ょ", "").Replace("ぁ", "")
                                                             .Replace("ぃ", "").Replace("ぅ", "")
                                                             .Replace("ぇ", "").Replace("ぉ", "")
                                                             .Replace("っ", "").Replace("ッ", "");
                        }
                        else
                        {
                            isProcessed = true;
                        }

                        // Also stop if we've made too many attempts
                        if (attemptCount >= maxAttempts)
                        {
                            isProcessed = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        // Log and consider this word processed to avoid infinite loop
                        Console.WriteLine($"[Error] Failed to process word '{wordData.wordInfo.Text}': {ex.Message}");
                        isProcessed = true;
                    }
                } while (!isProcessed);

                if (processedWord == null) return processedWord;

                processedWord.Occurrences = wordData.occurrences;

                if (!UseCache) return processedWord;
                try
                {
                    await DeckWordCache.SetAsync(cacheKey,
                                                 new DeckWord
                                                 {
                                                     WordId = processedWord.WordId, OriginalText = processedWord.OriginalText,
                                                     ReadingIndex = processedWord.ReadingIndex, Conjugations = processedWord.Conjugations,
                                                     PartsOfSpeech = processedWord.PartsOfSpeech, Origin = processedWord.Origin
                                                 }, CommandFlags.FireAndForget);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Warning] Failed to write to DeckWordCache: {ex.Message}");
                }


                return processedWord;
            }
            finally
            {
                _processSemaphore.Release();
            }
        }

        private static async Task<(bool success, DeckWord? word)> DeconjugateWord((WordInfo wordInfo, int occurrences) wordData)
        {
            string text = wordData.wordInfo.Text;

            // Exclude full digits or single latin character
            if (text.All(char.IsDigit) || (text.Length == 1 && text.IsAsciiOrFullWidthLetter()))
            {
                return (false, null);
            }

            _lookups.TryGetValue(text, out List<int>? candidates);
            var textInHiragana = WanaKana.ToHiragana(wordData.wordInfo.Text, new DefaultOptions() { ConvertLongVowelMark = false, });
            _lookups.TryGetValue(textInHiragana, out var candidatesHiragana);

            candidates ??= new List<int>();

            var textNormalized = KanaNormalizer.Normalize(textInHiragana);
            if (textNormalized != textInHiragana)
            {
                _lookups.TryGetValue(textNormalized, out List<int>? candidatesNormalized);
                if (candidatesNormalized is { Count: not 0 })
                    candidates.AddRange(candidatesNormalized);
            }


            // Add candidatesInHiragana to candidates, deduplicate 
            if (candidatesHiragana is { Count: not 0 })
            {
                var newCandidates = new List<int>(candidates);
                newCandidates.AddRange(candidatesHiragana);
                candidates = newCandidates.Distinct().ToList();
            }

            bool isStripped = false;

            string textStripped = "";
            if (text.Contains('ー'))
            {
                isStripped = true;
                textStripped = text.Replace("ー", "");

                // HEURISTIC:
                // If the word ends with 'ー' and the stripped version is very short (<= 2 chars),
                // it is almost certainly a slang adjective (e.g., すげー -> すげ, やべー -> やべ).
                // We should SKIP the stripped lookup here to avoid matching random nouns like "Sedge" (すげ).
                //
                // However, allow it if the 'ー' was in the middle (e.g., だーかーら -> だから).
                bool endsInBar = text.EndsWith("ー");
                bool isShort = textStripped.Length <= 2;

                if (!endsInBar || !isShort)
                {
                    var textStrippedHira = WanaKana.ToHiragana(textStripped);
                    _lookups.TryGetValue(textStrippedHira, out List<int>? stripIds);
                    if (stripIds != null) candidates ??= new List<int>();
                    if (stripIds != null) candidates.AddRange(stripIds);
                }
            }

            if (candidates is { Count: not 0 })
            {
                candidates = candidates.OrderBy(c => c).ToList();

                Dictionary<int, JmDictWord> wordCache;
                try
                {
                    wordCache = await JmDictCache.GetWordsAsync(candidates);
                }
                catch (Exception ex)
                {
                    // If we hit an exception when retrieving from cache, return a failure
                    // but don't crash the entire process
                    Console.WriteLine($"Error retrieving word cache: {ex.Message}");
                    return (false, null);
                }

                // Early return if we got no words from cache to avoid NullReferenceException
                if (wordCache.Count == 0)
                {
                    return (false, null);
                }

                List<JmDictWord> matches = new();
                JmDictWord? bestMatch = null;

                foreach (var id in candidates)
                {
                    if (!wordCache.TryGetValue(id, out var word)) continue;

                    List<PartOfSpeech> pos = word.PartsOfSpeech.ToPartOfSpeech();
                    // Is stripped part to handle interjection like よー and こーら
                    if (!pos.Contains(wordData.wordInfo.PartOfSpeech) &&
                        (!isStripped || isStripped && !pos.Contains(PartOfSpeech.Interjection))) continue;

                    matches.Add(word);
                }

                if (matches.Count == 0)
                {
                    if (candidates.Count > 0 && wordCache.TryGetValue(candidates[0], out var fallbackWord))
                        bestMatch = fallbackWord;
                    else
                        return (true, null);
                }
                else if (matches.Count > 1)
                    bestMatch = matches.OrderByDescending(m => m.GetPriorityScore(WanaKana.IsKana(wordData.wordInfo.Text)) +
                                                               (m.Readings.All(r => r != wordData.wordInfo.Text)
                                                                   ? -50
                                                                   : 0) // Deprioritize words where the long voxel mark has been stripped
                                                         ).First();
                else
                    bestMatch = matches[0];

                var normalizedReadings =
                    bestMatch.Readings.ToList();
                byte readingIndex = (byte)normalizedReadings.IndexOf(text);

                // not found, try stripped form if stripped
                if (isStripped && readingIndex == 255)
                {
                    readingIndex = (byte)normalizedReadings.IndexOf(textStripped);
                }

                // not found, try with hiragana form
                if (readingIndex == 255)
                {
                    var normalizedHiraganaReadings =
                        bestMatch.Readings.Select(r => WanaKana.ToHiragana(r, new DefaultOptions() { ConvertLongVowelMark = false }))
                                 .ToList();
                    readingIndex = (byte)normalizedHiraganaReadings.IndexOf(textInHiragana);
                }

                // not found, try with converting the long vowel mark
                if (readingIndex == 255)
                {
                    normalizedReadings =
                        bestMatch.Readings.Select(r => WanaKana.ToHiragana(r)).ToList();
                    readingIndex = (byte)normalizedReadings.IndexOf(textInHiragana);
                }

                // Still not found, skip the word completely
                if (readingIndex == 255)
                {
                    return (false, null);
                }

                DeckWord deckWord = new()
                                    {
                                        WordId = bestMatch.WordId, OriginalText = wordData.wordInfo.Text, ReadingIndex = readingIndex,
                                        Occurrences = wordData.occurrences, PartsOfSpeech = bestMatch.PartsOfSpeech.ToPartOfSpeech(),
                                        Origin = bestMatch.Origin
                                    };
                return (true, deckWord);
            }

            return (false, null);
        }

        private static async Task<(bool success, DeckWord? word)> DeconjugateVerbOrAdjective(
            (WordInfo wordInfo, int occurrences) wordData, Deconjugator deconjugator)
        {
            var normalizedText = KanaNormalizer.Normalize(WanaKana.ToHiragana(wordData.wordInfo.Text));

            // Exclude full digits or single latin character
            if (normalizedText.All(char.IsDigit) || (normalizedText.Length == 1 && normalizedText.IsAsciiOrFullWidthLetter()))
            {
                return (false, null);
            }

            var deconjugated = deconjugator.Deconjugate(normalizedText)
                                           .OrderByDescending(d => d.Text.Length).ToList();

            List<(DeconjugationForm form, List<int> ids)> candidates = new();
            foreach (var form in deconjugated)
            {
                if (_lookups.TryGetValue(form.Text, out List<int> lookup))
                {
                    candidates.Add((form, lookup));
                }
            }

            if (candidates.Count == 0)
            {
                return (true, null);
            }

            // if there's a candidate that's the same as the base word, put it first in the list
            var baseDictionaryWord = WanaKana.ToHiragana(wordData.wordInfo.DictionaryForm.Replace("ゎ", "わ").Replace("ヮ", "わ"),
                                                         new DefaultOptions() { ConvertLongVowelMark = false });
            var baseDictionaryWordIndex = candidates.FindIndex(c => c.form.Text == baseDictionaryWord);
            if (baseDictionaryWordIndex != -1)
            {
                var baseDictionaryWordCandidate = candidates[baseDictionaryWordIndex];
                candidates.RemoveAt(baseDictionaryWordIndex);
                candidates.Insert(0, baseDictionaryWordCandidate);
            }

            // if there's a candidate that's the same as the base word, put it first in the list
            var baseWord = WanaKana.ToHiragana(wordData.wordInfo.Text);
            var baseWordIndex = candidates.FindIndex(c => c.form.Text == baseWord);
            if (baseWordIndex != -1 && candidates[0].form.Text != baseDictionaryWord)
            {
                var baseWordCandidate = candidates[baseWordIndex];
                candidates.RemoveAt(baseWordIndex);
                candidates.Insert(0, baseWordCandidate);
            }

            var allCandidateIds = candidates.SelectMany(c => c.ids).Distinct().ToList();

            if (!allCandidateIds.Any())
                return (false, null);

            Dictionary<int, JmDictWord> wordCache;
            try
            {
                wordCache = await JmDictCache.GetWordsAsync(allCandidateIds);

                // Check if we got any results
                if (wordCache.Count == 0)
                {
                    return (false, null);
                }
            }
            catch (Exception ex)
            {
                // If we hit an exception when retrieving from cache, return a failure
                // but don't crash the entire process
                Console.WriteLine($"Error retrieving verb/adjective word cache: {ex.Message}");
                return (false, null);
            }

            List<(JmDictWord word, DeconjugationForm form)> matches = new();
            (JmDictWord word, DeconjugationForm form) bestMatch;

            foreach (var candidate in candidates)
            {
                foreach (var id in candidate.ids)
                {
                    if (!wordCache.TryGetValue(id, out var word)) continue;

                    List<PartOfSpeech> pos = word.PartsOfSpeech.ToPartOfSpeech();
                    if (!pos.Contains(wordData.wordInfo.PartOfSpeech)) continue;

                    matches.Add((word, candidate.form));
                }
            }

            if (matches.Count == 0)
            {
                return (false, null);
            }

            if (matches.Count > 1)
            {
                matches = matches.OrderByDescending(m => m.Item1.GetPriorityScore(WanaKana.IsKana(wordData.wordInfo.Text))).ToList();
                bestMatch = matches.First();

                if (!WanaKana.IsKana(wordData.wordInfo.NormalizedForm))
                {
                    foreach (var match in matches)
                    {
                        if (match.word.Readings.Any(r => r == wordData.wordInfo.NormalizedForm))
                        {
                            bestMatch = match;
                            break;
                        }
                    }
                }
            }
            else
                bestMatch = matches[0];

            // Reading selection
            // Distinguishes betsween katakana readings, tries to filter out long vowel, etc

            var readings = bestMatch.word.Readings.ToList();
            var targetHiragana = bestMatch.form.Text;
            var originalText = wordData.wordInfo.Text;

            byte bestReadingIndex = 255;
            int maxScriptScore = 0;

            for (int i = 0; i < readings.Count; i++)
            {
                string reading = readings[i];

                // A. Filter: The reading MUST correspond to the deconjugated word phonetically.
                // We verify this by converting the reading to Hiragana and checking if it matches our target.
                var readingHiragana = WanaKana.ToHiragana(reading, new DefaultOptions { ConvertLongVowelMark = false });

                // Use loose check for long vowels if strict check fails, or just strict. 
                if (KanaNormalizer.Normalize(readingHiragana) != KanaNormalizer.Normalize(targetHiragana))
                {
                    // Try with long vowel conversion if simple failed
                    if (KanaNormalizer.Normalize(WanaKana.ToHiragana(reading)) != KanaNormalizer.Normalize(targetHiragana))
                        continue;
                }

                // B. Score: Calculate common prefix length between Original Text and Reading
                int score = GetCommonPrefixLength(originalText, reading);

                if (score > maxScriptScore)
                {
                    maxScriptScore = score;
                    bestReadingIndex = (byte)i;
                }
            }

            // Fallback Logic (if no script match found, e.g. Kanji input or standard Hiragana)
            if (bestReadingIndex == 255)
            {
                // Try strict Hiragana match
                var normalizedReadings = readings.Select(r => WanaKana.ToHiragana(r, new DefaultOptions() { ConvertLongVowelMark = false }))
                                                 .ToList();
                var idx = normalizedReadings.IndexOf(targetHiragana);

                if (idx != -1) bestReadingIndex = (byte)idx;
                else
                {
                    // Try loose match (long vowels)
                    normalizedReadings = readings.Select(r => WanaKana.ToHiragana(r)).ToList();
                    bestReadingIndex = (byte)normalizedReadings.IndexOf(targetHiragana);
                }
            }

            DeckWord deckWord = new()
                                {
                                    WordId = bestMatch.word.WordId, OriginalText = wordData.wordInfo.Text, ReadingIndex = bestReadingIndex,
                                    Occurrences = wordData.occurrences, Conjugations = bestMatch.form.Process,
                                    PartsOfSpeech = bestMatch.word.PartsOfSpeech.ToPartOfSpeech(), Origin = bestMatch.word.Origin
                                };

            return (true, deckWord);

            static int GetCommonPrefixLength(string s1, string s2)
            {
                int len = Math.Min(s1.Length, s2.Length);
                int match = 0;
                for (int i = 0; i < len; i++)
                {
                    if (s1[i] == s2[i]) match++;
                    else break;
                }

                return match;
            }
        }

        public static async Task<List<DeckWord>> GetWordsDirectLookup(IDbContextFactory<JitenDbContext> contextFactory, List<string> words)
        {
            _contextFactory = contextFactory;
            if (!_initialized)
            {
                await _initSemaphore.WaitAsync();
                try
                {
                    if (!_initialized) // Double-check to avoid race conditions
                    {
                        await InitDictionaries();
                        _initialized = true;
                    }
                }
                finally
                {
                    _initSemaphore.Release();
                }
            }

            List<DeckWord> matchedWords = new();

            foreach (var word in words)
            {
                var wordInHiragana = WanaKana.ToHiragana(word, new DefaultOptions() { ConvertLongVowelMark = false, });
                var wordNormalized = KanaNormalizer.Normalize(wordInHiragana);

                if (!_lookups.TryGetValue(wordNormalized, out var matchesIds) || matchesIds.Count == 0)
                    continue;

                var wordCache = await JmDictCache.GetWordsAsync(matchesIds);

                if (wordCache == null || wordCache.Count == 0)
                    continue;

                List<JmDictWord> matches = new();

                foreach (var id in matchesIds)
                {
                    if (!wordCache.TryGetValue(id, out var match)) continue;
                    if (match.Readings != null && match.Readings.Any(r => r == word))
                        matches.Add(match);
                }

                if (matches.Count == 0)
                    continue;

                var bestMatch = matches.OrderByDescending(m => m.GetPriorityScore(WanaKana.IsKana(word))).First();

                var readingIndex = bestMatch.Readings?.IndexOf(word) ?? -1;
                if (readingIndex == -1)
                    continue;

                matchedWords.Add(new DeckWord()
                                 {
                                     WordId = bestMatch.WordId,
                                     ReadingIndex = (byte)readingIndex,
                                     OriginalText = word
                                 });
            }

            return matchedWords;
        }
    }
}