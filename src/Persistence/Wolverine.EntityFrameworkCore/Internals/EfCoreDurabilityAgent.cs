using JasperFx;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wolverine.Runtime;
using Wolverine.Runtime.Agents;
using Wolverine.Runtime.Serialization;
using Wolverine.Transports;

namespace Wolverine.EntityFrameworkCore.Internals;

/// <summary>
/// Simple durability agent for EF Core message store that handles basic recovery operations
/// </summary>
internal class EfCoreDurabilityAgent<TDbContext> : IAgent where TDbContext : DbContext
{
    private readonly EfCoreMessageStore<TDbContext> _store;
    private readonly IWolverineRuntime _runtime;
    private readonly DurabilitySettings _settings;
    private readonly ILogger _logger;
    private Timer? _recoveryTimer;
    private Timer? _scheduledJobTimer;

    public EfCoreDurabilityAgent(EfCoreMessageStore<TDbContext> store, IWolverineRuntime runtime, DurabilitySettings settings)
    {
        _store = store;
        _runtime = runtime;
        _settings = settings;
        _logger = runtime.LoggerFactory.CreateLogger<EfCoreDurabilityAgent<TDbContext>>();
        
        Uri = new Uri("agent://efcore/durability");
    }

    public Uri Uri { get; }

    public AgentStatus Status { get; private set; } = AgentStatus.Running;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Start recovery timer for processing orphaned messages
        var recoveryStart = _settings.ScheduledJobFirstExecution.Add(TimeSpan.FromMilliseconds(new Random().Next(0, 1000)));
        
        _recoveryTimer = new Timer(async _ =>
        {
            try
            {
                await RecoverIncomingMessagesAsync();
                await RecoverOutgoingMessagesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during message recovery");
            }
        }, null, recoveryStart, _settings.ScheduledJobPollingTime);

        // Start scheduled job timer for processing scheduled messages
        _scheduledJobTimer = new Timer(async _ =>
        {
            try
            {
                await ProcessScheduledMessagesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing scheduled messages");
            }
        }, null, recoveryStart, _settings.ScheduledJobPollingTime);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Status = AgentStatus.Stopped;
        
        _recoveryTimer?.Dispose();
        _scheduledJobTimer?.Dispose();
        
        return Task.CompletedTask;
    }

    private async Task RecoverIncomingMessagesAsync()
    {
        try
        {
            // Find listeners that have orphaned messages
            var dbContext = _store.DbContext;
            var listeners = await dbContext.Set<IncomingMessage>()
                .Where(x => x.OwnerId == TransportConstants.AnyNode)
                .Select(x => x.ReceivedAt)
                .Distinct()
                .ToListAsync();

            foreach (var listenerAddress in listeners.Where(x => !string.IsNullOrEmpty(x)))
            {
                var uri = new Uri(listenerAddress!);
                var circuit = _runtime.Endpoints.FindListenerCircuit(uri);
                if (circuit.Status != ListeningStatus.Accepting)
                {
                    continue;
                }

                // Load a page of messages and reassign them
                var envelopes = await _store.LoadPageOfGloballyOwnedIncomingAsync(uri, _settings.RecoveryBatchSize);
                if (envelopes.Count > 0)
                {
                    await _store.ReassignIncomingAsync(_settings.AssignedNodeNumber, envelopes);
                    await circuit.EnqueueDirectlyAsync(envelopes);
                    
                    _logger.LogInformation("Recovered {Count} incoming messages for listener {Listener}", 
                        envelopes.Count, uri);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recovering incoming messages");
        }
    }

    private async Task RecoverOutgoingMessagesAsync()
    {
        try
        {
            // Find outgoing messages that need to be reassigned
            var dbContext = _store.DbContext;
            var destinations = await dbContext.Set<OutgoingMessage>()
                .Where(x => x.OwnerId == TransportConstants.AnyNode)
                .Select(x => x.Destination)
                .Distinct()
                .ToListAsync();

            foreach (var destination in destinations)
            {
                var uri = new Uri(destination);
                var envelopes = await _store.Outbox.LoadOutgoingAsync(uri);
                
                if (envelopes.Count > 0)
                {
                    // Reassign to current node
                    await dbContext.Set<OutgoingMessage>()
                        .Where(x => x.Destination == destination && x.OwnerId == TransportConstants.AnyNode)
                        .ExecuteUpdateAsync(setter => setter
                            .SetProperty(x => x.OwnerId, _settings.AssignedNodeNumber));
                    
                    _logger.LogInformation("Recovered {Count} outgoing messages for destination {Destination}", 
                        envelopes.Count, uri);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recovering outgoing messages");
        }
    }

    private async Task ProcessScheduledMessagesAsync()
    {
        try
        {
            var dbContext = _store.DbContext;
            var now = DateTimeOffset.UtcNow;
            
            // Find scheduled messages that are ready to execute
            var scheduledMessages = await dbContext.Set<IncomingMessage>()
                .Where(x => x.Status == EnvelopeStatus.Scheduled.ToString() 
                         && x.ExecutionTime <= now)
                .Take(_settings.RecoveryBatchSize)
                .ToListAsync();

            foreach (var message in scheduledMessages)
            {
                // Update status to incoming so it can be processed
                message.Status = EnvelopeStatus.Incoming.ToString();
                message.OwnerId = _settings.AssignedNodeNumber;
            }

            if (scheduledMessages.Count > 0)
            {
                await dbContext.SaveChangesAsync();
                
                // Convert to envelopes and enqueue
                var envelopes = scheduledMessages.Select(im =>
                {
                    var envelope = EnvelopeSerializer.Deserialize(im.Body);
                    envelope.Status = EnvelopeStatus.Incoming;
                    envelope.OwnerId = im.OwnerId;
                    envelope.ScheduledTime = null; // Clear scheduled time
                    envelope.Attempts = im.Attempts;
                    if (im.ReceivedAt != null)
                    {
                        envelope.Destination = new Uri(im.ReceivedAt);
                    }
                    return envelope;
                }).ToList();

                // Group by destination and enqueue
                foreach (var group in envelopes.GroupBy(x => x.Destination))
                {
                    var circuit = _runtime.Endpoints.FindListenerCircuit(group.Key!);
                    if (circuit.Status == ListeningStatus.Accepting)
                    {
                        await circuit.EnqueueDirectlyAsync(group.ToList());
                    }
                }
                
                _logger.LogInformation("Processed {Count} scheduled messages", scheduledMessages.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing scheduled messages");
        }
    }
}
