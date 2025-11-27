using Jiten.Api.Dtos;
using Jiten.Api.Dtos.Requests;
using Jiten.Core.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Jiten.Api.Controllers;

public partial class AdminController
{
    [HttpGet("tag-mappings")]
    public async Task<IActionResult> GetTagMappings([FromQuery] LinkType? provider, [FromQuery] int? tagId, [FromQuery] string? search)
    {
        var query = dbContext.ExternalTagMappings
                             .AsNoTracking()
                             .Include(m => m.Tag)
                             .AsQueryable();

        if (provider.HasValue)
            query = query.Where(m => m.Provider == provider.Value);

        if (tagId.HasValue)
            query = query.Where(m => m.TagId == tagId.Value);

        if (!string.IsNullOrEmpty(search))
            query = query.Where(m => m.ExternalTagName.ToLower().Contains(search.ToLower()));

        var mappings = await query
                             .OrderBy(m => m.Provider)
                             .ThenBy(m => m.ExternalTagName)
                             .ThenBy(m => m.Tag.Name)
                             .Select(m => new TagMappingDto
                                          {
                                              ExternalTagMappingId = m.ExternalTagMappingId, Provider = m.Provider,
                                              ProviderName = m.Provider.ToString(), ExternalTagName = m.ExternalTagName, TagId = m.TagId,
                                              TagName = m.Tag.Name
                                          })
                             .ToListAsync();

        return Ok(mappings);
    }

    [HttpPost("tag-mappings")]
    public async Task<IActionResult> CreateTagMapping([FromBody] CreateTagMappingRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var tagExists = await dbContext.Tags.AnyAsync(t => t.TagId == request.TagId);
        if (!tagExists)
            return BadRequest(new { Message = $"Tag with ID {request.TagId} does not exist" });

        var exists = await dbContext.ExternalTagMappings.AnyAsync(m =>
                                                                      m.Provider == request.Provider &&
                                                                      m.ExternalTagName.ToLower() == request.ExternalTagName.ToLower() &&
                                                                      m.TagId == request.TagId);

        if (exists)
            return Conflict(new { Message = "This tag mapping already exists" });

        var mapping = new ExternalTagMapping
                      {
                          Provider = request.Provider, ExternalTagName = request.ExternalTagName.Trim(), TagId = request.TagId
                      };

        dbContext.ExternalTagMappings.Add(mapping);
        await dbContext.SaveChangesAsync();

        logger.LogInformation("Admin created tag mapping: MappingId={MappingId}, Provider={Provider}, ExternalName={ExternalName}, TagId={TagId}",
                              mapping.ExternalTagMappingId, mapping.Provider, mapping.ExternalTagName, mapping.TagId);

        return Ok(new { Message = "Tag mapping created successfully", Id = mapping.ExternalTagMappingId });
    }

    [HttpPut("tag-mappings/{id}")]
    public async Task<IActionResult> UpdateTagMapping(int id, [FromBody] UpdateTagMappingRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var mapping = await dbContext.ExternalTagMappings.FindAsync(id);
        if (mapping == null)
            return NotFound(new { Message = $"Tag mapping with ID {id} not found" });

        var tagExists = await dbContext.Tags.AnyAsync(t => t.TagId == request.TagId);
        if (!tagExists)
            return BadRequest(new { Message = $"Tag with ID {request.TagId} does not exist" });

        var exists = await dbContext.ExternalTagMappings.AnyAsync(m =>
                                                                      m.ExternalTagMappingId != id &&
                                                                      m.Provider == request.Provider &&
                                                                      m.ExternalTagName.ToLower() == request.ExternalTagName.ToLower() &&
                                                                      m.TagId == request.TagId);

        if (exists)
            return Conflict(new { Message = "This tag mapping already exists" });

        mapping.Provider = request.Provider;
        mapping.ExternalTagName = request.ExternalTagName.Trim();
        mapping.TagId = request.TagId;
        await dbContext.SaveChangesAsync();

        logger.LogInformation("Admin updated tag mapping: MappingId={MappingId}, Provider={Provider}, ExternalName={ExternalName}, TagId={TagId}",
                              mapping.ExternalTagMappingId, mapping.Provider, mapping.ExternalTagName, mapping.TagId);

        return Ok(new { Message = "Tag mapping updated successfully" });
    }

