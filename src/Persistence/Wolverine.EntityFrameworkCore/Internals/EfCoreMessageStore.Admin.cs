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
        try
        {
            await using var tx = await _dbContext.Database.BeginTransactionAsync();
            
            await _dbContext.Set<IncomingMessage>().ExecuteDeleteAsync();
            await _dbContext.Set<OutgoingMessage>().ExecuteDeleteAsync();
            await _dbContext.Set<DeadLetterMessage>().ExecuteDeleteAsync();
            
            if (Settings.Role == MessageStoreRole.Main)
            {
                await _dbContext.Set<AgentRestrictionEntity>().ExecuteDeleteAsync();
                await _dbContext.Set<NodeRecordEntity>().ExecuteDeleteAsync();
            }
            
            await tx.CommitAsync();
        }
        catch (Exception e)
        {
            throw new InvalidOperationException(
                "Failure trying to execute the statements to clear envelope storage", e);
        }
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
        var incomingCounts = await _dbContext.Set<IncomingMessage>()
            .GroupBy(x => x.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync();

        foreach (var statusCount in incomingCounts)
        {
            var status = Enum.Parse<EnvelopeStatus>(statusCount.Status);
            switch (status)
            {
                case EnvelopeStatus.Incoming:
                    counts.Incoming = statusCount.Count;
                    break;
                case EnvelopeStatus.Scheduled:
                    counts.Scheduled = statusCount.Count;
                    break;
                case EnvelopeStatus.Handled:
                    counts.Handled = statusCount.Count;
                    break;
            }
        }

        // Count outgoing messages
        counts.Outgoing = await _dbContext.Set<OutgoingMessage>().CountAsync();

        // Count dead letter messages
        counts.DeadLetter = await _dbContext.Set<DeadLetterMessage>().CountAsync();

        return counts;
    }

    public async Task<IReadOnlyList<Envelope>> AllIncomingAsync()
    {
        var incomingMessages = await _dbContext.Set<IncomingMessage>().ToListAsync();
        return incomingMessages.Select(im => im.ToEnvelope()).ToList();
    }

    public async Task<IReadOnlyList<Envelope>> AllOutgoingAsync()
    {
        var outgoingMessages = await _dbContext.Set<OutgoingMessage>().ToListAsync();
        return outgoingMessages.Select(om => om.ToEnvelope()).ToList();
    }

    public async Task ReleaseAllOwnershipAsync()
    {
        // Release ownership of all incoming messages by setting OwnerId to AnyNode
        await _dbContext.Set<IncomingMessage>()
            .ExecuteUpdateAsync(setter => setter
                .SetProperty(x => x.OwnerId, TransportConstants.AnyNode));

        // Release ownership of all outgoing messages by setting OwnerId to AnyNode  
        await _dbContext.Set<OutgoingMessage>()
            .ExecuteUpdateAsync(setter => setter
                .SetProperty(x => x.OwnerId, TransportConstants.AnyNode));
    }

    public async Task ReleaseAllOwnershipAsync(int ownerId)
    {
        // Release ownership of incoming messages for a specific owner
        await _dbContext.Set<IncomingMessage>()
            .Where(x => x.OwnerId == ownerId)
            .ExecuteUpdateAsync(setter => setter
                .SetProperty(x => x.OwnerId, TransportConstants.AnyNode));

        // Release ownership of outgoing messages for a specific owner
        await _dbContext.Set<OutgoingMessage>()
            .Where(x => x.OwnerId == ownerId)
            .ExecuteUpdateAsync(setter => setter
                .SetProperty(x => x.OwnerId, TransportConstants.AnyNode));
    }

    public async Task CheckConnectivityAsync(CancellationToken token)
    {
        // Simple connectivity check - try to execute a basic query
        try
        {
            await _dbContext.Database.CanConnectAsync(token);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Unable to connect to the database", ex);
        }
    }

    public async Task MigrateAsync()
    {
        // Apply any pending migrations to bring the database up to date
        await _dbContext.Database.MigrateAsync();
    }
}
