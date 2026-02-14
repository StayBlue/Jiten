using System.Text;
using System.Text.RegularExpressions;

namespace Jiten.Cli;

/// <summary>
/// Extract Stuff Script Engine's MSC scenario files
/// </summary>
public class MscExtractor
{
    public async Task<string> Extract(string? filePath, bool verbose)
    {
        if (string.IsNullOrEmpty(filePath))
            return "";

        string[] files;

        // TODO: Handle subfolders separately
        if (Directory.Exists(filePath))
        {
            files = Directory.GetFiles(filePath, "*.txt", SearchOption.AllDirectories);
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
            bool isMessage = false;

            foreach (var line in lines)
            {
                if (line == "#1-MESSAGE")
                {
                    isMessage = true;
                    continue;
                }

                if (!isMessage) continue;

                isMessage = false;
                var match = Regex.Match(line, @"'([^']*)'");
                var message = match.Groups[1].Value;

                message = message.Replace("']", "").Replace("_n_r", "").Replace("'", "").Replace(@"\u3000", "").Replace("_n", "").Replace(@"\\n", "");
                message = Regex.Replace(message, @"\【.*?\】", "", RegexOptions.None);
                message = Regex.Replace(message, @"\“<(.*?),.*?>\”", "$1", RegexOptions.None);
                message = Regex.Replace(message, @"<(.*?),.*?>", "$1", RegexOptions.None);
                message = Regex.Replace(message, @"\[.*?\]", "", RegexOptions.None);
                
                if (!string.IsNullOrWhiteSpace(message))
                    extractedText.AppendLine(message);
            }
        }

        return extractedText.ToString();
    }
}