    [HttpDelete("tag-mappings/{id}")]
    public async Task<IActionResult> DeleteTagMapping(int id)
    {
        var mapping = await dbContext.ExternalTagMappings.FindAsync(id);
        if (mapping == null)
            return NotFound(new { Message = $"Tag mapping with ID {id} not found" });

        dbContext.ExternalTagMappings.Remove(mapping);
        await dbContext.SaveChangesAsync();

        logger.LogInformation("Admin deleted tag mapping: MappingId={MappingId}, Provider={Provider}, ExternalName={ExternalName}",
                              mapping.ExternalTagMappingId, mapping.Provider, mapping.ExternalTagName);

        return Ok(new { Message = "Tag mapping deleted successfully" });
    }

    [HttpGet("tag-mappings/summary")]
    public async Task<IActionResult> GetTagMappingSummary()
    {
        var mappings = await dbContext.ExternalTagMappings.AsNoTracking().ToListAsync();

        var summary = new TagMappingSummaryDto
                      {
                          TotalMappings = mappings.Count, MappingsByProvider = mappings
                                                                               .GroupBy(m => m.Provider.ToString())
                                                                               .ToDictionary(g => g.Key, g => g.Count())
                      };

        return Ok(summary);
    }

    [HttpGet("tags")]
    public async Task<IActionResult> GetTags()
    {
        var tags = await dbContext.Tags
                                  .AsNoTracking()
                                  .OrderBy(t => t.Name)
                                  .Select(t => new TagDto
                                               {
                                                   TagId = t.TagId,
                                                   Name = t.Name
                                               })
                                  .ToListAsync();

        return Ok(tags);
    }

    [HttpPost("tags")]
    public async Task<IActionResult> CreateTag([FromBody] CreateTagRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var exists = await dbContext.Tags.AnyAsync(t => t.Name.ToLower() == request.Name.ToLower());
        if (exists)
            return Conflict(new { Message = "A tag with this name already exists" });

        var tag = new Tag { Name = request.Name.Trim() };
        dbContext.Tags.Add(tag);
        await dbContext.SaveChangesAsync();

        logger.LogInformation("Admin created tag: TagId={TagId}, Name={Name}", tag.TagId, tag.Name);
        return Ok(new { Message = "Tag created successfully", Id = tag.TagId });
    }

    [HttpPut("tags/{id}")]
    public async Task<IActionResult> UpdateTag(int id, [FromBody] UpdateTagRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var tag = await dbContext.Tags.FindAsync(id);
        if (tag == null)
            return NotFound(new { Message = $"Tag with ID {id} not found" });

        var exists = await dbContext.Tags.AnyAsync(t => t.TagId != id && t.Name.ToLower() == request.Name.ToLower());
        if (exists)
            return Conflict(new { Message = "A tag with this name already exists" });

        tag.Name = request.Name.Trim();
        await dbContext.SaveChangesAsync();

        logger.LogInformation("Admin updated tag: TagId={TagId}, Name={Name}", tag.TagId, tag.Name);
        return Ok(new { Message = "Tag updated successfully" });
    }

    [HttpDelete("tags/{id}")]
    public async Task<IActionResult> DeleteTag(int id)
    {
        var tag = await dbContext.Tags.FindAsync(id);
        if (tag == null)
            return NotFound(new { Message = $"Tag with ID {id} not found" });

        dbContext.Tags.Remove(tag);
        await dbContext.SaveChangesAsync();

        logger.LogInformation("Admin deleted tag: TagId={TagId}, Name={Name}", tag.TagId, tag.Name);
        return Ok(new { Message = "Tag deleted successfully" });
    }

    [HttpGet("tags/{id}/usage")]
    public async Task<IActionResult> GetTagUsage(int id)
    {
        var tag = await dbContext.Tags.FindAsync(id);
        if (tag == null)
            return NotFound(new { Message = $"Tag with ID {id} not found" });

        var deckCount = await dbContext.Set<DeckTag>().CountAsync(dt => dt.TagId == id);
        var mappingCount = await dbContext.ExternalTagMappings.CountAsync(m => m.TagId == id);

        return Ok(new TagUsageDto { DeckCount = deckCount, MappingCount = mappingCount });
    }
}