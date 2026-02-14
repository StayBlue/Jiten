using System.Text.Json.Serialization;

namespace Jiten.Core.Data.Providers.Vndb;

public class VndbReleaseResult
{
    public string Id { get; set; } = default!;
    public int? MinAge { get; set; }
    [JsonPropertyName("has_ero")]
    public bool? HasEro { get; set; }

    public List<VndbReleaseResultVn> Vns { get; set; } = [];
    public List<VndbReleaseResultLanguage> Languages { get; set; } = [];

}

public class VndbReleaseResultVn
{
    public string Rtype { get; set; } = default!;
}

public class VndbReleaseResultLanguage
{
    public string Lang { get; set; } = default!;
}