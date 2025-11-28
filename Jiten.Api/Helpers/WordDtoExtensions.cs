using Jiten.Api.Dtos;
using Jiten.Core.Data.User;

namespace Jiten.Api.Helpers;

public static class WordDtoExtensions
{
    public static void ApplyKnownWordsState(this IEnumerable<WordDto> words,
                                             Dictionary<(int WordId, byte ReadingIndex), KnownState> knownWords)
    {
        foreach (var word in words)
        {
            word.KnownState = knownWords.GetValueOrDefault((word.WordId, word.MainReading.ReadingIndex), KnownState.New);
        }
    }
}