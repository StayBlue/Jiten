using Jiten.Core;
using Jiten.Core.Data;

namespace Jiten.Parser;

public class WordInfo
{
    public string Text { get; set; } = string.Empty;
    public PartOfSpeech PartOfSpeech { get; set; }
    public PartOfSpeechSection PartOfSpeechSection1 { get; set; }
    public PartOfSpeechSection PartOfSpeechSection2 { get; set; }
    public PartOfSpeechSection PartOfSpeechSection3 { get; set; }
    public string NormalizedForm { get; set; } = string.Empty;
    public string DictionaryForm { get; set; } = string.Empty;
    public string Reading { get; set; } = string.Empty;
    public bool IsInvalid { get; set; }
    public bool IsPersonNameContext { get; set; }
    public int? PreMatchedWordId { get; set; }

    public WordInfo(){}

    public WordInfo(WordInfo other)
    {
        Text = other.Text;
        PartOfSpeech = other.PartOfSpeech;
        PartOfSpeechSection1 = other.PartOfSpeechSection1;
        PartOfSpeechSection2 = other.PartOfSpeechSection2;
        PartOfSpeechSection3 = other.PartOfSpeechSection3;
        NormalizedForm = other.NormalizedForm;
        DictionaryForm = other.DictionaryForm;
        Reading = other.Reading;
        IsInvalid = other.IsInvalid;
        IsPersonNameContext = other.IsPersonNameContext;
        PreMatchedWordId = other.PreMatchedWordId;
    }

    public WordInfo(string sudachiLine)
    {
        // Parse tab-separated Sudachi output without Regex.Split
        // Format: Text\tPOS\tNormalizedForm\tDictionaryForm\t?\tReading
        var span = sudachiLine.AsSpan();

        // Find first 6 tab positions
        Span<int> tabPositions = stackalloc int[6];
        int tabCount = 0;
        for (int i = 0; i < span.Length && tabCount < 6; i++)
        {
            if (span[i] == '\t')
            {
                tabPositions[tabCount++] = i;
            }
        }

        if (tabCount < 5)
        {
            IsInvalid = true;
            return;
        }

        // Extract Text (before first tab)
        Text = span[..tabPositions[0]].ToString();

        // Extract and parse POS (between first and second tab)
        var posSpan = span[(tabPositions[0] + 1)..tabPositions[1]];

        // Find first 4 commas in POS
        Span<int> commaPositions = stackalloc int[4];
        int commaCount = 0;
        for (int i = 0; i < posSpan.Length && commaCount < 4; i++)
        {
            if (posSpan[i] == ',')
            {
                commaPositions[commaCount++] = i;
            }
        }

        if (commaCount < 3)
        {
            IsInvalid = true;
            return;
        }

        PartOfSpeech = posSpan[..commaPositions[0]].ToString().ToPartOfSpeech();
        PartOfSpeechSection1 = posSpan[(commaPositions[0] + 1)..commaPositions[1]].ToString().ToPartOfSpeechSection();
        PartOfSpeechSection2 = posSpan[(commaPositions[1] + 1)..commaPositions[2]].ToString().ToPartOfSpeechSection();
        PartOfSpeechSection3 = (commaCount >= 4
            ? posSpan[(commaPositions[2] + 1)..commaPositions[3]]
            : posSpan[(commaPositions[2] + 1)..]).ToString().ToPartOfSpeechSection();

        // Extract remaining fields
        NormalizedForm = span[(tabPositions[1] + 1)..tabPositions[2]].ToString();
        DictionaryForm = span[(tabPositions[2] + 1)..tabPositions[3]].ToString();
        Reading = tabCount >= 6
            ? span[(tabPositions[4] + 1)..tabPositions[5]].ToString()
            : span[(tabPositions[4] + 1)..].ToString();
    }
    
    public bool HasPartOfSpeechSection(PartOfSpeechSection section)
    {
        return PartOfSpeechSection1 == section || PartOfSpeechSection2 == section || PartOfSpeechSection3 == section;
    }
}
