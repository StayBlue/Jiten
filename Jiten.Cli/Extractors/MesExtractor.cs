using System.Text;
using System.Text.RegularExpressions;

namespace Jiten.Cli;

/// <summary>
/// Extract Silky's Plus Engine MES scenario files
/// </summary>
public class MesExtractor
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
            bool isUncrypt = false;

            foreach (var line in lines)
            {
                if (line == "#1-STR_UNCRYPT")
                {
                    isUncrypt = true;
                    continue;
                }

                if (!isUncrypt) continue;
                
                isUncrypt = false;
                var match = Regex.Match(line, @"\""([^\""]*)\""");
                if (!match.Success) continue;
                
                var message = match.Groups[1].Value.Trim();
                if (!string.IsNullOrWhiteSpace(message))
                {
                    extractedText.AppendLine(message);
                }
            }
        }

        return extractedText.ToString();
    }
}