using System.Text;
using System.Text.RegularExpressions;

namespace Jiten.Cli;

public class YuRisExtractor
{
    /// <summary>
    ///  
    /// </summary>
    /// <param name="filePath"></param>
    /// <param name="verbose"></param>
    /// <returns></returns>
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

        foreach (var file in files)
        {
            var lines = await File.ReadAllLinesAsync(file, Encoding.UTF8);

            foreach (var line in lines)
            {
                var filteredLine = line;
                filteredLine = Regex.Replace(filteredLine, @"^[^「]*「", "「", RegexOptions.None);

                if (!string.IsNullOrWhiteSpace(filteredLine))
                    extractedText.AppendLine(filteredLine);
            }
        }

        return extractedText.ToString();
    }
}