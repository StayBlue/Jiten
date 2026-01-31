namespace Jiten.Core.Data;

public class WordSetMember
{
    public int SetId { get; set; }
    public int WordId { get; set; }
    public short ReadingIndex { get; set; }
    public int Position { get; set; }

    public WordSet Set { get; set; } = null!;
}
