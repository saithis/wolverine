using Microsoft.EntityFrameworkCore;
using Wolverine.Runtime.Agents;

namespace Wolverine.EntityFrameworkCore.Internals;

public partial class EfCoreMessageStore<TDbContext> : INodeAgentPersistence
{
    public INodeAgentPersistence Nodes => this;
    private string? _currentNodeId;
    private readonly object _leadershipLock = new object();
    private bool _hasLeadershipLock = false;

    public async Task ClearAllAsync(CancellationToken cancellationToken)
    {
        // Clear all node-related data
        await dbContext.Set<NodeAgentAssignmentEntity>().ExecuteDeleteAsync(cancellationToken);
        await dbContext.Set<AgentRestrictionEntity>().ExecuteDeleteAsync(cancellationToken);
        await dbContext.Set<NodeRecordEntity>().ExecuteDeleteAsync(cancellationToken);
        await dbContext.Set<WolverineNodeEntity>().ExecuteDeleteAsync(cancellationToken);
        await dbContext.Set<LeadershipLockEntity>().ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<int> PersistAsync(WolverineNode node, CancellationToken cancellationToken)
    {
        var existingEntity = await dbContext.Set<WolverineNodeEntity>()
            .FirstOrDefaultAsync(x => x.NodeId == node.NodeId, cancellationToken);

        if (existingEntity == null)
        {
            // Assign node number if not set
            if (node.AssignedNodeNumber == 0)
            {
                var maxNodeNumber = await dbContext.Set<WolverineNodeEntity>()
                    .MaxAsync(x => (int?)x.AssignedNodeNumber, cancellationToken) ?? 0;
                node.AssignedNodeNumber = maxNodeNumber + 1;
            }

            var entity = WolverineNodeEntity.FromWolverineNode(node);
            dbContext.Set<WolverineNodeEntity>().Add(entity);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        else
        {
            // Update existing node
            existingEntity.ControlUri = node.ControlUri?.ToString();
            existingEntity.Description = node.Description;
            existingEntity.LastHealthCheck = node.LastHealthCheck;
            existingEntity.Version = node.Version.ToString();
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return node.AssignedNodeNumber;
    }

    public async Task DeleteAsync(Guid nodeId, int assignedNodeNumber)
    {
        var entity = await dbContext.Set<WolverineNodeEntity>()
            .FirstOrDefaultAsync(x => x.NodeId == nodeId);
        
        if (entity != null)
        {
            dbContext.Set<WolverineNodeEntity>().Remove(entity);
            await dbContext.SaveChangesAsync();
        }
    }

    public async Task<IReadOnlyList<WolverineNode>> LoadAllNodesAsync(CancellationToken cancellationToken)
    {
        var entities = await dbContext.Set<WolverineNodeEntity>()
            .Include(x => x.AgentAssignments)
            .ToListAsync(cancellationToken);

        return entities.Select(x => x.ToWolverineNode()).ToList();
    }

    public async Task PersistAgentRestrictionsAsync(IReadOnlyList<AgentRestriction> restrictions, CancellationToken cancellationToken)
    {
        // Clear existing restrictions
        await dbContext.Set<AgentRestrictionEntity>().ExecuteDeleteAsync(cancellationToken);
        
        // Add new restrictions
        if (restrictions.Any())
        {
            var entities = restrictions.Select(AgentRestrictionEntity.FromAgentRestriction);
            dbContext.Set<AgentRestrictionEntity>().AddRange(entities);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<NodeAgentState> LoadNodeAgentStateAsync(CancellationToken cancellationToken)
    {
        var nodes = await LoadAllNodesAsync(cancellationToken);
        
        var restrictionEntities = await dbContext.Set<AgentRestrictionEntity>()
            .ToListAsync(cancellationToken);
        
        var restrictions = restrictionEntities.Select(x => x.ToAgentRestriction()).ToArray();
        var agentRestrictions = new AgentRestrictions(restrictions);
        
        return new NodeAgentState(nodes, agentRestrictions);
    }

    public async Task AssignAgentsAsync(Guid nodeId, IReadOnlyList<Uri> agents, CancellationToken cancellationToken)
    {
        // Remove existing assignments for this node
        await dbContext.Set<NodeAgentAssignmentEntity>()
            .Where(x => x.NodeId == nodeId)
            .ExecuteDeleteAsync(cancellationToken);
        
        // Add new assignments
        if (agents.Any())
        {
            var assignments = agents.Select(agent => new NodeAgentAssignmentEntity
            {
                NodeId = nodeId,
                AgentUri = agent.ToString(),
                Started = DateTimeOffset.UtcNow
            });
            
            dbContext.Set<NodeAgentAssignmentEntity>().AddRange(assignments);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task RemoveAssignmentAsync(Guid nodeId, Uri agentUri, CancellationToken cancellationToken)
    {
        await dbContext.Set<NodeAgentAssignmentEntity>()
            .Where(x => x.NodeId == nodeId && x.AgentUri == agentUri.ToString())
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task AddAssignmentAsync(Guid nodeId, Uri agentUri, CancellationToken cancellationToken)
    {
        // Check if assignment already exists
        var exists = await dbContext.Set<NodeAgentAssignmentEntity>()
            .AnyAsync(x => x.NodeId == nodeId && x.AgentUri == agentUri.ToString(), cancellationToken);
        
        if (!exists)
        {
            var assignment = new NodeAgentAssignmentEntity
            {
                NodeId = nodeId,
                AgentUri = agentUri.ToString(),
                Started = DateTimeOffset.UtcNow
            };
            
            dbContext.Set<NodeAgentAssignmentEntity>().Add(assignment);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<WolverineNode?> LoadNodeAsync(Guid nodeId, CancellationToken cancellationToken)
    {
        var entity = await dbContext.Set<WolverineNodeEntity>()
            .Include(x => x.AgentAssignments)
            .FirstOrDefaultAsync(x => x.NodeId == nodeId, cancellationToken);
        
        return entity?.ToWolverineNode();
    }

    public async Task MarkHealthCheckAsync(WolverineNode node, CancellationToken cancellationToken)
    {
        var entity = await dbContext.Set<WolverineNodeEntity>()
            .FirstOrDefaultAsync(x => x.NodeId == node.NodeId, cancellationToken);
        
        if (entity != null)
        {
            entity.LastHealthCheck = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task OverwriteHealthCheckTimeAsync(Guid nodeId, DateTimeOffset lastHeartbeatTime)
    {
        var entity = await dbContext.Set<WolverineNodeEntity>()
            .FirstOrDefaultAsync(x => x.NodeId == nodeId);
        
        if (entity != null)
        {
            entity.LastHealthCheck = lastHeartbeatTime;
            await dbContext.SaveChangesAsync();
        }
    }

    public async Task LogRecordsAsync(params NodeRecord[] records)
    {
        if (records.Any())
        {
            var entities = records.Select(NodeRecordEntity.FromNodeRecord);
            dbContext.Set<NodeRecordEntity>().AddRange(entities);
            await dbContext.SaveChangesAsync();
        }
    }

    public async Task<IReadOnlyList<NodeRecord>> FetchRecentRecordsAsync(int count)
    {
        var entities = await dbContext.Set<NodeRecordEntity>()
            .OrderByDescending(x => x.Timestamp)
            .Take(count)
            .ToListAsync();
        
        return entities.Select(x => x.ToNodeRecord()).ToList();
    }

    public bool HasLeadershipLock()
    {
        lock (_leadershipLock)
        {
            return _hasLeadershipLock;
        }
    }

    public async Task<bool> TryAttainLeadershipLockAsync(CancellationToken token)
    {
        if (_runtime == null) return false;
        
        var lockId = "wolverine_leadership";
        var nodeId = _runtime.Options.UniqueNodeId;
        var serviceName = _runtime.Options.ServiceName;
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddMinutes(5); // 5-minute lock
        
        try
        {
            using var transaction = await dbContext.Database.BeginTransactionAsync(token);
            
            try
            {
                // Strategy 1: Try to insert new lock (handles case where no lock exists)
                try
                {
                    var newLock = new LeadershipLockEntity
                    {
                        LockId = lockId,
                        NodeId = nodeId,
                        ServiceName = serviceName,
                        AcquiredAt = now,
                        ExpiresAt = expiresAt
                    };
                    
                    dbContext.Set<LeadershipLockEntity>().Add(newLock);
                    await dbContext.SaveChangesAsync(token);
                    await transaction.CommitAsync(token);
                    
                    lock (_leadershipLock)
                    {
                        _hasLeadershipLock = true;
                        _currentNodeId = nodeId.ToString();
                    }
                    return true;
                }
                catch (Exception) // Constraint violation - lock already exists
                {
                    // Clear the failed entity
                    dbContext.ChangeTracker.Clear();
                }
                
                // Strategy 2: Try to update existing lock atomically
                // This uses a WHERE clause to ensure we only update if conditions are met
                var rowsAffected = await dbContext.Database.ExecuteSqlAsync($@"
                    UPDATE wolverine_leadership_lock 
                    SET NodeId = {nodeId}, 
                        ServiceName = {serviceName}, 
                        AcquiredAt = {now}, 
                        ExpiresAt = {expiresAt}
                    WHERE LockId = {lockId} 
                    AND (NodeId = {nodeId} OR ExpiresAt < {now})", token);
                
                if (rowsAffected > 0)
                {
                    await transaction.CommitAsync(token);
                    
                    lock (_leadershipLock)
                    {
                        _hasLeadershipLock = true;
                        _currentNodeId = nodeId.ToString();
                    }
                    return true;
                }
                
                // Failed to acquire lock - another node owns it and it hasn't expired
                await transaction.RollbackAsync(token);
                
                lock (_leadershipLock)
                {
                    _hasLeadershipLock = false;
                }
                return false;
            }
            catch (Exception)
            {
                await transaction.RollbackAsync(token);
                throw;
            }
        }
        catch (Exception)
        {
            // Failed to acquire lock due to database error or concurrency
            lock (_leadershipLock)
            {
                _hasLeadershipLock = false;
            }
            return false;
        }
    }

    public async Task ReleaseLeadershipLockAsync()
    {
        if (_runtime == null) return;
        
        var lockId = "wolverine_leadership";
        var nodeId = _runtime.Options.UniqueNodeId;
        
        try
        {
            // Use atomic delete operation - only delete if we own the lock
            var rowsAffected = await dbContext.Database.ExecuteSqlAsync($@"
                DELETE FROM wolverine_leadership_lock 
                WHERE LockId = {lockId} AND NodeId = {nodeId}");
            
            // Note: We don't check rowsAffected because the lock might have already expired
            // or been taken by another node, which is fine
        }
        catch (Exception)
        {
            // Ignore errors during release - the lock will expire anyway
        }
        finally
        {
            // Always clear local state regardless of database operation result
            lock (_leadershipLock)
            {
                _hasLeadershipLock = false;
                _currentNodeId = null;
            }
        }
    }
}
