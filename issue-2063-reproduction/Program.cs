using JasperFx.Resources;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Persistence;
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
                npgsqlOptions.EnableRetryOnFailure();
            }
        );

        options.AddInterceptors(new MyInterceptor());
    },
    optionsLifetime: ServiceLifetime.Singleton
);

builder.Services.AddWolverine(opts =>
{
    opts.ServiceName = "test";

    opts.PersistMessagesWithPostgresql(settings.ConnectionStrings.Database, "wolverine");
    opts.Durability.MessageStorageSchemaName = "wolverine";
    opts.Policies.AutoApplyTransactions();
    opts.UseEntityFrameworkCoreTransactions(TransactionMiddlewareMode.Lightweight);

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

app.MapGet("/efcore-with-interceptor", async (AppDbContext dbContext) =>
{
    var entity = new MyDbEntity
    {
        Data = "Event will be added in interceptor + efcore save",
    };
    
    dbContext.MyDbEntities.Add(entity);

    // Problem: Event will not be published (Wolverine completely unaware of events)
    await dbContext.SaveChangesAsync();
    
    return $"ID: {entity.Id}";
});

app.MapGet("/efcore-without-interceptor", async (AppDbContext dbContext) =>
{
    var entity = new MyDbEntity
    {
        Data = "Hello world",
        SkipInterceptorEvent = true
    };
    entity.Events.Add(new SomeEvent // usually this would happen in a domain method, but for the reproduction I just put it here
    {
        EventData = "Event from the endpoint, saved via efcore"
    });
    
    dbContext.MyDbEntities.Add(entity);

    // Problem: Event will not be published (Wolverine completely unaware of events)
    await dbContext.SaveChangesAsync();
    
    return $"ID: {entity.Id}";
});

app.MapGet("/outbox-with-interceptor", async (IDbContextOutbox<AppDbContext> dbContext) =>
{
    var entity = new MyDbEntity
    {
        Data = "Event will be added in interceptor + outbox save",
    };
    
    dbContext.DbContext.MyDbEntities.Add(entity);

    // Problem: Event will not be published (interceptor to late for DomainEventScraper?!?)
    await dbContext.SaveChangesAndFlushMessagesAsync();
    
    return $"ID: {entity.Id}";
});

app.MapGet("/outbox-without-interceptor", async (IDbContextOutbox<AppDbContext> dbContext) =>
{
    var entity = new MyDbEntity
    {
        Data = "Hello world",
        SkipInterceptorEvent = true
    };
    entity.Events.Add(new SomeEvent // usually this would happen in a domain method, but for the reproduction I just put it here
    {
        EventData = "Event from the endpoint, saved via outbox"
    });
    
    dbContext.DbContext.MyDbEntities.Add(entity);

    // Problem: Event will not be published and endpoints errors out (System.InvalidOperationException: Collection was modified; enumeration operation may not execute.)
    await dbContext.SaveChangesAndFlushMessagesAsync();
    
    return $"ID: {entity.Id}";
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