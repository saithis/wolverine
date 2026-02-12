using Microsoft.EntityFrameworkCore.Diagnostics;

namespace WolverineBugs;

public sealed class MyInterceptor : SaveChangesInterceptor
{
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (!(eventData.Context is AppDbContext context))
            throw new InvalidOperationException($"DbContext of type '{eventData.Context?.GetType().Name ?? "null"}' is not a AppDbContext.");

        foreach (var entityEntry in context.ChangeTracker.Entries<MyDbEntity>())
        {
            if (entityEntry.Entity.SkipInterceptorEvent)
                continue;
            
            entityEntry.Entity.Publish(new SomeEvent
            {
                EventData = entityEntry.Entity.Data
            });
        }
        
        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}