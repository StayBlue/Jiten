namespace Jiten.Api.Dtos.Requests;

public class LookupVocabularyRequest
{
    public required List<int[]> Words { get; set; }
}