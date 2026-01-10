namespace Jiten.Core.Data.JMDict;

public class WordKanji
{
    public int WordId { get; set; }
    public short ReadingIndex { get; set; }
    public string KanjiCharacter { get; set; } = "";
    public short Position { get; set; }

    public JmDictWord Word { get; set; } = null!;
    public Kanji Kanji { get; set; } = null!;
}
