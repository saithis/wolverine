
namespace Wolverine.EntityFrameworkCore.Internals;

public class NodeAgentAssignmentEntity
{
    public required string Id { get; set; }

    public required Guid NodeId { get; set; }

    public required DateTimeOffset StartedAt { get; set; }

    public WolverineNodeEntity? Node { get; set; }
}
