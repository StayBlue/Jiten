namespace Jiten.Core.Data.Providers.Anilist;

public class AnilistMedia
{
    public int Id { get; set; }
    public int? IdMal { get; set; }
    public string? Description { get; set; }
    public List<string> Genres { get; set; } = new ();
    public List<AnilistTag> Tags { get; set; } = new ();
    public required AnilistTitle Title { get; set; }
    public required AnilistDate StartDate { get; set; }
    public required string BannerImage { get; set; }
    public required AnilistImage CoverImage { get; set; }
    public List<string> Synonyms { get; set; } = new ();
    public int? AverageScore { get; set; }
    public int? MeanScore { get; set; }
    public bool IsAdult { get; set; }
    public string? CountryOfOrigin { get; set; }
    public AnilistRelations? Relations { get; set; }

    public DateTime ReleaseDate => new(
                                       StartDate.Year.GetValueOrDefault(1),
                                       StartDate.Month.GetValueOrDefault(1),
                                       StartDate.Day.GetValueOrDefault(1)
                                      );
}