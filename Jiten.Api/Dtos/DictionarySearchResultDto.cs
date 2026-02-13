namespace Jiten.Api.Dtos;

public class DictionarySearchResultDto
{
    public string Query { get; set; } = "";
    public string QueryType { get; set; } = "";
    public List<DictionaryEntryDto> Results { get; set; } = [];
    public List<DictionaryEntryDto> DictionaryResults { get; set; } = [];
    public bool HasMore { get; set; }
}

public class DictionaryEntryDto
{
    public int WordId { get; set; }
    public byte ReadingIndex { get; set; }
    public string Text { get; set; } = "";
    public string? PrimaryKanjiText { get; set; }
    public List<string> PartsOfSpeech { get; set; } = [];
    public List<string> Meanings { get; set; } = [];
    public int FrequencyRank { get; set; }
}
