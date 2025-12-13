using power_position_tracker.Models;

namespace power_position_tracker.Services.Interfaces;

/// <summary>
/// File-based dead letter queue for managing extraction failures 
/// that exhausted all retry attempts.
/// Provides guaranteed recovery through persistent storage across application restarts.
/// </summary>
public interface IDeadLetterQueue
{
    /// <summary>
    /// Adds a failed extraction to the dead letter queue for later processing.
    /// </summary>
    /// <param name="failedExtraction">Details of the failed extraction including retry count and error</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task EnqueueAsync(FailedExtraction failedExtraction, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves and removes all failed extractions form the queue for processing 
    /// Returns oldest items first
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Collection of failed extractions, empty if queue is empty</returns>
    Task<IEnumerable<FailedExtraction>> DequeueAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current number of items in the dead letter queue.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of queued failed extractions</returns>
    Task<int> GetCountAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a specific failed extraction from the queue after successful recovery.
    /// </summary>
    /// <param name="extractionTimeUtc">The extraction time to remove from the queue</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if item was found and removed, false if not found</returns>
    Task<bool> RemoveAsync(DateTime extractionTimeUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Peeks at all failed extractions without removing them from the queue.
    /// Useful for monitoring and reporting purposes.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Collection of failed extractions currently in queue</returns>
    Task<IEnumerable<FailedExtraction>> PeekAllAsync(CancellationToken cancellationToken = default);
}
