using Jiten.Core.Data.JMDict;

namespace Jiten.Api.Dtos;

public class MissingFuriganaDto
{
    public int WordId { get; set; }
    public short ReadingIndex { get; set; }
    public string Text { get; set; } = "";
    public string RubyText { get; set; } = "";
    public JmDictFormType FormType { get; set; }
    public List<string> PartsOfSpeech { get; set; } = [];
    public List<WordFormSummary> AllForms { get; set; } = [];
}

public class WordFormSummary
{
    public short ReadingIndex { get; set; }
    public string Text { get; set; } = "";
    public string RubyText { get; set; } = "";
    public JmDictFormType FormType { get; set; }
}

public class MissingFuriganaPaginatedResponse
{
    public List<MissingFuriganaDto> Items { get; set; } = [];
    public int TotalCount { get; set; }
}
