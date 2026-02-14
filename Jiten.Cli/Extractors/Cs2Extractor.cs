using System.Text;
using System.Text.RegularExpressions;

namespace Jiten.Cli;

/// <summary>
/// Extract CatSystem2's scenario files
/// </summary>
public class Cs2Extractor
{
    private List<string> _stripLinesStartingWith = new()
                                                   {
                                                       "#",
                                                       "undo",
                                                       "bg",
                                                       "cg",
                                                       "fg",
                                                       "eg",
                                                       "fw",
                                                       "pl",
                                                       "part",
                                                       "plac",
                                                       "wait",
                                                       "auto",
                                                       "draw",
                                                       "wipe",
                                                       "frame",
                                                       "novel",
                                                       "view",
                                                       "se",
                                                       "rdraw",
                                                       "rwipe",
                                                       "next",
                                                       "pcm",
                                                       "wbreak",
                                                       "if",
                                                       "fselect",
                                                       "fes",
                                                       "wiat",
                                                       "str",
                                                       "jump",
                                                       "movie",
                                                       "title",
                                                       "end_of",
                                                       "init",
                                                       "sysbtn",
                                                       "keyskip",
                                                   };

    private List<string> _regexFilter = new()
                                        {
                                            @"\\r",
                                            @"\\pc",
                                            @"\\p",
                                            @"\\@",
                                            @"\\n",
                                            @"\\fl",
                                            @"\\f\d+;",
                                            @"\\w0;",
                                            @"\\fs",
                                            @"\\fn",
                                            @"\\m",
                                            "・"
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
                // replace whatever is in regex filters with a regex
                string cleanedLine = line;
                foreach (var filter in _regexFilter)
                {
                    cleanedLine = Regex.Replace(cleanedLine, filter, "", RegexOptions.Multiline);
                }

                string trimmedLine = cleanedLine.Trim();
                // This will clean most of the text, but there might still be some JS garbage left, easy to clean manually
                if (string.IsNullOrEmpty(trimmedLine) || _stripLinesStartingWith.Any(trimmedLine.StartsWith))
                    continue;

                // remove code from choices
                trimmedLine = Regex.Replace(trimmedLine, @"^\d+\s+\S+\s+", "");

                // Remove the readings from ruby text and only keep the kanji
                trimmedLine = Regex.Replace(trimmedLine, @"\[(.+?)/.*?\]", "$1");

                // Filter speaker name
                trimmedLine = Regex.Replace(trimmedLine, @"^.*?\t(?=\「.*?\」$)", "");
                trimmedLine = Regex.Replace(trimmedLine, @"^.*?\t(?=\（.*?\）$)", "");
                trimmedLine = Regex.Replace(trimmedLine, @"^.*?\t(?=\『.*?\』$)", "");
                trimmedLine = Regex.Replace(trimmedLine, @"^.*?\t(?=\「.*?\」$)", "");

                if (!string.IsNullOrWhiteSpace(trimmedLine))
                    extractedText.AppendLine(trimmedLine);
            }
        }

        return extractedText.ToString();
    }
}