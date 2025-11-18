using Jiten.Core;
using Jiten.Core.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Jiten.Api.Controllers;

[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("api/frequency-list")]
public class FrequencyListController(JitenDbContext context, ILogger<FrequencyListController> logger) : ControllerBase
{
    /// <summary>
    /// DON'T TOUCH THIS
    /// IT'S BEING USED BY THE YOMITAN EXTENSION
    /// </summary>
    /// <param name="mediaType"></param>
    /// <param name="downloadType"></param>
    /// <returns></returns>
    [HttpGet("download")]
    [EnableRateLimiting("download")]
    public async Task<IResult> GetFrequencyList([FromQuery] MediaType? mediaType = null, string downloadType = "yomitan")
    {
        var configuration = HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        string path = Path.Join(configuration["StaticFilesPath"], "yomitan");

        string fileName, filePath;
        byte[] bytes;
        switch (downloadType)
        {
            case "yomitan":
                fileName = mediaType == null ? "jiten_freq_global.zip" : $"jiten_freq_{mediaType.ToString()}.zip";
                filePath = Path.Join(path, fileName);

                if (!System.IO.File.Exists(filePath))
                {
                    return Results.NotFound($"Frequency list not found: {fileName}");
                }

                bytes = await System.IO.File.ReadAllBytesAsync(filePath);
                logger.LogInformation("User downloaded frequency list: MediaType={MediaType}, DownloadType={DownloadType}, FileName={FileName}",
                                      mediaType?.ToString() ?? "global", downloadType, fileName);
                return Results.File(bytes, "application/zip", fileName);

            case "csv":
            default:
                fileName = mediaType == null ? "jiten_freq_global.csv" : $"jiten_freq_{mediaType.ToString()}.csv";
                filePath = Path.Join(path, fileName);

                if (!System.IO.File.Exists(filePath))
                {
                    return Results.NotFound($"Frequency list not found: {fileName}");
                }

                bytes = await System.IO.File.ReadAllBytesAsync(filePath);
                logger.LogInformation("User downloaded frequency list: MediaType={MediaType}, DownloadType={DownloadType}, FileName={FileName}",
                                      mediaType?.ToString() ?? "global", downloadType, fileName);
                return Results.File(bytes, "text/csv", fileName);
        }
    }

    /// <summary>
    /// DON'T TOUCH THIS
    /// IT'S BEING USED BY THE YOMITAN EXTENSION
    /// </summary>
    /// <param name="mediaType"></param>
    /// <param name="downloadType"></param>
    /// <returns></returns>
    [HttpGet("index")]
    public async Task<IResult> GetFrequencyListIndex([FromQuery] MediaType? mediaType = null)
    {
        var configuration = HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        string path = Path.Join(configuration["StaticFilesPath"], "yomitan");

        string fileName = mediaType == null ? "jiten_freq_global.json" : $"jiten_freq_{mediaType.ToString()}.json";
        string filePath = Path.Join(path, fileName);

        if (!System.IO.File.Exists(filePath))
        {
            return Results.NotFound($"Frequency list not found: {fileName}");
        }

        byte[] bytes = await System.IO.File.ReadAllBytesAsync(filePath);
        return Results.File(bytes, "text/json", fileName);
    }
}