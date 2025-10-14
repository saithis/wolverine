using Microsoft.EntityFrameworkCore;
using Wolverine.Runtime.Agents;

namespace Wolverine.EntityFrameworkCore.Internals;

public partial class EfCoreMessageStore<TDbContext> : INodeAgentPersistence
{
    public INodeAgentPersistence Nodes => this;

    public async Task ClearAllAsync(CancellationToken cancellationToken)
    {
        // Clear all node-related data
        await dbContext.Set<NodeAgentAssignmentEntity>().ExecuteDeleteAsync(cancellationToken);
        await dbContext.Set<AgentRestrictionEntity>().ExecuteDeleteAsync(cancellationToken);
        await dbContext.Set<NodeRecordEntity>().ExecuteDeleteAsync(cancellationToken);
        await dbContext.Set<WolverineNodeEntity>().ExecuteDeleteAsync(cancellationToken);
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
