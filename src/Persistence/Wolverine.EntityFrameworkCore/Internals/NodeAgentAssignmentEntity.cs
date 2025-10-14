using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Wolverine.EntityFrameworkCore.Internals;

[Table("wolverine_node_assignments")]
public class NodeAgentAssignmentEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();
    
    [Required]
    public Guid NodeId { get; set; }
    
    [Required]
    [MaxLength(500)]
    public string AgentUri { get; set; } = string.Empty;
    
    [Required]
    public DateTimeOffset Started { get; set; } = DateTimeOffset.UtcNow;
    
    // Navigation property
    [ForeignKey(nameof(NodeId))]
    public virtual WolverineNodeEntity? Node { get; set; }
}
