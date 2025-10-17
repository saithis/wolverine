using Weasel.Core;
using Wolverine.Persistence.Durability;

namespace Wolverine.EntityFrameworkCore;

/// <summary>
/// Configuration settings for EF Core message store
/// </summary>
public class EfCoreSettings
{
    /// <summary>
    /// The advisory lock implementation to use for distributed locking.
    /// </summary>
    public IAdvisoryLock? AdvisoryLock { get; set; } // TODO: can we make this optional for storage types that support distributed locks?
    
    /// <summary>
    /// The lock ID to use for leadership election. Default is 12000.
    /// Change this to prevent collisions between different applications
    /// using the same database.
    /// </summary>
    public int LeadershipLockId { get; set; } = 12000; // TODO: is this fine like this? PG generates this from the schewma name, can we do something similar for ef core?
    
    /// <summary>
    /// The role of this message store. Default is Main.
    /// </summary>
    public MessageStoreRole Role { get; set; } = MessageStoreRole.Main;
    
    /// <summary>
    /// The name of this message store instance
    /// </summary>
    public string? Name { get; set; }
}

