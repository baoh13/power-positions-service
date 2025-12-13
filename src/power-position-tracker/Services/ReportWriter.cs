using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NodaTime;
using power_position_tracker.Models;
using power_position_tracker.Services.Interfaces;
using System.Text;

namespace power_position_tracker.Services;

/// <summary>
/// Writes power position reports to CSV files
/// </summary>
public class ReportWriter : IReportWriter, IDisposable
{
    private readonly ILogger<ReportWriter> _logger;
    private readonly PowerPositionSettings _settings;
    private readonly SemaphoreSlim _fileLock = new SemaphoreSlim(1, 1);
    private bool _disposed = false;

    /// <inheritdoc/>
    public ReportWriter(
        ILogger<ReportWriter> logger, 
        IOptions<PowerPositionSettings> settings)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));

        if (string.IsNullOrWhiteSpace(_settings.OutputDirectory))
            throw new ArgumentException("Output directory is required.");
    }

    /// <inheritdoc/>
    public async Task<string> WriteReportAsync(
        IEnumerable<AggregatedPosition> positions, 
        ZonedDateTime extractionTime, 
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (positions is null)
        {
            throw new ArgumentNullException(nameof(positions), "Positions collection cannot be null");
        }

        var positionList = positions.ToList();

        if (positionList.Count != 24)
        {
            _logger.LogWarning("Expected 24 aggregated positions, but received {PositionCount}", positionList.Count);
        }

        // Generate filename: PowerPosition_YYYYMMDD_HHMM.csv
        var fileName = $"PowerPosition_{extractionTime:yyyyMMdd_HHmm}.csv";
        var filePath = Path.Combine(_settings.OutputDirectory, fileName);

        await _fileLock.WaitAsync(cancellationToken);

        try
        {
            Directory.CreateDirectory(_settings.OutputDirectory);

            var csv = BuildCsvContent(positionList);

            await File.WriteAllTextAsync(filePath, csv, Encoding.UTF8, cancellationToken);

            _logger.LogInformation(
                                    "Report written successfully: {FilePath} ({Periods} periods, {Size} bytes)",
                                    filePath,
                                    positionList.Count,
                                    csv.Length);

            return filePath;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Report writing was cancelled for file: {FilePath}", filePath);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error writing report to file: {FilePath}", filePath);
            throw;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <summary>
    /// Disposes managed resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _fileLock.Dispose();
        _disposed = true;
    }

    private string BuildCsvContent(IEnumerable<AggregatedPosition> positionList)
    {
        var sb = new StringBuilder();

        // Header row
        sb.AppendLine("LocalTime,Volume");

        // Data rows ordered by Period (1-24)
        // Period 1 = 23:00 previous day, Period 2 = 00:00, ..., Period 24 = 22:00
        foreach (var position in positionList.OrderBy(p => p.period))
        {
            // Format: HH:mm,Volume - 23:00,150.50
            sb.AppendLine($"{position.localTime:HH:mm},{position.volume:F2}");
        }

        return sb.ToString();
    }
}
