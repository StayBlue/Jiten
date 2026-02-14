using Jiten.Core.Data.JMDict;
using Jiten.Core.Data;

namespace Jiten.Api.Dtos;

public class WordDto
{
    public int WordId { get; set; }
    public WordFormDto MainReading { get; set; } = new();
    public List<WordFormDto> AlternativeReadings { get; set; } = new();
    public List<string> PartsOfSpeech { get; set; } = new();
    public List<DefinitionDto> Definitions { get; set; } = new();
    public int Occurrences { get; set; }
    public List<int>? PitchAccents { get; set; }
    public List<KnownState> KnownStates { get; set; } = new();
}

public class WordFormDto
{
    public string Text { get; set; } = "";
    public JmDictReadingType ReadingType { get; set; }
    public byte ReadingIndex { get; set; }
    public int FrequencyRank { get; set; }
    public double FrequencyPercentage { get; set; }
    public int UsedInMediaAmount { get; set; }
    public Dictionary<int, int> UsedInMediaAmountByType { get; set; } = new();
}