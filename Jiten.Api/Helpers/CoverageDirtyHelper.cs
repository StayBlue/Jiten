using Jiten.Core;
using Microsoft.EntityFrameworkCore;

namespace Jiten.Api.Helpers;

public static class CoverageDirtyHelper
{
    public static async Task MarkCoverageDirty(UserDbContext userContext, string userId, DateTime? dirtyAt = null)
    {
        var now = dirtyAt ?? DateTime.UtcNow;
        var userGuid = Guid.Parse(userId);

        var rows = await userContext.Database.ExecuteSqlRawAsync("""
            UPDATE "user"."UserMetadatas"
            SET "CoverageDirty" = TRUE,
                "CoverageDirtyAt" = {1}::timestamptz
            WHERE "UserId" = {0}::uuid
            """, userGuid, now);

        if (rows == 0)
        {
            await userContext.Database.ExecuteSqlRawAsync("""
                INSERT INTO "user"."UserMetadatas" ("UserId", "CoverageDirty", "CoverageDirtyAt", "CoverageRefreshedAt")
                VALUES ({0}::uuid, TRUE, {1}::timestamptz, NULL)
                ON CONFLICT DO NOTHING
                """, userGuid, now);
        }
    }
}

