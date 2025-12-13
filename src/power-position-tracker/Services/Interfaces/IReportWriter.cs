using NodaTime;
using power_position_tracker.Models;

namespace power_position_tracker.Services.Interfaces;

/// <summary>
/// Writes power position reports.
/// </summary>
public interface IReportWriter
{
    /// <summary>
    /// Writes aggregated position data to a file in the configured output directory
    /// </summary>
    /// <param name="positions">Collection of 24 aggregated positions (one per hour) to write</param>
    /// <param name="extractionTime">London local time when extraction occurred (used for filename)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Full path to the generated file</returns>
    Task<string> WriteReportAsync(
        IEnumerable<AggregatedPosition> positions,
        ZonedDateTime extractionTime,
        CancellationToken cancellationToken);
}
