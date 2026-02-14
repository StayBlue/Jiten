using Jiten.Core.Data;

namespace Jiten.Api.Dtos;

public class ExampleSentenceDto
{
    public required string Text { get; set; }
    public int WordPosition { get; set; }
    public int WordLength { get; set; }
    public Deck? SourceDeckParent { get; set; }
    public Deck? SourceDeck { get; set; }
} 