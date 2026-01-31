using System;

namespace Jiten.Core.Data;

public class UserCoverageChunk
{
    public string UserId { get; set; } = string.Empty;
    public short Metric { get; set; }
    public int ChunkIndex { get; set; }
    public short[] Values { get; set; } = Array.Empty<short>();
    public DateTime ComputedAt { get; set; }
}

