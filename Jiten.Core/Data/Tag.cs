namespace Jiten.Core.Data;

public class Tag
{
    public int TagId { get; set; }
    public string Name { get; set; } = string.Empty;

    public ICollection<DeckTag> DeckTags { get; set; } = new List<DeckTag>();
}
