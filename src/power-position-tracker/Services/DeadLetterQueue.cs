using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using power_position_tracker.Models;
using power_position_tracker.Services.Interfaces;
using System.Text.Json;

namespace power_position_tracker.Services;

/// <summary>
/// File-based implementation of dead letter queue using JSON persistence.
/// Thread-safe with semaphore locking for concurrent access.
/// </summary>
public class DeadLetterQueue : IDeadLetterQueue, IDisposable
{
    private readonly ILogger<DeadLetterQueue> _logger;
    private readonly PowerPositionSettings _settings;
    private readonly SemaphoreSlim _fileLock = new SemaphoreSlim(1, 1);
    private readonly string _queueFilePath;
    private bool _disposed = false;

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public DeadLetterQueue(
        ILogger<DeadLetterQueue> logger, 
        IOptions<PowerPositionSettings> settings)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));

        _queueFilePath = Path.Combine(_settings.DlqDirectory, "FailedExtractions.json");

        Directory.CreateDirectory(_settings.DlqDirectory);

        _logger.LogInformation(
            "DeadLetterQueue initialized. Queue file: {QueueFilePath}",
            _queueFilePath);
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<FailedExtraction>> DequeueAllAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _fileLock.WaitAsync(cancellationToken);

        try
        {
            var queue = await LoadQueueAsync(cancellationToken);

            if (!queue.Any())
            {
                _logger.LogInformation("DLQ is empty on dequeue attempt");
                return Enumerable.Empty<FailedExtraction>();
            }

            var count = queue.Count;

            // Clear the queue
            await SaveQueueAsync(new List<FailedExtraction>(), cancellationToken);

            _logger.LogInformation("Dequeued all {Count} items from DLQ", count);

            // Return items ordered by ExtractionTimeUtc (oldest first)
            return queue.OrderBy(f => f.ExtractionTimeUtc).ToList();
        }

        catch (Exception ex)
        {
            _logger.LogError(ex, "Error dequeueing all failed extractions from DLQ");
            throw;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task EnqueueAsync(FailedExtraction failedExtraction, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (failedExtraction is null)
            throw new ArgumentNullException(nameof(failedExtraction));

        await _fileLock.WaitAsync(cancellationToken);

        try
        {
            var queue = await LoadQueueAsync(cancellationToken);

            // Check for duplicate based on ExtractionTimeUtc
            var existing = queue.FirstOrDefault(fe => fe.ExtractionTimeUtc == failedExtraction.ExtractionTimeUtc);

            if (existing is not null)
            {
                queue.Remove(existing);
                _logger.LogWarning(
                    "Updating existing DLQ entry for {ExtractionTime}. Previous retry count: {OldCount}, New retry count: {NewCount}",
                    failedExtraction.ExtractionTimeUtc,
                    existing.RetryCount,
                    failedExtraction.RetryCount);
            }

            queue.Add(failedExtraction);

            // Sort by ExtractionTimeUtc (oldest first) for FIFO behavior
            queue = queue.OrderBy(fe => fe.ExtractionTimeUtc).ToList();

            await SaveQueueAsync(queue, cancellationToken);

            _logger.LogWarning(
                "Added failed extraction to DLQ: {ExtractionTime}, RetryCount: {RetryCount}, Error: {Error}",
                failedExtraction.ExtractionTimeUtc,
                failedExtraction.RetryCount,
                failedExtraction.LastError);
        }

        catch (Exception ex) 
        { 
            _logger.LogError(ex, "Error enqueueing failed extraction to DLQ: {ExtractionTime}", failedExtraction.ExtractionTimeUtc);
            throw;
        }

        finally
        {
            _fileLock.Release();
        }
    }       

    /// <inheritdoc/>
    public async Task<int> GetCountAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _fileLock.WaitAsync(cancellationToken);

        try
        {
            var queue = await LoadQueueAsync(cancellationToken);
            return queue.Count;
        }

        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting DLQ count");
            throw;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<FailedExtraction>> PeekAllAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _fileLock.WaitAsync(cancellationToken);

        try
        {
            var queue = await LoadQueueAsync(cancellationToken);
            return queue.OrderBy(f => f.ExtractionTimeUtc).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error peeking all failed extractions from DLQ");
            throw;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<bool> RemoveAsync(DateTime extractionTimeUtc, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _fileLock.WaitAsync(cancellationToken);

        try
        {
            var queue = await LoadQueueAsync(cancellationToken);

            var itemToRemove = queue.FirstOrDefault(fe => fe.ExtractionTimeUtc == extractionTimeUtc);

            if (itemToRemove is null)
            {
                _logger.LogWarning("Attempted to remove non-existent item from DLQ: {ExtractionTime}", extractionTimeUtc);
                return false;
            }

            queue.Remove(itemToRemove);
            await SaveQueueAsync(queue, cancellationToken);

            _logger.LogInformation("Removed item from DLQ: {ExtractionTime}", extractionTimeUtc);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing item from DLQ: {ExtractionTime}", extractionTimeUtc);
            throw;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _fileLock.Dispose();
        _disposed = true;
    }

    private async Task<List<FailedExtraction>> LoadQueueAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_queueFilePath))
        {
            _logger.LogInformation("Queue file does not exist.");
            return new List<FailedExtraction>();
        }

        try
        {
            var json = await File.ReadAllTextAsync(_queueFilePath, cancellationToken);

            if (string.IsNullOrWhiteSpace(json))
            {
                _logger.LogDebug("DLQ file is empty, returning empty queue");
                return new List<FailedExtraction>();
            }

            var queue = JsonSerializer.Deserialize<List<FailedExtraction>>(json, JsonOptions);

            if (queue is null)
            {
                _logger.LogWarning("Failed to Deserialize DLQ queue, returning empty queue");
                return new List<FailedExtraction>();
            }

            _logger.LogInformation("Loaded {Count} items from DLQ", queue.Count);
            return queue;
        }

        catch (JsonException ex) 
        { 
            _logger.LogError(ex, "JSON deserialization error while loading DLQ from file");

            return new List<FailedExtraction>();
        }
    }

    private async Task SaveQueueAsync(List<FailedExtraction> queue, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(queue, JsonOptions);

        // Atomic write: write to temp file, then replace original
        var tempPath = $"{_queueFilePath}.tmp";

        try
        {
            await File.WriteAllTextAsync(tempPath, json, cancellationToken);

            File.Move(tempPath, _queueFilePath, overwrite: true);

            _logger.LogInformation("DLQ saved with {Count} items", queue.Count);
        }

        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving DLQ to file");

            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch
                {
                    // Ignore deletion errors of temp file
                }
            }

            throw;
        }
    }
}
