using Jiten.Api.Dtos;
using Jiten.Api.Helpers;
using Jiten.Api.Services;
using Jiten.Core;
using Jiten.Core.Data;
using Jiten.Core.Data.JMDict;
using Jiten.Core.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using Swashbuckle.AspNetCore.Annotations;
using System.Text.Json;
using WanaKanaShaapu;

namespace Jiten.Api.Controllers;

/// <summary>
/// Endpoints for working with vocabulary: words, parsing text, and example sentences.
/// </summary>
[ApiController]
[Route("api/vocabulary")]
[EnableRateLimiting("fixed")]
[Produces("application/json")]
public class VocabularyController(JitenDbContext context, IDbContextFactory<JitenDbContext> contextFactory, ICurrentUserService currentUserService, IConnectionMultiplexer redis, ILogger<VocabularyController> logger) : ControllerBase
{
    /// <summary>
    /// Gets a word by its ID and reading index, including definitions, readings, frequency and user known state.
    /// </summary>
    /// <param name="wordId">The unique identifier of the word.</param>
    /// <param name="readingIndex">Index of the reading to treat as main (zero-based).</param>
    /// <returns>The full word data.</returns>
    [HttpGet("{wordId}/{readingIndex}")]
    [SwaggerOperation(Summary = "Get word by ID and reading index", Description = "Returns a word with main and alternative readings, definitions, parts of speech, pitch accents, frequency and known state.")]
    [ProducesResponseType(typeof(WordDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    // [ResponseCache(Duration = 3600)]
    public async Task<IResult> GetWord([FromRoute] int wordId, [FromRoute] byte readingIndex)
    {
        var word = await context.JMDictWords.AsNoTracking()
                                .Include(w => w.Definitions)
                                .FirstOrDefaultAsync(w => w.WordId == wordId);

        if (word == null)
            return Results.NotFound();

        var wordForms = await WordFormHelper.LoadWordFormsForWord(context, wordId);
        var mainForm = wordForms.FirstOrDefault(wf => wf.ReadingIndex == readingIndex);
        if (mainForm == null)
            return Results.NotFound();

        var formFreqs = await context.WordFormFrequencies
            .AsNoTracking()
            .Where(wff => wff.WordId == wordId)
            .ToDictionaryAsync(wff => wff.ReadingIndex);

        var usedInMediaByType = await context.DeckWords.AsNoTracking()
                                             .Where(dw => dw.WordId == wordId && dw.ReadingIndex == readingIndex)
                                             .Join(
                                                   context.Decks.AsNoTracking()
                                                          .Where(d => d.ParentDeckId == null)
                                                          .Select(d => new { d.DeckId, d.MediaType }),
                                                   dw => dw.DeckId,
                                                   d => d.DeckId,
                                                   (dw, d) => d.MediaType
                                                  )
                                             .GroupBy(mediaType => mediaType)
                                             .Select(g => new { MediaType = g.Key, Count = g.Count() })
                                             .ToDictionaryAsync(x => (int)x.MediaType, x => x.Count);

        var mainFreq = formFreqs.GetValueOrDefault(mainForm.ReadingIndex);
        var mainReading = WordFormHelper.ToFormDto(mainForm, mainFreq, usedInMediaByType);

        List<WordFormDto> alternativeReadings = wordForms
                                                   .Select(form =>
                                                   {
                                                       var freq = formFreqs.GetValueOrDefault(form.ReadingIndex);
                                                       return WordFormHelper.ToPlainFormDto(form, freq);
                                                   })
                                                   .ToList();

        return Results.Ok(new WordDto
                          {
                              WordId = word.WordId, MainReading = mainReading, AlternativeReadings = alternativeReadings,
                              Definitions = word.Definitions.ToDefinitionDtos(), PartsOfSpeech = word.PartsOfSpeech,
                              PitchAccents = word.PitchAccents, KnownStates = await currentUserService.GetKnownWordState(wordId, readingIndex)
                          });
    }

    /// <summary>
    /// Gets the kanji breakdown for a specific word reading.
    /// </summary>
    /// <param name="wordId"></param>
    /// <param name="readingIndex"></param>
    /// <returns>List of kanji in the word with their metadata.</returns>
    [HttpGet("{wordId}/{readingIndex}/kanji")]
    [SwaggerOperation(Summary = "Get kanji breakdown for word", Description = "Returns the kanji characters in a word reading with their metadata (stroke count, JLPT, meanings, frequency).")]
    [ProducesResponseType(typeof(List<KanjiListDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IResult> GetWordKanji([FromRoute] int wordId, [FromRoute] short readingIndex)
    {
        var wordKanjis = await context.WordKanjis
            .AsNoTracking()
            .Where(wk => wk.WordId == wordId && wk.ReadingIndex == readingIndex)
            .OrderBy(wk => wk.Position)
            .Select(wk => wk.KanjiCharacter)
            .ToListAsync();

        if (wordKanjis.Count == 0)
            return Results.Ok(new List<KanjiListDto>());
        

        var kanjis = await context.Kanjis
            .AsNoTracking()
            .Where(k => wordKanjis.Contains(k.Character))
            .ToDictionaryAsync(k => k.Character);

        // Preserve order based on position in word
        var result = wordKanjis
            .Where(c => kanjis.ContainsKey(c))
            .Select(c => kanjis[c])
            .Select(k => new KanjiListDto
            {
                Character = k.Character,
                Meanings = k.Meanings,
                StrokeCount = k.StrokeCount,
                JlptLevel = k.JlptLevel,
                FrequencyRank = k.FrequencyRank
            })
            .ToList();

        return Results.Ok(result);
    }

    /// <summary>
    /// Parses the provided text and returns a sequence of parsed and unparsed segments as deck words.
    /// </summary>
    /// <param name="text">Text to parse. Max length 2000 characters.</param>
    /// <returns>List of parsed and unparsed segments preserving original order.</returns>
    [HttpGet("parse")]
    [SwaggerOperation(Summary = "Parse text into words", Description = "Parses the provided text and returns parsed words and any gaps as separate items, preserving order.")]
    [ProducesResponseType(typeof(List<ParsedWordDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IResult> Parse([FromQuery] string text)
    {
        if (text.Length > 2000)
            return Results.BadRequest("Text is too long");

        var parsedWords = await Parser.Parser.ParseText(contextFactory, text);

        var allWords = new List<ParsedWordDto>();
        var wordsWithPositions = new List<(ParsedWordDto Word, int Position)>();
        int currentPosition = 0;

        foreach (var word in parsedWords)
        {
            int position = text.IndexOf(word.OriginalText, currentPosition, StringComparison.Ordinal);
            if (position >= 0)
            {
                wordsWithPositions.Add((new ParsedWordDto(word), position));
                currentPosition = position + word.OriginalText.Length;
            }
        }

        currentPosition = 0;
        foreach (var (word, position) in wordsWithPositions)
        {
            // If there's a gap before this word, add it as an unparsed word
            if (position > currentPosition)
            {
                string gap = text.Substring(currentPosition, position - currentPosition);
                allWords.Add(new ParsedWordDto(gap));
            }

            allWords.Add(word);
            currentPosition = position + word.OriginalText.Length;
        }

        if (currentPosition < text.Length)
        {
            string gap = text.Substring(currentPosition);
            allWords.Add(new ParsedWordDto(gap));
        }

        return Results.Ok(allWords);
    }

    /// <summary>
    /// Normalises and parses the provided text. Converts romaji to hiragana,
    /// halfwidth digits/letters to fullwidth, then parses and returns words.
    /// </summary>
    /// <param name="text">Text to normalise and parse. Max length 2000 characters.</param>
    /// <returns>Normalised text and list of parsed/unparsed segments.</returns>
    [HttpGet("parse-normalised")]
    [SwaggerOperation(Summary = "Normalise and parse text into words",
                      Description = "Normalises input (romaji→hiragana, halfwidth→fullwidth) then parses into words.")]
    [ProducesResponseType(typeof(ParseNormalisedResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IResult> ParseNormalised([FromQuery] string text)
    {
        if (text.Length > 2000)
            return Results.BadRequest("Text is too long");

        var normalisedText = TextNormalizationHelper.NormaliseForParsing(text);
        var parsedWords = await Parser.Parser.ParseText(contextFactory, normalisedText);

        var allWords = new List<ParsedWordDto>();
        var wordsWithPositions = new List<(ParsedWordDto Word, int Position)>();
        int currentPosition = 0;

        foreach (var word in parsedWords)
        {
            int position = normalisedText.IndexOf(word.OriginalText, currentPosition, StringComparison.Ordinal);
            if (position >= 0)
            {
                wordsWithPositions.Add((new ParsedWordDto(word), position));
                currentPosition = position + word.OriginalText.Length;
            }
        }

        currentPosition = 0;
        foreach (var (word, position) in wordsWithPositions)
        {
            if (position > currentPosition)
            {
                string gap = normalisedText.Substring(currentPosition, position - currentPosition);
                allWords.Add(new ParsedWordDto(gap));
            }

            allWords.Add(word);
            currentPosition = position + word.OriginalText.Length;
        }

        if (currentPosition < normalisedText.Length)
        {
            string gap = normalisedText.Substring(currentPosition);
            allWords.Add(new ParsedWordDto(gap));
        }

        return Results.Ok(new ParseNormalisedResultDto { NormalisedText = normalisedText, Words = allWords });
    }

    /// <summary>
    /// Gets IDs of words whose media frequency rank falls within the specified inclusive range.
    /// </summary>
    /// <param name="minFrequency">Minimum frequency rank (inclusive).</param>
    /// <param name="maxFrequency">Maximum frequency rank (inclusive).</param>
    /// <returns>List of word IDs.</returns>
    [HttpGet("vocabulary-list-frequency/{minFrequency}/{maxFrequency}")]
    [SwaggerOperation(Summary = "Get vocabulary IDs by media frequency range")]
    [ProducesResponseType(typeof(List<int>), StatusCodes.Status200OK)]
    public IResult GetVocabularyByMediaFrequencyRange([FromRoute] int minFrequency, [FromRoute] int maxFrequency)
    {
        var query = context.JmDictWordFrequencies.Where(f => f.FrequencyRank >= minFrequency && f.FrequencyRank <= maxFrequency);

        return Results.Ok(query.Select(f => f.WordId).ToList());
    }

    /// <summary>
    /// Returns up to three random example sentences for the given word and reading index, excluding already loaded ones.
    /// </summary>
    /// <param name="wordId">The word ID.</param>
    /// <param name="readingIndex">The reading index for the word.</param>
    /// <param name="alreadyLoaded">A list of deck IDs already loaded on the client to avoid duplicates.</param>
    /// <returns>A list of example sentences with metadata.</returns>
    [HttpPost("{wordId}/{readingIndex}/random-example-sentences/{mediaType?}")]
    [SwaggerOperation(Summary = "Get random example sentences",
                      Description =
                          "Returns up to three random example sentences for the given word and reading index, excluding already loaded ones.")]
    [ProducesResponseType(typeof(List<ExampleSentenceDto>), StatusCodes.Status200OK)]
    public async Task<List<ExampleSentenceDto>> GetRandomExampleSentences([FromRoute] int wordId, [FromRoute] int readingIndex,
                                                                          [FromBody] List<int> alreadyLoaded, [FromRoute] MediaType? mediaType = null)
    {
        // Subquery: distinct sentence IDs for this word+reading (not materialised)
        var sentenceIdSubquery = context.ExampleSentenceWords
            .Where(w => w.WordId == wordId && w.ReadingIndex == readingIndex)
            .Select(w => w.ExampleSentenceId)
            .Distinct();

        // Filter, deduplicate, exclude already-loaded decks, sample 3 random — all in SQL
        var picked = await context.ExampleSentences
            .AsNoTracking()
            .Where(s => sentenceIdSubquery.Contains(s.SentenceId))
            .Join(context.Decks.AsNoTracking(),
                  s => s.DeckId, d => d.DeckId,
                  (s, d) => new { Sentence = s, Deck = d })
            .Where(j => !mediaType.HasValue || j.Deck.MediaType == mediaType.Value)
            .Where(j => !alreadyLoaded.Contains(j.Deck.DeckId)
                     && (!j.Deck.ParentDeckId.HasValue || !alreadyLoaded.Contains(j.Deck.ParentDeckId.Value)))
            .OrderBy(_ => EF.Functions.Random())
            .Take(3)
            .Select(j => new
            {
                SentenceId = j.Sentence.SentenceId,
                j.Sentence.Text,
                DeckId = j.Deck.DeckId,
                ParentDeckId = j.Deck.ParentDeckId
            })
            .ToListAsync();

        if (picked.Count == 0) return [];

        // Fetch word position/length for the selected sentences (at most a handful of rows)
        var selectedIds = picked.Select(p => p.SentenceId).ToList();
        var positionMap = (await context.ExampleSentenceWords
            .AsNoTracking()
            .Where(w => w.WordId == wordId && w.ReadingIndex == readingIndex
                        && selectedIds.Contains(w.ExampleSentenceId))
            .Select(w => new { w.ExampleSentenceId, w.Position, w.Length })
            .ToListAsync())
            .DistinctBy(w => w.ExampleSentenceId)
            .ToDictionary(w => w.ExampleSentenceId);

        // Build DTOs
        var childDeckIds = picked.Select(p => p.DeckId).Distinct().ToList();
        var childDecks = await context.Decks.AsNoTracking()
            .Where(d => childDeckIds.Contains(d.DeckId))
            .ToDictionaryAsync(d => d.DeckId, d => d);

        var parentIds = picked.Where(p => p.ParentDeckId.HasValue).Select(p => p.ParentDeckId!.Value).Distinct().ToList();
        var parentDecks = parentIds.Count > 0
            ? await context.Decks.AsNoTracking()
                .Where(d => parentIds.Contains(d.DeckId))
                .ToDictionaryAsync(d => d.DeckId, d => d)
            : new Dictionary<int, Deck>();

        return picked.Select(p =>
        {
            positionMap.TryGetValue(p.SentenceId, out var pos);
            childDecks.TryGetValue(p.DeckId, out var sourceDeck);
            Deck? parentDeck = null;
            if (p.ParentDeckId.HasValue)
                parentDecks.TryGetValue(p.ParentDeckId.Value, out parentDeck);

            return new ExampleSentenceDto
            {
                Text = p.Text,
                WordPosition = pos?.Position ?? 0,
                WordLength = pos?.Length ?? 0,
                SourceDeck = sourceDeck!,
                SourceDeckParent = parentDeck
            };
        }).ToList();
    }

    /// <summary>
    /// Searches the dictionary by Japanese text, romaji, English meaning, or wildcard pattern.
    /// </summary>
    [HttpGet("search")]
    [SwaggerOperation(Summary = "Search dictionary",
                      Description = "Searches by Japanese text, romaji, English meaning, or wildcard pattern (use * for wildcard).")]
    [ProducesResponseType(typeof(DictionarySearchResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IResult> SearchDictionary(
        [FromQuery] string query,
        [FromQuery] int limit = 50,
        [FromQuery] int offset = 0)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Results.Ok(new DictionarySearchResultDto { Query = query ?? "" });

        limit = Math.Clamp(limit, 1, 100);
        var trimmed = query.Trim().ToLowerInvariant();

        if (trimmed.Length > 200)
            return Results.BadRequest("Query too long");

        var cacheKey = $"jiten:dict-search:{trimmed}:{limit}:{offset}";
        var redisDb = redis.GetDatabase();
        try
        {
            var cachedJson = await redisDb.StringGetAsync(cacheKey);
            if (cachedJson.HasValue)
                return Results.Ok(JsonSerializer.Deserialize<DictionarySearchResultDto>(cachedJson!));
        }
        catch { }

        var hasWildcard = trimmed.Contains('*');
        var cleanText = trimmed.Replace("*", "");

        if (cleanText.Length == 0)
            return Results.BadRequest("Query must contain searchable text");

        var hasJapanese = ContainsJapanese(cleanText);
        var isAsciiOnly = cleanText.All(c => c < 128);
        var hasSpaces = cleanText.Contains(' ');

        string queryType;
        List<DictionaryEntryDto> results;
        List<DictionaryEntryDto> dictionaryResults = [];

        if (hasWildcard)
        {
            queryType = "wildcard";
            var parts = trimmed.Split('*');

            string[] processedParts;
            if (!hasJapanese && isAsciiOnly && !hasSpaces)
            {
                processedParts = parts.Select(p =>
                    string.IsNullOrEmpty(p) ? "" : SanitizeLikeInput(WanaKana.ToHiragana(p.ToLowerInvariant()))
                ).ToArray();
            }
            else
            {
                processedParts = parts.Select(SanitizeLikeInput).ToArray();
            }

            var likePattern = string.Join("%", processedParts);
            results = await SearchLookupsByPattern(likePattern, limit, offset);
        }
        else if (hasJapanese)
        {
            queryType = "japanese";
            var wordIds = await SearchLookupsExact(trimmed, limit, offset);
            results = await BuildDictionaryEntries(wordIds, trimmed);
        }
        else if (isAsciiOnly && !hasSpaces)
        {
            var hiragana = WanaKana.ToHiragana(trimmed.ToLowerInvariant());
            var isValidRomaji = hiragana.All(c =>
                (c >= '\u3040' && c <= '\u309F') ||
                (c >= '\u30A0' && c <= '\u30FF'));

            if (isValidRomaji)
            {
                var romajiWordIds = await SearchLookupsExact(hiragana, limit, offset);
                if (romajiWordIds.Count > 0)
                {
                    queryType = "romaji";
                    results = await BuildDictionaryEntries(romajiWordIds, hiragana);

                    var englishResults = await SearchByEnglishGloss(trimmed, limit, offset);
                    var existingWordIds = results.Select(r => r.WordId).ToHashSet();
                    dictionaryResults = englishResults.Where(e => !existingWordIds.Contains(e.WordId)).ToList();
                }
                else
                {
                    queryType = "english";
                    results = await SearchByEnglishGloss(trimmed, limit, offset);
                }
            }
            else
            {
                queryType = "english";
                results = await SearchByEnglishGloss(trimmed, limit, offset);
            }
        }
        else
        {
            queryType = "english";
            results = await SearchByEnglishGloss(trimmed, limit, offset);
        }

        var dto = new DictionarySearchResultDto
        {
            Query = trimmed,
            QueryType = queryType,
            Results = results,
            DictionaryResults = dictionaryResults,
            HasMore = results.Count >= limit
        };

        try { await redisDb.StringSetAsync(cacheKey, JsonSerializer.Serialize(dto), TimeSpan.FromDays(7)); }
        catch { }

        return Results.Ok(dto);
    }

    #region Dictionary search helpers

    private async Task<List<int>> SearchLookupsExact(string lookupKey, int limit, int offset)
    {
        return await context.Lookups
            .AsNoTracking()
            .Where(l => l.LookupKey == lookupKey)
            .Select(l => l.WordId)
            .Distinct()
            .OrderBy(id => id)
            .Skip(offset)
            .Take(limit)
            .ToListAsync();
    }

    private async Task<List<DictionaryEntryDto>> SearchLookupsByPattern(string likePattern, int limit, int offset)
    {
        var wordIds = await context.Lookups
            .AsNoTracking()
            .Where(l => EF.Functions.Like(l.LookupKey, likePattern))
            .Select(l => l.WordId)
            .Distinct()
            .OrderBy(id => id)
            .Skip(offset)
            .Take(limit)
            .ToListAsync();

        return await BuildDictionaryEntries(wordIds);
    }

    private async Task<List<DictionaryEntryDto>> SearchByEnglishGloss(string query, int limit, int offset)
    {
        var regexPattern = $@"\m{SanitizeRegexInput(query)}\M";
        var oversampleLimit = limit * 4;

        var wordIds = await context.Database
            .SqlQueryRaw<int>(
                """
                SELECT DISTINCT "WordId" AS "Value" FROM jmdict."Definitions"
                WHERE EXISTS (
                    SELECT 1 FROM unnest("EnglishMeanings") AS m
                    WHERE m ~* {0}
                )
                ORDER BY "WordId"
                LIMIT {1}
                """, regexPattern, oversampleLimit)
            .ToListAsync();

        var entries = await BuildDictionaryEntries(wordIds, englishQuery: query);
        return entries
            .OrderByDescending(e => e.Meanings.Any(m => m.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .ThenBy(e => e.FrequencyRank)
            .Skip(offset)
            .Take(limit)
            .ToList();
    }

    private async Task<List<DictionaryEntryDto>> BuildDictionaryEntries(List<int> wordIds, string? matchText = null, string? englishQuery = null)
    {
        if (wordIds.Count == 0) return [];

        var words = await context.JMDictWords
            .AsNoTracking()
            .Include(w => w.Definitions)
            .Where(w => wordIds.Contains(w.WordId))
            .ToListAsync();

        var wordForms = await context.WordForms
            .AsNoTracking()
            .Where(wf => wordIds.Contains(wf.WordId))
            .ToListAsync();

        var formsByWord = wordForms.GroupBy(f => f.WordId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var frequencies = await context.WordFormFrequencies
            .AsNoTracking()
            .Where(f => wordIds.Contains(f.WordId))
            .ToListAsync();

        var freqByWordReading = frequencies
            .ToDictionary(f => (f.WordId, f.ReadingIndex));

        return words
            .Select(w =>
            {
                if (!formsByWord.TryGetValue(w.WordId, out var forms) || forms.Count == 0)
                    return null;

                JmDictWordForm? bestForm = null;
                if (matchText != null)
                    bestForm = forms.FirstOrDefault(f => f.Text == matchText);
                bestForm ??= forms.OrderBy(f => f.ReadingIndex).First();

                var freq = freqByWordReading.GetValueOrDefault((w.WordId, bestForm.ReadingIndex));
                var firstDef = w.Definitions
                    .Where(d => d.EnglishMeanings.Count > 0)
                    .OrderBy(d => d.SenseIndex)
                    .FirstOrDefault();

                if (englishQuery != null)
                {
                    var matchingDef = w.Definitions
                        .Where(d => d.EnglishMeanings.Any(m => m.Contains(englishQuery, StringComparison.OrdinalIgnoreCase)))
                        .OrderBy(d => d.SenseIndex)
                        .FirstOrDefault();
                    if (matchingDef != null) firstDef = matchingDef;
                }

                string? primaryKanjiText = null;
                if (bestForm.FormType == JmDictFormType.KanaForm)
                {
                    var kanjiForm = forms
                        .Where(f => f.FormType == JmDictFormType.KanjiForm && !f.IsSearchOnly)
                        .OrderByDescending(f => freqByWordReading.GetValueOrDefault((w.WordId, f.ReadingIndex))?.FrequencyRank != null ? 1 : 0)
                        .ThenBy(f => freqByWordReading.GetValueOrDefault((w.WordId, f.ReadingIndex))?.FrequencyRank ?? int.MaxValue)
                        .ThenBy(f => f.ReadingIndex)
                        .FirstOrDefault();
                    if (kanjiForm != null)
                        primaryKanjiText = kanjiForm.RubyText;
                }

                return new DictionaryEntryDto
                {
                    WordId = w.WordId,
                    ReadingIndex = (byte)bestForm.ReadingIndex,
                    Text = bestForm.RubyText,
                    PrimaryKanjiText = primaryKanjiText,
                    PartsOfSpeech = w.PartsOfSpeech,
                    Meanings = firstDef?.EnglishMeanings ?? [],
                    FrequencyRank = freq?.FrequencyRank ?? int.MaxValue
                };
            })
            .Where(e => e != null)
            .OrderBy(e => e!.FrequencyRank)
            .Cast<DictionaryEntryDto>()
            .ToList();
    }

    private static bool ContainsJapanese(string text)
    {
        return text.Any(c =>
            (c >= '\u3040' && c <= '\u309F') ||
            (c >= '\u30A0' && c <= '\u30FF') ||
            (c >= '\u4E00' && c <= '\u9FFF') ||
            (c >= '\u3400' && c <= '\u4DBF'));
    }

    private static string SanitizeLikeInput(string input)
    {
        return input
            .Replace("\\", "\\\\")
            .Replace("%", "\\%")
            .Replace("_", "\\_");
    }

    private static string SanitizeRegexInput(string input)
    {
        return System.Text.RegularExpressions.Regex.Replace(input, @"[.*+?^${}()|[\]\\]", @"\$&");
    }

    #endregion
}