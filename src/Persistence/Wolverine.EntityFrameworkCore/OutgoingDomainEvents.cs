using System.Collections;
using Microsoft.EntityFrameworkCore;
using Wolverine.Runtime;

namespace Wolverine.EntityFrameworkCore;

/// <summary>
/// Can be used with the EF Core transactional middleware to publish domain events 
/// </summary>
public class OutgoingDomainEvents() : OutgoingMessages;

public class OutgoingDomainEventsScraper : IDomainEventScraper
{
    private readonly OutgoingDomainEvents _events;

    public OutgoingDomainEventsScraper(OutgoingDomainEvents events)
    {
        _events = events;
    }

    public Task ScrapeEvents(DbContext dbContext, MessageContext bus)
    {
        return bus.EnqueueCascadingAsync(_events);
    }
}

public interface IDomainEventScraper
{
    Task ScrapeEvents(DbContext dbContext, MessageContext bus);
}

public class DomainEventScraper<T, TEvent> : IDomainEventScraper
{
    private readonly Func<T, IEnumerable<TEvent>> _source;

    public DomainEventScraper(Func<T, IEnumerable<TEvent>> source)
    {
        _source = source;
    }

    public async Task ScrapeEvents(DbContext dbContext, MessageContext bus)
    {
        // Materialize entities and events up front to avoid "Collection was modified" errors
        // when PublishAsync modifies the change tracker (e.g., by starting a transaction)
        var entities = dbContext.ChangeTracker.Entries().Select(x => x.Entity).OfType<T>().ToList();
        var eventMessages = entities.SelectMany(_source).ToList();

        foreach (var eventMessage in eventMessages)
        {
            await bus.PublishAsync(eventMessage);
        }

        // Clear event collections to prevent double-scraping when both the outbox and
        // the SaveChanges interceptor are active (e.g., when a user-started transaction
        // prevents the interceptor from detecting that the outbox already scraped).
        // This only works when the source returns a mutable collection (e.g., List<T>).
        foreach (var entity in entities)
        {
            if (_source(entity) is IList list)
            {
                list.Clear();
            }
        }
    }
}