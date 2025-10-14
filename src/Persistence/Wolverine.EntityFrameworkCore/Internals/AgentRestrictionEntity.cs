using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Wolverine.Runtime.Agents;

namespace Wolverine.EntityFrameworkCore.Internals;

[Table("wolverine_agent_restrictions")]
public class AgentRestrictionEntity
{
    [Key]
    public Guid Id { get; set; }
    
    [Required]
    [MaxLength(500)]
    public string AgentUri { get; set; } = string.Empty;
    
    [Required]
    public AgentRestrictionType Type { get; set; }
    
    [Required]
    public int NodeNumber { get; set; }
    
    public AgentRestriction ToAgentRestriction()
    {
        return new AgentRestriction(Id, new Uri(AgentUri), Type, NodeNumber);
    }
    
    public static AgentRestrictionEntity FromAgentRestriction(AgentRestriction restriction)
    {
        return new AgentRestrictionEntity
        {
            Id = restriction.Id,
            AgentUri = restriction.AgentUri.ToString(),
            Type = restriction.Type,
            NodeNumber = restriction.NodeNumber
        };
    }
}
