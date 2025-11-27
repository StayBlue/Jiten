using System.Text.Json.Serialization;

namespace Jiten.Core.Data;

public class DeckGenre
{
    public int DeckId { get; set; }
    public Genre Genre { get; set; }

    [JsonIgnore]
    public Deck Deck { get; set; } = null!;
}
