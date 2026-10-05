using MDS.Studio.Application;
using MDS.Studio.Domain;
using Microsoft.EntityFrameworkCore;

namespace MDS.Studio.Infrastructure;
public sealed class EfInbox(StudioDbContext db) : IInbox
{
    public async Task<bool> TryRegisterAsync(Guid eventId, CancellationToken ct = default)
    {
        if (await db.InboxEvents.AnyAsync(x => x.Id == eventId, ct)) return false;
        db.InboxEvents.Add(new InboxEvent { Id = eventId, ProcessedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(ct); return true;
    }
}
