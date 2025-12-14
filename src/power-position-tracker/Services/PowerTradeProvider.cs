using Microsoft.Extensions.Logging;
using NodaTime;
using power_position_tracker.Services.Interfaces;
using Services;

namespace power_position_tracker.Services
{
    /// <summary>
    /// Wraps the external PowerService.dll API.
    /// </summary>
    public class PowerTradeProvider : IPowerTradeProvider
    {
        private readonly ILogger<PowerTradeProvider> _logger;
        private readonly IPowerService _powerService;

        public PowerTradeProvider(IPowerService powerService, ILogger<PowerTradeProvider> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _powerService = powerService ?? throw new ArgumentNullException(nameof(powerService));           
        }

        /// <summary>
        /// Retrieves power trades for the specified trading date with retry logic and validation.
        /// </summary>
        /// <param name="targetDate">The trading date </param>
        /// <returns>Collection of power trades for the 24 periods in the trading day</returns>
        public async Task<IEnumerable<PowerTrade>> GetTradesAsync(LocalDate targetDate)
        {
            _logger.LogInformation($"Retrieving power trades for date: {{TargetDate:{Constants.AuditDateFormat}}}", targetDate);
            
            // Convert LocalDate to DateTime for PowerService API (expects midnight)
            var targetDateTime = targetDate.AtMidnight().ToDateTimeUnspecified();
            _logger.LogInformation($"Calling PowerService to get trades for date: {{TargetDate:{Constants.AuditDateFormat}}}", targetDate);

            try
            {
                var trades = await _powerService.GetTradesAsync(targetDateTime);

                _logger.LogInformation($"Retrieved {{TradeCount}} trades from PowerService for date: {{TargetDate:{Constants.AuditDateFormat}}}", trades?.Count() ?? 0, targetDate);

                return trades ?? Enumerable.Empty<PowerTrade>();
            }
            catch (PowerServiceException ex)
            {
                _logger.LogError(ex, $"PowerService error retrieving trades for date: {{TargetDate:{Constants.AuditDateFormat}}}, Error: {{ErrorMessage}}", targetDate, ex.Message);
                throw;
            }
        }
    }
}
