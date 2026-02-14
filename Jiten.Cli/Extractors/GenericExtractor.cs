using System.Text;
using System.Text.RegularExpressions;

namespace Jiten.Cli;

public class GenericExtractor
{
    private List<string> _stripLinesStartingWith = new()
                                                   {
                                                       "*",
                                                       "$",
                                                       "{",
                                                       "}",
                                                       ";",
                                                       "tf",
                                                       "if",
                                                       "var",
                                                       "for",
                                                       "sf",
                                                       "kag",
                                                       "extra",
                                                       "dispose",
                                                       "function",
                                                       "/",
                                                       "else",
                                                       "class",
                                                       "return",
                                                       "sys",
                                                       "nvl",
                                                       "[eval",
                                                       "[bg",
                                                       // "【",
                                                       "@",
                                                       "<",
                                                       "^",
                                                       "none",
                                                       "\\",
                                                       "\u25cb",
                                                       "\u25cf",
                                                       "#",
                                                   };

    public async Task<string> Extract(string? filePath, string encoding, bool verbose)
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
            var lines = await File.ReadAllLinesAsync(file, Encoding.GetEncoding(encoding));

            foreach (var line in lines)
            {
                string trimmedLine = line.TrimStart();
                // This will clean most of the text, but there might still be some JS garbage left, easy to clean manually
                if (string.IsNullOrEmpty(trimmedLine) || _stripLinesStartingWith.Any(trimmedLine.StartsWith))
                    continue;

                // Remove the readings from ruby text and only keep the kanji
                trimmedLine = Regex.Replace(trimmedLine, @"\[(.*?)'.*?\]", "$1", RegexOptions.None);

                // Filter [tags]
                trimmedLine = Regex.Replace(trimmedLine, @"\[.*?\]", "", RegexOptions.None);

                if (!string.IsNullOrWhiteSpace(trimmedLine))
                    extractedText.AppendLine(trimmedLine);
            }
        }

        return extractedText.ToString();
    }
}