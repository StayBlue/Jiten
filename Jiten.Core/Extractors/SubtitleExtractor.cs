using System.Text;
using System.Text.RegularExpressions;
using SubtitlesParser.Classes.Parsers;

namespace Jiten.Core;

public partial class SubtitleExtractor
{
    public static readonly string[] SupportedExtensions = [".ass", ".srt", ".ssa"];
    // ASS styles/markers to skip (CN lines)
    public static readonly string[] ChineseLineMarkers =
    [
        "cn",
        "720通用注釋",
        "1080通用注釋",
        "通用720中文",
        "單語720中文",
        "通用1080中文",
        "單語1080中文",
        "花語製作通用",
        "CN",
        "CN2",
        "OPCN",
        "EDCN",
        "SCR",
        "STAFF",
        "CHI",
        "EDSC"
    ];

    /// <summary>
    /// Extract plain text from a subtitle file (.ass, .srt, .ssa)
    /// </summary>
    public async Task<string> Extract(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();

        // Preprocess ASS files (remove comments, cn lines)
        if (extension == ".ass")
        {
            filePath = await PreprocessAssFile(filePath);
        }

        // Parse subtitle file
        var parser = new SubParser();
        await using var fileStream = File.OpenRead(filePath);
        var items = parser.ParseStream(fileStream, Encoding.UTF8);

        // Extract and clean lines
        var lines = items.SelectMany(it => it.PlaintextLines).ToList();
        for (int i = lines.Count - 1; i >= 0; i--)
        {
            // Remove ruby/furigana annotations in both half-width and full-width parentheses
            lines[i] = RubyPattern().Replace(lines[i], "");
            lines[i] = FullWidthRubyPattern().Replace(lines[i], "");

            if (string.IsNullOrWhiteSpace(lines[i]))
            {
                lines.RemoveAt(i);
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// Preprocess ASS file by removing comments and chinese lines, converting to SSA
    /// </summary>
    private async Task<string> PreprocessAssFile(string filePath)
    {
        var ssaPath = Path.ChangeExtension(filePath, ".ssa");
        var lines = await File.ReadAllLinesAsync(filePath);
        // Drop comments and CN-marked lines
        var filteredLines = lines
            .Where(line =>
                !line.TrimStart().StartsWith(';') &&
                !line.StartsWith("Comment:", StringComparison.OrdinalIgnoreCase) &&
                !ChineseLineMarkers.Any(marker => line.Contains(marker, StringComparison.Ordinal)))
            .ToList();
        await File.WriteAllLinesAsync(ssaPath, filteredLines);
        return ssaPath;
    }

    [GeneratedRegex(@"\((.*?)\)")]
    private static partial Regex RubyPattern();

    [GeneratedRegex(@"（(.*?)）")]
    private static partial Regex FullWidthRubyPattern();
}
