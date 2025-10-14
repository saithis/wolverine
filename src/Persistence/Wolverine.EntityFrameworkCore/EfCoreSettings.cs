using Wolverine.Persistence.Durability;

namespace Wolverine.EntityFrameworkCore;

/// <summary>
/// Configuration settings for EF Core message store
/// </summary>
public class EfCoreSettings
{
    /// <summary>
    /// The advisory lock implementation to use for distributed locking.
    /// If null, a no-op implementation will be used.
    /// </summary>
    public IAdvisoryLock? AdvisoryLock { get; set; }
    
    /// <summary>
    /// The lock ID to use for leadership election. Default is 12000.
    /// Change this to prevent collisions between different applications
    /// using the same database.
    /// </summary>
    public int LeadershipLockId { get; set; } = 12000;
    
    /// <summary>
    /// The role of this message store. Default is Main.
    /// </summary>
    public MessageStoreRole Role { get; set; } = MessageStoreRole.Main;
    
    /// <summary>
    /// The name of this message store instance
    /// </summary>
    public string? Name { get; set; }
}

/// <summary>
/// No-op implementation of IAdvisoryLock for when no distributed locking is needed
/// </summary>
internal class NullAdvisoryLock : IAdvisoryLock
{
    public bool HasLock(int lockId) => false;

    public Task<bool> TryAttainLockAsync(int lockId, CancellationToken token) => Task.FromResult(false);

    public Task ReleaseLockAsync(int lockId) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
