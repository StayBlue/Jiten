using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Jiten.Core.Data;

public class DeckWord
{
    public long DeckWordId { get; set; }
    
    /// <summary>
    /// Corresponding deck id
    /// </summary>
    public int DeckId { get; set; }
    
    /// <summary>
    /// Corresponding word id
    /// </summary>
    public int WordId { get; set; }
    
    /// <summary>
    /// Original text before any deconjugation
    /// </summary>
    [JsonIgnore]
    [NotMapped]
    public string OriginalText { get; set; } = string.Empty;
    
    /// <summary>
    /// The index of the reading in the list of readings
    /// </summary>
    public byte ReadingIndex { get; set; }
    
    /// <summary>
    /// Number of times the exact word & reading appears in the deck
    /// </summary>
    public int Occurrences { get; set; }
    
    /// <summary>
    /// The list of conjugation strings, reconstructed from the byte indices when accessed
    /// </summary>
    // [JsonIgnore]
    [NotMapped]
    public List<string> Conjugations 
    { 
        get => _conjugationIndices.Select(ConjugationCache.GetString).ToList();
        set => _conjugationIndices = value.Select(ConjugationCache.GetOrAddByte).ToList();
    }

    [NotMapped]
    public List<PartOfSpeech> PartsOfSpeech { get; set; } = [];
    
    [NotMapped]
    public WordOrigin Origin { get; set; }

    [JsonIgnore]
    public Deck Deck { get; set; } = new();
    
    /// <summary>
    /// The conjugation bytes that reference the cached conjugation strings
    /// </summary>
    [JsonIgnore]
    [NotMapped]
    private List<byte> _conjugationIndices = new();
}
