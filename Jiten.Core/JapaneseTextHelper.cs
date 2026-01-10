using System.Text;

namespace Jiten.Core;

public static class JapaneseTextHelper
{
    /// <summary>
    /// Determines whether the specified Unicode rune is a CJK kanji character.
    /// Covers main CJK Unified Ideographs block and extensions A-E, plus compatibility ranges.
    /// </summary>
    public static bool IsKanji(Rune r)
    {
        int value = r.Value;
        return value is
            (>= 0x4E00 and <= 0x9FFF) or   // Main block (Common)
            (>= 0x3400 and <= 0x4DBF) or   // Extension A
            (>= 0x20000 and <= 0x2A6DF) or // Extension B
            (>= 0x2A700 and <= 0x2B73F) or // Extension C
            (>= 0x2B740 and <= 0x2B81F) or // Extension D
            (>= 0x2B820 and <= 0x2CEAF) or // Extension E
            (>= 0xF900 and <= 0xFAFF) or   // Compatibility Ideographs
            (>= 0x2F800 and <= 0x2FA1F);   // Compatibility Supplement
    }
}
