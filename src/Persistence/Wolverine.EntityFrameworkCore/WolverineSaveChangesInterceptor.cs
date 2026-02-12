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
/// Tracks which DbContext instances have a DB transaction that was started by Wolverine
/// (either via PersistOutgoingAsync or handler middleware). This allows the SaveChanges
/// interceptor to distinguish Wolverine-managed transactions from user-started transactions.
/// Uses ConditionalWeakTable so entries are automatically cleaned up when the DbContext is GC'd.
/// </summary>
public static class WolverineTransactionTracker
{
    private static readonly ConditionalWeakTable<DbContext, object> _tracked = new();

    /// <summary>
    /// Mark a DbContext as having a Wolverine-started DB transaction.
    /// Called by Wolverine infrastructure when it starts a transaction via BeginTransactionAsync.
    /// </summary>
    public static void Track(DbContext dbContext) => _tracked.AddOrUpdate(dbContext, new object());
    internal static bool IsTracked(DbContext dbContext) => _tracked.TryGetValue(dbContext, out _);
    internal static void Remove(DbContext dbContext) => _tracked.Remove(dbContext);
}

/// <summary>
/// EF Core SaveChangesInterceptor that automatically scrapes domain events from tracked entities
/// and publishes them through Wolverine using the outbox pattern for transactional consistency.
///
/// This interceptor only activates when Wolverine has NOT already started a DB transaction for
/// this DbContext (checked via <see cref="WolverineTransactionTracker"/>). This means:
/// - When Wolverine's handler middleware or outbox started a transaction (via PersistOutgoingAsync),
///   the interceptor skips to avoid double-scraping.
/// - When the user started their own transaction, the interceptor still runs and persists outbox
///   messages within the user's transaction. The interceptor does not commit or rollback the
///   user's transaction — the user retains control.
/// - When no transaction exists, the interceptor creates its own outbox transaction, commits it
///   atomically with the entity changes, and flushes messages.
/// </summary>
internal sealed class WolverineSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly IDomainEventScraper[] _scrapers;
    private readonly IServiceProvider _serviceProvider;
    private IWolverineRuntime? _runtime;

    // Track active interceptor state per DbContext instance for cross-method state (SavingChanges -> SavedChanges)
    // Uses ConditionalWeakTable to avoid memory leaks (weak references to DbContext keys)
    private readonly ConditionalWeakTable<DbContext, InterceptorState> _activeContexts = new();

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
        // 3. Wolverine has NOT already started a transaction for this DbContext
        //    (if it did, the middleware or outbox is handling scraping)
        //
        // NOTE: We intentionally do NOT check CurrentTransaction == null here.
        // A user may have started their own transaction, and we still want to
        // scrape domain events in that case. We only skip when Wolverine itself
        // started the transaction (tracked via WolverineTransactionTracker).
        if (_scrapers.Length > 0 && eventData.Context != null
            && !WolverineTransactionTracker.IsTracked(eventData.Context))
        {
            _runtime ??= _serviceProvider.GetRequiredService<IWolverineRuntime>();
            var bus = new MessageContext(_runtime);

            // Track whether we started the DB transaction (vs. user already had one).
            // We only commit/rollback transactions we started ourselves.
            var hadExistingTransaction = eventData.Context.Database.CurrentTransaction != null;

            // Set up EfCoreEnvelopeTransaction for proper outbox integration when a message database
            // is available. This ensures outgoing messages are persisted in the same DB transaction 
            // as the entity changes. If the user already has a transaction, PersistOutgoingAsync
            // will reuse it rather than starting a new one.
            if (bus.TryFindMessageDatabase(out _))
            {
                bus.EnlistInOutbox(new EfCoreEnvelopeTransaction(eventData.Context, bus));
            }

            foreach (var scraper in _scrapers)
            {
                await scraper.ScrapeEvents(eventData.Context, bus);
            }

            // Store the bus and transaction ownership info so SavedChangesAsync can commit and flush
            _activeContexts.AddOrUpdate(eventData.Context, new InterceptorState(bus, WeStartedTransaction: !hadExistingTransaction));
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context != null && _activeContexts.TryGetValue(eventData.Context, out var state))
        {
            _activeContexts.Remove(eventData.Context);

            // Clean up the transaction tracking that was set by PersistOutgoingAsync
            // (only relevant when we started the transaction ourselves)
            WolverineTransactionTracker.Remove(eventData.Context);

            // Only commit the transaction if we started it (not a user-managed transaction).
            // For user-managed transactions, the outbox messages were persisted within their
            // transaction and will be committed when the user commits.
            if (state.WeStartedTransaction && eventData.Context.Database.CurrentTransaction != null)
            {
                await eventData.Context.Database.CommitTransactionAsync(cancellationToken);
            }

            await state.Bus.FlushOutgoingMessagesAsync();
        }

        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    public override async Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context != null && _activeContexts.TryGetValue(eventData.Context, out var state))
        {
            _activeContexts.Remove(eventData.Context);

            // Clean up the transaction tracking
            WolverineTransactionTracker.Remove(eventData.Context);

            // Only rollback the transaction if we started it
            if (state.WeStartedTransaction && eventData.Context.Database.CurrentTransaction != null)
            {
                await eventData.Context.Database.RollbackTransactionAsync(cancellationToken);
            }
        }

        await base.SaveChangesFailedAsync(eventData, cancellationToken);
    }

    private record InterceptorState(MessageContext Bus, bool WeStartedTransaction);
}
