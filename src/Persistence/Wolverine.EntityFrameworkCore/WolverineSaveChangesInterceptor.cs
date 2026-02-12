using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Wolverine.EntityFrameworkCore.Internals;
using Wolverine.RDBMS;
using Wolverine.Runtime;

namespace Wolverine.EntityFrameworkCore;

/// <summary>
/// Internal registration type used to track which domain event scrapers
/// should participate in the SaveChanges interceptor (entity-based scrapers only).
/// </summary>
internal record SaveChangesScraperRegistration(IDomainEventScraper Scraper);

/// <summary>
/// EF Core SaveChangesInterceptor that automatically scrapes domain events from tracked entities
/// and publishes them through Wolverine using the outbox pattern for transactional consistency.
///
/// This interceptor only activates when there is no active database transaction, meaning Wolverine's
/// handler middleware or DbContextOutbox is not already managing the lifecycle.
///
/// When a message database is configured, the interceptor uses the outbox pattern:
/// 1. SavingChangesAsync: scrapes events, persists outgoing messages to the outbox (starts a DB transaction)
/// 2. The actual SaveChangesAsync runs within that same DB transaction  
/// 3. SavedChangesAsync: commits the transaction (entity changes + outbox atomically), then flushes messages
///
/// When no message database is configured, events are published directly after save.
/// </summary>
internal sealed class WolverineSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly IDomainEventScraper[] _scrapers;
    private readonly IServiceProvider _serviceProvider;
    private IWolverineRuntime? _runtime;

    // Track active outbox contexts per DbContext instance for cross-method state (SavingChanges -> SavedChanges)
    // Uses ConditionalWeakTable to avoid memory leaks (weak references to DbContext keys)
    private readonly ConditionalWeakTable<DbContext, MessageContext> _activeContexts = new();

    public WolverineSaveChangesInterceptor(IDomainEventScraper[] scrapers, IServiceProvider serviceProvider)
    {
        _scrapers = scrapers;
        _serviceProvider = serviceProvider;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        // Only scrape domain events when:
        // 1. There are scrapers registered
        // 2. We have a DbContext 
        // 3. No active DB transaction (if there is one, Wolverine's middleware or outbox is handling scraping)
        if (_scrapers.Length > 0 && eventData.Context != null
            && eventData.Context.Database.CurrentTransaction == null)
        {
            _runtime ??= _serviceProvider.GetRequiredService<IWolverineRuntime>();
            var bus = new MessageContext(_runtime);

            // Set up EfCoreEnvelopeTransaction for proper outbox integration when a message database
            // is available. This ensures outgoing messages are persisted in the same DB transaction 
            // as the entity changes.
            if (bus.TryFindMessageDatabase(out _))
            {
                bus.EnlistInOutbox(new EfCoreEnvelopeTransaction(eventData.Context, bus));
            }

            foreach (var scraper in _scrapers)
            {
                await scraper.ScrapeEvents(eventData.Context, bus);
            }

            // Store the bus so SavedChangesAsync can commit and flush
            _activeContexts.AddOrUpdate(eventData.Context, bus);
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context != null && _activeContexts.TryGetValue(eventData.Context, out var bus))
        {
            _activeContexts.Remove(eventData.Context);

            // Commit the outbox transaction if one was started by SavingChangesAsync.
            // This atomically commits both entity changes and outgoing messages.
            if (eventData.Context.Database.CurrentTransaction != null)
            {
                await eventData.Context.Database.CommitTransactionAsync(cancellationToken);
            }

            await bus.FlushOutgoingMessagesAsync();
        }

        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    public override async Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context != null && _activeContexts.TryGetValue(eventData.Context, out _))
        {
            _activeContexts.Remove(eventData.Context);

            // Roll back the transaction started by SavingChangesAsync so the outbox messages
            // are discarded along with the failed entity changes.
            if (eventData.Context.Database.CurrentTransaction != null)
            {
                await eventData.Context.Database.RollbackTransactionAsync(cancellationToken);
            }
        }

        await base.SaveChangesFailedAsync(eventData, cancellationToken);
    }
}
