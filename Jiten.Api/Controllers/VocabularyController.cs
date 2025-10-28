using Jiten.Api.Dtos;
using Jiten.Api.Helpers;
using Jiten.Api.Services;
using Jiten.Core;
using Jiten.Core.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Swashbuckle.AspNetCore.Annotations;

namespace Jiten.Api.Controllers;

/// <summary>
/// Endpoints for working with vocabulary: words, parsing text, and example sentences.
/// </summary>
[ApiController]
[Route("api/vocabulary")]
[EnableRateLimiting("fixed")]
[Produces("application/json")]
public class VocabularyController(JitenDbContext context, ICurrentUserService currentUserService) : ControllerBase
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

        var frequency = context.JmDictWordFrequencies.AsNoTracking().First(f => f.WordId == word.WordId);

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

        var mainReading = new ReadingDto()
                          {
                              Text = word.ReadingsFurigana[readingIndex], ReadingIndex = readingIndex,
                              ReadingType = word.ReadingTypes[readingIndex], FrequencyRank = frequency.ReadingsFrequencyRank[readingIndex],
                              FrequencyPercentage = frequency.ReadingsFrequencyPercentage[readingIndex].ZeroIfNaN(),
                              UsedInMediaAmount = frequency.ReadingsUsedInMediaAmount[readingIndex],
                              UsedInMediaAmountByType = usedInMediaByType
                          };

        List<ReadingDto> alternativeReadings = word.Readings
                                                   .Select((r, i) => new ReadingDto
                                                                     {
                                                                         Text = r, ReadingIndex = (byte)i, ReadingType = word.ReadingTypes[i],
                                                                         FrequencyRank =
                                                                             frequency.ReadingsFrequencyRank[i],
                                                                         FrequencyPercentage =
                                                                             frequency.ReadingsFrequencyPercentage[i].ZeroIfNaN(),
                                                                         UsedInMediaAmount = frequency.ReadingsUsedInMediaAmount[i]
                                                                     })
                                                   .ToList();
        

        return Results.Ok(new WordDto
                          {
                              WordId = word.WordId, MainReading = mainReading, AlternativeReadings = alternativeReadings,
                              Definitions = word.Definitions.ToDefinitionDtos(), PartsOfSpeech = word.PartsOfSpeech,
                              PitchAccents = word.PitchAccents, KnownState = await currentUserService.GetKnownWordState(wordId, readingIndex)
                          });
    }

    /// <summary>
    /// Parses the provided text and returns a sequence of parsed and unparsed segments as deck words.
    /// </summary>
    /// <param name="text">Text to parse. Max length 500 characters.</param>
    /// <returns>List of parsed and unparsed segments preserving original order.</returns>
    [HttpGet("parse")]
    [SwaggerOperation(Summary = "Parse text into words", Description = "Parses the provided text and returns parsed words and any gaps as separate items, preserving order.")]
    [ProducesResponseType(typeof(List<DeckWordDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IResult> Parse([FromQuery] string text)
    {
        if (text.Length > 500)
            return Results.BadRequest("Text is too long");

        var parsedWords = await Parser.Parser.ParseText(context, text);

        // We want both parsed words and unparsed ones
        var allWords = new List<DeckWordDto>();

        var wordsWithPositions = new List<(DeckWordDto Word, int Position)>();
        int currentPosition = 0;

        foreach (var word in parsedWords)
        {
            int position = text.IndexOf(word.OriginalText, currentPosition, StringComparison.Ordinal);
            if (position >= 0)
            {
                wordsWithPositions.Add((new DeckWordDto(word), position));
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
                allWords.Add(new DeckWordDto(gap));
            }

            allWords.Add(word);

            currentPosition = position + word.OriginalText.Length;
        }

        if (currentPosition < text.Length)
        {
            string gap = text.Substring(currentPosition);
            allWords.Add(new DeckWordDto(gap));
        }

        return Results.Ok(allWords);
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
        return await context.ExampleSentenceWords
                            .AsNoTracking()
                            .Where(w => w.WordId == wordId && w.ReadingIndex == readingIndex)
                            .OrderBy(_ => EF.Functions.Random())
                            .Join(
                                  context.ExampleSentences.AsNoTracking()
                                         .Where(s => !alreadyLoaded.Contains(s.DeckId)),
                                  w => w.ExampleSentenceId,
                                  s => s.SentenceId,
                                  (word, sentence) => new { Word = word, Sentence = sentence }
                                 )
                            .Join(
                                  context.Decks.AsNoTracking(),
                                  joined => joined.Sentence.DeckId,
                                  d => d.DeckId,
                                  (joined, deck) => new { joined, deck }
                                 )
                            .Where(j => !mediaType.HasValue || j.deck.MediaType == mediaType.Value)
                            .GroupJoin(
                                       context.Decks.AsNoTracking(),
                                       j => j.deck.ParentDeckId,
                                       pd => pd.DeckId,
                                       (j, parentDecks) => new ExampleSentenceDto
                                                           {
                                                               Text = j.joined.Sentence.Text, WordPosition = j.joined.Word.Position,
                                                               WordLength = j.joined.Word.Length, SourceDeck = j.deck,
                                                               SourceDeckParent = parentDecks.FirstOrDefault()
                                                           }
                                      )
                            .Take(3)
                            .ToListAsync();
    }
}