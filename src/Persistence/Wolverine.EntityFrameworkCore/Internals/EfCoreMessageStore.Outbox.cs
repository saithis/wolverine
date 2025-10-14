using Microsoft.EntityFrameworkCore;
using Wolverine.Persistence.Durability;
using Wolverine.Runtime.Serialization;

namespace Wolverine.EntityFrameworkCore.Internals;

public partial class EfCoreMessageStore<TDbContext> : IMessageOutbox
{
    public IMessageOutbox Outbox => this;
    
    // Only called from DurabilityAgent stuff
    public async Task<IReadOnlyList<Envelope>> LoadOutgoingAsync(Uri destination)
    {
        var outgoing = await dbContext
            .Set<OutgoingMessage>()
            .Where(x => x.Destination == destination.ToString())
            .ToListAsync();

        return outgoing.Select(x =>
        {
            var envelope = EnvelopeSerializer.Deserialize(x.Body);
            envelope.OwnerId = x.OwnerId;
            envelope.DeliverBy = x.DeliverBy;
            envelope.Attempts = x.Attempts;
            return envelope;
        }).ToList();
    }

    public async Task StoreOutgoingAsync(Envelope envelope, int ownerId)
    {
        var outgoing = new OutgoingMessage(envelope)
        {
            OwnerId = ownerId
        };
        dbContext.Add(outgoing);
        await dbContext.SaveChangesAsync();
    }

    public async Task DeleteOutgoingAsync(Envelope[] envelopes)
    {
        var ids = envelopes.Select(x => x.Id).ToArray();
        await dbContext.Set<OutgoingMessage>()
            .Where(x => ids.Contains(x.Id))
            .ExecuteDeleteAsync();
    }

    public async Task DeleteOutgoingAsync(Envelope envelope)
    {
        await dbContext.Set<OutgoingMessage>()
            .Where(x => x.Id == envelope.Id)
            .ExecuteDeleteAsync();
    }

    // Only called from DurabilityAgent
    public async Task DiscardAndReassignOutgoingAsync(Envelope[] discards, Envelope[] reassigned, int nodeId)
    {
        await DeleteOutgoingAsync(discards);
        
        var reassignIds = reassigned.Select(x => x.Id).ToArray();
        await dbContext.Set<OutgoingMessage>()
            .Where(x => reassignIds.Contains(x.Id))
            .ExecuteUpdateAsync(setters => 
                setters.SetProperty(entity => entity.OwnerId, nodeId));
    }
}