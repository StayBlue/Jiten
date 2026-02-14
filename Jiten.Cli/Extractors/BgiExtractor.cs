using System.Text;
using System.Text.RegularExpressions;

namespace Jiten.Cli;

public class BgiExtractor
{
    /// <summary>
    /// 
    /// </summary>
    /// <param name="filePath"></param>
    /// <param name="filterNamesPath">A file with a list of names to filter out, one per line</param>
    /// <param name="verbose"></param>
    /// <returns></returns>
    public async Task<string> Extract(string? filePath, string? filterNamesPath, bool verbose)
    {
        if (string.IsNullOrEmpty(filePath) || string.IsNullOrEmpty(filterNamesPath))
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

        List<string> names = (await File.ReadAllLinesAsync(filterNamesPath, Encoding.UTF8)).ToList();

        StringBuilder extractedText = new();

        foreach (var file in files)
        {
            var lines = await File.ReadAllLinesAsync(file, Encoding.UTF8);

            foreach (var line in lines)
            {
                var match = Regex.Match(line, @"◇.+◇(.+)$");
                if (!match.Success) continue;
                var message = match.Groups[1].Value;

                // Filter out name only lines
                if (names.Contains(message.Trim()))
                    continue;

                if (message.StartsWith("_"))
                    continue;
                
                // Filter [tags] and {tags}
                message = Regex.Replace(message, @"\[.*?\]", "", RegexOptions.None);
                message = Regex.Replace(message, @"{.*?}", "", RegexOptions.None);

                // Filter ( 、) (KKK)
                message = message.Replace("( 、)", "");

                // message = message.Replace("']", "").Replace("_n_r", "").Replace("'", "").Replace(@"\u3000", "").Replace("_n", "")
                //                  .Replace(@"\\n", "");
                // message = Regex.Replace(message, @"\【.*?\】", "", RegexOptions.None);
                // message = Regex.Replace(message, @"\“<(.*?),.*?>\”", "$1", RegexOptions.None);
                // message = Regex.Replace(message, @"<(.*?),.*?>", "$1", RegexOptions.None);
                // message = Regex.Replace(message, @"\[.*?\]", "", RegexOptions.None);

                if (!string.IsNullOrWhiteSpace(message))
                    extractedText.AppendLine(message);
            }
        }

        return extractedText.ToString();
    }
}