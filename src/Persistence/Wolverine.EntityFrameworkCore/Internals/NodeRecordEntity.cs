using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using Wolverine.Runtime.Agents;

namespace Wolverine.EntityFrameworkCore.Internals;

[Table("wolverine_node_records")]
public class NodeRecordEntity
{
    [Key]
    [MaxLength(50)]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    
    [Required]
    public int NodeNumber { get; set; }
    
    [Required]
    public NodeRecordType RecordType { get; set; }
    
    [Required]
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    
    [MaxLength(500)]
    public string Description { get; set; } = string.Empty;
    
    [Required]
    [MaxLength(100)]
    public string ServiceName { get; set; } = string.Empty;
    
    [MaxLength(500)]
    public string AgentUri { get; set; } = "none://";
    
    public NodeRecord ToNodeRecord()
    {
        return new NodeRecord
        {
            Id = Id,
            NodeNumber = NodeNumber,
            RecordType = RecordType,
            Timestamp = Timestamp,
            Description = Description,
            ServiceName = ServiceName,
            AgentUri = new Uri(AgentUri)
        };
    }
    
    public static NodeRecordEntity FromNodeRecord(NodeRecord record)
    {
        return new NodeRecordEntity
        {
            Id = record.Id,
            NodeNumber = record.NodeNumber,
            RecordType = record.RecordType,
            Timestamp = record.Timestamp,
            Description = record.Description,
            ServiceName = record.ServiceName,
            AgentUri = record.AgentUri.ToString()
        };
    }
}
