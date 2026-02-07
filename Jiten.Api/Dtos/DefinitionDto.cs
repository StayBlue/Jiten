namespace Jiten.Api.Dtos;

public class DefinitionDto
{
    public int Index { get; set; }
    public List<string> Meanings { get; set; } = new();
    public List<string> PartsOfSpeech { get; set; } = new();
    public List<string>? Pos { get; set; }
    public List<string>? Misc { get; set; }
    public List<string>? Field { get; set; }
    public List<string>? Dial { get; set; }
    public List<short>? RestrictedToReadingIndices { get; set; }
}