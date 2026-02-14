using System.Text;
using System.Text.RegularExpressions;

namespace Jiten.Cli;

/// <summary>
/// Extract whale engine scripts
/// </summary>
public class WhaleExtractor
{
    public async Task<string> Extract(string? filePath, bool verbose)
    {
        if (string.IsNullOrEmpty(filePath))
            return "";

        string[] files;

        // TODO: Handle subfolders separately
        if (Directory.Exists(filePath))
        {
            files = Directory.GetFiles(filePath, "*.*", SearchOption.AllDirectories);
        }
        else
        {
            files = [filePath];
        }

        if (verbose)
        {
            Console.WriteLine($"Found {files.Length} files to extract.");

            foreach (var file in files)
            {
                Console.WriteLine(file);
            }
        }

        StringBuilder extractedText = new();
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        foreach (var file in files)
        {
            var lines = await File.ReadAllLinesAsync(file, Encoding.GetEncoding("Shift-JIS"));

            foreach (var line in lines)
            {
                if (!Regex.IsMatch(line, @"^[\p{IsHiragana}\p{IsKatakana}\p{IsCJKUnifiedIdeographs}【]"))
                    continue;

                string cleanedLine = line;

                if (line.StartsWith("【"))
                {
                    var match = Regex.Match(line, @"[「（](.+?)[」）s]");
                    if (match.Success)
                        cleanedLine = match.Groups[0].Value;
                }

                cleanedLine = cleanedLine.Replace("[n]", "");
                cleanedLine = cleanedLine.Trim();

                if (!string.IsNullOrWhiteSpace(cleanedLine))
                    extractedText.AppendLine(cleanedLine);
            }
        }

        return extractedText.ToString();
    }
}