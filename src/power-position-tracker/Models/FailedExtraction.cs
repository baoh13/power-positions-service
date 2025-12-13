using NodaTime;

namespace power_position_tracker.Models;

/// <summary>
/// Represents a failed extraction stored in the dead letter queue.
/// All timestamps are in UTC for unambiguous persistence across timezone transitions.
/// </summary>
public record FailedExtraction(DateTime ExtractionTimeUtc, DateTime FailedAtUtc, int RetryCount, string LastError)
{
    /// <summary>
    /// Helper method to get the target date in the specified timezone for processing.
    /// </summary>
    public LocalDate GetTargetDate(DateTimeZone timeZone)
    {
        var instant = Instant.FromDateTimeUtc(ExtractionTimeUtc);
        return instant.InZone(timeZone).Date;
    }

    /// <summary>
    /// Helper method to get the extraction time in the specified timezone.
    /// </summary>
    public ZonedDateTime GetExtractionTimeLocal(DateTimeZone timeZone)
    {
        var instant = Instant.FromDateTimeUtc(ExtractionTimeUtc);
        return instant.InZone(timeZone);
    }
}
