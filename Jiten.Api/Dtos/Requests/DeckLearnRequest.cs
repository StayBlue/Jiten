namespace Jiten.Api.Dtos.Requests;

public class DeckLearnRequest
{
    public DeckDownloadType DownloadType { get; set; }
    public DeckOrder Order { get; set; }
    public int MinFrequency { get; set; }
    public int MaxFrequency { get; set; }
    public bool ExcludeKana { get; set; }
    public bool ExcludeMatureMasteredBlacklisted { get; set; }
    public bool ExcludeAllTrackedWords { get; set; }
    public float? TargetPercentage { get; set; }
    public string VocabularyState { get; set; } = "mastered";
}
