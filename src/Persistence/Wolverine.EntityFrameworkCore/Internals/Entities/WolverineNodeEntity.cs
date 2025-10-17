using JasperFx.Core;
using Wolverine.Runtime.Agents;

namespace Wolverine.EntityFrameworkCore.Internals;

public class WolverineNodeEntity
{
    public WolverineNodeEntity(){}
    
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembersAttribute]
    
    public WolverineNodeEntity(WolverineNode node)
    {
        Id = node.NodeId;
        AssignedNodeNumber = node.AssignedNodeNumber;
        Uri = node.ControlUri?.ToString();
        Description = node.Description;
        StartedAt = node.Started;
        LastHealthCheck = node.LastHealthCheck;
        Version = node.Version.ToString();
        Capabilities = node.Capabilities.Select(x => x.ToString()).Join(",");
    }
    
    public Guid Id { get; set; }
    
    public required int AssignedNodeNumber { get; set; }
    
    // TODO: better name? It is not not obvious what Uri is for
    public required string? Uri { get; set; }
    
    public required string Description { get; set; }
    
    public required DateTimeOffset StartedAt { get; set; }
    
    public required DateTimeOffset LastHealthCheck { get; set; }
    
    public required string Version { get; set; }
    
    public string? Capabilities { get; set; }
    
    // Navigation properties
    public virtual ICollection<NodeAgentAssignmentEntity> NodeAssignments { get; set; } = new List<NodeAgentAssignmentEntity>();
    
    public WolverineNode ToWolverineNode()
    {
        var node = new WolverineNode
        {
            NodeId = Id,
            AssignedNodeNumber = AssignedNodeNumber,
            ControlUri = Uri != null ? new Uri(Uri) : null,
            Description = Description,
            Started = StartedAt,
            LastHealthCheck = LastHealthCheck,
            Version = System.Version.Parse(Version),
        };
        
        if (Capabilities.IsNotEmpty())
        {
            node.Capabilities.AddRange(Capabilities.Split(',').Select(x => new Uri(x)));
        }
        
        return node;
    }
}
