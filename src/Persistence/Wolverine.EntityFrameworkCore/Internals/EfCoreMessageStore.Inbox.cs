using JasperFx.Core;
using Microsoft.EntityFrameworkCore;
using Wolverine.Persistence.Durability;
using Wolverine.Transports;

namespace Wolverine.EntityFrameworkCore.Internals;

public partial class EfCoreMessageStore<TDbContext> : IMessageInbox
{
    public IMessageInbox Inbox => this;
    
    public async Task ScheduleExecutionAsync(Envelope envelope)
    {
        await dbContext.Set<IncomingMessage>()
            .Where(x => x.Id == envelope.Id && x.ReceivedAt == envelope.Destination!.ToString())
            .ExecuteUpdateAsync(setter => setter
                .SetProperty(x => x.ExecutionTime, envelope.ScheduledTime!.Value)
                .SetProperty(x => x.Status, EnvelopeStatus.Scheduled.ToString())
                .SetProperty(x => x.Attempts, envelope.Attempts)
                .SetProperty(x => x.OwnerId, TransportConstants.AnyNode));
    }

    public async Task MoveToDeadLetterStorageAsync(Envelope envelope, Exception? exception)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            // Delete the incoming message
            await dbContext.Set<IncomingMessage>()
                .Where(x => x.Id == envelope.Id && x.ReceivedAt == envelope.Destination!.ToString())
                .ExecuteDeleteAsync();

            // Create the dead letter message
            var dlq = new DeadLetterMessage(envelope, exception);

            if (envelope.DeliverBy.HasValue)
            {
                dlq.Expires = envelope.DeliverBy.Value;
            }
            else if (durability.DeadLetterQueueExpirationEnabled)
            {
                dlq.Expires = DateTimeOffset.UtcNow.Add(durability.DeadLetterQueueExpiration);
            }

            dbContext.Add(dlq);
            await dbContext.SaveChangesAsync();
            
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task IncrementIncomingEnvelopeAttemptsAsync(Envelope envelope)
    {
        await dbContext.Set<IncomingMessage>()
            .Where(x => x.Id == envelope.Id)
            .ExecuteUpdateAsync(setter => setter
                .SetProperty(x => x.Attempts, envelope.Attempts));
    }

    public async Task StoreIncomingAsync(Envelope envelope)
    {
        try
        {
            var incoming = new IncomingMessage(envelope);
            dbContext.Add(incoming);
            await dbContext.SaveChangesAsync();
        }
        catch (DbUpdateException e)
        {
            if (IsExceptionFromDuplicateEnvelope(e))
            {
                throw new DuplicateIncomingEnvelopeException(envelope);
            }

            throw;
        }

        return;

        bool IsExceptionFromDuplicateEnvelope(DbUpdateException dbUpdateEx)
        {
            // Check the inner exception for database-specific duplicate key violations
            var innerException = dbUpdateEx.InnerException;
            if (innerException == null) 
                return false;
            
            var message = innerException.Message;

            switch (innerException.GetType().Name)
            {
                // SQL Server - check for primary key or unique constraint violations
                case "SqlException":
                {
                    // SQL Server error numbers: 2601 (unique index), 2627 (primary key)
                    var sqlErrorNumber = innerException.GetType().GetProperty("Number")?.GetValue(innerException);
                    if (sqlErrorNumber is int errorNumber && (errorNumber == 2601 || errorNumber == 2627))
                    {
                        return true;
                    }
                    // Fallback to message checking
                    return message.ContainsIgnoreCase("Violation of PRIMARY KEY constraint") ||
                           message.ContainsIgnoreCase("Cannot insert duplicate key");
                }
                // PostgreSQL - check for unique constraint violations
                case "PostgresException":
                {
                    // PostgreSQL error code 23505 is unique_violation
                    var pgErrorCode = innerException.GetType().GetProperty("SqlState")?.GetValue(innerException);
                    if (pgErrorCode?.ToString() == "23505")
                    {
                        return true;
                    }
                    // Fallback to message checking
                    return message.Contains("duplicate key value violates unique constraint");
                }
                // SQLite
                case "SqliteException":
                    return message.ContainsIgnoreCase("UNIQUE constraint failed") ||
                           message.ContainsIgnoreCase("PRIMARY KEY constraint failed");
                // MySQL
                case "MySqlException":
                    return message.ContainsIgnoreCase("Duplicate entry");
                default:
                    // Generic fallback for other database providers
                    return message.ContainsIgnoreCase("duplicate") ||
                           message.ContainsIgnoreCase("unique") ||
                           message.ContainsIgnoreCase("primary key");
            }
        }
    }

    public async Task StoreIncomingAsync(IReadOnlyList<Envelope> envelopes)
    {
        foreach (var envelope in envelopes)
        {
            var incoming = new IncomingMessage(envelope);
            dbContext.Add(incoming);
        }

        // It's okay if it does fail here with the duplicate detection, because that
        // will force the DurableReceiver to try envelope at a time to get at the actual differences
        await dbContext.SaveChangesAsync();
    }

    public Task RescheduleExistingEnvelopeForRetryAsync(Envelope envelope)
    {
        envelope.Status = EnvelopeStatus.Scheduled;
        envelope.OwnerId = TransportConstants.AnyNode;

        return StoreIncomingAsync(envelope);
    }

    public async Task MarkIncomingEnvelopeAsHandledAsync(Envelope envelope)
    {
        var expirationTime = DateTimeOffset.UtcNow.Add(durability.KeepAfterMessageHandling);
        
        await dbContext.Set<IncomingMessage>()
            .Where(x => x.Id == envelope.Id && x.ReceivedAt == envelope.Destination!.ToString())
            .ExecuteUpdateAsync(setter => setter
                .SetProperty(x => x.KeepUntil, expirationTime)
                .SetProperty(x => x.Status, EnvelopeStatus.Handled.ToString()));
    }

    public async Task MarkIncomingEnvelopeAsHandledAsync(IReadOnlyList<Envelope> envelopes)
    {
        var expirationTime = DateTimeOffset.UtcNow.Add(durability.KeepAfterMessageHandling);
        
        var ids = envelopes.Select(x => x.Id).ToList();
        var idsWithDestinations = envelopes.Select(x => $"{x.Id}|{x.Destination}").ToList();
        await dbContext.Set<IncomingMessage>()
            .Where(x => ids.Contains(x.Id))
            .Where(x => idsWithDestinations.Contains($"{x.Id}|{x.ReceivedAt}"))
            .ExecuteUpdateAsync(setter => setter
                .SetProperty(x => x.KeepUntil, expirationTime)
                .SetProperty(x => x.Status, EnvelopeStatus.Handled.ToString()));
    }

    public async Task ReleaseIncomingAsync(int ownerId, Uri receivedAt)
    {
        await dbContext.Set<IncomingMessage>()
            .Where(x => x.OwnerId == ownerId && x.ReceivedAt == receivedAt.ToString())
            .ExecuteUpdateAsync(setter => setter
                .SetProperty(x => x.OwnerId, 0));
    }
}