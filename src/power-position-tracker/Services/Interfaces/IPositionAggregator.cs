using NodaTime;
using power_position_tracker.Models;
using Services;

namespace power_position_tracker.Services.Interfaces;
public interface IPositionAggregator
{
    /// <summary>
    /// Aggregates power trades by period
    /// </summary>
    /// <param name="trades">Collection of trades to aggregate</param>
    /// <param name="targetDate">The trading date </param>
    /// <returns>Collection of aggregated positions with local time and total volume</returns>
    /// <exception cref="ArgumentNullException">Thrown when trades is null</exception>
    /// <exception cref="InvalidOperationException">Thrown when period count is not 24</exception>
    IEnumerable<AggregatedPosition> Aggregate(IEnumerable<PowerTrade> trades, LocalDate targetDate);
}
