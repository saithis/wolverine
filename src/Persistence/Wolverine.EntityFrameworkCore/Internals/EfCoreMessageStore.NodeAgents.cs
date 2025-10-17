using Microsoft.EntityFrameworkCore;
using Wolverine.Runtime.Agents;

namespace Wolverine.EntityFrameworkCore.Internals;

public partial class EfCoreMessageStore<TDbContext> : INodeAgentPersistence
{
    public INodeAgentPersistence Nodes => this;

    public async Task ClearAllAsync(CancellationToken cancellationToken)
    {
        // Clear all node-related data
        await _dbContext.Set<NodeAgentAssignmentEntity>().ExecuteDeleteAsync(cancellationToken);
        await _dbContext.Set<AgentRestrictionEntity>().ExecuteDeleteAsync(cancellationToken);
        await _dbContext.Set<NodeRecordEntity>().ExecuteDeleteAsync(cancellationToken);
        await _dbContext.Set<WolverineNodeEntity>().ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<int> PersistAsync(WolverineNode node, CancellationToken cancellationToken)
    {
        var existingEntity = await _dbContext.Set<WolverineNodeEntity>()
            .FirstOrDefaultAsync(x => x.Id == node.NodeId, cancellationToken);

        if (existingEntity == null)
        {
            // Assign node number if not set
            if (node.AssignedNodeNumber == 0)
            {
                var maxNodeNumber = await _dbContext.Set<WolverineNodeEntity>()
                    .MaxAsync(x => (int?)x.AssignedNodeNumber, cancellationToken) ?? 0;
                node.AssignedNodeNumber = maxNodeNumber + 1;
            }

            var entity = new WolverineNodeEntity(node);
            _dbContext.Set<WolverineNodeEntity>().Add(entity);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        else
        {
            // Update existing node
            existingEntity.Uri = node.ControlUri?.ToString();
            existingEntity.Description = node.Description;
            existingEntity.LastHealthCheck = node.LastHealthCheck;
            existingEntity.Version = node.Version.ToString();
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return node.AssignedNodeNumber;
    }

    public async Task DeleteAsync(Guid nodeId, int assignedNodeNumber)
    {
        var entity = await _dbContext.Set<WolverineNodeEntity>()
            .FirstOrDefaultAsync(x => x.Id == nodeId);
        
        if (entity != null)
        {
            _dbContext.Set<WolverineNodeEntity>().Remove(entity);
            await _dbContext.SaveChangesAsync();
        }
    }

    public async Task<IReadOnlyList<WolverineNode>> LoadAllNodesAsync(CancellationToken cancellationToken)
    {
        var entities = await _dbContext.Set<WolverineNodeEntity>()
            .Include(x => x.NodeAssignments)
            .ToListAsync(cancellationToken);

        return entities.Select(x => x.ToWolverineNode()).ToList();
    }

    public async Task PersistAgentRestrictionsAsync(IReadOnlyList<AgentRestriction> restrictions, CancellationToken cancellationToken)
    {
        // Clear existing restrictions
        await _dbContext.Set<AgentRestrictionEntity>().ExecuteDeleteAsync(cancellationToken);
        
        // Add new restrictions
        if (restrictions.Any())
        {
            var entities = restrictions.Select(x => new AgentRestrictionEntity(x));
            _dbContext.Set<AgentRestrictionEntity>().AddRange(entities);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<NodeAgentState> LoadNodeAgentStateAsync(CancellationToken cancellationToken)
    {
        var nodes = await LoadAllNodesAsync(cancellationToken);
        
        var restrictionEntities = await _dbContext.Set<AgentRestrictionEntity>()
            .ToListAsync(cancellationToken);
        
        var restrictions = restrictionEntities.Select(x => x.ToAgentRestriction()).ToArray();
        var agentRestrictions = new AgentRestrictions(restrictions);
        
        return new NodeAgentState(nodes, agentRestrictions);
    }

    public async Task AssignAgentsAsync(Guid nodeId, IReadOnlyList<Uri> agents, CancellationToken cancellationToken)
    {
        // Remove existing assignments for this node
        await _dbContext.Set<NodeAgentAssignmentEntity>()
            .Where(x => x.NodeId == nodeId)
            .ExecuteDeleteAsync(cancellationToken);
        
        // Add new assignments
        if (agents.Any())
        {
            var assignments = agents.Select(agent => new NodeAgentAssignmentEntity
            {
                NodeId = nodeId,
                Id = agent.ToString(),
                StartedAt = DateTimeOffset.UtcNow
            });
            
            _dbContext.Set<NodeAgentAssignmentEntity>().AddRange(assignments);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task RemoveAssignmentAsync(Guid nodeId, Uri agentUri, CancellationToken cancellationToken)
    {
        await _dbContext.Set<NodeAgentAssignmentEntity>()
            .Where(x => x.NodeId == nodeId && x.Id == agentUri.ToString())
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task AddAssignmentAsync(Guid nodeId, Uri agentUri, CancellationToken cancellationToken)
    {
        // Check if assignment already exists
        var exists = await _dbContext.Set<NodeAgentAssignmentEntity>()
            .AnyAsync(x => x.NodeId == nodeId && x.Id == agentUri.ToString(), cancellationToken);
        
        if (!exists)
        {
            var assignment = new NodeAgentAssignmentEntity
            {
                NodeId = nodeId,
                Id = agentUri.ToString(),
                StartedAt = DateTimeOffset.UtcNow
            };
            
            _dbContext.Set<NodeAgentAssignmentEntity>().Add(assignment);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<WolverineNode?> LoadNodeAsync(Guid nodeId, CancellationToken cancellationToken)
    {
        var entity = await _dbContext.Set<WolverineNodeEntity>()
            .Include(x => x.NodeAssignments)
            .FirstOrDefaultAsync(x => x.Id == nodeId, cancellationToken);
        
        return entity?.ToWolverineNode();
    }

    public async Task MarkHealthCheckAsync(WolverineNode node, CancellationToken cancellationToken)
    {
        var entity = await _dbContext.Set<WolverineNodeEntity>()
            .FirstOrDefaultAsync(x => x.Id == node.NodeId, cancellationToken);
        
        if (entity != null)
        {
            entity.LastHealthCheck = DateTimeOffset.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task OverwriteHealthCheckTimeAsync(Guid nodeId, DateTimeOffset lastHeartbeatTime)
    {
        var entity = await _dbContext.Set<WolverineNodeEntity>()
            .FirstOrDefaultAsync(x => x.Id == nodeId);
        
        if (entity != null)
        {
            entity.LastHealthCheck = lastHeartbeatTime;
            await _dbContext.SaveChangesAsync();
        }
    }

    public async Task LogRecordsAsync(params NodeRecord[] records)
    {
        if (records.Any())
        {
            var entities = records.Select(x => new NodeRecordEntity(x));
            _dbContext.Set<NodeRecordEntity>().AddRange(entities);
            await _dbContext.SaveChangesAsync();
        }
    }

    public async Task<IReadOnlyList<NodeRecord>> FetchRecentRecordsAsync(int count)
    {
        var entities = await _dbContext.Set<NodeRecordEntity>()
            .OrderByDescending(x => x.Timestamp)
            .Take(count)
            .ToListAsync();
        
        return entities.Select(x => x.ToNodeRecord()).ToList();
    }

    public bool HasLeadershipLock()
    {
        return AdvisoryLock.HasLock(Settings.LeadershipLockId);
    }

    public Task<bool> TryAttainLeadershipLockAsync(CancellationToken token)
    {
        return AdvisoryLock.TryAttainLockAsync(Settings.LeadershipLockId, token);
    }

    public Task ReleaseLeadershipLockAsync()
    {
        return AdvisoryLock.ReleaseLockAsync(Settings.LeadershipLockId);
    }
}
