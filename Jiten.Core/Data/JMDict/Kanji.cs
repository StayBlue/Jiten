namespace Jiten.Core.Data.JMDict;

public class Kanji
{
    public string Character { get; set; } = "";
    public List<string> OnReadings { get; set; } = [];
    public List<string> KunReadings { get; set; } = [];
    public List<string> Meanings { get; set; } = [];
    public short StrokeCount { get; set; }
    public short? JlptLevel { get; set; }
    public short? Grade { get; set; }
    public int? FrequencyRank { get; set; }

    public ICollection<WordKanji> WordKanjis { get; set; } = [];
}
