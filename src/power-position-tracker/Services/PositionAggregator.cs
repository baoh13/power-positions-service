using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NodaTime;
using power_position_tracker.Models;
using power_position_tracker.Services.Interfaces;
using Services;

namespace power_position_tracker.Services;
public class PositionAggregator : IPositionAggregator
{
    private readonly ILocalTimeProvider _localTimeProvider;
    private readonly ILogger<PositionAggregator> _logger;

    public PositionAggregator(
        ILocalTimeProvider localTimeProvider,
        ILogger<PositionAggregator> logger)
    {
        _localTimeProvider = localTimeProvider ?? throw new ArgumentNullException(nameof(localTimeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public IEnumerable<AggregatedPosition> Aggregate(IEnumerable<PowerTrade> trades, LocalDate targetDate)
    {
        if (trades is null)
        {
            _logger.LogError("Trades collection is null");
            throw new ArgumentNullException(nameof(trades), "Trades collection cannot be null");
        }

        _logger.LogInformation($"Aggregating trades for target date: {targetDate.ToString(Constants.AuditDateFormat, null)}");

        var allPeriods = trades.SelectMany(t => t.Periods).ToList();
        var periodCount = allPeriods.Count;

        if (periodCount == 0 || periodCount % 24 != 0)
        {
            var tradeCount = periodCount / 24.0;
            _logger.LogError($"Invalid total period count: {allPeriods.Count} for target date: {targetDate.ToString(Constants.AuditDateFormat, null)}");
            throw new InvalidOperationException(
                $"Expected period count to be a multiple of 24, but found {allPeriods.Count} periods ({tradeCount:F1} trades).");
        }

        _logger.LogInformation($"Total periods retrieved: {allPeriods.Count}");

        // Group by period and sum volumes
        var aggregatedPositions = allPeriods.GroupBy(p => p.Period)
                                            .Select(g => new { Period = g.Key, TotalVolume = g.Sum(p => p.Volume) })
                                            .OrderBy(p => p.Period)
                                            .ToList();

        var tradingDayStart = _localTimeProvider.GetTradingDayStart(targetDate);

        _logger.LogInformation($"Trading day starts at: {tradingDayStart} (Period 1 = 23:00 on {targetDate.PlusDays(-1)})");

        // Map periods to aggregated positions with local time
        return aggregatedPositions.Select(p =>
        {
            var periodTime = _localTimeProvider.PeriodToZonedDateTime(tradingDayStart, p.Period);
            var localTimeString = _localTimeProvider.FormatToLocalTime(periodTime);

            return new AggregatedPosition(localTimeString, p.TotalVolume, p.Period);
        }).ToList();
    }
}
