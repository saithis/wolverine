using JasperFx.Resources;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Postgresql;
using Wolverine.RabbitMQ;
using WolverineBugs;

var builder = WebApplication.CreateBuilder(args);
var settings = builder.Configuration.Get<AppSettings>()!;


builder.Services.AddDbContext<AppDbContext>(
    (serviceProvider, options) =>
    {
        options.UseNpgsql(
            settings.ConnectionStrings.Database,
            npgsqlOptions =>
            {
                npgsqlOptions.MigrationsHistoryTable("__EFMigrationsHistory", AppDbContext.SchemaName);
                npgsqlOptions.EnableRetryOnFailure(); // BUG1: Wolverine makes this unusable
            }
        );
    },
    optionsLifetime: ServiceLifetime.Singleton
);

builder.Services.AddWolverine(opts =>
{
    opts.ServiceName = "test";

    opts.PersistMessagesWithPostgresql(settings.ConnectionStrings.Database, "wolverine");
    opts.Durability.MessageStorageSchemaName = "wolverine";
    opts.Policies.AutoApplyTransactions();
    opts.UseEntityFrameworkCoreTransactions();

    opts.Policies.UseDurableLocalQueues();
    opts.Policies.UseDurableOutboxOnAllSendingEndpoints();
    opts.Policies.UseDurableInboxOnAllListeners();

    opts.PublishDomainEventsFromEntityFrameworkCore<Entity>(x => x.Events);

    opts.UseRabbitMq(new Uri(settings.ConnectionStrings.RabbitMQ))
        .CustomizeDeadLetterQueueing(new DeadLetterQueue("app.wolverine-dead-letter-queue"))
        .ConfigureListeners(l =>
        {
            l.DeadLetterQueueing(new DeadLetterQueue($"{l.QueueName}.dlq"));
        })
        .DisableSystemRequestReplyQueueDeclaration()
        .DeclareExchange(
            "my-exchange",
            e =>
            {
                e.ExchangeType = ExchangeType.Topic;
                e.IsDurable = true;
            }
        );


    opts.PublishMessage<SomeEvent>()
        .ToRabbitRoutingKey("my-exchange", "some.event");
});
builder.Services.AddResourceSetupOnStartup();


var app = builder.Build();

await MigrateAsync(app.Services);

app.MapGet("/", async (IDbContextOutbox<AppDbContext> dbContext) =>
{
    var data = $"hello world {Guid.NewGuid()}";

    var entity = new MyDbEntity
    {
        Data = data
    };
    entity.Events.Add(new SomeEvent
    {
        EventData = data
    });
    
    // BUG2: This will not be saved in the db, even though the endpoint returns success
    dbContext.DbContext.MyDbEntities.Add(entity);
    
    await dbContext.SaveChangesAndFlushMessagesAsync();
    
    return "Data saved and event published!";
});

app.MapGet("/remove", async (IDbContextOutbox<AppDbContext> dbContext) =>
{
    var entity = await dbContext.DbContext.MyDbEntities.FirstOrDefaultAsync();
    entity.Publish(new SomeEvent
    {
        EventData = $"removed {DateTimeOffset.UtcNow.ToString()}"
    });
    dbContext.DbContext.MyDbEntities.Remove(entity);
    
    await dbContext.SaveChangesAndFlushMessagesAsync();
    return entity;
});

app.MapGet("/list", async (AppDbContext dbContext) =>
{
    var entries = await dbContext.MyDbEntities.ToArrayAsync();
    return entries;
});

app.Run();



async Task MigrateAsync(IServiceProvider appServices)
{
    using var scope = appServices.GetRequiredService<IServiceScopeFactory>().CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await dbContext.Database.MigrateAsync();
}