using Jiten.Api.Dtos;
using Jiten.Api.Dtos.Requests;
using Jiten.Core.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Jiten.Api.Controllers;

public partial class AdminController
{
    [HttpGet("genre-mappings")]
    public async Task<IActionResult> GetGenreMappings([FromQuery] LinkType? provider, [FromQuery] Genre? jitenGenre,
                                                      [FromQuery] string? search)
    {
        var query = dbContext.ExternalGenreMappings.AsNoTracking().AsQueryable();

        if (provider.HasValue)
            query = query.Where(m => m.Provider == provider.Value);

        if (jitenGenre.HasValue)
            query = query.Where(m => m.JitenGenre == jitenGenre.Value);

        if (!string.IsNullOrEmpty(search))
            query = query.Where(m => m.ExternalGenreName.ToLower().Contains(search.ToLower()));

        var mappings = await query
                             .OrderBy(m => m.Provider)
                             .ThenBy(m => m.ExternalGenreName)
                             .ThenBy(m => m.JitenGenre)
                             .Select(m => new GenreMappingDto
                                          {
                                              ExternalGenreMappingId = m.ExternalGenreMappingId, Provider = m.Provider,
                                              ProviderName = m.Provider.ToString(), ExternalGenreName = m.ExternalGenreName,
                                              JitenGenre = m.JitenGenre, JitenGenreName = m.JitenGenre.ToString()
                                          })
                             .ToListAsync();

        logger.LogInformation("Admin retrieved genre mappings: Count={Count}, Provider={Provider}, JitenGenre={JitenGenre}, Search={Search}",
                              mappings.Count, provider, jitenGenre, search);

        return Ok(mappings);
    }

    [HttpPost("genre-mappings")]
    public async Task<IActionResult> CreateGenreMapping([FromBody] CreateGenreMappingRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var exists = await dbContext.ExternalGenreMappings.AnyAsync(m =>
                                                                        m.Provider == request.Provider &&
                                                                        m.ExternalGenreName.ToLower() ==
                                                                        request.ExternalGenreName.ToLower() &&
                                                                        m.JitenGenre == request.JitenGenre);

        if (exists)
            return Conflict(new { Message = "This genre mapping already exists" });

        var mapping = new ExternalGenreMapping
                      {
                          Provider = request.Provider, ExternalGenreName = request.ExternalGenreName.Trim(), JitenGenre = request.JitenGenre
                      };

        dbContext.ExternalGenreMappings.Add(mapping);
        await dbContext.SaveChangesAsync();

        logger.LogInformation("Admin created genre mapping: MappingId={MappingId}, Provider={Provider}, ExternalName={ExternalName}, JitenGenre={JitenGenre}",
                              mapping.ExternalGenreMappingId, mapping.Provider, mapping.ExternalGenreName, mapping.JitenGenre);

        return Ok(new { Message = "Genre mapping created successfully", Id = mapping.ExternalGenreMappingId });
    }

    [HttpPut("genre-mappings/{id}")]
    public async Task<IActionResult> UpdateGenreMapping(int id, [FromBody] UpdateGenreMappingRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var mapping = await dbContext.ExternalGenreMappings.FindAsync(id);
        if (mapping == null)
            return NotFound(new { Message = $"Genre mapping with ID {id} not found" });

        var exists = await dbContext.ExternalGenreMappings.AnyAsync(m =>
                                                                        m.ExternalGenreMappingId != id &&
                                                                        m.Provider == request.Provider &&
                                                                        m.ExternalGenreName.ToLower() ==
                                                                        request.ExternalGenreName.ToLower() &&
                                                                        m.JitenGenre == request.JitenGenre);

        if (exists)
            return Conflict(new { Message = "This genre mapping already exists" });

        mapping.Provider = request.Provider;
        mapping.ExternalGenreName = request.ExternalGenreName.Trim();
        mapping.JitenGenre = request.JitenGenre;
        await dbContext.SaveChangesAsync();

        logger.LogInformation("Admin updated genre mapping: MappingId={MappingId}, Provider={Provider}, ExternalName={ExternalName}, JitenGenre={JitenGenre}",
                              mapping.ExternalGenreMappingId, mapping.Provider, mapping.ExternalGenreName, mapping.JitenGenre);

        return Ok(new { Message = "Genre mapping updated successfully" });
    }

    [HttpDelete("genre-mappings/{id}")]
    public async Task<IActionResult> DeleteGenreMapping(int id)
    {
        var mapping = await dbContext.ExternalGenreMappings.FindAsync(id);
        if (mapping == null)
            return NotFound(new { Message = $"Genre mapping with ID {id} not found" });

        dbContext.ExternalGenreMappings.Remove(mapping);
        await dbContext.SaveChangesAsync();

        logger.LogInformation("Admin deleted genre mapping: MappingId={MappingId}, Provider={Provider}, ExternalName={ExternalName}",
                              mapping.ExternalGenreMappingId, mapping.Provider, mapping.ExternalGenreName);

        return Ok(new { Message = "Genre mapping deleted successfully" });
    }

    [HttpGet("genre-mappings/providers")]
    public IActionResult GetProviders()
    {
        var providers = Enum.GetValues(typeof(LinkType))
                            .Cast<LinkType>()
                            .Select(e => new { Value = (int)e, Name = e.ToString() })
                            .ToList();

        return Ok(providers);
    }
}