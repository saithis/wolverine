using JasperFx.Descriptors;
using Microsoft.EntityFrameworkCore;
using Weasel.Core;
using Wolverine.Persistence.Durability;
using Wolverine.Runtime;
using Wolverine.Runtime.Agents;
using Wolverine.Runtime.Serialization;
using Wolverine.Transports;

namespace Wolverine.EntityFrameworkCore.Internals;

public partial class EfCoreMessageStore<TDbContext> : IMessageStoreWithAgentSupport
    where TDbContext : DbContext
{
    private IWolverineRuntime? _runtime;
    internal TDbContext DbContext => _dbContext;
    internal EfCoreSettings Settings { get; }
    private IAdvisoryLock AdvisoryLock => Settings.AdvisoryLock;
    private readonly TDbContext _dbContext;
    private readonly DurabilitySettings _durability;

    public EfCoreMessageStore(TDbContext dbContext, DurabilitySettings durability, EfCoreSettings settings)
    {
        _dbContext = dbContext;
        _durability = durability;
        Settings = settings;
        
        Name = Settings.Name ?? $"EfCore-{_dbContext.Database.GetDbConnection().Database}";
        
        // Set up the URI based on the database connection
        var connectionString = _dbContext.Database.GetConnectionString();
        if (!string.IsNullOrEmpty(connectionString))
        {
            Uri = new Uri($"efcore://{_dbContext.Database.GetDbConnection().Database}/wolverine");
        }
    }

    public ValueTask DisposeAsync()
    {
        return _dbContext.DisposeAsync();
    }

    public MessageStoreRole Role => Settings.Role;
    public List<string> TenantIds { get; } = [];

    public Uri Uri { get; } = new("efcore://localhost/wolverine");
    
    public bool HasDisposed => false; // EF Core handles disposal through the DbContext

    public string Name { get; init; }
    
    public void Initialize(IWolverineRuntime runtime)
    {
        _runtime = runtime;
    }

    public DatabaseDescriptor Describe()
    {
        return new DatabaseDescriptor(this)
        {
            Engine = "EntityFrameworkCore",
            DatabaseName = _dbContext.Database.GetDbConnection().Database
        };
    }

    public Task DrainAsync()
    {
        // For EF Core, draining means ensuring all pending changes are saved
        // and the context is ready for disposal
        return Task.CompletedTask;
    }

    public IAgent StartScheduledJobs(IWolverineRuntime runtime)
    {
        // For EF Core, we create a simple agent that handles durability operations
        // This would be expanded to include scheduled job polling, dead letter cleanup, etc.
        return BuildAgent(runtime);
    }

    public IAgent BuildAgent(IWolverineRuntime runtime)
    {
        return new EfCoreDurabilityAgent<TDbContext>(this, runtime, _durability);
    }

    public async Task<IReadOnlyList<Envelope>> LoadPageOfGloballyOwnedIncomingAsync(Uri listenerAddress, int limit)
    {
        var incomingMessages = await _dbContext.Set<IncomingMessage>()
            .Where(x => x.OwnerId == TransportConstants.AnyNode 
                     && x.Status == EnvelopeStatus.Incoming.ToString() 
                     && x.ReceivedAt == listenerAddress.ToString())
            .OrderBy(x => x.Id)
            .Take(limit)
            .ToListAsync();

        return incomingMessages.Select(im =>
        {
            var envelope = EnvelopeSerializer.Deserialize(im.Body);
            envelope.Status = Enum.Parse<EnvelopeStatus>(im.Status);
            envelope.OwnerId = im.OwnerId;
            envelope.ScheduledTime = im.ExecutionTime;
            envelope.Attempts = im.Attempts;
            if (im.ReceivedAt != null)
            {
                envelope.Destination = new Uri(im.ReceivedAt);
            }
            return envelope;
        }).ToList();
    }

    public async Task ReassignIncomingAsync(int ownerId, IReadOnlyList<Envelope> incoming)
    {
        if (!incoming.Any()) return;

        var envelopeIds = incoming.Select(x => x.Id).ToArray();
        
        await _dbContext.Set<IncomingMessage>()
            .Where(x => envelopeIds.Contains(x.Id))
            .ExecuteUpdateAsync(setter => setter
                .SetProperty(x => x.OwnerId, ownerId));
    }

    public void PromoteToMain(IWolverineRuntime runtime)
    {
        Settings.Role = MessageStoreRole.Main;
        Initialize(runtime);
    }

    public void DemoteToAncillary()
    {
        Settings.Role = MessageStoreRole.Ancillary;
    }
}