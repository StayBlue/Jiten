namespace Jiten.Core.Data;

public class WordSet
{
    public int SetId { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int WordCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<WordSetMember> Members { get; set; } = new List<WordSetMember>();
}
