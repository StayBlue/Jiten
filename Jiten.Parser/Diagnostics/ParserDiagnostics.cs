using System.Text.Json.Serialization;
using Jiten.Core.Data;

namespace Jiten.Parser.Diagnostics;

/// <summary>
/// Root diagnostics container for parser analysis
/// </summary>
public class ParserDiagnostics
{
    public string InputText { get; set; } = string.Empty;
    public long TotalElapsedMs { get; set; }
    public SudachiDiagnostics? Sudachi { get; set; }
    public List<TokenProcessingStage> TokenStages { get; set; } = [];
    public List<WordResult> Results { get; set; } = [];
}

/// <summary>
/// Diagnostics from Sudachi morphological analysis
/// </summary>
public class SudachiDiagnostics
{
    public double ElapsedMs { get; set; }
    public string RawOutput { get; set; } = string.Empty;
    public List<SudachiToken> Tokens { get; set; } = [];
}

/// <summary>
/// Individual token from Sudachi output
/// </summary>
public class SudachiToken
{
    public string Surface { get; set; } = string.Empty;
    public string PartOfSpeech { get; set; } = string.Empty;
    public string[] PosDetail { get; set; } = [];
    public string DictionaryForm { get; set; } = string.Empty;
    public string Reading { get; set; } = string.Empty;
    public string NormalizedForm { get; set; } = string.Empty;
}

/// <summary>
/// Records processing at a single stage of the token pipeline
/// </summary>
public class TokenProcessingStage
{
    public string StageName { get; set; } = string.Empty;
    public double ElapsedMs { get; set; }
    public int InputTokenCount { get; set; }
    public int OutputTokenCount { get; set; }
    public List<TokenModification> Modifications { get; set; } = [];
}

/// <summary>
/// Individual modification made during token processing
/// </summary>
public class TokenModification
{
    public string Type { get; set; } = string.Empty; // "merge", "split", "reclassify", "remove"
    public string[] InputTokens { get; set; } = [];
    public string? OutputToken { get; set; }
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// Final word result after all processing
/// </summary>
public class WordResult
{
    public string Text { get; set; } = string.Empty;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PartOfSpeech PartOfSpeech { get; set; }

    public string? DictionaryForm { get; set; }
    public string? Reading { get; set; }
}

/// <summary>
/// Result from running the diagnostic test suite
/// </summary>
public class TestRunResult
{
    public int TotalTests { get; set; }
    public int Passed { get; set; }
    public int Failed { get; set; }
    public List<TestFailure> Failures { get; set; } = [];
}

/// <summary>
/// Individual test failure with full diagnostics
/// </summary>
public class TestFailure
{
    public string Input { get; set; } = string.Empty;
    public string[] Expected { get; set; } = [];
    public string[] Actual { get; set; } = [];
    public ParserDiagnostics? Diagnostics { get; set; }
    public FailureAnalysis? Analysis { get; set; }
}

/// <summary>
/// Analysis of why a test failed and suggested fix
/// </summary>
public class FailureAnalysis
{
    public string Type { get; set; } = string.Empty; // "OverSegmentation", "UnderSegmentation", "TokenMismatch"
    public string Description { get; set; } = string.Empty;
    public string? ProbableCause { get; set; }
    public string? SuggestedFix { get; set; }
}
