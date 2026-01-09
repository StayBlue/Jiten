namespace Jiten.Api.Dtos;

public class AccomplishmentVocabularyDto
{
    public List<WordDto> Words { get; set; } = [];
}

public class AggregatedWord
{
    public int WordId { get; set; }
    public byte ReadingIndex { get; set; }
    public int TotalOccurrences { get; set; }
}
