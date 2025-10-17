using Wolverine.Runtime.Agents;

namespace Wolverine.EntityFrameworkCore.Internals;

public class AgentRestrictionEntity
{
    public AgentRestrictionEntity(){}
    
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembersAttribute]
    public AgentRestrictionEntity(AgentRestriction restriction)
    {
        Id = restriction.Id;
        Uri = restriction.AgentUri.ToString();
        Type = restriction.Type;
        NodeNumber = restriction.NodeNumber;
    }
    
    public Guid Id { get; set; }
    
    // TODO: better name? It is not not obvious what Uri is for
    public required string Uri { get; set; }
    
    public required AgentRestrictionType Type { get; set; }
    
    public required int NodeNumber { get; set; }
    
    public AgentRestriction ToAgentRestriction()
    {
        return new AgentRestriction(Id, new Uri(Uri), Type, NodeNumber);
    }
}
