using System.Text;
using System.Text.RegularExpressions;

namespace Jiten.Cli;

public class KiriKiriExtractor
{
    private static List<string> _stripLinesStartingWith = new()
    {
        ";",
        "@",
        "}",
        "tf",
        "if",
        "kag",
        "f.",
        "[",
        "*"
    };
    
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
                var trimmedLine = line.Trim();

                // Ignore commented lines and commands
                if (string.IsNullOrEmpty(trimmedLine) || _stripLinesStartingWith.Any(trimmedLine.StartsWith))
                    continue;

                var match = Regex.Match(line, @"^\t?(?<tabbed>.*)$|(?<ending>.*)\[(r|np|p|plc)\]$");
                if (!match.Success) continue;

                string editedLine = match.Groups["tabbed"].Success ? match.Groups["tabbed"].Value : match.Groups["ending"].Value;

                // Remove the readings from ruby text and only keep the kanji
                editedLine = Regex.Replace(editedLine, @"\[(.*?)'.*?\]", "$1", RegexOptions.None);

                // Filter [tags]
                editedLine = Regex.Replace(editedLine, @"\[.*?\]", "", RegexOptions.None);

                extractedText.AppendLine(editedLine);
            }
        }

        return extractedText.ToString();
    }
}