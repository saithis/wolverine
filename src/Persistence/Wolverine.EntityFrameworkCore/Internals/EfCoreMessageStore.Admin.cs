using Microsoft.EntityFrameworkCore;
using Wolverine.Logging;
using Wolverine.Persistence.Durability;
using Wolverine.Runtime.Serialization;
using Wolverine.Transports;

namespace Wolverine.EntityFrameworkCore.Internals;

public partial class EfCoreMessageStore<TDbContext> : IMessageStoreAdmin
{
    public IMessageStoreAdmin Admin => this;

    public async Task ClearAllAsync()
    {
        // Clear all message tables
        await dbContext.Set<IncomingMessage>().ExecuteDeleteAsync();
        await dbContext.Set<OutgoingMessage>().ExecuteDeleteAsync();
        await dbContext.Set<DeadLetterMessage>().ExecuteDeleteAsync();
    }

    public Task RebuildAsync()
    {
        // For EF Core, rebuilding is essentially clearing all data
        // The schema is managed by EF migrations
        return ClearAllAsync();
    }

    public async Task<PersistedCounts> FetchCountsAsync()
    {
        var counts = new PersistedCounts();

        // Count incoming messages by status
        var incomingCounts = await dbContext.Set<IncomingMessage>()
            .GroupBy(x => x.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync();

        foreach (var statusCount in incomingCounts)
        {
            switch (statusCount.Status)
            {
                case nameof(EnvelopeStatus.Incoming):
                    counts.Incoming = statusCount.Count;
                    break;
                case nameof(EnvelopeStatus.Scheduled):
                    counts.Scheduled = statusCount.Count;
                    break;
                case nameof(EnvelopeStatus.Handled):
                    counts.Handled = statusCount.Count;
                    break;
            }
        }

        // Count outgoing messages
        counts.Outgoing = await dbContext.Set<OutgoingMessage>().CountAsync();

        // Count dead letter messages
        counts.DeadLetter = await dbContext.Set<DeadLetterMessage>().CountAsync();

        return counts;
    }

    public async Task<IReadOnlyList<Envelope>> AllIncomingAsync()
    {
        var incomingMessages = await dbContext.Set<IncomingMessage>().ToListAsync();

        return incomingMessages.Select(im =>
        {
            var envelope = EnvelopeSerializer.Deserialize(im.Body);
            envelope.Status = Enum.Parse<EnvelopeStatus>(im.Status);
            envelope.OwnerId = im.OwnerId;
            envelope.ScheduledTime = im.ExecutionTime;
            envelope.Attempts = im.Attempts;
            if (im.ReceivedAt != null)
            {
                envelope.Destination = new Uri(im.ReceivedAt);
            }
            return envelope;
        }).ToList();
    }

    public async Task<IReadOnlyList<Envelope>> AllOutgoingAsync()
    {
        var outgoingMessages = await dbContext.Set<OutgoingMessage>().ToListAsync();

        return outgoingMessages.Select(om =>
        {
            var envelope = EnvelopeSerializer.Deserialize(om.Body);
            envelope.OwnerId = om.OwnerId;
            envelope.DeliverBy = om.DeliverBy;
            envelope.Attempts = om.Attempts;
            envelope.Destination = new Uri(om.Destination);
            return envelope;
        }).ToList();
    }

    public async Task ReleaseAllOwnershipAsync()
    {
        // Release ownership of all incoming messages by setting OwnerId to AnyNode
        await dbContext.Set<IncomingMessage>()
            .ExecuteUpdateAsync(setter => setter
                .SetProperty(x => x.OwnerId, TransportConstants.AnyNode));

        // Release ownership of all outgoing messages by setting OwnerId to AnyNode  
        await dbContext.Set<OutgoingMessage>()
            .ExecuteUpdateAsync(setter => setter
                .SetProperty(x => x.OwnerId, TransportConstants.AnyNode));
    }

    public async Task ReleaseAllOwnershipAsync(int ownerId)
    {
        // Release ownership of incoming messages for a specific owner
        await dbContext.Set<IncomingMessage>()
            .Where(x => x.OwnerId == ownerId)
            .ExecuteUpdateAsync(setter => setter
                .SetProperty(x => x.OwnerId, TransportConstants.AnyNode));

        // Release ownership of outgoing messages for a specific owner
        await dbContext.Set<OutgoingMessage>()
            .Where(x => x.OwnerId == ownerId)
            .ExecuteUpdateAsync(setter => setter
                .SetProperty(x => x.OwnerId, TransportConstants.AnyNode));
    }

    public async Task CheckConnectivityAsync(CancellationToken token)
    {
        // Simple connectivity check - try to execute a basic query
        try
        {
            await dbContext.Database.CanConnectAsync(token);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Unable to connect to the database", ex);
        }
    }

    public async Task MigrateAsync()
    {
        // Apply any pending migrations to bring the database up to date
        await dbContext.Database.MigrateAsync();
    }
}
