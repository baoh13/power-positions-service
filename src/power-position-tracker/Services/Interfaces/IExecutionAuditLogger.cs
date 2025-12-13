using NodaTime;

namespace power_position_tracker.Services.Interfaces;

/// <summary>
/// Responsible for logging execution audit records.
/// </summary>
public interface IExecutionAuditLogger
{
    /// <summary>
    /// Logs the completion of an extraction attempt.
    /// </summary>
    /// <param name="startTime">Start time in London local time</param>
    /// <param name="endTime">End time in London local time</param>
    /// <param name="targetDate">Target date for extraction (London local date)</param>
    /// <param name="status">Final status (Done, Failed, RecoveredFromDLQ, RetryAttempt, Cancelled)</param>
    /// <param name="attempt">Attempt number</param>
    /// <param name="errorMessage">Optional error message</param>
    /// <param name="reportFileName">Optional report file name</param>
    /// <returns></returns>
    Task LogExtractionCompletionAsync(
        ZonedDateTime startTime,
        ZonedDateTime endTime,
        LocalDate targetDate,
        string status,
        int attempt,
        string? errorMessage = null,
        string? reportFileName = null);
}
