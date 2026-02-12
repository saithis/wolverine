using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Wolverine.EntityFrameworkCore;

namespace WolverineBugs;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public const string SchemaName = "appData";
    
    public DbSet<MyDbEntity> MyDbEntities => Set<MyDbEntity>();
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);

        // This enables your DbContext to map the incoming and outgoing messages as part of the outbox
        // It does NOT add the schema to migrations
        modelBuilder.MapWolverineEnvelopeStorage("wolverine");
    }
}

public class MyDbEntity : Entity
{
    public int Id { get; set; }
    public required string Data { get; set; }
    
    [NotMapped]
    public bool SkipInterceptorEvent { get; set; }
}
public abstract class Entity
{
    public List<object> Events { get; } = new();

    public void Publish(object @event)
    {
        Events.Add(@event);
    }
}