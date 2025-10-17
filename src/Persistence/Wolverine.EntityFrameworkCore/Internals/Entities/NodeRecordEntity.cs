using Wolverine.Runtime.Agents;

namespace Wolverine.EntityFrameworkCore.Internals;

public class NodeRecordEntity
{
    public NodeRecordEntity(){}
    
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembersAttribute]
    public NodeRecordEntity(NodeRecord record)
    {
        NodeNumber = record.NodeNumber;
        EventName = record.RecordType.ToString();
        Timestamp = record.Timestamp;
        Description = record.Description;
    }
    
    public int Id { get; set; }
    
    public required int NodeNumber { get; set; }
    
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    
    public required string Description { get; set; }

    public required string EventName { get; set; }

    public NodeRecord ToNodeRecord()
    {
        return new NodeRecord
        {
            NodeNumber = NodeNumber,
            RecordType = Enum.Parse<NodeRecordType>(EventName),
            Timestamp = Timestamp,
            Description = Description,
        };
    }
}
