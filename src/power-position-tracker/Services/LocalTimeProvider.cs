using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NodaTime;

namespace power_position_tracker.Services;

/// <summary>
/// Provides London local time operations with automatic BST/GMT transition handling.
/// </summary>
public class LocalTimeProvider : Services.Interfaces.ILocalTimeProvider
{
    private readonly ILogger<LocalTimeProvider> _logger;
    private readonly DateTimeZone _timeZone;

    public LocalTimeProvider(
        ILogger<LocalTimeProvider> logger,
        IOptions<PowerPositionSettings> settings)
    {
        _logger = logger;

        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings), "PowerPositionSettings cannot be null");
        }

        var timeZoneId = settings.Value.TimeZoneId ?? "Europe/London";
        _timeZone = DateTimeZoneProviders.Tzdb[timeZoneId];

        _logger.LogInformation($"LocalTimeProvider initialized with timezone: {timeZoneId}");
    }

    /// <inheritdoc/>
    public DateTimeZone TimeZone => _timeZone;

    /// <inheritdoc/>
    public ZonedDateTime GetTradingDayStart(LocalDate localDate)
    {
        // Trading day starts at 23:00 on the previous calendar day
        var previousDay = localDate.PlusDays(-1);
        var tradingDayStartLocal = previousDay.At(new LocalTime(23, 0));

        ZonedDateTime tradingDayStart;

        try
        {
            // AtStrictly will throw if the time is ambiguous or invalid (e.g., during GMT/BST transitions)
            tradingDayStart = _timeZone.AtStrictly(tradingDayStartLocal);
        }
        catch (AmbiguousTimeException)
        {
            _logger.LogError($"Ambiguous time encountered for trading day start at {tradingDayStartLocal} in timezone {_timeZone.Id}");

            // Resolve ambiguity by choosing the earlier time mapping
            tradingDayStart = _timeZone.AtLeniently(tradingDayStartLocal);
        }
        catch (SkippedTimeException)
        {
            _logger.LogError($"Skipped time encountered for trading day start at {tradingDayStartLocal} in timezone {_timeZone.Id}");

            // Resolve skipped time to the next valid time
            tradingDayStart = _timeZone.AtLeniently(tradingDayStartLocal);
        }

        _logger.LogInformation($"Trading day start for local date {localDate} is {tradingDayStart}");

        return tradingDayStart;
    }

    /// <inheritdoc/>
    public ZonedDateTime PeriodToZonedDateTime(ZonedDateTime tradingDayStart, int period)
    {
        if (period < 1 || period > 24)
        {
            throw new ArgumentOutOfRangeException(nameof(period), "Period must be between 1 and 24");
        }

        // Period 1 = tradingDayStart + 0 hours (23:00 previous day)
        // Period 2 = tradingDayStart + 1 hour (00:00 current day)
        // Period 24 = tradingDayStart + 23 hours (22:00 current day)
        return tradingDayStart.Plus(Duration.FromHours(period - 1));
    }

    /// <inheritdoc/>
    public ZonedDateTime ToLocalTime(DateTime utcDateTime)
    {
        var instant = Instant.FromDateTimeUtc(DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc));
        return instant.InZone(_timeZone);
    }

     /// <inheritdoc/>
    public string FormatToLocalTime(ZonedDateTime zonedDateTime)
    {
        return zonedDateTime.LocalDateTime.TimeOfDay.ToString("HH:mm", null);
    }
}
