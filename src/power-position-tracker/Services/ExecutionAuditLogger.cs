using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NodaTime;
using power_position_tracker.Services.Interfaces;
using System.Text;

namespace power_position_tracker.Services;
public class ExecutionAuditLogger : IExecutionAuditLogger, IDisposable
{
    private readonly ILogger<ExecutionAuditLogger> _logger;
    private readonly PowerPositionSettings _settings;
    private bool _disposed = false;
    private readonly SemaphoreSlim _fileLock = new SemaphoreSlim(1, 1);

    public ExecutionAuditLogger(
        IOptions<PowerPositionSettings> settings,
        ILogger<ExecutionAuditLogger> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));

        Directory.CreateDirectory(_settings.AuditDirectory);
    }

    public async Task LogExtractionCompletionAsync(
        ZonedDateTime startTime, 
        ZonedDateTime endTime, 
        LocalDate targetDate, 
        string status, 
        int attempt, 
        string? errorMessage = null, 
        string? reportFileName = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (attempt < 1)
            throw new ArgumentOutOfRangeException(nameof(attempt), "Attempt number must be greater than zero.");

        if (string.IsNullOrWhiteSpace(status))
            throw new ArgumentException("Status is required.", nameof(status));

        await LogAuditRecordAsync(startTime, endTime, targetDate, status, attempt, errorMessage, reportFileName);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _fileLock.Dispose();
        _disposed = true;
    }

    private async Task LogAuditRecordAsync(
        ZonedDateTime startTime,
        ZonedDateTime endTime,
        LocalDate targetDate,
        string status, 
        int attempt, 
        string? errorMessage, 
        string? reportFileName)
    {
        var duration = endTime.ToInstant() - startTime.ToInstant();

        // Generate daily audit log file name: ExecutionAudit_YYYYMMDD.csv
        var fileName = $"ExecutionAudit_{endTime:yyyyMMdd}.csv";
        var filePath = Path.Combine(_settings.AuditDirectory, fileName);

        await _fileLock.WaitAsync();

        try
        {            
            var isNewFile = !File.Exists(filePath);

            // Format: StartTimeLocal,EndTimeLocal,TargetDate,DurationSeconds,Status,Attempt,ErrorMessage,ReportFileName
            var csvLine = BuildCsvLine(startTime, endTime, targetDate, duration, status, attempt, errorMessage, reportFileName);

            using var writer = new StreamWriter(filePath, append: true, Encoding.UTF8);

            if (isNewFile)
            {
                await writer.WriteLineAsync("StartTimeLocal,EndTimeLocal,TargetDate,DurationSeconds,Status,Attempt,ErrorMessage,ReportFileName");
                _logger.LogInformation("Created new audit log file at {FilePath}", filePath);
            }

            await writer.WriteLineAsync(csvLine);
            _logger.LogInformation(
                "Audit record written: File={FileName}, Status={Status}, Attempt={Attempt}, Duration={Duration:F2}s",
                fileName,
                status,
                attempt,
                duration.TotalSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log audit record for extraction targeting {TargetDate} (Attempt {Attempt})", targetDate, attempt);
            // Swallow exception to avoid impacting main flow
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private string BuildCsvLine(
        ZonedDateTime startTime,
        ZonedDateTime endTime, 
        LocalDate targetDate,
        Duration duration,
        string status, 
        int attempt, 
        string? errorMessage, 
        string? reportFileName)
    {
        // Format timestamps as London local time: yyyy-MM-dd HH:mm:ss
        var startTimeStr = startTime.ToString(Constants.AuditDateTimeFormat, null);
        var endTimeStr = endTime.ToString(Constants.AuditDateTimeFormat, null);
        var targetDateStr = targetDate.ToString(Constants.AuditDateFormat, null);
        var durationStr = duration.TotalSeconds.ToString("F2");

        var escapedStatus = EscapeCsvField(status);
        var escapedErrorMessage = EscapeCsvField(errorMessage ?? string.Empty);
        var escapedReportFileName = EscapeCsvField(reportFileName ?? string.Empty);

        return $"{startTimeStr},{endTimeStr},{targetDateStr},{durationStr},{escapedStatus},{attempt},{escapedErrorMessage},{escapedReportFileName}";
    }

    /// <summary>
    /// Escapes a CSV field by wrapping in quotes and doubling internal quotes if needed.
    /// </summary>
    private static string EscapeCsvField(string field)
    {
        if (string.IsNullOrWhiteSpace(field))
            return string.Empty;

        // Characters that require CSV field escaping
        char[] csvSpecialChars = Constants.CsvSpecialChars;
        
        if (field.IndexOfAny(csvSpecialChars) >= 0)
        {
            // Replace " with "" (CSV standard) and wrap in quotes
            return $"\"{field.Replace("\"", "\"\"")}\"";
        }

        return field;
    }
}
