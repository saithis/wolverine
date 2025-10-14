namespace Wolverine.EntityFrameworkCore;

/// <summary>
/// Interface for distributed advisory locks
/// </summary>
public interface IAdvisoryLock : IAsyncDisposable
{
    /// <summary>
    /// Check if this instance currently holds the specified lock
    /// </summary>
    /// <param name="lockId">The lock identifier</param>
    /// <returns>True if the lock is held by this instance</returns>
    bool HasLock(int lockId);

    /// <summary>
    /// Try to acquire the specified advisory lock
    /// </summary>
    /// <param name="lockId">The lock identifier</param>
    /// <param name="token">Cancellation token</param>
    /// <returns>True if the lock was successfully acquired</returns>
    Task<bool> TryAttainLockAsync(int lockId, CancellationToken token);

    /// <summary>
    /// Release the specified advisory lock
    /// </summary>
    /// <param name="lockId">The lock identifier</param>
    /// <returns>Task representing the release operation</returns>
    Task ReleaseLockAsync(int lockId);
}

