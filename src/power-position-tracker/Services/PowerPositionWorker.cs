using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NodaTime;
using power_position_tracker.Models;
using power_position_tracker.Services.Interfaces;

namespace power_position_tracker.Services
{
    /// <summary>
    /// Background service that extracts power position data at regular intervals.
    /// Implements hybrid retry strategy with dead letter queue for guaranteed recovery.
    /// </summary>
    public class PowerPositionWorker : BackgroundService
    {
        private readonly IPowerTradeProvider _powerTradeProvider;
        private readonly IPositionAggregator _positionAggregator;
        private readonly IReportWriter _reportWriter;
        private readonly IExecutionAuditLogger _auditLogger;
        private readonly ILocalTimeProvider _localTimeProvider;
        private readonly IDeadLetterQueue _deadLetterQueue;
        private readonly PowerPositionSettings _settings;
        private readonly ILogger<PowerPositionWorker> _logger;

        public PowerPositionWorker(
            IPowerTradeProvider powerTradeProvider,
            IPositionAggregator positionAggregator,
            IReportWriter reportWriter,
            IExecutionAuditLogger auditLogger,
            ILocalTimeProvider localTimeProvider,
            IDeadLetterQueue deadLetterQueue,
            IOptions<PowerPositionSettings> settings,
            ILogger<PowerPositionWorker> logger)
        {
            _powerTradeProvider = powerTradeProvider ?? throw new ArgumentNullException(nameof(powerTradeProvider));
            _positionAggregator = positionAggregator ?? throw new ArgumentNullException(nameof(positionAggregator));
            _reportWriter = reportWriter ?? throw new ArgumentNullException(nameof(reportWriter));
            _auditLogger = auditLogger ?? throw new ArgumentNullException(nameof(auditLogger));
            _localTimeProvider = localTimeProvider ?? throw new ArgumentNullException(nameof(localTimeProvider));
            _deadLetterQueue = deadLetterQueue ?? throw new ArgumentNullException(nameof(deadLetterQueue));
            _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            ValidateConfigurations();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation(
                "PowerPositionWorker starting. Interval: {IntervalMinutes} minutes, TimeZone: {TimeZone}",
                _settings.IntervalMinutes,
                _settings.TimeZoneId);

            // Phase 1: Process DLQ on startup
            await ProcessDeadLetterQueueAsync(stoppingToken);

            // Phase 2: Main extraction loop
            _logger.LogInformation("Running initial extraction");
            await RunExtractionWithRetryAsync(stoppingToken);

            // Phase 3: Start periodic scheduled extractions
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(_settings.IntervalMinutes));

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Wait for next interval
                    await timer.WaitForNextTickAsync(stoppingToken);

                    if (stoppingToken.IsCancellationRequested)
                        break;

