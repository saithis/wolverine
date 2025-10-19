using JasperFx.Core;
using Microsoft.EntityFrameworkCore;
using Wolverine.Persistence.Durability;
using Wolverine.Persistence.Durability.DeadLetterManagement;
using Wolverine.Runtime.Serialization;
using Wolverine.Transports;

namespace Wolverine.EntityFrameworkCore.Internals;

public partial class EfCoreMessageStore<TDbContext> : IDeadLetters
{
    public IDeadLetters DeadLetters => this;
    
    private IQueryable<DeadLetterMessage> BuildDeadLetterQuery(DeadLetterEnvelopeQuery query)
    {
        var queryable = _dbContext.Set<DeadLetterMessage>().AsQueryable();

        // Handle specific message IDs first (takes precedence)
        if (query.MessageIds.Length > 0)
        {
            return queryable.Where(x => query.MessageIds.Contains(x.Id));
        }

        // Apply filters
        if (query.Range.From.HasValue)
        {
            queryable = queryable.Where(x => x.SentAt >= query.Range.From.Value.ToUniversalTime());
        }

        if (query.Range.To.HasValue)
        {
            queryable = queryable.Where(x => x.SentAt <= query.Range.To.Value.ToUniversalTime());
        }

        if (query.MessageType.IsNotEmpty())
        {
            queryable = queryable.Where(x => x.MessageType == query.MessageType);
        }

        if (query.ExceptionType.IsNotEmpty())
        {
            queryable = queryable.Where(x => x.ExceptionType == query.ExceptionType);
        }

        if (query.ExceptionMessage.IsNotEmpty())
        {
            queryable = queryable.Where(x => x.ExceptionMessage != null && x.ExceptionMessage.Contains(query.ExceptionMessage));
        }

        if (query.ReceivedAt.IsNotEmpty())
        {
            queryable = queryable.Where(x => x.ReceivedAt == query.ReceivedAt);
        }
        
        return queryable;
    }
    
    public async Task<DeadLetterEnvelope?> DeadLetterEnvelopeByIdAsync(Guid id, string? tenantId = null)
    {
        var deadLetterMessage = await _dbContext.Set<DeadLetterMessage>()
            .Where(x => x.Id == id)
            .FirstOrDefaultAsync();

        return deadLetterMessage?.ToEnvelope();
    }

    public async Task<IReadOnlyList<DeadLetterQueueCount>> SummarizeAllAsync(string serviceName, TimeRange range, CancellationToken token)
    {
        var query = _dbContext.Set<DeadLetterMessage>().AsQueryable();

        // Apply time range filters
        if (range.From.HasValue)
        {
            query = query.Where(x => x.SentAt >= range.From.Value.ToUniversalTime());
        }

        if (range.To.HasValue)
        {
            query = query.Where(x => x.SentAt <= range.To.Value.ToUniversalTime());
        }

        var results = await query
            .GroupBy(x => new { x.ReceivedAt, x.MessageType, x.ExceptionType })
            .Select(g => new
            {
                ReceivedAt = g.Key.ReceivedAt,
                MessageType = g.Key.MessageType,
                ExceptionType = g.Key.ExceptionType,
                Count = g.Count()
            })
            .ToListAsync(token);

        return results.Select(r => new DeadLetterQueueCount(
            serviceName,
            r.ReceivedAt != null ? new Uri(r.ReceivedAt) : new Uri("unknown://localhost"),
            r.MessageType,
            r.ExceptionType ?? string.Empty,
            Uri, // Database URI from the store
            r.Count
        )).ToList();
    }

    public async Task<DeadLetterEnvelopeResults> QueryAsync(DeadLetterEnvelopeQuery query, CancellationToken token)
    {
        var queryable = BuildDeadLetterQuery(query);

        // Get total count for pagination
        var totalCount = await queryable.CountAsync(token);

        // Apply pagination
        var pagedQuery = queryable
            .OrderBy(x => x.ExecutionTime)
            .Skip(query.PageNumber * query.PageSize)
            .Take(query.PageSize);

        var deadLetterMessages = await pagedQuery.ToListAsync(token);

        var envelopes = deadLetterMessages.Select(dlm => dlm.ToEnvelope()).ToList();

        return new DeadLetterEnvelopeResults
        {
            TotalCount = totalCount,
            Envelopes = envelopes,
            PageNumber = query.PageNumber
        };
    }

    public async Task DiscardAsync(DeadLetterEnvelopeQuery query, CancellationToken token)
    {
        var queryable = BuildDeadLetterQuery(query);
        await queryable.ExecuteDeleteAsync(token);
    }

    public async Task ReplayAsync(DeadLetterEnvelopeQuery query, CancellationToken token)
    {
        var queryable = BuildDeadLetterQuery(query);
        await queryable.ExecuteUpdateAsync(setter => setter
            .SetProperty(x => x.Replayable, true), token);
    }
}
