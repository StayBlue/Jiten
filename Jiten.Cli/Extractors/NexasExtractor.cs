using System.Text;
using System.Text.RegularExpressions;

namespace Jiten.Cli;

/// <summary>
/// Extract Giga's NeXaS scenario files
/// </summary>
public class NexasExtractor
{
    private List<string> _regexFilters = [
        @"^ことね$",
        @"^達観$",
        @"^海希$",
        @"^千尋$",
        @"^結$",
        @"^根津$",
        @"@v\d+",
        @"@n",
        @"@m\d+",
        @"@t\d+",
        @"@s\d+",
        @"@k",
        @"@e",
        @"@d",
        @"^(?!.*[\u3040-\u30FF\u4E00-\u9FFF]).*$",
        @"@h[a-zA-Z\d_]+(?![\u3040-\u30FF\u4E00-\u9FFF])",
        @"2",
        @"@h",
        @"@r",
        @"@",
        ];
    
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
            bool keepLine = false;
            
            foreach (var line in lines)
            {
                if (line.StartsWith("\u25cb"))
                {
                    keepLine = true;
                    continue;
                }
                
                if (!keepLine) continue;
                
                keepLine= false;
                
                string cleanedLine = line;
                
                // Filter out name only lines and script lines
                foreach (var filter in _regexFilters)
                {
                    cleanedLine = Regex.Replace(cleanedLine, filter, "", RegexOptions.None);
                }

                if (!string.IsNullOrWhiteSpace(cleanedLine))
                    extractedText.AppendLine(cleanedLine);
            }
        }

        return extractedText.ToString();
    }
}