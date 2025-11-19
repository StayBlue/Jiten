using Jiten.Api.Dtos;
using Jiten.Api.Dtos.Requests;
using Jiten.Api.Services;
using Jiten.Core;
using Jiten.Core.Data;
using Jiten.Core.Data.JMDict;
using Jiten.Core.Data.User;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Swashbuckle.AspNetCore.Annotations;

namespace Jiten.Api.Controllers;

[ApiController]
[Route("api/reader")]
[Authorize]
public class ReaderController(JitenDbContext context, IDbContextFactory<JitenDbContext> contextFactory, ICurrentUserService currentUserService, ILogger<ReaderController> logger) : ControllerBase
{
    [HttpPost("ping")]
    public IResult Ping()
    {
        return Results.Ok(new { success = true });
    }

    /// <summary>
    /// Parses the provided text and returns a sequence of parsed and unparsed segments as deck words.
    /// </summary>
    /// <param name="text">Text to parse. Max length 500 characters.</param>
    /// <returns>List of parsed and unparsed segments preserving original order.</returns>
    [HttpPost("parse")]
    [SwaggerOperation(Summary = "Parse text into words",
                      Description = "Parses the provided text and returns parsed words and any gaps as separate items, preserving order.")]
    // [ProducesResponseType(typeof(List<DeckWordDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IResult> Parse(ReaderParseRequest request)
    {
        if (string.Join("", request.Text).Length > 17000)
            return Results.BadRequest("Text is too long");

        List<List<ReaderToken>> allTokens = new();
        List<ReaderWord> allWords = new();
        List<List<DeckWord>> parsedParagraphs = new();

        const string stopToken = "\n|\n";
        var combinedText = string.Join(stopToken, request.Text);
        var allParsedWords = await Parser.Parser.ParseText(contextFactory, combinedText);

        var paragraphOffsets = new int[request.Text.Length];
        var currentOffset = 0;
        for (var i = 0; i < request.Text.Length; i++)
        {
            paragraphOffsets[i] = currentOffset;
            currentOffset += request.Text[i].Length + stopToken.Length;
        }

        var wordIndex = 0;
        var positionInCombined = 0;
        for (var i = 0; i < request.Text.Length; i++)
        {
            var paragraphWords = new List<DeckWord>();
            var paragraphEnd = paragraphOffsets[i] + request.Text[i].Length;

            while (wordIndex < allParsedWords.Count)
            {
                var word = allParsedWords[wordIndex];
                var wordPosition = combinedText.IndexOf(word.OriginalText, positionInCombined, StringComparison.Ordinal);

                if (wordPosition >= paragraphEnd)
                    break;

                if (wordPosition >= paragraphOffsets[i] && wordPosition < paragraphEnd)
                {
                    paragraphWords.Add(word);
                    positionInCombined = wordPosition + word.OriginalText.Length;
                }

                wordIndex++;
            }

            parsedParagraphs.Add(paragraphWords);
        }

        var wordIds = parsedParagraphs.SelectMany(p => p).Select(w => w.WordId).ToList();
        var jmdictWords = await context.JMDictWords.Where(w => wordIds.Contains(w.WordId)).Include(w => w.Definitions).ToListAsync();
        var frequencyData = await context.JmDictWordFrequencies
            .AsNoTracking()
            .Where(f => wordIds.Contains(f.WordId))
            .ToDictionaryAsync(f => f.WordId, f => f);


        for (var i = 0; i < parsedParagraphs.Count; i++)
        {
            List<DeckWord>? parsedWords = parsedParagraphs[i];
            List<ReaderToken> tokens = new();
            int currentPosition = 0;

            var knownStates = await currentUserService.GetKnownWordsState(parsedWords.Select(dw => (dw.WordId, dw.ReadingIndex)).ToList());
            
            foreach (var word in parsedWords)
            {
                int position = request.Text[i].IndexOf(word.OriginalText, currentPosition, StringComparison.Ordinal);
                if (position >= 0)
                {
                    tokens.Add(new ReaderToken
                               {
                                   WordId = word.WordId, ReadingIndex = word.ReadingIndex, Start = position,
                                   End = position + word.OriginalText.Length, Length = word.OriginalText.Length
                               });
                    var jmdictWord = jmdictWords.First(jw => jw.WordId == word.WordId);
                    knownStates.TryGetValue((word.WordId,word.ReadingIndex), out var knownState);
                    var readerWord = new ReaderWord()
                                     {
                                         WordId = word.WordId, ReadingIndex = word.ReadingIndex, Spelling = word.OriginalText, Reading =
                                             jmdictWord.ReadingsFurigana[word.ReadingIndex],
                                         PartsOfSpeech = jmdictWord.PartsOfSpeech.ToHumanReadablePartsOfSpeech(),
                                         MeaningsChunks = jmdictWord.Definitions.Where(d => d.EnglishMeanings.Count > 0).Select(d => d.EnglishMeanings).ToList(),
                                         MeaningsPartOfSpeech = jmdictWord.Definitions.SelectMany(d => d.PartsOfSpeech).ToList() ?? [""],
                                         FrequencyRank = frequencyData.TryGetValue(word.WordId, out var freq)
                                             ? freq.ReadingsFrequencyRank[word.ReadingIndex]
                                             : 0,
                                         KnownState = knownState,
                                     };
                    allWords.Add(readerWord);

                    currentPosition = position + word.OriginalText.Length;
                }
            }

            allTokens.Add(tokens);
        }

        logger.LogInformation("Reader parsed text: ParagraphCount={ParagraphCount}, TotalWords={TotalWords}",
            request.Text.Length, parsedParagraphs.Sum(p => p.Count));
        return Results.Ok(new { tokens = allTokens, vocabulary = allWords });
    }

    [HttpPost("lookup-vocabulary")]
    [SwaggerOperation(Summary = "Lookup vocabulary known states",
                      Description = "Returns the known state for each word/reading combination for the authenticated user.")]
    public async Task<IResult> LookupVocabulary(LookupVocabularyRequest request)
    {
        var keys = request.Words.Select(w => (w[0], (byte)w[1])).ToList();
        var knownStates = await currentUserService.GetKnownWordsState(keys);

        var result = request.Words.Select(w =>
            knownStates.TryGetValue((w[0], (byte)w[1]), out var state)
                ? new[] { state.ToString().ToLower() }
                : new[] { nameof(KnownState.Unknown).ToLower() }
                                         ).ToList();

        return Results.Ok(new { result = result });
    }
}