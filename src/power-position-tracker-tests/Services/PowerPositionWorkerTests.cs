using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NodaTime;
using power_position_tracker;
using power_position_tracker.Models;
using power_position_tracker.Services;
using power_position_tracker.Services.Interfaces;
using Services;

namespace power_position_tracker_tests.Services;

[TestFixture]
public class PowerPositionWorkerTests
{
    private Mock<IPowerTradeProvider> _mockPowerTradeProvider;
    private Mock<IPositionAggregator> _mockPositionAggregator;
    private Mock<IReportWriter> _mockReportWriter;
    private Mock<IExecutionAuditLogger> _mockAuditLogger;
    private Mock<ILocalTimeProvider> _mockLocalTimeProvider;
    private Mock<IDeadLetterQueue> _mockDeadLetterQueue;
    private Mock<ILogger<PowerPositionWorker>> _mockLogger;
    private PowerPositionSettings _settings;
    private IOptions<PowerPositionSettings> _options;
    private DateTimeZone _londonTz;

    [SetUp]
    public void Setup()
    {
        _mockPowerTradeProvider = new Mock<IPowerTradeProvider>();
        _mockPositionAggregator = new Mock<IPositionAggregator>();
        _mockReportWriter = new Mock<IReportWriter>();
        _mockAuditLogger = new Mock<IExecutionAuditLogger>();
        _mockLocalTimeProvider = new Mock<ILocalTimeProvider>();
        _mockDeadLetterQueue = new Mock<IDeadLetterQueue>();
        _mockLogger = new Mock<ILogger<PowerPositionWorker>>();

        _londonTz = DateTimeZoneProviders.Tzdb["Europe/London"];

        _settings = new PowerPositionSettings
        {
            IntervalMinutes = 5,
            OutputDirectory = "C:\\Test\\Output",
            AuditDirectory = "C:\\Test\\Audit",
            DlqDirectory = "C:\\Test\\Dlq",
            TimeZoneId = "Europe/London",
            RunTime = null,
            RetryAttempts = 3,
            RetryDelaySeconds = 1, // Use 1 second for tests
        };

        _options = Options.Create(_settings);
        _mockLocalTimeProvider.Setup(x => x.TimeZone).Returns(_londonTz);

        Directory.CreateDirectory(_settings.OutputDirectory);
        Directory.CreateDirectory(_settings.AuditDirectory);
        Directory.CreateDirectory(_settings.DlqDirectory);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_settings.OutputDirectory))
            Directory.Delete(_settings.OutputDirectory, true);
        if (Directory.Exists(_settings.AuditDirectory))
            Directory.Delete(_settings.AuditDirectory, true);
        if (Directory.Exists(_settings.DlqDirectory))
            Directory.Delete(_settings.DlqDirectory, true);

        Environment.SetEnvironmentVariable("DOTNET_RUNTIME", null);
    }

    #region Constructor Tests

    [Test]
    public void Constructor_WithNullPowerTradeProvider_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new PowerPositionWorker(
            null!,
            _mockPositionAggregator.Object,
            _mockReportWriter.Object,
            _mockAuditLogger.Object,
            _mockLocalTimeProvider.Object,
            _mockDeadLetterQueue.Object,
            _options,
            _mockLogger.Object));
    }

    [Test]
    public void Constructor_WithNullPositionAggregator_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new PowerPositionWorker(
            _mockPowerTradeProvider.Object,
            null!,
            _mockReportWriter.Object,
            _mockAuditLogger.Object,
            _mockLocalTimeProvider.Object,
            _mockDeadLetterQueue.Object,
            _options,
            _mockLogger.Object));
    }

    [Test]
    public void Constructor_WithNullReportWriter_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new PowerPositionWorker(
            _mockPowerTradeProvider.Object,
            _mockPositionAggregator.Object,
            null!,
            _mockAuditLogger.Object,
            _mockLocalTimeProvider.Object,
            _mockDeadLetterQueue.Object,
            _options,
            _mockLogger.Object));
    }

    [Test]
    public void Constructor_WithNullAuditLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new PowerPositionWorker(
            _mockPowerTradeProvider.Object,
            _mockPositionAggregator.Object,
            _mockReportWriter.Object,
            null!,
            _mockLocalTimeProvider.Object,
            _mockDeadLetterQueue.Object,
            _options,
            _mockLogger.Object));
    }

    [Test]
    public void Constructor_WithNullLocalTimeProvider_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new PowerPositionWorker(
            _mockPowerTradeProvider.Object,
            _mockPositionAggregator.Object,
            _mockReportWriter.Object,
            _mockAuditLogger.Object,
            null!,
            _mockDeadLetterQueue.Object,
            _options,
            _mockLogger.Object));
    }

    [Test]
    public void Constructor_WithNullDeadLetterQueue_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new PowerPositionWorker(
            _mockPowerTradeProvider.Object,
            _mockPositionAggregator.Object,
            _mockReportWriter.Object,
            _mockAuditLogger.Object,
            _mockLocalTimeProvider.Object,
            null!,
            _options,
            _mockLogger.Object));
    }

    [Test]
    public void Constructor_WithNullSettings_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new PowerPositionWorker(
            _mockPowerTradeProvider.Object,
            _mockPositionAggregator.Object,
            _mockReportWriter.Object,
            _mockAuditLogger.Object,
            _mockLocalTimeProvider.Object,
            _mockDeadLetterQueue.Object,
            null!,
            _mockLogger.Object));
    }

    [Test]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new PowerPositionWorker(
            _mockPowerTradeProvider.Object,
            _mockPositionAggregator.Object,
            _mockReportWriter.Object,
            _mockAuditLogger.Object,
            _mockLocalTimeProvider.Object,
            _mockDeadLetterQueue.Object,
            _options,
            null!));
    }

    #endregion

    #region Configuration Validation Tests

    [Test]
    public void Constructor_WithInvalidIntervalMinutes_ThrowsInvalidOperationException()
    {
        // Arrange
        _settings.IntervalMinutes = 0;

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => CreateWorker());
        Assert.That(ex.Message, Does.Contain("IntervalMinutes must be greater than 0"));
    }

    [Test]
    public void Constructor_WithInvalidRetryAttempts_ThrowsInvalidOperationException()
    {
        // Arrange
        _settings.RetryAttempts = 0;

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => CreateWorker());
        Assert.That(ex.Message, Does.Contain("RetryAttempts must be at least 1"));
    }

    [Test]
    public void Constructor_WithInvalidRetryDelaySeconds_ThrowsInvalidOperationException()
    {
        // Arrange
        _settings.RetryDelaySeconds = 0;

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => CreateWorker());
        Assert.That(ex.Message, Does.Contain("RetryDelaySeconds must be at least 1"));
    }

    [Test]
    public void Constructor_WithEmptyOutputDirectory_ThrowsInvalidOperationException()
    {
        // Arrange
        _settings.OutputDirectory = "";

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => CreateWorker());
        Assert.That(ex.Message, Does.Contain("OutputDirectory is required"));
    }

    [Test]
    public void Constructor_WithEmptyAuditDirectory_ThrowsInvalidOperationException()
    {
        // Arrange
        _settings.AuditDirectory = "";

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => CreateWorker());
        Assert.That(ex.Message, Does.Contain("AuditDirectory is required"));
    }

    [Test]
    public void Constructor_WithEmptyTimeZoneId_ThrowsInvalidOperationException()
    {
        // Arrange
        _settings.TimeZoneId = "";

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => CreateWorker());
        Assert.That(ex.Message, Does.Contain("TimeZoneId is required"));
    }

    [Test]
    public void Constructor_WithInvalidTimeZoneId_ThrowsInvalidOperationException()
    {
        // Arrange
        _settings.TimeZoneId = "Invalid/TimeZone";

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => CreateWorker());
        Assert.That(ex.Message, Does.Contain("TimeZoneId 'Invalid/TimeZone' is not a valid timezone"));
    }

    [Test]
    public void Constructor_WithValidSettings_DoesNotThrow()
    {
        // Act & Assert
        Assert.DoesNotThrow(() => CreateWorker());
    }

    #endregion

    #region GetRunTimeUtc Tests

    [Test]
    public void GetRunTimeUtc_WithEnvironmentVariable_ReturnsEnvironmentVariableValue()
    {
        // Arrange
        var expectedTime = new DateTime(2025, 12, 10, 14, 30, 0, DateTimeKind.Utc);
        Environment.SetEnvironmentVariable("DOTNET_RUNTIME", expectedTime.ToString("O"));

        _mockDeadLetterQueue.Setup(x => x.GetCountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        SetupSuccessfulExtraction();

        var worker = CreateWorker();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // Act
        var task = worker.StartAsync(cts.Token);

        // Give it time to start
        Thread.Sleep(500);
        cts.Cancel();

        // Assert - verify the logger was called with the environment variable time
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Using DOTNET_RUNTIME environment variable")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [Test]
    public void GetRunTimeUtc_WithConfiguredRunTime_ReturnsConfiguredValue()
    {
        // Arrange
        var expectedTime = new DateTime(2025, 12, 10, 14, 30, 0, DateTimeKind.Utc);
        _settings.RunTime = expectedTime;

        _mockDeadLetterQueue.Setup(x => x.GetCountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        SetupSuccessfulExtraction();

        var worker = CreateWorker();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // Act
        var task = worker.StartAsync(cts.Token);

        // Give it time to start
        Thread.Sleep(500);
        cts.Cancel();

        // Assert - verify the logger was called with configured runtime
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Using configured RunTime from settings")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [Test]
    public void GetRunTimeUtc_WithoutAnyOverride_UsesCurrentTime()
    {
        // Arrange
        _settings.RunTime = null;
        Environment.SetEnvironmentVariable("DOTNET_RUNTIME", null);

        _mockDeadLetterQueue.Setup(x => x.GetCountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        SetupSuccessfulExtraction();

        var worker = CreateWorker();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // Act
        var task = worker.StartAsync(cts.Token);

        // Give it time to start
        Thread.Sleep(500);
        cts.Cancel();

        // Assert - should not log about overrides
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("DOTNET_RUNTIME")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    #endregion

    #region Successful Extraction Flow Tests

    [Test]
    public async Task ExecuteAsync_SuccessfulExtraction_CompletesWithoutErrors()
    {
        // Arrange
        _mockDeadLetterQueue.Setup(x => x.GetCountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        SetupSuccessfulExtraction();

        var worker = CreateWorker();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));

        // Act
        await worker.StartAsync(cts.Token);
        await Task.Delay(500); // Allow extraction to run
        await worker.StopAsync(cts.Token);

        // Assert
        _mockPowerTradeProvider.Verify(
            x => x.GetTradesAsync(It.IsAny<LocalDate>()),
            Times.AtLeastOnce);

        _mockPositionAggregator.Verify(
            x => x.Aggregate(It.IsAny<IEnumerable<PowerTrade>>(), It.IsAny<LocalDate>()),
            Times.AtLeastOnce);

        _mockReportWriter.Verify(
            x => x.WriteReportAsync(
                It.IsAny<IEnumerable<AggregatedPosition>>(),
                It.IsAny<ZonedDateTime>(),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);

        _mockAuditLogger.Verify(
            x => x.LogExtractionCompletionAsync(
                It.IsAny<ZonedDateTime>(),
                It.IsAny<ZonedDateTime>(),
                It.IsAny<LocalDate>(),
                "Done",
                1,
                null,
                It.IsAny<string>()),
            Times.AtLeastOnce);
    }

    [Test]
    public async Task ExecuteAsync_SuccessfulExtraction_LogsStatusAsDone()
    {
        // Arrange
        _mockDeadLetterQueue.Setup(x => x.GetCountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        SetupSuccessfulExtraction();

        var worker = CreateWorker();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));

        // Act
        await worker.StartAsync(cts.Token);
        await Task.Delay(500);
        await worker.StopAsync(cts.Token);

        // Assert
        _mockAuditLogger.Verify(
            x => x.LogExtractionCompletionAsync(
                It.IsAny<ZonedDateTime>(),
                It.IsAny<ZonedDateTime>(),
                It.IsAny<LocalDate>(),
                "Done",
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<string>()),
            Times.AtLeastOnce);
    }

    [Test]
    public async Task ExecuteAsync_SuccessfulExtraction_WritesCorrectNumberOfPositions()
    {
        // Arrange
        _mockDeadLetterQueue.Setup(x => x.GetCountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        SetupSuccessfulExtraction();

        var worker = CreateWorker();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));

        // Act
        await worker.StartAsync(cts.Token);
        await Task.Delay(500);
        await worker.StopAsync(cts.Token);

        // Assert
        _mockReportWriter.Verify(
            x => x.WriteReportAsync(
                It.Is<IEnumerable<AggregatedPosition>>(p => p.Count() == 24),
                It.IsAny<ZonedDateTime>(),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    #endregion

    #region Error Handling Tests

    [Test]
    public async Task ExecuteAsync_PowerTradeProviderThrows_RetriesAndLogsError()
    {
        // Arrange
        _mockDeadLetterQueue.Setup(x => x.GetCountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        _mockPowerTradeProvider
            .Setup(x => x.GetTradesAsync(It.IsAny<LocalDate>()))
            .ThrowsAsync(new Exception("PowerService connection failed"));

        _mockAuditLogger
            .Setup(x => x.LogExtractionCompletionAsync(
                It.IsAny<ZonedDateTime>(),
                It.IsAny<ZonedDateTime>(),
                It.IsAny<LocalDate>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        var worker = CreateWorker();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Act
        await worker.StartAsync(cts.Token);
        await Task.Delay(5000); // Allow retries (3 attempts + 2 delays of 1 second each)
        await worker.StopAsync(cts.Token);

        // Assert - should retry 3 times
        _mockPowerTradeProvider.Verify(
            x => x.GetTradesAsync(It.IsAny<LocalDate>()),
            Times.Exactly(3));

        _mockAuditLogger.Verify(
            x => x.LogExtractionCompletionAsync(
                It.IsAny<ZonedDateTime>(),
                It.IsAny<ZonedDateTime>(),
                It.IsAny<LocalDate>(),
                It.Is<string>(s => s == "RetryAttempt" || s == "Failed"),
                It.IsAny<int>(),
                It.Is<string>(msg => msg!.Contains("PowerService connection failed")),
                null),
            Times.AtLeastOnce);
    }

    [Test]
    public async Task ExecuteAsync_AllRetriesFail_EnqueuesToDLQ()
    {
        // Arrange
        _mockDeadLetterQueue.Setup(x => x.GetCountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        _mockPowerTradeProvider
            .Setup(x => x.GetTradesAsync(It.IsAny<LocalDate>()))
            .ThrowsAsync(new Exception("PowerService unavailable"));

        _mockAuditLogger
            .Setup(x => x.LogExtractionCompletionAsync(
                It.IsAny<ZonedDateTime>(),
                It.IsAny<ZonedDateTime>(),
                It.IsAny<LocalDate>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        _mockDeadLetterQueue
            .Setup(x => x.EnqueueAsync(It.IsAny<FailedExtraction>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var worker = CreateWorker();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Act
        await worker.StartAsync(cts.Token);
        await Task.Delay(5000); // Allow all retries (3 attempts + 2 delays of 1 second each)
        await worker.StopAsync(cts.Token);

        // Assert
        _mockDeadLetterQueue.Verify(
            x => x.EnqueueAsync(
                It.Is<FailedExtraction>(f => f.RetryCount == 3 && f.LastError == "All retry attempts exhausted"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_FailsThenSucceeds_CompletesSuccessfully()
    {
        // Arrange
        _mockDeadLetterQueue.Setup(x => x.GetCountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var callCount = 0;
        _mockPowerTradeProvider
            .Setup(x => x.GetTradesAsync(It.IsAny<LocalDate>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount < 2)
                    throw new Exception("Transient error");
                return new List<PowerTrade> { PowerTrade.Create(DateTime.UtcNow, 24) };
            });

        var aggregatedPositions = Enumerable.Range(0, 24)
            .Select(i => new AggregatedPosition($"{i:D2}:00", 100.0 * i, i + 1))
            .ToList();

        _mockPositionAggregator
            .Setup(x => x.Aggregate(It.IsAny<IEnumerable<PowerTrade>>(), It.IsAny<LocalDate>()))
            .Returns(aggregatedPositions);

        _mockReportWriter
            .Setup(x => x.WriteReportAsync(
                It.IsAny<IEnumerable<AggregatedPosition>>(),
                It.IsAny<ZonedDateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("C:\\Test\\Output\\PowerPosition_20251210_1405.csv");

        _mockAuditLogger
            .Setup(x => x.LogExtractionCompletionAsync(
                It.IsAny<ZonedDateTime>(),
                It.IsAny<ZonedDateTime>(),
                It.IsAny<LocalDate>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        var worker = CreateWorker();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Act
        await worker.StartAsync(cts.Token);
        await Task.Delay(3000); // Allow retry (2 attempts + 1 delay of 1 second)
        await worker.StopAsync(cts.Token);

        // Assert - should succeed on second attempt
        _mockPowerTradeProvider.Verify(
            x => x.GetTradesAsync(It.IsAny<LocalDate>()),
            Times.Exactly(2));

        _mockAuditLogger.Verify(
            x => x.LogExtractionCompletionAsync(
                It.IsAny<ZonedDateTime>(),
                It.IsAny<ZonedDateTime>(),
                It.IsAny<LocalDate>(),
                "Done",
                2,
                null,
                It.IsAny<string>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_AggregatorReturnsWrongCount_ThrowsInvalidOperationException()
    {
        // Arrange
        _mockDeadLetterQueue.Setup(x => x.GetCountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        _mockPowerTradeProvider
            .Setup(x => x.GetTradesAsync(It.IsAny<LocalDate>()))
            .ReturnsAsync(new List<PowerTrade> { PowerTrade.Create(DateTime.UtcNow, 24) });

        // Return only 23 periods instead of 24
        var invalidPositions = Enumerable.Range(0, 23)
            .Select(i => new AggregatedPosition($"{i:D2}:00", 100.0 * i, i + 1))
            .ToList();

        _mockPositionAggregator
            .Setup(x => x.Aggregate(It.IsAny<IEnumerable<PowerTrade>>(), It.IsAny<LocalDate>()))
            .Returns(invalidPositions);

        _mockAuditLogger
            .Setup(x => x.LogExtractionCompletionAsync(
                It.IsAny<ZonedDateTime>(),
                It.IsAny<ZonedDateTime>(),
                It.IsAny<LocalDate>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        var worker = CreateWorker();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Act
        await worker.StartAsync(cts.Token);
        await Task.Delay(5000); // Allow retries (3 attempts + 2 delays of 1 second each)
        await worker.StopAsync(cts.Token);

        // Assert
        _mockAuditLogger.Verify(
            x => x.LogExtractionCompletionAsync(
                It.IsAny<ZonedDateTime>(),
                It.IsAny<ZonedDateTime>(),
                It.IsAny<LocalDate>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.Is<string>(msg => msg!.Contains("Expected 24 periods, got 23")),
                null),
            Times.AtLeastOnce);
    }

    [Test]
    public async Task ExecuteAsync_ReportWriterThrows_LogsErrorAndRetries()
    {
        // Arrange
        _mockDeadLetterQueue.Setup(x => x.GetCountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        _mockPowerTradeProvider
            .Setup(x => x.GetTradesAsync(It.IsAny<LocalDate>()))
            .ReturnsAsync(new List<PowerTrade> { PowerTrade.Create(DateTime.UtcNow, 24) });

        var aggregatedPositions = Enumerable.Range(0, 24)
            .Select(i => new AggregatedPosition($"{i:D2}:00", 100.0 * i, i + 1))
            .ToList();

        _mockPositionAggregator
            .Setup(x => x.Aggregate(It.IsAny<IEnumerable<PowerTrade>>(), It.IsAny<LocalDate>()))
            .Returns(aggregatedPositions);

        _mockReportWriter
            .Setup(x => x.WriteReportAsync(
                It.IsAny<IEnumerable<AggregatedPosition>>(),
                It.IsAny<ZonedDateTime>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("Disk full"));

        _mockAuditLogger
            .Setup(x => x.LogExtractionCompletionAsync(
                It.IsAny<ZonedDateTime>(),
                It.IsAny<ZonedDateTime>(),
                It.IsAny<LocalDate>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        var worker = CreateWorker();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Act
        await worker.StartAsync(cts.Token);
        await Task.Delay(5000); // Allow retries (3 attempts + 2 delays of 1 second each)
        await worker.StopAsync(cts.Token);

        // Assert
        _mockReportWriter.Verify(
            x => x.WriteReportAsync(
                It.IsAny<IEnumerable<AggregatedPosition>>(),
                It.IsAny<ZonedDateTime>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(3)); // Should retry 3 times

        _mockAuditLogger.Verify(
            x => x.LogExtractionCompletionAsync(
                It.IsAny<ZonedDateTime>(),
                It.IsAny<ZonedDateTime>(),
                It.IsAny<LocalDate>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.Is<string>(msg => msg!.Contains("Disk full")),
                null),
            Times.AtLeastOnce);
    }

    #endregion

    #region Dead Letter Queue Tests

    [Test]
    public async Task ExecuteAsync_DlqHasItems_ProcessesDlqBeforeScheduledExtractions()
    {
        // Arrange
        var failedExtraction = new FailedExtraction(
            new DateTime(2025, 12, 10, 14, 0, 0, DateTimeKind.Utc),
            DateTime.UtcNow.AddHours(-1),
            2,
            "Previous failure");

        _mockDeadLetterQueue.Setup(x => x.GetCountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _mockDeadLetterQueue.Setup(x => x.DequeueAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FailedExtraction> { failedExtraction });

        SetupSuccessfulExtraction();

        var worker = CreateWorker();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));

        // Act
        await worker.StartAsync(cts.Token);
        await Task.Delay(500);
        await worker.StopAsync(cts.Token);

        // Assert
        _mockDeadLetterQueue.Verify(
            x => x.GetCountAsync(It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);

        _mockDeadLetterQueue.Verify(
            x => x.DequeueAllAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_DlqRecoverySucceeds_LogsRecoveredFromDLQStatus()
    {
        // Arrange
        var failedExtraction = new FailedExtraction(
            new DateTime(2025, 12, 10, 14, 0, 0, DateTimeKind.Utc),
            DateTime.UtcNow.AddHours(-1),
            5, // More than normal retry attempts
            "Previous failure");

        _mockDeadLetterQueue.Setup(x => x.GetCountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _mockDeadLetterQueue.Setup(x => x.DequeueAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FailedExtraction> { failedExtraction });

        SetupSuccessfulExtraction();

        var worker = CreateWorker();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));

        // Act
        await worker.StartAsync(cts.Token);
        await Task.Delay(500);
        await worker.StopAsync(cts.Token);

        // Assert
        _mockAuditLogger.Verify(
            x => x.LogExtractionCompletionAsync(
                It.IsAny<ZonedDateTime>(),
                It.IsAny<ZonedDateTime>(),
                It.IsAny<LocalDate>(),
                "RecoveredFromDLQ",
                6, // Previous 5 + 1 new attempt
                null,
                It.IsAny<string>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_DlqRecoveryFails_RequeuesWithIncrementedRetryCount()
    {
        // Arrange
        var failedExtraction = new FailedExtraction(
            new DateTime(2025, 12, 10, 14, 0, 0, DateTimeKind.Utc),
            DateTime.UtcNow.AddHours(-1),
            2,
            "Previous failure");

        _mockDeadLetterQueue.Setup(x => x.GetCountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _mockDeadLetterQueue.Setup(x => x.DequeueAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FailedExtraction> { failedExtraction });

        _mockDeadLetterQueue
            .Setup(x => x.EnqueueAsync(It.IsAny<FailedExtraction>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockPowerTradeProvider
            .Setup(x => x.GetTradesAsync(It.IsAny<LocalDate>()))
            .ThrowsAsync(new Exception("Still failing"));

        _mockAuditLogger
            .Setup(x => x.LogExtractionCompletionAsync(
                It.IsAny<ZonedDateTime>(),
                It.IsAny<ZonedDateTime>(),
                It.IsAny<LocalDate>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        var worker = CreateWorker();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));

        // Act
        await worker.StartAsync(cts.Token);
        await Task.Delay(500);
        await worker.StopAsync(cts.Token);

        // Assert
        _mockDeadLetterQueue.Verify(
            x => x.EnqueueAsync(
                It.Is<FailedExtraction>(f => f.RetryCount == 3), // Original 2 + 1
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_EmptyDlq_SkipsDlqProcessing()
    {
        // Arrange
        _mockDeadLetterQueue.Setup(x => x.GetCountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        SetupSuccessfulExtraction();

        var worker = CreateWorker();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));

        // Act
        await worker.StartAsync(cts.Token);
        await Task.Delay(500);
        await worker.StopAsync(cts.Token);

        // Assert
        _mockDeadLetterQueue.Verify(
            x => x.DequeueAllAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion

    #region Helper Methods

    private PowerPositionWorker CreateWorker()
    {
        return new PowerPositionWorker(
            _mockPowerTradeProvider.Object,
            _mockPositionAggregator.Object,
            _mockReportWriter.Object,
            _mockAuditLogger.Object,
            _mockLocalTimeProvider.Object,
            _mockDeadLetterQueue.Object,
            _options,
            _mockLogger.Object);
    }

    private void SetupSuccessfulExtraction()
    {
        var trades = new List<PowerTrade>
        {
            PowerTrade.Create(DateTime.UtcNow, 24)
        };

        var aggregatedPositions = Enumerable.Range(0, 24)
            .Select(i => new AggregatedPosition($"{i:D2}:00", 100.0 * i, i + 1))
            .ToList();

        _mockPowerTradeProvider
            .Setup(x => x.GetTradesAsync(It.IsAny<LocalDate>()))
            .ReturnsAsync(trades);

        _mockPositionAggregator
            .Setup(x => x.Aggregate(It.IsAny<IEnumerable<PowerTrade>>(), It.IsAny<LocalDate>()))
            .Returns(aggregatedPositions);

        _mockReportWriter
            .Setup(x => x.WriteReportAsync(
                It.IsAny<IEnumerable<AggregatedPosition>>(),
                It.IsAny<ZonedDateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("C:\\Test\\Output\\PowerPosition_20251210_1405.csv");

        _mockAuditLogger
            .Setup(x => x.LogExtractionCompletionAsync(
                It.IsAny<ZonedDateTime>(),
                It.IsAny<ZonedDateTime>(),
                It.IsAny<LocalDate>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Returns(Task.CompletedTask);
    }

    #endregion
}
