using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Wolverine.Runtime.Agents;

namespace Wolverine.EntityFrameworkCore.Internals;

[Table("wolverine_nodes")]
public class WolverineNodeEntity
{
    [Key]
    public Guid NodeId { get; set; }
    
    [Required]
    public int AssignedNodeNumber { get; set; } = 1;
    
    public string? ControlUri { get; set; }
    
    [Required]
    [MaxLength(200)]
    public string Description { get; set; } = Environment.MachineName;
    
    [Required]
    public DateTimeOffset Started { get; set; }
    
    [Required]
    public DateTimeOffset LastHealthCheck { get; set; } = DateTimeOffset.UtcNow;
    
    [Required]
    [MaxLength(50)]
    public string Version { get; set; } = "0.0.0.0";
    
    // Navigation properties
    public virtual ICollection<NodeAgentAssignmentEntity> AgentAssignments { get; set; } = new List<NodeAgentAssignmentEntity>();
    
    public WolverineNode ToWolverineNode()
    {
        var node = new WolverineNode
        {
            NodeId = NodeId,
            AssignedNodeNumber = AssignedNodeNumber,
            ControlUri = ControlUri != null ? new Uri(ControlUri) : null,
            Description = Description,
            Started = Started,
            LastHealthCheck = LastHealthCheck,
            Version = System.Version.Parse(Version)
        };
        
        // Add active agents from assignments
        foreach (var assignment in AgentAssignments)
        {
            node.ActiveAgents.Add(new Uri(assignment.AgentUri));
        }
        
        return node;
    }
    
    public static WolverineNodeEntity FromWolverineNode(WolverineNode node)
    {
        return new WolverineNodeEntity
        {
            NodeId = node.NodeId,
            AssignedNodeNumber = node.AssignedNodeNumber,
            ControlUri = node.ControlUri?.ToString(),
            Description = node.Description,
            Started = node.Started,
            LastHealthCheck = node.LastHealthCheck,
            Version = node.Version.ToString()
        };
    }
}
