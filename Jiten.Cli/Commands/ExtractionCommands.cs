using System.Diagnostics;
using Jiten.Core;

namespace Jiten.Cli.Commands;

public class ExtractionCommands
{
    public async Task<bool> Extract(CliOptions options)
    {
        Stopwatch watch = new Stopwatch();
        watch.Start();
        if (options.Script == null)
        {
            Console.WriteLine("Please specify an extraction script.");
            return true;
        }

        string result = "";
        switch (options.Script)
        {
            case "epub":
                var extractor = new EbookExtractor();

                if (Directory.Exists(options.ExtractFilePath))
                {
                    string?[] files = Directory.GetFiles(options.ExtractFilePath, "*.epub",
                                                         new EnumerationOptions()
                                                         {
                                                             IgnoreInaccessible = true, RecurseSubdirectories = true
                                                         });

                    if (options.Verbose)
                        Console.WriteLine($"Found {files.Length} files to extract.");

                    var parallelOpts = new ParallelOptions() { MaxDegreeOfParallelism = options.Threads };

                    await Parallel.ForEachAsync(files, parallelOpts, async (file, _) =>
                    {
                        await File.WriteAllTextAsync(file + ".extracted.txt", await ExtractEpub(file, extractor, options), _);
                        if (options.Verbose)
                        {
                            Console.WriteLine($"Progress: {Array.IndexOf(files, file) + 1}/{files.Length}, {Array.IndexOf(files, file) * 100 / files.Length}%, {watch.ElapsedMilliseconds} ms");
                        }
                    });
                }
                else
                {
                    var file = options.ExtractFilePath;
                    await File.WriteAllTextAsync(file + ".extracted.txt", await ExtractEpub(file, extractor, options));
                }

                break;

            case "krkr":
                result = await new KiriKiriExtractor().Extract(options.ExtractFilePath, options.Verbose);
                if (options.Output != null)
                {
                    await File.WriteAllTextAsync(options.Output, result);
                }

                break;
            case "generic":
                result = await new GenericExtractor().Extract(options.ExtractFilePath, "SHIFT-JIS", options.Verbose);
                if (options.Output != null)
                {
                    await File.WriteAllTextAsync(options.Output, result);
                }

                break;
            case "generic-utf8":
                result = await new GenericExtractor().Extract(options.ExtractFilePath, "UTF-8", options.Verbose);
                if (options.Output != null)
                {
                    await File.WriteAllTextAsync(options.Output, result);
                }

                break;
            case "psb":
                result = await new PsbExtractor().Extract(options.ExtractFilePath, options.Verbose);
                if (options.Output != null)
                {
                    await File.WriteAllTextAsync(options.Output, result);
                }

                break;
            case "msc":
                result = await new MscExtractor().Extract(options.ExtractFilePath, options.Verbose);
                if (options.Output != null)
                {
                    await File.WriteAllTextAsync(options.Output, result);
                }

                break;
            case "cs2":
                result = await new Cs2Extractor().Extract(options.ExtractFilePath, options.Verbose);
                if (options.Output != null)
                {
                    await File.WriteAllTextAsync(options.Output, result);
                }

                break;

            case "mes":
                result = await new MesExtractor().Extract(options.ExtractFilePath, options.Verbose);
                if (options.Output != null)
                {
                    await File.WriteAllTextAsync(options.Output, result);
                }

                break;

            case "nexas":
                result = await new NexasExtractor().Extract(options.ExtractFilePath, options.Verbose);
                if (options.Output != null)
                {
                    await File.WriteAllTextAsync(options.Output, result);
                }

                break;

            case "whale":
                result = await new WhaleExtractor().Extract(options.ExtractFilePath, options.Verbose);
                if (options.Output != null)
                {
                    await File.WriteAllTextAsync(options.Output, result);
                }

                break;

            case "yuris":
                result = await new YuRisExtractor().Extract(options.ExtractFilePath, options.Verbose);
                if (options.Output != null)
                {
                    await File.WriteAllTextAsync(options.Output, result);
                }

                break;

            case "utf":
                result = await new UtfExtractor().Extract(options.ExtractFilePath, options.Verbose);
                if (options.Output != null)
                {
                    await File.WriteAllTextAsync(options.Output, result);
                }

                break;

            case "bgi":
                if (options.Extra == null)
                {
                    Console.WriteLine("Please specify a filter file for BGI extraction with the -x option.");
                    return true;
                }

                result = await new BgiExtractor().Extract(options.ExtractFilePath, options.Extra, options.Verbose);
                if (options.Output != null)
                {
                    await File.WriteAllTextAsync(options.Output, result);
                }

                break;

            case "txt":
                result = await new TxtExtractor().Extract(options.ExtractFilePath, "SHIFT-JIS", options.Verbose);
                if (options.Output != null)
                {
                    await File.WriteAllTextAsync(options.Output, result);
                }

                break;
            case "txt-utf8":
                result = await new TxtExtractor().Extract(options.ExtractFilePath, "UTF-8", options.Verbose);
                if (options.Output != null)
                {
                    await File.WriteAllTextAsync(options.Output, result);
                }

                break;

            case "txt-utf16":
                result = await new TxtExtractor().Extract(options.ExtractFilePath, "UTF-16", options.Verbose);
                if (options.Output != null)
                {
                    await File.WriteAllTextAsync(options.Output, result);
                }

                break;

            case "brute" or "bruteforce":
                result = await new BruteforceExtractor().Extract(options.ExtractFilePath, "SHIFT-JIS", options.Verbose);
                if (options.Output != null)
                {
                    await File.WriteAllTextAsync(options.Output, result);
                }

                break;

            case "brute-utf8" or "bruteforce-utf8":
                result = await new BruteforceExtractor().Extract(options.ExtractFilePath, "UTF-8", options.Verbose);
                if (options.Output != null)
                {
                    await File.WriteAllTextAsync(options.Output, result);
                }

                break;

            case "brute-utf16" or "bruteforce-utf16":
                result = await new BruteforceExtractor().Extract(options.ExtractFilePath, "UTF-16", options.Verbose);
                if (options.Output != null)
                {
                    await File.WriteAllTextAsync(options.Output, result);
                }

                break;

            case "mokuro":
                var directories = Directory.GetDirectories(options.ExtractFilePath!).ToList();
                for (var i = 0; i < directories.Count; i++)
                {
                    string? directory = directories[i];

                    result = await new MokuroExtractor().Extract(directory, options.Verbose);
                    if (options.Output != null)
                    {
                        await File.WriteAllTextAsync(Path.Combine(options.Output, $"Volume {(i + 1):00}.txt"), result);
                    }
                }

                break;
        }

        return false;
    }

    private async Task<string> ExtractEpub(string? file, EbookExtractor extractor, CliOptions options)
    {
        if (options.Verbose)
        {
            Console.WriteLine("=========================================");
            Console.WriteLine($"=== Processing {file} ===");
            Console.WriteLine("=========================================");
        }

        var extension = Path.GetExtension(file)?.ToLower();
        if (extension is ".epub" or ".txt")
        {
            var text = extension switch
            {
                ".epub" => await extractor.ExtractTextFromEbook(file),
                ".txt" => await File.ReadAllTextAsync(file!),
                _ => throw new NotSupportedException($"File extension {extension} not supported")
            };

            if (String.IsNullOrEmpty(text))
            {
                Console.WriteLine("ERROR: TEXT RETURNED EMPTY");
                return "";
            }

            return text;
        }

        return "";
    }
}
