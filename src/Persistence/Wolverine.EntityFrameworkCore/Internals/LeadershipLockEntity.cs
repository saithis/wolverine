using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Wolverine.EntityFrameworkCore.Internals;

[Table("wolverine_leadership_lock")]
public class LeadershipLockEntity
{
    [Key]
    [MaxLength(100)]
    public string LockId { get; set; } = "wolverine_leadership";
    
    [Required]
    public Guid NodeId { get; set; }
    
    [Required]
    [MaxLength(100)]
    public string ServiceName { get; set; } = string.Empty;
    
    [Required]
    public DateTimeOffset AcquiredAt { get; set; } = DateTimeOffset.UtcNow;
    
    [Required]
    public DateTimeOffset ExpiresAt { get; set; }
}
