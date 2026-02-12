using Microsoft.EntityFrameworkCore.Diagnostics;

namespace WolverineBugs;

public sealed class MyInterceptor : SaveChangesInterceptor
{
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default (CancellationToken))
    {
        if (!(eventData.Context is AppDbContext context))
            throw new InvalidOperationException($"DbContext of type '{eventData.Context?.GetType().Name ?? "null"}' is not a AppDbContext.");

        foreach (var entityEntry in context.ChangeTracker.Entries<MyDbEntity>())
        {
            entityEntry.Entity.Publish(new SomeEvent
            {
                EventData = "Event from interceptor"
            });
        }
        
        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}