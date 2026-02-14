namespace Jiten.Core.Data.Providers.Anilist;

public class AnilistTag
{
    public string Name { get; set; } = default!;
    public int Rank { get; set; }
    public bool IsMediaSpoiler { get; set; }
}