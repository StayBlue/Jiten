using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// Extractor for FJSys UTF
/// </summary>
public class UtfExtractor
{
    private static List<string> _stripLinesContaining = new() { "GS", "::" };

    public async Task<string> Extract(string? filePath, bool verbose)
    {
        if (string.IsNullOrEmpty(filePath))
            return "";

        string[] files;

        // TODO: Handle subfolders separately
        if (Directory.Exists(filePath))
        {
            files = Directory.GetFiles(filePath, "*.utf", SearchOption.AllDirectories);
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
            var lines = await File.ReadAllLinesAsync(file, Encoding.UTF8);

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();

                // Ignore commented lines and commands
                if (string.IsNullOrEmpty(trimmedLine) || _stripLinesContaining.Any(trimmedLine.Contains))
                    continue;

                trimmedLine = Regex.Replace(trimmedLine, "^-", "");
                trimmedLine = Regex.Replace(trimmedLine, "=", "");
                trimmedLine = Regex.Replace(trimmedLine, ">>", "");
                trimmedLine = Regex.Replace(trimmedLine, "<.{4}>", "");
                trimmedLine = trimmedLine.Trim();

                extractedText.AppendLine(trimmedLine);
            }
        }

        return extractedText.ToString();
    }
}