using System.Text;
using System.Text.RegularExpressions;

namespace Jiten.Cli;

public class BruteforceExtractor
{
    public async Task<string> Extract(string? filePath, string encoding, bool verbose)
    {
        string?[] files = [];

        // TODO: Handle subfolders separately
        if (Directory.Exists(filePath))
        {
            files = Directory.GetFiles(filePath, "*.*", SearchOption.AllDirectories);
        }
        else
        {
            files = new string[] { filePath };
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

        string regexPattern = @"[" +
                              @"…―」”" +
                              @"\p{IsHiragana}" + // Hiragana
                              @"\p{IsKatakana}" + // Katakana
                              @"\p{IsCJKUnifiedIdeographs}" + // Basic Kanji
                              @"\p{IsCJKUnifiedIdeographsExtensionA}" + // Extended Kanji Set A
                              @"\p{IsCJKSymbolsandPunctuation}" + // Japanese punctuation (note: "and" not "And")
                              @"\p{IsHalfwidthandFullwidthForms}" + // Full-width forms (includes digits and romaji)
                              @"\p{IsCJKCompatibilityForms}" + // CJK compatibility forms
                              @"\p{IsEnclosedCJKLettersandMonths}" + // Enclosed CJK letters (note: "and" not "And")
                              @"]{2,}"; // At least 2 consecutive characters
        Regex regex = new Regex(regexPattern);


        foreach (var file in files)
        {
            using (var streamReader = new StreamReader(file, Encoding.GetEncoding(encoding)))
            {
                while (await streamReader.ReadLineAsync() is { } line)
                {
                    string trimmedLine = line.Trim();

                    bool atLeastOneMatch = false;
                    if (!string.IsNullOrWhiteSpace(trimmedLine))
                    {
                        MatchCollection matches = regex.Matches(trimmedLine);

                        foreach (Match match in matches)
                        {
                            atLeastOneMatch = true;
                            extractedText.Append(match.Value);
                        }
                    }

                    if (atLeastOneMatch)
                        extractedText.AppendLine();
                }
            }
        }

        return extractedText.ToString();
    }
}