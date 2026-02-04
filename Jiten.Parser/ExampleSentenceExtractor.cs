using System.Diagnostics;
using Jiten.Core.Data;

namespace Jiten.Parser;

public static class ExampleSentenceExtractor
{
    private const int FIRST_PASS_MIN_LENGTH = 15;
    private const int FIRST_PASS_MAX_LENGTH = 40;
    private const float FIRST_PASS_PERCENTAGE = 0.25f;

    private const int SECOND_PASS_MIN_LENGTH = 10;
    private const int SECOND_PASS_MAX_LENGTH = 45;
    private const float SECOND_PASS_PERCENTAGE = 0.5f;

    private const int THIRD_PASS_MIN_LENGTH = 10;
    private const int THIRD_PASS_MAX_LENGTH = 55;
    private const float THIRD_PASS_PERCENTAGE = 1f;

    public static List<ExampleSentence> ExtractSentences(List<SentenceInfo> sentences, DeckWord[] words)
    {
        static bool IsPosCompatible(DeckWord deckWord, WordInfo token)
        {
            if (deckWord.PartsOfSpeech.Any(pos => pos == token.PartOfSpeech))
                return true;

            // Sudachi emits proper nouns as POS=名詞 (Noun) with name-like POS sections.
            // Allow those to match JMnedict name entries (PartOfSpeech.Name).
            if (PosMapper.IsNameLikeSudachiNoun(
                    token.PartOfSpeech,
                    token.PartOfSpeechSection1,
                    token.PartOfSpeechSection2,
                    token.PartOfSpeechSection3) &&
                deckWord.PartsOfSpeech.Any(pos => pos == PartOfSpeech.Name))
            {
                return true;
            }

            return false;
        }

        // Pre-filter sentences with insufficient character diversity
        var validSentences = new HashSet<SentenceInfo>(); 
        for (int i = 0; i < sentences.Count; i++)
        {
            var sentence = sentences[i];
            var distinctChars = new HashSet<char>();
            foreach (var (wordInfo, _, _) in sentence.Words)
            {
                foreach (char c in wordInfo.Text)
                {
                    distinctChars.Add(c);
                    if (distinctChars.Count >= 6) break;
                }

                if (distinctChars.Count >= 6) break;
            }

            if (distinctChars.Count >= 6)
            {
                validSentences.Add(sentence);
            }
        }

        // Create position lookup for valid sentences only
        var sentencePositions = new Dictionary<SentenceInfo, int>();
        for (int i = 0; i < sentences.Count; i++)
        {
            if (validSentences.Contains(sentences[i]))
            {
                sentencePositions[sentences[i]] = i;
            }
        }

        // Group words by text for O(1) lookup instead of linear search
        var wordsByText = new Dictionary<string, List<DeckWord>>();
        foreach (var word in words)
        {
            if (!wordsByText.ContainsKey(word.OriginalText))
            {
                wordsByText[word.OriginalText] = new List<DeckWord>();
            }

            wordsByText[word.OriginalText].Add(new DeckWord
                                               {
                                                   WordId = word.WordId, ReadingIndex = word.ReadingIndex,
                                                   OriginalText = word.OriginalText, PartsOfSpeech = word.PartsOfSpeech
                                               });
        }

        var exampleSentences = new List<ExampleSentence>();
        var usedSentences = new HashSet<SentenceInfo>();

        var passes = new[]
                     {
                         new { MinLength = FIRST_PASS_MIN_LENGTH, MaxLength = FIRST_PASS_MAX_LENGTH, Percentage = FIRST_PASS_PERCENTAGE },
                         new
                         {
                             MinLength = SECOND_PASS_MIN_LENGTH, MaxLength = SECOND_PASS_MAX_LENGTH, Percentage = SECOND_PASS_PERCENTAGE
                         },
                         new { MinLength = THIRD_PASS_MIN_LENGTH, MaxLength = THIRD_PASS_MAX_LENGTH, Percentage = THIRD_PASS_PERCENTAGE }
                     };

        foreach (var pass in passes)
        {
            // Only consider sentences from the first X% of the text
            int maxPosition = (int)(sentences.Count * pass.Percentage);

            // Pre-filter and sort sentences for this pass
            var candidateSentences = new List<SentenceInfo>();
            foreach (var sentence in validSentences)
            {
                if (!usedSentences.Contains(sentence) &&
                    sentence.Text.Length >= pass.MinLength &&
                    sentence.Text.Length <= pass.MaxLength &&
                    sentencePositions[sentence] < maxPosition)
                {
                    candidateSentences.Add(sentence);
                }
            }

            // Sort by length descending
            candidateSentences.Sort((a, b) => b.Text.Length.CompareTo(a.Text.Length));

            for (int i = 0; i < candidateSentences.Count; i++)
            {
                var sentence = candidateSentences[i];
                var exampleSentence = new ExampleSentence
                                      {
                                          Text = sentence.Text, Position = sentencePositions[sentence],
                                          Words = new List<ExampleSentenceWord>()
                                      };

                bool foundAnyWord = false;

                foreach (var (wordInfo, position, length) in sentence.Words)
                {
                    if (!wordsByText.TryGetValue(wordInfo.Text, out var wordList) || wordList.Count <= 0) continue;

                    // Find first word with matching POS
                    int matchIndex = -1;
                    for (int j = 0; j < wordList.Count; j++)
                    {
                        if (IsPosCompatible(wordList[j], wordInfo))
                        {
                            matchIndex = j;
                            break;
                        }
                    }

                    // If we found a match, add it and remove from the list
                    if (matchIndex >= 0)
                    {
                        var foundWord = wordList[matchIndex];
                        exampleSentence.Words.Add(new ExampleSentenceWord
                                                  {
                                                      WordId = foundWord.WordId, ReadingIndex = foundWord.ReadingIndex,
                                                      Position = (byte)Math.Min(position, 255),
                                                      Length = (byte)Math.Min(length, 255)
                                                  });

                        foundAnyWord = true;

                        // Remove the matched word from the list
                        wordList.RemoveAt(matchIndex);

                        // Remove empty lists to avoid future lookups
                        if (wordList.Count == 0)
                        {
                            wordsByText.Remove(wordInfo.Text);
                        }
                    }
                }

                if (foundAnyWord)
                {
                    exampleSentences.Add(exampleSentence);
                }

                usedSentences.Add(sentence);

                // Early exit if no more words available
                if (wordsByText.Count == 0)
                {
                    return exampleSentences;
                }
            }

            // Early exit if no more words available
            if (wordsByText.Count == 0)
            {
                break;
            }
        }

        return exampleSentences;
    }
}
