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

        // Create directory once during initialization
        Directory.CreateDirectory(_settings.OutputDirectory);
        
        _logger.LogInformation("Output directory initialized: {OutputDirectory}", _settings.OutputDirectory);
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
            var csv = BuildCsvContent(positionList);

            await File.WriteAllTextAsync(filePath, csv, Encoding.UTF8, cancellationToken);

            _logger.LogInformation(
                                    "Report written successfully: {FilePath} ({Periods} periods, {Size} bytes)",
                                    filePath,
                                    positionList.Count,
                                    csv.Length);

            return filePath;
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

    private static string BuildCsvContent(IEnumerable<AggregatedPosition> positions)
    {
        var lines = positions
            .OrderBy(p => p.Period)
            .Select(p => $"{p.LocalTime},{p.Volume:F2}");
        
        return "LocalTime,Volume" + Environment.NewLine + string.Join(Environment.NewLine, lines);
    }
}
