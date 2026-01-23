using System.Diagnostics;
using System.Text.RegularExpressions;
using Jiten.Cli.ML;
using Jiten.Core;
using Jiten.Core.Data;
using Jiten.Core.Data.JMDict;
using Jiten.Core.Utils;
using Jiten.Parser.Data.Redis;
using Jiten.Parser.Diagnostics;
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
        private static readonly bool UseRescue = true;
        private static IDeckWordCache DeckWordCache;
        private static IJmDictCache JmDictCache;

        private static IDbContextFactory<JitenDbContext> _contextFactory;
        private static Dictionary<string, List<int>> _lookups;

        // Cache for compound expression lookups
        private static readonly Dictionary<string, (bool validExpression, int? wordId)> CompoundExpressionCache = new();
        private static readonly Lock CompoundCacheLock = new();
        private const int MaxCompoundCacheSize = 200_000;

        // Valid POS for compound expressions
        private static readonly HashSet<PartOfSpeech> ValidCompoundPos = new()
                                                                         {
                                                                             PartOfSpeech.Expression, PartOfSpeech.Verb,
                                                                             PartOfSpeech.IAdjective, PartOfSpeech.NaAdjective,
                                                                             PartOfSpeech.Auxiliary
                                                                         };

        // POS tags from deconjugator rules that should be validated against word POS.
        // If a deconjugation form has any of these tags, the matched word must have the same tag.
        private static readonly HashSet<string> DeconjugationPosTagsToValidate = new(StringComparer.OrdinalIgnoreCase)
        {
            "v1", "v1-s", "vs-s", "vs-i", "vk", "v5aru", "v5b", "v5g", "v5k", "v5k-s",
            "v5m", "v5n", "v5r", "v5r-i", "v5s", "v5t", "v5u", "v5u-s", "v5uru",
            "adj-i", "adj-na", "adj-ix", "aux", "vs-c"
        };

        // Helper to check if a word's POS is compatible with a deconjugation tag
        private static bool IsPosCompatibleWithTag(IEnumerable<string> wordPos, string tag)
        {
            if (wordPos.Contains(tag)) return true;
            // Handle suru-verb mapping: deconjugator uses vs-i/vs-s, JMDict uses vs
            if (tag is "vs-i" or "vs-s" && wordPos.Contains("vs")) return true;
            return false;
        }

        // Excluded (WordId, ReadingIndex) pairs to filter from final parsing results
        private static readonly HashSet<(int WordId, byte ReadingIndex)> ExcludedMisparses = new()
            {
                (1291070, 1), (1587980, 1), (1443970, 5), (2029660, 0), (1177490, 5), (2029000, 1),
            };

        private static async Task InitDictionaries()
        {
            var configuration = new ConfigurationBuilder()
                                .SetBasePath(Directory.GetCurrentDirectory())
                                .AddJsonFile(Path.Combine(Environment.CurrentDirectory, "..", "Shared", "sharedsettings.json"),
                                             optional: true,
                                             reloadOnChange: true)
                                .AddJsonFile(Path.Combine("Shared", "sharedsettings.json"), optional: true, reloadOnChange: true)
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
                                                           bool preserveStopToken = false,
                                                           ParserDiagnostics? diagnostics = null)
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
            var sentences = await parser.Parse(text, preserveStopToken: preserveStopToken, diagnostics: diagnostics);

            // Clean text in sentences directly
            foreach (var sentence in sentences)
            {
                foreach (var (word, _, _) in sentence.Words)
                {
                    // Keep SupplementarySymbol tokens as boundary markers
                    if (word.PartOfSpeech == PartOfSpeech.SupplementarySymbol)
                        continue;

                    word.Text = Regex.Replace(word.Text,
                                              "[^a-zA-Z0-9\u3040-\u309F\u30A0-\u30FF\u4E00-\u9FAF\uFF21-\uFF3A\uFF41-\uFF5A\uFF10-\uFF19\u3005．]",
                                              "");
                    word.Text = Regex.Replace(word.Text, "ッー", "");
                }


                sentence.Words.RemoveAll(w => string.IsNullOrWhiteSpace(w.word.Text) &&
                                              w.word.PartOfSpeech != PartOfSpeech.SupplementarySymbol);
            }

            CombineNounCompounds(sentences);
            await CombineCompounds(sentences);
            RepairLongVowelTokens(sentences);
            // Filter out SupplementarySymbol tokens before further processing
            var wordInfos = sentences.SelectMany(s => s.Words)
                                     .Where(w => w.word.PartOfSpeech != PartOfSpeech.SupplementarySymbol)
                                     .Select(w => w.word).ToList();

            var deconjugator = Deconjugator.Instance;

            const int BATCH_SIZE = 1000;
            List<DeckWord?> allProcessedWords = new();
            List<(int index, WordInfo wordInfo)> failedWords = new();

            int globalIndex = 0;
            for (int i = 0; i < wordInfos.Count; i += BATCH_SIZE)
            {
                var batch = wordInfos.Skip(i).Take(BATCH_SIZE).ToList();
                var processBatch = batch.Select(word => ProcessWord((word, 0), deconjugator)).ToList();
                var batchResults = await Task.WhenAll(processBatch);

                for (int j = 0; j < batch.Count; j++)
                {
                    allProcessedWords.Add(batchResults[j]);
                    if (batchResults[j] == null)
                        failedWords.Add((globalIndex, batch[j]));
                    globalIndex++;
                }
            }

            if (UseRescue && failedWords.Count > 0)
            {
                var rescueInput = failedWords.Select(f => (f.wordInfo, 0)).ToList();
                var rescuedGroups = await RescueFailedWords(rescueInput, deconjugator);

                int offset = 0;
                for (int i = 0; i < failedWords.Count; i++)
                {
                    var (originalIndex, _) = failedWords[i];
                    var adjustedIndex = originalIndex + offset;
                    var rescuedGroup = rescuedGroups[i];

                    allProcessedWords.RemoveAt(adjustedIndex);
                    allProcessedWords.InsertRange(adjustedIndex, rescuedGroup);

                    offset += (rescuedGroup.Count - 1);
                }
            }

            return ExcludeFinalMisparses(allProcessedWords.Where(w => w != null).Select(w => w!));
        }

        /// <summary>
        /// Parses a single text into a Deck. Delegates to ParseTextsToDeck for a single codepath.
        /// </summary>
        public static async Task<Deck> ParseTextToDeck(IDbContextFactory<JitenDbContext> contextFactory, string text,
                                                       bool storeRawText = false,
                                                       bool predictDifficulty = true,
                                                       MediaType mediatype = MediaType.Novel,
                                                       ParserDiagnostics? diagnostics = null)
        {
            var results = await ParseTextsToDeck(contextFactory, [text], storeRawText, predictDifficulty, mediatype, diagnostics);
            return results.Count > 0 ? results[0] : new Deck();
        }

        /// <summary>
        /// Parses multiple texts into Decks in a single batch for efficiency.
        /// </summary>
        public static async Task<List<Deck>> ParseTextsToDeck(IDbContextFactory<JitenDbContext> contextFactory,
                                                              List<string> texts,
                                                              bool storeRawText = false,
                                                              bool predictDifficulty = true,
                                                              MediaType mediatype = MediaType.Novel,
                                                              ParserDiagnostics? diagnostics = null)
        {
            if (texts.Count == 0) return [];

            _contextFactory = contextFactory;
            if (!_initialized)
            {
                await _initSemaphore.WaitAsync();
                try
                {
                    if (!_initialized)
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

            // Batch morphological analysis
            var parser = new MorphologicalAnalyser();
            var batchedSentences = await parser.ParseBatch(texts, diagnostics: diagnostics);

            timer.Stop();
            double sudachiTime = timer.Elapsed.TotalMilliseconds;
            Console.WriteLine($"Batch parsed {texts.Count} texts. Sudachi time: {sudachiTime:0.0}ms");

            timer.Restart();

            // Process each result through deconjugation/lookup pipeline
            var decks = new List<Deck>();
            Deconjugator deconjugator = Deconjugator.Instance;

            for (int textIndex = 0; textIndex < batchedSentences.Count; textIndex++)
            {
                var sentences = batchedSentences[textIndex];
                var text = texts[textIndex];
                var deck = await ProcessSentencesToDeck(sentences, text, deconjugator, storeRawText, predictDifficulty, mediatype);
                decks.Add(deck);
            }

            timer.Stop();
            Console.WriteLine($"Processing time: {timer.Elapsed.TotalMilliseconds:0.0}ms");

            return decks;
        }

        /// <summary>
        /// Helper: Processes sentences into a Deck (deconjugation, rescue, statistics).
        /// Extracted to share between batch and single-item paths.
        /// </summary>
        private static async Task<Deck> ProcessSentencesToDeck(
            List<SentenceInfo> sentences,
            string text,
            Deconjugator deconjugator,
            bool storeRawText,
            bool predictDifficulty,
            MediaType mediatype)
        {
            // Clean text in sentences directly
            foreach (var sentence in sentences)
            {
                foreach (var (word, _, _) in sentence.Words)
                {
                    // Keep SupplementarySymbol tokens as boundary markers
                    if (word.PartOfSpeech == PartOfSpeech.SupplementarySymbol)
                        continue;

                    word.Text = Regex.Replace(word.Text,
                                              "[^a-zA-Z0-9\u3040-\u309F\u30A0-\u30FF\u4E00-\u9FAF\uFF21-\uFF3A\uFF41-\uFF5A\uFF10-\uFF19\u3005．]",
                                              "");
                    word.Text = Regex.Replace(word.Text, "ッー", "");
                }


                sentence.Words.RemoveAll(w => string.IsNullOrWhiteSpace(w.word.Text) &&
                                              w.word.PartOfSpeech != PartOfSpeech.SupplementarySymbol);
            }

            CombineNounCompounds(sentences);
            await CombineCompounds(sentences);
            RepairLongVowelTokens(sentences);
            // Filter out SupplementarySymbol tokens before further processing
            var wordInfos = sentences.SelectMany(s => s.Words)
                                     .Where(w => w.word.PartOfSpeech != PartOfSpeech.SupplementarySymbol)
                                     .Select(w => w.word).ToList();

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

            const int BATCH_SIZE = 1000;
            List<DeckWord?> allProcessedWords = new();
            List<(int index, WordInfo wordInfo, int occurrences)> failedWords = new();

            int globalIndex = 0;
            for (int i = 0; i < uniqueWords.Count; i += BATCH_SIZE)
            {
                var batch = uniqueWords.Skip(i).Take(BATCH_SIZE).ToList();
                var processBatch = batch.Select(word => ProcessWord(word, deconjugator)).ToList();
                var batchResults = await Task.WhenAll(processBatch);

                for (int j = 0; j < batch.Count; j++)
                {
                    allProcessedWords.Add(batchResults[j]);
                    if (batchResults[j] == null)
                        failedWords.Add((globalIndex, batch[j].wordInfo, batch[j].occurrences));
                    globalIndex++;
                }
            }

            if (UseRescue && failedWords.Count > 0)
            {
                var rescueInput = failedWords.Select(f => (f.wordInfo, f.occurrences)).ToList();
                var rescuedGroups = await RescueFailedWords(rescueInput, deconjugator);

                int offset = 0;
                for (int i = 0; i < failedWords.Count; i++)
                {
                    var (originalIndex, _, _) = failedWords[i];
                    var adjustedIndex = originalIndex + offset;
                    var rescuedGroup = rescuedGroups[i];

                    allProcessedWords.RemoveAt(adjustedIndex);
                    allProcessedWords.InsertRange(adjustedIndex, rescuedGroup);

                    offset += (rescuedGroup.Count - 1);
                }
            }

            var processedWords = allProcessedWords.Where(w => w != null).Select(w => w!).ToArray();

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

            processedWords = ExcludeFinalMisparses(processedWords).ToArray();

            List<ExampleSentence>? exampleSentences = null;

            if (mediatype is MediaType.Novel or MediaType.NonFiction or MediaType.VideoGame or MediaType.VisualNovel or MediaType.WebNovel)
                exampleSentences = ExampleSentenceExtractor.ExtractSentences(sentences, processedWords);

            var totalWordCount = processedWords.Select(w => w.Occurrences).Sum();
            var characterCount = wordInfos.Sum(x => x.Text.Length);

            var textWithoutDialogues = Regex.Replace(text, @"[「『].{0,200}?[」』]", "", RegexOptions.Singleline);
            textWithoutDialogues = Regex.Replace(textWithoutDialogues,
                                                 "[^a-zA-Z0-9\u3040-\u309F\u30A0-\u30FF\u4E00-\u9FAF\uFF21-\uFF3A\uFF41-\uFF5A\uFF10-\uFF19\u3005．]",
                                                 "");
            var textWithoutPunctuation = Regex.Replace(text,
                                                       "[^a-zA-Z0-9\u3040-\u309F\u30A0-\u30FF\u4E00-\u9FAF\uFF21-\uFF3A\uFF41-\uFF5A\uFF10-\uFF19\u3005．]",
                                                       "");

            int dialogueCharacterCount = textWithoutPunctuation.Length - textWithoutDialogues.Length;
            float dialoguePercentage = textWithoutPunctuation.Length > 0
                ? (float)dialogueCharacterCount / textWithoutPunctuation.Length * 100f
                : 0f;

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
            
            deck.Difficulty = 0;

            return deck;
        }

        public static async Task<List<DeckWord?>> ParseMorphenes(IDbContextFactory<JitenDbContext> contextFactory, string text,
                                                                 ParserDiagnostics? diagnostics = null)
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
            var sentences = await parser.Parse(text, morphemesOnly: true, diagnostics: diagnostics);
            var wordInfos = sentences.SelectMany(s => s.Words).Select(w => w.word).ToList();

            // Filter bad lines that cause exceptions
            wordInfos.ForEach(x => x.Text = Regex.Replace(x.Text, "ッー", ""));

            Deconjugator deconjugator = Deconjugator.Instance;

            const int BATCH_SIZE = 5000;
            List<DeckWord?> allProcessedWords = new();

            for (int i = 0; i < wordInfos.Count; i += BATCH_SIZE)
            {
                var batch = wordInfos.Skip(i).Take(BATCH_SIZE).ToList();
                var processBatch = batch.Select(word => ProcessWord((word, 0), deconjugator)).ToList();
                var batchResults = await Task.WhenAll(processBatch);

                allProcessedWords.AddRange(batchResults);
            }

            return allProcessedWords
                   .Where(w => w == null || !ExcludedMisparses.Contains((w.WordId, w.ReadingIndex)))
                   .ToList();
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
                // Early check for digits before anything else (including cache lookup)
                var textWithoutBar = wordData.wordInfo.Text.TrimEnd('ー');
                if (textWithoutBar.Length > 0 && textWithoutBar.All(char.IsDigit) ||
                    (textWithoutBar.Length == 1 && textWithoutBar.IsAsciiOrFullWidthLetter()))
                {
                    return null;
                }

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

                        if (cachedWord != null && cachedWord.WordId != -1)
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
                        // If the word has a pre-matched wordId (e.g. from CombineCompounds), use it directly
                        if (wordData.wordInfo.PreMatchedWordId.HasValue)
                        {
                            var preMatchedWordId = wordData.wordInfo.PreMatchedWordId.Value;
                            var wordCache = await JmDictCache.GetWordsAsync([preMatchedWordId]);
                            if (wordCache.TryGetValue(preMatchedWordId, out var preMatchedWord))
                            {
                                // Use DictionaryForm for reading lookup since Text may be conjugated (e.g. そうしよう vs そうする)
                                var textForReadingLookup = !string.IsNullOrEmpty(wordData.wordInfo.DictionaryForm)
                                    ? wordData.wordInfo.DictionaryForm
                                    : wordData.wordInfo.Text;
                                var readingIndex = GetBestReadingIndex(preMatchedWord, textForReadingLookup);
                                processedWord = new DeckWord
                                                {
                                                    WordId = preMatchedWordId, ReadingIndex = readingIndex,
                                                    OriginalText = wordData.wordInfo.Text, Occurrences = wordData.occurrences,
                                                    PartsOfSpeech = preMatchedWord.PartsOfSpeech.ToPartOfSpeech(),
                                                    Origin = preMatchedWord.Origin
                                                };
                                break;
                            }
                        }

                        if (wordData.wordInfo.PartOfSpeech is PartOfSpeech.Verb or PartOfSpeech.IAdjective or PartOfSpeech.Auxiliary
                                or PartOfSpeech.NaAdjective or PartOfSpeech.Expression || wordData.wordInfo.PartOfSpeechSection1 is PartOfSpeechSection.Adjectival)
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

                        if (processedWord != null)
                        {
                            // Restore original text (before any stripping/modifications)
                            processedWord.OriginalText = baseWord;
                            break;
                        }

                        // We haven't found a match, let's try to remove the last character if it's a っ, a ー or a duplicate
                        if (wordData.wordInfo.Text.Length > 2 &&
                            (wordData.wordInfo.Text[^1] == 'っ' || wordData.wordInfo.Text[^1] == 'ー' ||
                             wordData.wordInfo.Text[^2] == wordData.wordInfo.Text[^1]))
                        {
                            wordData.wordInfo.Text = wordData.wordInfo.Text[..^1];
                        }
                        // Let's try to remove any honorifics in front of the word
                        else if (wordData.wordInfo.Text.StartsWith("お") || wordData.wordInfo.Text.StartsWith("御"))
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

                if (processedWord == null)
                {
                    // Restore original text for RescueFailedWords to use
                    wordData.wordInfo.Text = baseWord;
                    return processedWord;
                }

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

            // Exclude text that is primarily digits (with optional trailing ー) or single latin character
            var textWithoutBar = text.TrimEnd('ー');
            if ((textWithoutBar.Length > 0 && textWithoutBar.All(char.IsDigit)) || (text.Length == 1 && text.IsAsciiOrFullWidthLetter()))
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
            // Early check for digits before WanaKana (which can't convert full-width digits)
            var textWithoutBar = wordData.wordInfo.Text.TrimEnd('ー');
            if (textWithoutBar.Length > 0 && textWithoutBar.All(char.IsDigit) ||
                (textWithoutBar.Length == 1 && textWithoutBar.IsAsciiOrFullWidthLetter()))
            {
                return (false, null);
            }

            var normalizedText = KanaNormalizer.Normalize(WanaKana.ToHiragana(wordData.wordInfo.Text));

            // Exclude single latin character
            if (normalizedText.Length == 1 && normalizedText.IsAsciiOrFullWidthLetter())
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

            // Track if we need to try DictionaryForm lookup later (for compound expressions)
            bool tryDictionaryFormFallback = candidates.Count == 0 && !string.IsNullOrEmpty(wordData.wordInfo.DictionaryForm);

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

            // If we have DictionaryForm fallback to try, add those IDs to the list
            List<int>? dictFormLookupIds = null;
            if (tryDictionaryFormFallback)
            {
                if (_lookups.TryGetValue(baseDictionaryWord, out dictFormLookupIds) ||
                    _lookups.TryGetValue(wordData.wordInfo.DictionaryForm, out dictFormLookupIds))
                {
                    if (dictFormLookupIds is { Count: > 0 })
                    {
                        allCandidateIds = allCandidateIds.Concat(dictFormLookupIds).Distinct().ToList();
                    }
                }
            }

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

                    // Validate that deconjugation POS tags match the word's POS.
                    // This prevents nouns from being incorrectly matched via verb deconjugation rules.
                    var formPosTags = candidate.form.Tags.Where(t => DeconjugationPosTagsToValidate.Contains(t)).ToList();
                    if (formPosTags.Count > 0 && !formPosTags.Any(tag => IsPosCompatibleWithTag(word.PartsOfSpeech, tag)))
                        continue;

                    matches.Add((word, candidate.form));
                }
            }

            if (matches.Count == 0)
            {
                // Last resort: try using Sudachi's DictionaryForm directly if available
                // This handles cases like おかけして → 掛ける where the お prefix
                // prevents standard deconjugation rules from matching
                if (!string.IsNullOrEmpty(wordData.wordInfo.DictionaryForm))
                {
                    var dictFormHiragana = WanaKana.ToHiragana(wordData.wordInfo.DictionaryForm.Replace("ゎ", "わ").Replace("ヮ", "わ"),
                                                               new DefaultOptions() { ConvertLongVowelMark = false });
                    List<int>? dictLookup = null;
                    if (_lookups.TryGetValue(dictFormHiragana, out dictLookup) ||
                        _lookups.TryGetValue(wordData.wordInfo.DictionaryForm, out dictLookup))
                    {
                        if (dictLookup is { Count: > 0 })
                        {
                            try
                            {
                                var dictWordCache = await JmDictCache.GetWordsAsync(dictLookup);
                                foreach (var dictWord in dictWordCache.Values)
                                {
                                    List<PartOfSpeech> pos = dictWord.PartsOfSpeech.ToPartOfSpeech();
                                    if (pos.Contains(wordData.wordInfo.PartOfSpeech))
                                    {
                                        var form = new DeconjugationForm(dictFormHiragana, wordData.wordInfo.Text,
                                                                         new List<string>(), new HashSet<string>(), new List<string>());
                                        matches.Add((dictWord, form));
                                    }
                                }
                            }
                            catch
                            {
                                /* Cache lookup failed */
                            }
                        }
                    }
                }

                if (matches.Count == 0)
                {
                    return (false, null);
                }
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
                    idx = normalizedReadings.IndexOf(targetHiragana);
                    if (idx != -1) bestReadingIndex = (byte)idx;
                }
            }

            // No matching reading found - skip this word
            if (bestReadingIndex == 255)
                return (false, null);

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

                List<(JmDictWord match, int readingIndex)> matchesWithReading = new();

                foreach (var id in matchesIds)
                {
                    if (!wordCache.TryGetValue(id, out var match)) continue;
                    var readingIndex = match.Readings?.IndexOf(word) ?? -1;
                    if (readingIndex >= 0)
                        matchesWithReading.Add((match, readingIndex));
                }

                if (matchesWithReading.Count == 0)
                    continue;

                // Fetch frequency data from database
                var candidateWordIds = matchesWithReading.Select(m => m.match.WordId).ToList();
                await using var context = await _contextFactory.CreateDbContextAsync();
                var frequencies = await context.JmDictWordFrequencies
                    .AsNoTracking()
                    .Where(f => candidateWordIds.Contains(f.WordId))
                    .ToDictionaryAsync(f => f.WordId);

                // Order by frequency rank (lower = more frequent = better)
                var best = matchesWithReading
                    .OrderBy(m =>
                    {
                        if (frequencies.TryGetValue(m.match.WordId, out var freq) &&
                            freq.ReadingsFrequencyRank.Count > m.readingIndex)
                            return freq.ReadingsFrequencyRank[m.readingIndex];
                        return int.MaxValue; // No frequency data = lowest priority
                    })
                    .First();

                matchedWords.Add(new DeckWord { WordId = best.match.WordId, ReadingIndex = (byte)best.readingIndex, OriginalText = word });
            }

            return ExcludeFinalMisparses(matchedWords);
        }

        private static async Task<List<List<DeckWord>>> RescueFailedWords(
            List<(WordInfo wordInfo, int occurrences)> failedWords,
            Deconjugator deconjugator)
        {
            var groupedResults = failedWords.Select(_ => new List<DeckWord>()).ToList();

            for (int i = 0; i < failedWords.Count; i++)
            {
                var (wordInfo, occurrences) = failedWords[i];
                var text = wordInfo.Text;

                // Skip punctuation and digit only (including digits with trailing ー)
                var textWithoutBar = text.TrimEnd('ー');
                if (string.IsNullOrEmpty(text) ||
                    text.All(c => !char.IsLetterOrDigit(c)) ||
                    (textWithoutBar.Length > 0 && textWithoutBar.All(char.IsDigit) ||
                     (textWithoutBar.Length == 1 && textWithoutBar.IsAsciiOrFullWidthLetter())))
                    continue;

                // Check cache for already-attempted rescues
                if (UseCache)
                {
                    try
                    {
                        var cacheKey = new DeckWordCacheKey(text, wordInfo.PartOfSpeech, wordInfo.DictionaryForm);
                        var cached = await DeckWordCache.GetAsync(cacheKey);
                        if (cached is { WordId: -1 })
                            continue;
                    }
                    catch
                    {
                        /* Cache read failed, proceed */
                    }
                }

                var results = new List<DeckWord>();

                while (text.Length > 0)
                {
                    DeckWord? match = null;
                    int matchLen = 0;

                    // Try window sizes from largest to smallest
                    for (int windowSize = Math.Min(10, text.Length); windowSize >= 1; windowSize--)
                    {
                        var candidate = text[..windowSize];

                        // Skip candidates that are pure digits
                        var candidateWithoutBar = candidate.TrimEnd('ー');
                        if (candidateWithoutBar.Length > 0 && candidateWithoutBar.All(char.IsDigit)  || (candidateWithoutBar.Length == 1 && candidateWithoutBar.IsAsciiOrFullWidthLetter()))
                            continue;

                        string hiragana;
                        try
                        {
                            hiragana = WanaKana.ToHiragana(candidate, new DefaultOptions { ConvertLongVowelMark = false });
                        }
                        catch (KeyNotFoundException)
                        {
                            continue; // Skip this window size if conversion fails (e.g., full-width digits)
                        }

                        var normalized = KanaNormalizer.Normalize(hiragana);

                        // Try direct lookup
                        List<int>? wordIds = null;
                        if (_lookups.TryGetValue(candidate, out wordIds) ||
                            _lookups.TryGetValue(hiragana, out wordIds) ||
                            _lookups.TryGetValue(normalized, out wordIds))
                        {
                            if (wordIds is { Count: > 0 })
                            {
                                try
                                {
                                    var wordCache = await JmDictCache.GetWordsAsync(wordIds);
                                    if (wordCache.Count > 0)
                                    {
                                        // For single-char honorific prefixes (ご, お), prefer Prefix over Name
                                        var candidates = wordCache.Values.AsEnumerable();
                                        if (candidate is "ご" or "お")
                                        {
                                            var prefixMatch =
                                                candidates.FirstOrDefault(w => w.PartsOfSpeech.ToPartOfSpeech()
                                                                                .Contains(PartOfSpeech.Prefix));
                                            if (prefixMatch != null)
                                                candidates = [prefixMatch];
                                        }

                                        var bestMatch = candidates
                                                        .OrderByDescending(w => w.GetPriorityScore(WanaKana.IsKana(candidate)))
                                                        .First();
                                        var readingIndex = GetBestReadingIndex(bestMatch, candidate);
                                        // Skip single-char matches that are primarily numerals
                                        if (readingIndex != 255 && !(candidate.Length == 1 && bestMatch.PartsOfSpeech.Contains("num")))
                                        {
                                            match = new DeckWord
                                                    {
                                                        WordId = bestMatch.WordId, OriginalText = candidate, ReadingIndex = readingIndex,
                                                        Occurrences = occurrences, PartsOfSpeech = bestMatch.PartsOfSpeech.ToPartOfSpeech(),
                                                        Origin = bestMatch.Origin
                                                    };
                                            matchLen = windowSize;
                                            break;
                                        }
                                    }
                                }
                                catch
                                {
                                }
                            }
                        }

                        // Try deconjugation
                        var deconjugated = deconjugator.Deconjugate(hiragana);
                        foreach (var form in deconjugated.OrderByDescending(f => f.Text.Length))
                        {
                            if (_lookups.TryGetValue(form.Text, out wordIds) && wordIds is { Count: > 0 })
                            {
                                try
                                {
                                    var wordCache = await JmDictCache.GetWordsAsync(wordIds);
                                    if (wordCache.Count > 0)
                                    {
                                        // For single-char honorific prefixes (ご, お), prefer Prefix over Name
                                        var deconjCandidates = wordCache.Values.AsEnumerable();
                                        if (candidate is "ご" or "お")
                                        {
                                            var prefixMatch =
                                                deconjCandidates.FirstOrDefault(w => w.PartsOfSpeech.ToPartOfSpeech()
                                                                                      .Contains(PartOfSpeech.Prefix));
                                            if (prefixMatch != null)
                                                deconjCandidates = [prefixMatch];
                                        }

                                        // Filter candidates by POS compatibility with deconjugation tags
                                        var formPosTags = form.Tags.Where(t => DeconjugationPosTagsToValidate.Contains(t)).ToList();
                                        if (formPosTags.Count > 0)
                                        {
                                            deconjCandidates = deconjCandidates.Where(w => formPosTags.Any(tag => IsPosCompatibleWithTag(w.PartsOfSpeech, tag)));
                                        }

                                        var bestMatch = deconjCandidates
                                                        .OrderByDescending(w => w.GetPriorityScore(WanaKana.IsKana(candidate)))
                                                        .FirstOrDefault();
                                        if (bestMatch == null) continue;

                                        var readingIndex = GetBestReadingIndex(bestMatch, form.Text);
                                        // Skip single-char matches that are primarily numerals
                                        if (readingIndex != 255 && !(candidate.Length == 1 && bestMatch.PartsOfSpeech.Contains("num")))
                                        {
                                            match = new DeckWord
                                                    {
                                                        WordId = bestMatch.WordId, OriginalText = candidate, ReadingIndex = readingIndex,
                                                        Occurrences = occurrences, Conjugations = form.Process,
                                                        PartsOfSpeech = bestMatch.PartsOfSpeech.ToPartOfSpeech(), Origin = bestMatch.Origin
                                                    };
                                            matchLen = windowSize;
                                            break;
                                        }
                                    }
                                }
                                catch
                                {
                                }
                            }
                        }

                        if (match != null) break;
                    }

                    if (match != null)
                    {
                        results.Add(match);
                        text = text[matchLen..];

                        // Skip trailing ー if the match already ends with ー (emphasis marks)
                        if (match.OriginalText.EndsWith("ー"))
                        {
                            while (text.Length > 0 && text[0] == 'ー')
                                text = text[1..];
                        }
                    }
                    else
                    {
                        // No match found, skip one character and continue
                        text = text.Length > 1 ? text[1..] : "";
                    }
                }

                // Cache failed rescue attempts
                if (UseCache && results.Count == 0)
                {
                    try
                    {
                        var cacheKey = new DeckWordCacheKey(wordInfo.Text, wordInfo.PartOfSpeech, wordInfo.DictionaryForm);
                        await DeckWordCache.SetAsync(cacheKey,
                                                     new DeckWord { WordId = -1, OriginalText = wordInfo.Text },
                                                     CommandFlags.FireAndForget);
                    }
                    catch
                    {
                    }
                }

                groupedResults[i] = results;
            }

            return groupedResults;
        }

        private static byte GetBestReadingIndex(JmDictWord word, string originalText)
        {
            var readings = word.Readings;
            if (readings.Count == 0)
                return 0;

            var textHiragana = WanaKana.ToHiragana(originalText, new DefaultOptions { ConvertLongVowelMark = false });
            var textHiraganaNormalized = KanaNormalizer.Normalize(textHiragana);

            byte bestIndex = 0;
            int maxScore = -1;

            for (int i = 0; i < readings.Count; i++)
            {
                var reading = readings[i];
                var readingHiragana = WanaKana.ToHiragana(reading, new DefaultOptions { ConvertLongVowelMark = false });

                // Check phonetic match
                if (KanaNormalizer.Normalize(readingHiragana) != textHiraganaNormalized)
                {
                    if (KanaNormalizer.Normalize(WanaKana.ToHiragana(reading)) != textHiraganaNormalized)
                        continue;
                }

                // Score by script match (prefer readings that match the input script)
                int score = GetPrefixLength(originalText, reading);
                if (score > maxScore)
                {
                    maxScore = score;
                    bestIndex = (byte)i;
                }
            }

            // Fallback if no phonetic match: find any reading matching hiragana
            if (maxScore == -1)
            {
                var normalizedReadings = readings.Select(r => WanaKana.ToHiragana(r, new DefaultOptions { ConvertLongVowelMark = false }))
                                                 .ToList();
                var idx = normalizedReadings.IndexOf(textHiragana);
                if (idx != -1) return (byte)idx;

                // Try with long vowel conversion
                normalizedReadings = readings.Select(r => WanaKana.ToHiragana(r)).ToList();
                idx = normalizedReadings.IndexOf(textHiragana);
                if (idx != -1) return (byte)idx;
            }

            // No match found - return 255 to indicate failure
            return maxScore == -1 ? (byte)255 : bestIndex;

            static int GetPrefixLength(string s1, string s2)
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

        private static async Task CombineCompounds(List<SentenceInfo> sentences)
        {
            foreach (var sentence in sentences)
            {
                if (sentence.Words.Count < 2)
                    continue;

                var wordInfos = sentence.Words.Select(w => w.word).ToList();
                var result = new List<(WordInfo word, byte position, byte length)>(sentence.Words.Count);
                int i = 0;
                int lastConsumedIndex = -1;

                while (i < wordInfos.Count)
                {
                    var word = wordInfos[i];

                    if (word.PartOfSpeech is PartOfSpeech.Verb or PartOfSpeech.IAdjective or PartOfSpeech.Expression)
                    {
                        var match = await TryMatchCompounds(wordInfos, i, lastConsumedIndex);
                        if (match.HasValue)
                        {
                            var (startIndex, dictForm, wordId) = match.Value;

                            int tokensToRemove = i - startIndex;
                            if (tokensToRemove > 0 && result.Count >= tokensToRemove)
                            {
                                result.RemoveRange(result.Count - tokensToRemove, tokensToRemove);
                            }

                            var startPosition = sentence.Words[startIndex].position;
                            byte combinedLength = 0;
                            for (int j = startIndex; j <= i; j++)
                            {
                                combinedLength += sentence.Words[j].length;
                            }

                            var originalText = string.Concat(wordInfos.Skip(startIndex).Take(i - startIndex + 1).Select(w => w.Text));
                            var combinedWordInfo = new WordInfo
                                                   {
                                                       Text = originalText, DictionaryForm = dictForm,
                                                       PartOfSpeech = PartOfSpeech.Expression, NormalizedForm = dictForm,
                                                       Reading = WanaKana.ToHiragana(originalText),
                                                       PreMatchedWordId = wordId
                                                   };

                            result.Add((combinedWordInfo, startPosition, combinedLength));
                            lastConsumedIndex = i;
                            i++;
                            continue;
                        }
                    }

                    result.Add(sentence.Words[i]);
                    i++;
                }

                sentence.Words = result;
            }
        }

        /// <summary>
        /// Repairs tokens that were broken by trailing ー.
        /// When Sudachi sees e.g. 久しぶりー, it may split incorrectly.
        /// This method detects standalone ー tokens and tries to combine
        /// preceding tokens, validating against JMDict.
        ///
        /// Priority:
        /// 1. Try WITH ー (わー, ねー, すげー are valid words)
        /// 2. Try WITHOUT ー (久しぶりー → 久しぶり)
        /// 3. Fallback: attach ー to preceding token (preserve stylistic usage)
        /// </summary>
        private static void RepairLongVowelTokens(List<SentenceInfo> sentences)
        {
            foreach (var sentence in sentences)
            {
                for (int i = sentence.Words.Count - 1; i >= 0; i--)
                {
                    var word = sentence.Words[i].word;

                    // Find standalone ー tokens
                    if (word.Text != "ー" || word.PartOfSpeech != PartOfSpeech.SupplementarySymbol)
                        continue;

                    if (i == 0)
                    {
                        // ー at start with nothing to combine - just remove it
                        sentence.Words.RemoveAt(i);
                        continue;
                    }

                    bool found = false;

                    // Try combining preceding tokens (longest match first)
                    for (int combineCount = Math.Min(4, i); combineCount >= 1; combineCount--)
                    {
                        var combinedTokens = sentence.Words
                                                     .Skip(i - combineCount).Take(combineCount)
                                                     .Select(w => w.word.Text);
                        var combined = string.Concat(combinedTokens);
                        // PRIORITY 1: Try WITH ー (preserves わー, ねー, すげー, etc.)
                        var withBar = combined + "ー";
                        string? withBarHiragana;
                        try
                        {
                            withBarHiragana = WanaKana.ToHiragana(withBar);
                        }
                        catch
                        {
                            withBarHiragana = null;
                        }


                        if (withBarHiragana != null && _lookups.TryGetValue(withBarHiragana, out var idsWithBar) && idsWithBar.Count > 0)
                        {
                            // Found valid word WITH ー - merge and keep ー
                            var firstToken = sentence.Words[i - combineCount];
                            firstToken.word.Text = withBar;
                            sentence.Words.RemoveRange(i - combineCount + 1, combineCount);
                            i = i - combineCount; // Adjust index after removal
                            found = true;
                            break;
                        }

                        // PRIORITY 2: Try WITHOUT ー (handles 久しぶりー → 久しぶり)
                        string? withoutBarHiragana;
                        try
                        {
                            withoutBarHiragana = WanaKana.ToHiragana(combined);
                        }
                        catch
                        {
                            withoutBarHiragana = null;
                        }

                        if (withoutBarHiragana != null && _lookups.TryGetValue(withoutBarHiragana, out var idsWithout) &&
                            idsWithout.Count > 0)
                        {
                            // Found valid word WITHOUT ー - merge and strip ー
                            var firstToken = sentence.Words[i - combineCount];
                            firstToken.word.Text = combined;
                            sentence.Words.RemoveRange(i - combineCount + 1, combineCount);
                            i = i - combineCount; // Adjust index after removal
                            found = true;
                            break;
                        }
                    }

                    // PRIORITY 3: Fallback
                    if (!found)
                    {
                        var prevToken = sentence.Words[i - 1].word;
                        // If preceding token already ends with ー, this is just emphasis - discard it
                        // Otherwise attach ー to preserve stylistic usage
                        if (!prevToken.Text.EndsWith("ー"))
                        {
                            prevToken.Text += "ー";
                        }

                        sentence.Words.RemoveAt(i);
                        // No need to adjust i since only 1 item removed at current position
                    }
                }
            }
        }

        /// <summary>
        /// Combines adjacent noun tokens that form valid JMDict entries.
        /// Uses greedy longest-match: tries 4-token, then 3-token, then 2-token combinations.
        /// </summary>
        private static void CombineNounCompounds(List<SentenceInfo> sentences)
        {
            foreach (var sentence in sentences)
            {
                if (sentence.Words.Count < 2)
                    continue;

                var result = new List<(WordInfo word, byte position, byte length)>(sentence.Words.Count);
                int i = 0;

                while (i < sentence.Words.Count)
                {
                    var (word, position, length) = sentence.Words[i];

                    if (!IsNounForCompounding(word.PartOfSpeech))
                    {
                        result.Add(sentence.Words[i]);
                        i++;
                        continue;
                    }

                    // Try longest match first (4 tokens, then 3, then 2)
                    int bestMatch = 1;
                    for (int windowSize = Math.Min(4, sentence.Words.Count - i); windowSize >= 2; windowSize--)
                    {
                        // Check if all tokens in window are nouns
                        bool allNouns = true;
                        for (int j = 0; j < windowSize; j++)
                        {
                            if (!IsNounForCompounding(sentence.Words[i + j].word.PartOfSpeech))
                            {
                                allNouns = false;
                                break;
                            }
                        }

                        if (!allNouns)
                            continue;

                        // Combine the text
                        var combinedText = string.Concat(
                                                         sentence.Words.Skip(i).Take(windowSize).Select(w => w.word.Text));

                        // Check if it exists in JMDict lookups
                        if (_lookups.TryGetValue(combinedText, out var wordIds) && wordIds.Count > 0)
                        {
                            bestMatch = windowSize;
                            break;
                        }

                        // Also try hiragana version
                        var hiraganaText = WanaKana.ToHiragana(combinedText,
                                                               new DefaultOptions { ConvertLongVowelMark = false });
                        if (hiraganaText != combinedText &&
                            _lookups.TryGetValue(hiraganaText, out wordIds) && wordIds.Count > 0)
                        {
                            bestMatch = windowSize;
                            break;
                        }
                    }

                    if (bestMatch > 1)
                    {
                        // Merge tokens
                        var combinedText = string.Concat(
                                                         sentence.Words.Skip(i).Take(bestMatch).Select(w => w.word.Text));
                        byte combinedLength = 0;
                        for (int j = 0; j < bestMatch; j++)
                        {
                            combinedLength += sentence.Words[i + j].length;
                        }

                        var combinedWord = new WordInfo
                                           {
                                               Text = combinedText, DictionaryForm = combinedText,
                                               PartOfSpeech = sentence.Words[i].word.PartOfSpeech, NormalizedForm = combinedText,
                                               Reading = WanaKana.ToHiragana(combinedText)
                                           };

                        result.Add((combinedWord, position, combinedLength));
                        i += bestMatch;
                    }
                    else
                    {
                        result.Add(sentence.Words[i]);
                        i++;
                    }
                }

                sentence.Words = result;
            }
        }

        private static bool IsNounForCompounding(PartOfSpeech pos)
        {
            return pos is PartOfSpeech.Noun or PartOfSpeech.Name or PartOfSpeech.CommonNoun
                or PartOfSpeech.NaAdjective or PartOfSpeech.Prefix;
        }

        private static void CleanCompoundCache()
        {
            if (CompoundExpressionCache.Count >= MaxCompoundCacheSize)
            {
                CompoundExpressionCache.Clear();
            }
        }

        private static List<DeckWord> ExcludeFinalMisparses(IEnumerable<DeckWord> words)
        {
            if (ExcludedMisparses.Count == 0)
                return words.ToList();

            return words.Where(w => !ExcludedMisparses.Contains((w.WordId, w.ReadingIndex))).ToList();
        }

        private static async Task<(int startIndex, string dictionaryForm, int wordId)?> TryMatchCompounds(
            List<WordInfo> wordInfos,
            int wordIndex,
            int lastConsumedIndex = -1)
        {
            var verb = wordInfos[wordIndex];

            var dictForm = verb.DictionaryForm;

            // Try to deconjugate the dictionary form
            if (string.IsNullOrEmpty(dictForm) || dictForm == verb.Text)
            {
                var deconjugated = Deconjugator.Instance.Deconjugate(WanaKana.ToHiragana(verb.Text));
                if (deconjugated.Count == 0) return null;
                dictForm = deconjugated.First().Text;
            }

            // Backward window scan for greedy matching
            for (int windowSize = Math.Min(5, wordIndex + 1); windowSize >= 2; windowSize--)
            {
                int startIndex = wordIndex - windowSize + 1;

                // Skip if this window would overlap with previously consumed indices
                if (startIndex <= lastConsumedIndex)
                    continue;

                // Skip if first token is a particle or suffix (e.g. たち plural marker)
                var firstWord = wordInfos[startIndex];
                if (firstWord.PartOfSpeech == PartOfSpeech.Particle || firstWord.PartOfSpeech == PartOfSpeech.Suffix)
                    continue;

                // Skip if any token in the window is punctuation (SupplementarySymbol)
                // This prevents combining tokens across punctuation boundaries like commas
                bool hasPunctuation = false;
                for (int j = startIndex; j < wordIndex; j++)
                {
                    if (wordInfos[j].PartOfSpeech == PartOfSpeech.SupplementarySymbol)
                    {
                        hasPunctuation = true;
                        break;
                    }
                }

                if (hasPunctuation)
                    continue;

                var prefix = string.Concat(wordInfos.Skip(startIndex).Take(windowSize - 1).Select(w => w.Text));
                var candidate = prefix + dictForm;

                lock (CompoundCacheLock)
                {
                    if (CompoundExpressionCache.TryGetValue(candidate, out var cached))
                    {
                        if (cached.validExpression)
                            return (startIndex, candidate, cached.wordId!.Value);

                        // If we have something in the cache but it's not marked as valid, try a smaller window
                        continue;
                    }
                }

                if (_lookups.TryGetValue(candidate, out var wordIds) && wordIds.Count > 0)
                {
                    var validWordId = await FindValidCompoundWordId(wordIds);
                    if (validWordId.HasValue)
                    {
                        lock (CompoundCacheLock)
                        {
                            CleanCompoundCache();
                            CompoundExpressionCache[candidate] = (true, validWordId.Value);
                        }

                        return (startIndex, candidate, validWordId.Value);
                    }
                }

                // If we failed to find it, then try a pure hiragana version
                var hiraganaCandidate = WanaKana.ToHiragana(candidate, new DefaultOptions() { ConvertLongVowelMark = false });
                if (hiraganaCandidate != candidate && _lookups.TryGetValue(hiraganaCandidate, out wordIds) && wordIds.Count > 0)
                {
                    var validWordId = await FindValidCompoundWordId(wordIds);
                    if (validWordId.HasValue)
                    {
                        lock (CompoundCacheLock)
                        {
                            CleanCompoundCache();
                            CompoundExpressionCache[candidate] = (true, validWordId.Value);
                        }

                        return (startIndex, candidate, validWordId.Value);
                    }
                }

                // If we failed, then it's not a valid expression
                lock (CompoundCacheLock)
                {
                    CleanCompoundCache();
                    CompoundExpressionCache[candidate] = (false, null);
                }
            }

            return null;
        }

        private static async Task<int?> FindValidCompoundWordId(List<int> wordIds)
        {
            try
            {
                var wordCache = await JmDictCache.GetWordsAsync(wordIds);

                // First pass: prefer Expression POS (e.g. そうする "to do so" over 奏する "to play music")
                foreach (var wordId in wordIds)
                {
                    if (!wordCache.TryGetValue(wordId, out var word)) continue;

                    var posList = word.PartsOfSpeech.ToPartOfSpeech();
                    if (posList.Contains(PartOfSpeech.Expression))
                        return wordId;
                }

                // Second pass: accept any valid compound POS
                foreach (var wordId in wordIds)
                {
                    if (!wordCache.TryGetValue(wordId, out var word)) continue;

                    var posList = word.PartsOfSpeech.ToPartOfSpeech();
                    if (posList.Any(pos => ValidCompoundPos.Contains(pos)))
                        return wordId;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Warning] Error validating compound POS: {ex.Message}");
            }

            return null;
        }
    }
}