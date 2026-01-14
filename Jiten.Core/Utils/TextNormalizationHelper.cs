using System.Text;
using WanaKanaShaapu;

namespace Jiten.Core.Utils;

public static class TextNormalizationHelper
{
    /// <summary>
    /// Normalises input text for Japanese parsing by converting:
    /// 1. Uppercase ASCII letters to fullwidth
    /// 2. Lowercase romaji to hiragana
    /// 3. Halfwidth digits to fullwidth
    /// 4. Remaining halfwidth lowercase letters to fullwidth
    /// </summary>
    public static string NormaliseForParsing(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        // Convert uppercase to fullwidth first so WanaKana won't convert them
        var result = text.ToFullWidthUppercaseLetters();

        // Extract katakana positions and replace with placeholders
        // WanaKana.ToHiragana converts katakana to hiragana, which we don't want
        var katakanaPositions = new List<(int Index, char Char)>();
        var sb = new StringBuilder(result.Length);

        for (int i = 0; i < result.Length; i++)
        {
            char c = result[i];
            if (IsKatakana(c))
            {
                katakanaPositions.Add((sb.Length, c));
                sb.Append('\uFFFD');
            }
            else
            {
                sb.Append(c);
            }
        }

        result = WanaKana.ToHiragana(sb.ToString());

        // Restore katakana characters
        sb = new StringBuilder(result);
        foreach (var (index, katakanaChar) in katakanaPositions)
        {
            if (index < sb.Length)
                sb[index] = katakanaChar;
        }

        result = sb.ToString();
        result = result.ToFullWidthDigits();
        result = result.ToFullWidthLowercaseLetters();

        return result;
    }

    private static bool IsKatakana(char c)
    {
        return (c >= '\u30A0' && c <= '\u30FF') ||  // Katakana block
               (c >= '\u31F0' && c <= '\u31FF') ||  // Katakana phonetic extensions
               (c >= '\uFF65' && c <= '\uFF9F');    // Halfwidth katakana
    }
}
