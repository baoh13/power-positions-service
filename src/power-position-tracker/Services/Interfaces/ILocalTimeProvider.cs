using NodaTime;

namespace power_position_tracker.Services.Interfaces;
public interface ILocalTimeProvider
{
    /// <summary>
    /// Gets the configured timezone (default: Europe/London)
    /// </summary>
    DateTimeZone TimeZone { get; }

    /// <summary>
    /// Converts a UTC DateTime to a ZonedDateTime in the configured timezone.
    /// </summary>
    /// <param name="utcDateTime">DateTime in UTC</param>
    /// <returns>ZonedDateTime in configured timezone</returns>
    ZonedDateTime ToLocalTime(DateTime utcDateTime);

    /// <summary>
    /// Gets the trading day start time for a given local date.
    /// Trading day starts at 23:00 on the previous calendar day in the local timezone.
    /// </summary>
    /// <param name="localDate">The target trading day date</param>
    /// <returns>ZonedDateTime representing 23:00 on the previous day</returns>
    ZonedDateTime GetTradingDayStart(LocalDate localDate);

    /// <summary>
    /// Converts a period number (1-24) to a ZonedDateTime for a given trading day
    /// </summary>
    /// <param name="tradingDayStart">The trading day start time (23:00 previous day)</param>
    /// <param name="period">period number (1-24)</param>
    /// <returns>ZonedDateTime representing the period's time</returns>
    ZonedDateTime PeriodToZonedDateTime(ZonedDateTime tradingDayStart, int period);

    /// <summary>
    /// Formats a ZonedDateTime to local time string (HH:mm)
    /// </summary>
    /// <param name="zonedDateTime">The ZonedDateTime to format</param>
    /// <returns>The string in HH:mm format</returns>
    string FormatToLocalTime(ZonedDateTime zonedDateTime);
}
