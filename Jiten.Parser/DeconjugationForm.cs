namespace Jiten.Parser;

public class DeconjugationForm
{
    public List<string> Tags { get; }
    public string Text { get; }
    public string OriginalText { get; }
    public HashSet<string> SeenText { get; }
    public List<string> Process { get; }

    private readonly int _hashCode;


    public DeconjugationForm(string text, string originalText, List<string> tags, HashSet<string> seenText, List<string> process)
    {
        Text = text;
        OriginalText = originalText;
        Tags = tags;
        SeenText = seenText;
        Process = process;

        var hash = new HashCode();
        hash.Add(Text, StringComparer.Ordinal);
        hash.Add(OriginalText, StringComparer.Ordinal);
        foreach (var tag in Tags)
        {
            hash.Add(tag, StringComparer.Ordinal);
        }

        foreach (var p in Process)
        {
            hash.Add(p, StringComparer.Ordinal);
        }

        // Use XOR for SeenText since HashSet iteration order is non-deterministic
        // but Equals uses SetEquals (order-independent). XOR is commutative.
        int seenTextHash = 0;
        foreach (var st in SeenText)
        {
            seenTextHash ^= StringComparer.Ordinal.GetHashCode(st);
        }
        hash.Add(seenTextHash);

        _hashCode = hash.ToHashCode();
    }

    public override bool Equals(object? obj)
    {
        if (obj == null || GetType() != obj.GetType())
            return false;

        DeconjugationForm other = (DeconjugationForm)obj;
        return Text == other.Text &&
               OriginalText == other.OriginalText &&
               Tags.SequenceEqual(other.Tags) &&
               SeenText.SetEquals(other.SeenText) &&
               Process.SequenceEqual(other.Process);
    }

    public override int GetHashCode()
    {
        return _hashCode;
    }
}