                    // Run extraction with retry logic
                    await RunExtractionWithRetryAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    // Graceful shutdown
                    _logger.LogInformation("PowerPositionWorker is stopping due to cancellation request.");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error in extraction loop. Service will continue.");
                }
            }
        }

        private async Task ProcessDeadLetterQueueAsync(CancellationToken stoppingToken)
        {
            try
            {
                var queueCount = await _deadLetterQueue.GetCountAsync(stoppingToken);

                if (queueCount == 0)
                {
                    _logger.LogInformation("Dead Letter Queue is empty on startup.");
                    return;
                }

                _logger.LogInformation("Processing {QueueCount} items from Dead Letter Queue on startup.", queueCount);

                var failedExtractions = await _deadLetterQueue.DequeueAllAsync(stoppingToken);

                foreach (var failedExtraction in failedExtractions)
                {
                    if (stoppingToken.IsCancellationRequested)
                    {
                        _logger.LogInformation("Cancellation requested during DLQ processing. Exiting.");
                        break;
                    }

                    // Use helper methods to convert UTC to local time for extraction
                    var extractionTimeLocal = failedExtraction.GetExtractionTimeLocal(_localTimeProvider.TimeZone);
                    var targetDate = failedExtraction.GetTargetDate(_localTimeProvider.TimeZone);

                    _logger.LogInformation(
                        $"Retrying failed extraction from DLQ: TargetDate={{TargetDate:{Constants.AuditDateFormat}}} (Previous attempts: {{RetryCount}})",
                        targetDate,
                        failedExtraction.RetryCount);

                    var success = await RunSingleExtractionAsync(
                        failedExtraction.ExtractionTimeUtc,
                        extractionTimeLocal,
                        targetDate,
                        failedExtraction.RetryCount + 1, 
                        stoppingToken);

                    if (success)
                    {
                        _logger.LogInformation(
                            $"Successfully recovered extraction from DLQ: TargetDate={{TargetDate:{Constants.AuditDateFormat}}}",
                            targetDate);
                    }

                    else
                    {
                        // Re-queue with updated retry count
                        var updatedFailedExtraction = new FailedExtraction(
                            failedExtraction.ExtractionTimeUtc, 
                            DateTime.UtcNow,
                            failedExtraction.RetryCount + 1,
                            LastError: "DLQ retry failed - all attempts exhausted");

                        await _deadLetterQueue.EnqueueAsync(updatedFailedExtraction, stoppingToken);

                        _logger.LogError(
                            "Failed to recover extraction from DLQ: TargetDate={TargetDate:yyyy-MM-dd}. Re-queued for next startup. Total attempts: {TotalAttempts}",
                            targetDate,
                            updatedFailedExtraction.RetryCount);
                    }
                }

                _logger.LogInformation("Completed processing Dead Letter Queue on startup.");
            }

            catch (Exception ex)
            {
               _logger.LogError(ex, "Failed to process Dead Letter Queue on startup.");

                // Swallow exception to allow main service to continue
            }
        }

        private async Task RunExtractionWithRetryAsync(CancellationToken stoppingToken)
        {
            var runTimeUtc = GetRunTimeUtc();
            var extractionTimeUtc = runTimeUtc;

            // Convert UTC to London time once at the start
            var instant = Instant.FromDateTimeUtc(extractionTimeUtc);
            var extractionTimeLocal = instant.InZone(_localTimeProvider.TimeZone);
            var targetDate = extractionTimeLocal.Date;

            _logger.LogInformation(
                $"Starting extraction for target date {{TargetDate:{Constants.AuditDateFormat}}} (ExtractionTime UTC: {{ExtractionTimeUtc:{Constants.AuditDateTimeFormat}}}, Local: {{ExtractionTimeLocal:{Constants.AuditDateTimeFormat}}})",
                targetDate,
                extractionTimeUtc,
                extractionTimeLocal);

            for (int attempt = 1; attempt <= _settings.RetryAttempts; attempt++)
            {
                if (stoppingToken.IsCancellationRequested)
                    return;

                _logger.LogInformation(
                    "Starting extraction attempt {Attempt}/{MaxAttempts} for target date {TargetDate:yyyy-MM-dd}",
                    attempt,
                    _settings.RetryAttempts,
                    targetDate);

                var success = await RunSingleExtractionAsync(
                    extractionTimeUtc, 
                    extractionTimeLocal, 
                    targetDate, 
                    attempt, 
                    stoppingToken);

                if (success)
                {
                    _logger.LogInformation(
                        $"Extraction completed successfully on attempt {{Attempt}} for target date {{TargetDate:{Constants.AuditDateFormat}}}",
                        attempt,
                        targetDate);

                    return;
                }

                if (attempt < _settings.RetryAttempts)
                {
                    var delaySeconds = _settings.RetryDelaySeconds;
                    _logger.LogWarning(
                        "Extraction attempt {Attempt} failed. Retrying in {DelaySeconds} seconds...",
                        attempt,
                        delaySeconds);

                    try 
                    { 
                        await Task.Delay(TimeSpan.FromSeconds(delaySeconds), stoppingToken);
                    }
                    catch (OperationCanceledException)
                    {
                        _logger.LogWarning("Retry delay cancelled due to service shutdown");
                        return;
                    }
                }
            }

            // All attempts exhausted - enqueue to DLQ
            _logger.LogError(
                "All {MaxAttempts} retry attempts failed for extraction {ExtractionTime}. Adding to dead letter queue.",
                _settings.RetryAttempts,
                extractionTimeUtc);

            var failedExtraction = new FailedExtraction(extractionTimeUtc, 
                                                        DateTime.UtcNow, 
                                                        _settings.RetryAttempts, 
                                                        LastError: "All retry attempts exhausted");

            await _deadLetterQueue.EnqueueAsync(failedExtraction, stoppingToken);
        }

        private async Task<bool> RunSingleExtractionAsync(
            DateTime extractionTimeUtc,
            ZonedDateTime extractionTimeLocal,
            LocalDate targetDate,
            int attempt, 
            CancellationToken stoppingToken)
        {
            string? reportFileName = null;
            string status = "FAILED";
            string? errorMessage = null;

            try
            {
                _logger.LogInformation(
                    $"Processing extraction attempt {{Attempt}} for target date {{TargetDate:{Constants.AuditDateFormat}}}",
                    attempt,
                    targetDate);

                // Step 1: Retrieve power trades
                var trades = await _powerTradeProvider.GetTradesAsync(targetDate);

                // Step 2: Aggregate positions
                var aggregatedPositions = _positionAggregator.Aggregate(trades, targetDate);

                if (aggregatedPositions is null || aggregatedPositions.Count() != 24)
                {
                    throw new InvalidOperationException(
                        $"Aggregation produced invalid result. Expected 24 periods, got {aggregatedPositions?.Count() ?? 0}");
                }

                // Step 3: Write report
                var filePath = await _reportWriter.WriteReportAsync(
                    aggregatedPositions,
                    extractionTimeLocal, 
                    stoppingToken);

                reportFileName = Path.GetFileName(filePath);

                // Step 4: Determine final status
                status = attempt > _settings.RetryAttempts ? "RecoveredFromDLQ" : "Done";

                _logger.LogInformation(
                    $"Extraction succeeded: TargetDate={{TargetDate:{Constants.AuditDateFormat}}}, Status={{Status}}, Attempt={{Attempt}}, ReportFile={{ReportFileName}}",
                    targetDate,
                    status,
                    attempt,
                    reportFileName);

                return true;
            }
            catch (OperationCanceledException)
            {
                status = "Cancelled";
                errorMessage = "Operation was cancelled.";
                _logger.LogWarning($"Extraction cancelled for target date {{TargetDate:{Constants.AuditDateFormat}}}", targetDate);
                return false;
            }
            catch (Exception ex)
            {
                status = attempt < _settings.RetryAttempts ? "RetryAttempt" : "Failed";
                errorMessage = ex.Message;

                _logger.LogError(
                    ex,
                    $"Extraction failed: TargetDate={{TargetDate:{Constants.AuditDateFormat}}}, Status={{Status}}, Attempt={{Attempt}}, Error={{ErrorMessage}}",
                    targetDate,
                    status,
                    attempt,
                    ex.Message);

                return false;
            }

            finally
            {
                try
                {
                    // Capture end time for audit logging
                    var endTimeInstant = Instant.FromDateTimeUtc(DateTime.UtcNow);
                    var endTimeLocal = endTimeInstant.InZone(_localTimeProvider.TimeZone);

                    // Always log audit record
                    await _auditLogger.LogExtractionCompletionAsync(
                        extractionTimeLocal,
                        endTimeLocal,
                        targetDate,
                        status,
                        attempt,
                        errorMessage,
                        reportFileName);
                }
                catch(Exception ex)
                {
                    _logger.LogError(
                        "Failed to log audit record for extraction targeting {TargetDate} (Attempt {Attempt})",
                        targetDate,
                        attempt);

                    // Swallow exception to not impact main flow
                }
            }
        }

        private DateTime GetRunTimeUtc()
        {
            var envRunTime = Environment.GetEnvironmentVariable("DOTNET_RUNTIME");

            // Priority 1: Check environment variable DOTNET_RUNTIME
            if (!string.IsNullOrWhiteSpace(envRunTime))
            {
                if (DateTime.TryParse(envRunTime, 
                                      null,
                                      System.Globalization.DateTimeStyles.RoundtripKind,
                                      out var parsedRunTime))
                {
                    var utcRunTime = parsedRunTime.ToUniversalTime();

                    _logger.LogInformation("Using DOTNET_RUNTIME environment variable for RunTime: {RunTime:O}", utcRunTime);
                    return utcRunTime;
                }
            }

            // Priority 2: Check configuration setting
            if (_settings.RunTime.HasValue)
            {
                var utcRunTime = _settings.RunTime.Value.ToUniversalTime();
                _logger.LogInformation("Using configured RunTime from settings: {RunTime} (UTC: {UtcTime})", _settings.RunTime, utcRunTime);

                return utcRunTime;
            }

            // Priority 3: Default to current UTC time
            return DateTime.UtcNow;
        }

        private void ValidateConfigurations()
        {
            var errors = new List<string>();

            if (_settings.IntervalMinutes <= 0)
                errors.Add($"IntervalMinutes must be greater than 0. Current value: {_settings.IntervalMinutes}");

            if (_settings.RetryAttempts < 1)
                errors.Add($"RetryAttempts must be at least 1. Current value: {_settings.RetryAttempts}");

            if (_settings.RetryDelaySeconds < 1)
                errors.Add($"RetryDelaySeconds must be at least 1. Current value: {_settings.RetryDelaySeconds}");

            if (string.IsNullOrWhiteSpace(_settings.OutputDirectory))
                errors.Add("OutputDirectory is required.");

            if (string.IsNullOrWhiteSpace(_settings.AuditDirectory))
                errors.Add("AuditDirectory is required.");

            if (string.IsNullOrWhiteSpace(_settings.TimeZoneId))
                errors.Add("TimeZoneId is required.");

            try
            {
                var tz = DateTimeZoneProviders.Tzdb[_settings.TimeZoneId];
                _logger.LogInformation("Validated TimeZoneId: {TimeZoneId}", _settings.TimeZoneId);
            }
            catch (Exception ex)
            {
                errors.Add($"TimeZoneId '{_settings.TimeZoneId}' is not a valid timezone.");
            }

            try
            {
                Directory.CreateDirectory(_settings.OutputDirectory);
                _logger.LogInformation("Validated OutputDirectory: {OutputDirectory}", _settings.OutputDirectory);
            }

            catch (Exception ex)
            {
                errors.Add($"OutputDirectory '{_settings.OutputDirectory}' is not accessible: {ex.Message}");
            }

            try
            {
                Directory.CreateDirectory(_settings.AuditDirectory);
                _logger.LogInformation("Validated AuditDirectory: {AuditDirectory}", _settings.AuditDirectory);
            }

            catch (Exception ex)
            {
                errors.Add($"AuditDirectory '{_settings.AuditDirectory}' is not accessible: {ex.Message}");
            }

            if (errors.Any())
            {
                var errorMessage = "Configuration validation failed: " + string.Join("; ", errors);
                _logger.LogError(errorMessage);
                throw new InvalidOperationException(errorMessage);
            }

            _logger.LogInformation("All configurations validated successfully.");
        }
    }
}
