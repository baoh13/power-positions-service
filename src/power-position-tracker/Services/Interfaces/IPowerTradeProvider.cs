using NodaTime;
using Services;

namespace power_position_tracker.Services.Interfaces
{
    /// <summary>
    /// Interface for retrieving power trading position data from the external PowerService API.
    /// Provides an abstraction layer over PowerService.dll for dependency injection and testing.
    /// </summary>
    public interface IPowerTradeProvider
    {
        /// <summary>
        /// Retrieves power trades for the specified trading date.
        /// A trading day spans from 23:00 on the previous calendar day through 22:00 on the target date.
        /// </summary>
        /// <param name="targetDate">The trading date in London local time</param>
        /// <returns>Collection of power trades for the 24 periods in the trading day</returns>
        Task<IEnumerable<PowerTrade>> GetTradesAsync(LocalDate targetDate);
    }
}
