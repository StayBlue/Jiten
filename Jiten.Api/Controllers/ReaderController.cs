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
public class ReaderController(
    JitenDbContext context,
    IDbContextFactory<JitenDbContext> contextFactory,
    ICurrentUserService currentUserService,
    ILogger<ReaderController> logger) : ControllerBase
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
        if (string.Join("", request.Text).Length > 81000)
            return Results.BadRequest("Text is too long");

        List<List<ReaderToken>> allTokens = new();
        List<ReaderWord> allWords = new();
        List<List<DeckWord>> parsedParagraphs = new();

        const string stopToken = "\n|\n";
        var combinedText = string.Join(stopToken, request.Text);
        
        // The parsed text is different to add stop tokens to characters that break because sudachi would ignore them and parse as a whole word
        // But we need the original combined text for the paragraph logic position tracking
        var parsedText = combinedText.Replace(" ", stopToken);
        var allParsedWords = await Parser.Parser.ParseText(contextFactory, parsedText, preserveStopToken: true);

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

                // Word not found from current position - skip it
                if (wordPosition < 0)
                {
                    wordIndex++;
                    continue;
                }

                // Check if this match might be wrong (found too far ahead)
                // by looking for subsequent words between current position and found position
                if (wordPosition - positionInCombined > 10)
                {
                    var foundCloserWord = false;
                    for (var lookAhead = 1; lookAhead <= 5 && wordIndex + lookAhead < allParsedWords.Count; lookAhead++)
                    {
                        var futureWord = allParsedWords[wordIndex + lookAhead];
                        var futurePos = combinedText.IndexOf(futureWord.OriginalText, positionInCombined, StringComparison.Ordinal);

                        if (futurePos >= 0 && futurePos < wordPosition)
                        {
                            // Found a subsequent word that appears BEFORE our current match
                            // This means our current match jumped too far - skip it
                            foundCloserWord = true;
                            break;
                        }
                    }

                    if (foundCloserWord)
                    {
                        wordIndex++;
                        continue;
                    }
                }

                // Word is beyond current paragraph - let a later paragraph iteration handle it
                if (wordPosition >= paragraphEnd)
                    break;

                // Word is within current paragraph
                if (wordPosition >= paragraphOffsets[i])
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
                                   End = position + word.OriginalText.Length, Length = word.OriginalText.Length,
                                   Conjugations = word.Conjugations
                               });
                    var jmdictWord = jmdictWords.First(jw => jw.WordId == word.WordId);
                    knownStates.TryGetValue((word.WordId, word.ReadingIndex), out var knownState);
                    var readerWord = new ReaderWord()
                                     {
                                         WordId = word.WordId, ReadingIndex = word.ReadingIndex,
                                         Spelling = jmdictWord.Readings[word.ReadingIndex], Reading =
                                             jmdictWord.ReadingsFurigana[word.ReadingIndex],
                                         PartsOfSpeech = jmdictWord.PartsOfSpeech.ToHumanReadablePartsOfSpeech(), MeaningsChunks =
                                             jmdictWord.Definitions.Where(d => d.EnglishMeanings.Count > 0)
                                                       .Select(d => d.EnglishMeanings).ToList(),
                                         MeaningsPartOfSpeech = jmdictWord.Definitions.SelectMany(d => d.PartsOfSpeech).ToList() ?? [""],
                                         FrequencyRank = frequencyData.TryGetValue(word.WordId, out var freq)
                                             ? freq.ReadingsFrequencyRank[word.ReadingIndex]
                                             : 0,
                                         KnownState = knownState ?? [KnownState.New],
                                     };
                    allWords.Add(readerWord);

                    currentPosition = position + word.OriginalText.Length;
                }
            }

            allTokens.Add(tokens);
        }

        logger.LogInformation("Reader parsed text: ParagraphCount={ParagraphCount}, TotalWords={TotalWords}, TotalLength={TotalLength}",
                              request.Text.Length, parsedParagraphs.Sum(p => p.Count), string.Join("", request.Text).Length);
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
                                                  ? state.Select(s => (int)s)
                                                  : [0]
                                         ).ToList();

        return Results.Ok(new { result = result });
    }
}