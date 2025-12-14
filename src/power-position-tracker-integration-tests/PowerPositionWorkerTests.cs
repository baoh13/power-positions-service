using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using power_position_tracker;
using power_position_tracker.Models;
using power_position_tracker.Services;
using power_position_tracker.Services.Interfaces;
using power_position_tracker_integration_tests.TestHelpers;
using Services;

namespace power_position_tracker_integration_tests;

/// <summary>
/// Integration tests for PowerPositionWorker covering all phases:
/// Phase 1: DLQ Processing on startup
/// Phase 2: Initial extraction
/// Phase 3: Periodic scheduled extractions
/// Uses real PowerService.dll instead of mocks
/// </summary>
[TestFixture]
public class PowerPositionWorkerTests
{
    private string _testOutputDirectory = string.Empty;
    private string _testAuditDirectory = string.Empty;
    private string _testDlqDirectory = string.Empty;

    [SetUp]
    public void SetUp()
    {
        // Create unique test directories for each test run
        var testRunId = Guid.NewGuid().ToString("N")[..8];
        var baseTestDir = Path.Combine(Path.GetTempPath(), "PowerPositionTests", testRunId);

        _testOutputDirectory = Path.Combine(baseTestDir, "Output");
        _testAuditDirectory = Path.Combine(baseTestDir, "Audit");
        _testDlqDirectory = Path.Combine(baseTestDir, "Dlq");

        Directory.CreateDirectory(_testOutputDirectory);
        Directory.CreateDirectory(_testAuditDirectory);
        Directory.CreateDirectory(_testDlqDirectory);
    }

    [TearDown]
    public void TearDown()
    {
        // Clean up test directories
        try
        {
            var baseTestDir = Directory.GetParent(_testOutputDirectory)?.Parent?.FullName;
            if (baseTestDir != null && Directory.Exists(baseTestDir))
            {
                Directory.Delete(baseTestDir, true);
            }
        }
        catch (Exception ex)
        {
            TestContext.Out.WriteLine($"Warning: Could not clean up test directory: {ex.Message}");
        }
    }

    #region Phase 1: DLQ Processing on Startup Tests

    [Test]
    public async Task WhenDlqIsEmptyOnStartup_ThenProcessesWithoutErrors()
    {
        // Arrange
        var host = CreateHostBuilder(DateTime.UtcNow).Build();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));

        // Act
        await host.StartAsync(cts.Token);
        await Task.Delay(TimeSpan.FromSeconds(5)); // Let it run initial extraction
        await host.StopAsync(cts.Token);

        // Assert
        var reportFiles = Directory.GetFiles(_testOutputDirectory, "PowerPosition_*.csv");
        Assert.That(reportFiles.Length, Is.GreaterThan(0), "Should create at least one report file");

        var auditFiles = Directory.GetFiles(_testAuditDirectory, "ExecutionAudit_*.csv");
        Assert.That(auditFiles.Length, Is.EqualTo(1), "Should create one audit file");
    }

    [Test]
    public async Task WhenDlqHasFailedExtractions_ThenProcessesThemOnStartup()
    {
        // Arrange
        var host = CreateHostBuilder(DateTime.UtcNow).Build();
        var dlq = host.Services.GetRequiredService<IDeadLetterQueue>();

        // Add a failed extraction from 2 hours ago
        var extractionTimeUtc = DateTime.UtcNow.AddHours(-2);
        var failedExtraction = new FailedExtraction(
            extractionTimeUtc,
            DateTime.UtcNow.AddHours(-2),
            RetryCount: 3,
            LastError: "Previous test failure");

        await dlq.EnqueueAsync(failedExtraction);

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));

        await host.StartAsync(cts.Token);
        await Task.Delay(TimeSpan.FromSeconds(5)); // Allow DLQ processing
        await host.StopAsync(cts.Token);

        // Assert
        var dlqCount = await dlq.GetCountAsync();
        Assert.That(dlqCount, Is.EqualTo(0), "DLQ should be empty after successful recovery");

        var reportFiles = Directory.GetFiles(_testOutputDirectory, "PowerPosition_*.csv");
        Assert.That(reportFiles.Length, Is.GreaterThan(0), "Should create report files");

        // Check audit log for RecoveredFromDLQ status
        var auditFiles = Directory.GetFiles(_testAuditDirectory, "ExecutionAudit_*.csv");
        Assert.That(auditFiles.Length, Is.EqualTo(1));

        var auditContent = await File.ReadAllTextAsync(auditFiles[0]);
        Assert.That(auditContent, Does.Contain("RecoveredFromDLQ").Or.Contain("Done"),
            "Audit log should show recovery from DLQ or success");
    }

    [Test]
    public async Task WhenDlqProcessingFails_ThenRequeuesWithUpdatedRetryCount()
    {
        // Arrange
        var mockPowerService = new Mock<IPowerService>();
        mockPowerService
            .Setup(ps => ps.GetTradesAsync(It.IsAny<DateTime>()))
            .ThrowsAsync(new InvalidOperationException("PowerService unavailable"));

        var host = CreateHostBuilder(DateTime.UtcNow, mockPowerService).Build();
        var dlq = host.Services.GetRequiredService<IDeadLetterQueue>();

        // Create a scenario where extraction might fail by using an invalid runtime
        // This simulates a persistent failure during DLQ processing
        var extractionTimeUtc = DateTime.UtcNow.AddHours(-2);
        var initialRetryCount = 3;
        var failedExtraction = new FailedExtraction(
            extractionTimeUtc,
            DateTime.UtcNow.AddHours(-2),
            RetryCount: initialRetryCount,
            LastError: "Simulated persistent failure");

        await dlq.EnqueueAsync(failedExtraction);

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
        await host.StartAsync(cts.Token);
        await Task.Delay(TimeSpan.FromSeconds(5)); // Allow DLQ processing attempt
        await host.StopAsync(cts.Token);

        // Assert
        var dlqCount = await dlq.GetCountAsync();
        Assert.That(dlqCount, Is.GreaterThan(0), "Failed extraction should be re-queued");

        var queuedItems = await dlq.PeekAllAsync();
        var requeuedItem = queuedItems.FirstOrDefault();
        Assert.That(requeuedItem, Is.Not.Null);
        Assert.That(requeuedItem!.RetryCount, Is.GreaterThan(initialRetryCount),
            "Retry count should be incremented after failed DLQ processing");
    }

    #endregion

    #region Phase 2: Initial Extraction Tests

    [Test]
    public async Task WhenWorkerStarts_ThenRunsInitialExtractionImmediately()
    {
        // Arrange
        var host = CreateHostBuilder(DateTime.UtcNow).Build();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));

        // Act
        var startTime = DateTime.UtcNow;
        await host.StartAsync(cts.Token);
        await Task.Delay(TimeSpan.FromSeconds(5)); // Allow initial extraction
        await host.StopAsync(cts.Token);

        // Assert
        var reportFiles = Directory.GetFiles(_testOutputDirectory, "PowerPosition_*.csv");
        Assert.That(reportFiles.Length, Is.GreaterThan(0), "Should create report file immediately");

        var auditFiles = Directory.GetFiles(_testAuditDirectory, "ExecutionAudit_*.csv");
        Assert.That(auditFiles.Length, Is.EqualTo(1), "Should create audit file");
    }

    [Test]
    public async Task WhenInitialExtractionSucceeds_ThenCreatesValidCsvReport()
    {
        // Arrange
        var host = CreateHostBuilder(DateTime.UtcNow).Build();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));

        // Act
        await host.StartAsync(cts.Token);
        await Task.Delay(TimeSpan.FromSeconds(5));
        await host.StopAsync(cts.Token);

        // Assert
        var reportFiles = Directory.GetFiles(_testOutputDirectory, "PowerPosition_*.csv");
        Assert.That(reportFiles.Length, Is.GreaterThan(0));

        var csvContent = await File.ReadAllTextAsync(reportFiles[0]);
        var lines = csvContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        // Verify header
        Assert.That(lines[0], Is.EqualTo("LocalTime,Volume"));

        // Verify 24 data rows
        Assert.That(lines.Length, Is.EqualTo(25), "Should have header + 24 data rows");

        // Verify first period is 23:00 (previous day)
        Assert.That(lines[1], Does.StartWith("23:00,"));

        // Verify all rows have valid format
        foreach (var line in lines.Skip(1))
        {
            var parts = line.Split(',');
            Assert.That(parts.Length, Is.EqualTo(2), $"Line should have 2 columns: {line}");
            Assert.That(TimeOnly.TryParse(parts[0], out _), Is.True, $"Invalid time format: {parts[0]}");
            Assert.That(double.TryParse(parts[1], out _), Is.True, $"Invalid volume format: {parts[1]}");
        }
    }

    [Test]
    public async Task WhenInitialExtractionSucceeds_ThenLogsAuditRecord()
    {
        // Arrange
        var host = CreateHostBuilder(DateTime.UtcNow).Build();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));

        // Act
        await host.StartAsync(cts.Token);
        await Task.Delay(TimeSpan.FromSeconds(5));
        await host.StopAsync(cts.Token);

        // Assert
        var auditFiles = Directory.GetFiles(_testAuditDirectory, "ExecutionAudit_*.csv");
        Assert.That(auditFiles.Length, Is.EqualTo(1));

        var auditContent = await File.ReadAllTextAsync(auditFiles[0]);
        var lines = auditContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        // Verify header
        Assert.That(lines[0], Does.Contain("StartTimeLocal"));
        Assert.That(lines[0], Does.Contain("Status"));

        // Verify at least one success record
        var successRecords = lines.Skip(1).Where(l => l.Contains("Done") || l.Contains("Success"));
        Assert.That(successRecords, Is.Not.Empty, "Should have at least one successful extraction record");
    }

    #endregion

    #region Phase 3: Periodic Scheduled Extractions Tests

    [Test]
    public async Task WhenMultipleIntervalsElapse_ThenCreatesUniqueReportFiles()
    {
        // Arrange
        var host = CreateHostBuilder(DateTime.UtcNow, intervalMinutes: 1).Build();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(70));

        // Act
        await host.StartAsync(cts.Token);
        await Task.Delay(TimeSpan.FromSeconds(65));
        await host.StopAsync(cts.Token);

        // Assert
        var reportFiles = Directory.GetFiles(_testOutputDirectory, "PowerPosition_*.csv");
        var fileNames = reportFiles.Select(Path.GetFileName).ToList();

        Assert.That(fileNames.Count, Is.EqualTo(fileNames.Distinct().Count()),
            "All report files should have unique names");

        // Verify filename format: PowerPosition_YYYYMMDD_HHMM.csv
        foreach (var fileName in fileNames)
        {
            Assert.That(fileName, Does.Match(@"PowerPosition_\d{8}_\d{4}\.csv"),
                $"File should match naming pattern: {fileName}");
        }
    }

    [Test]
    public async Task WhenScheduledExtractionRuns_ThenProcessesRealPowerServiceData()
    {
        // Arrange
        var host = CreateHostBuilder(DateTime.UtcNow).Build();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));

        // Act
        await host.StartAsync(cts.Token);
        await Task.Delay(TimeSpan.FromSeconds(5));
        await host.StopAsync(cts.Token);

        // Assert
        var reportFiles = Directory.GetFiles(_testOutputDirectory, "PowerPosition_*.csv");
        var csvContent = await File.ReadAllTextAsync(reportFiles[0]);
        var lines = csvContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        // Verify we have exactly 24 periods from PowerService
        Assert.That(lines.Length, Is.EqualTo(25), "Should have 24 periods + header");

        // Verify volumes are aggregated (checking that values exist)
        var dataLines = lines.Skip(1).ToList();
        foreach (var line in dataLines)
        {
            var parts = line.Split(',');
            var volume = double.Parse(parts[1]);
            // PowerService returns real data, so volumes should be non-zero for at least some periods
            Assert.That(volume, Is.GreaterThanOrEqualTo(0), "Volume should be non-negative");
        }
    }

    #endregion

    #region Error Handling & Retry Tests

    [Test]
    public async Task WhenExtractionFails_ThenRetriesConfiguredTimes()
    {
        // Arrange
        var callCount = 0;
        var mockPowerService = new Mock<IPowerService>();
        mockPowerService
            .Setup(ps => ps.GetTradesAsync(It.IsAny<DateTime>()))
            .Callback(() => callCount++)
            .ThrowsAsync(new InvalidOperationException("PowerService unavailable"));

        var host = CreateHostBuilder(DateTime.UtcNow, mockPowerService, retryAttempts: 3).Build();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Act
        await host.StartAsync(cts.Token);
        await Task.Delay(TimeSpan.FromSeconds(9)); // Allow retries
        await host.StopAsync(cts.Token);

        // Assert
        Assert.That(callCount, Is.GreaterThanOrEqualTo(3),
            "Should retry at least 3 times as configured");
    }

    [Test]
    public async Task WhenAllRetriesFail_ThenAddsToDeadLetterQueue()
    {
        // Arrange
        var mockPowerService = new Mock<IPowerService>();
        mockPowerService
            .Setup(ps => ps.GetTradesAsync(It.IsAny<DateTime>()))
            .ThrowsAsync(new InvalidOperationException("PowerService unavailable"));

        var host = CreateHostBuilder(DateTime.UtcNow, mockPowerService, retryAttempts: 2).Build();
        var dlq = host.Services.GetRequiredService<IDeadLetterQueue>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Act
        await host.StartAsync(cts.Token);
        await Task.Delay(TimeSpan.FromSeconds(9));
        await host.StopAsync(cts.Token);

        // Assert
        var dlqCount = await dlq.GetCountAsync();
        Assert.That(dlqCount, Is.GreaterThan(0),
            "Failed extraction should be added to dead letter queue");

        var queuedItems = await dlq.PeekAllAsync();
        var failedItem = queuedItems.First();
        Assert.That(failedItem.RetryCount, Is.GreaterThan(0),
            "Failed extraction should have retry count recorded");
    }

    [Test]
    public async Task WhenExtractionFails_ThenLogsRetryAttemptsInAudit()
    {
        // Arrange
        var mockPowerService = new Mock<IPowerService>();
        mockPowerService
            .Setup(ps => ps.GetTradesAsync(It.IsAny<DateTime>()))
            .ThrowsAsync(new TimeoutException("PowerService timeout"));

        var host = CreateHostBuilder(DateTime.UtcNow, mockPowerService, retryAttempts: 2).Build();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Act
        await host.StartAsync(cts.Token);
        await Task.Delay(TimeSpan.FromSeconds(9));
        await host.StopAsync(cts.Token);

        // Assert
        var auditFiles = Directory.GetFiles(_testAuditDirectory, "ExecutionAudit_*.csv");
        Assert.That(auditFiles.Length, Is.GreaterThan(0), "Should create audit file");

        var auditContent = await File.ReadAllTextAsync(auditFiles[0]);
        Assert.That(auditContent, Does.Contain("Failed").Or.Contain("Error"),
            "Audit log should contain failure records");
    }

    #endregion

    #region Runtime Configuration Tests


    [Test]
    public async Task WhenRunTimeSetInConfig_ThenUsesSpecifiedTime()
    {
        // Arrange
        var specificRunTime = new DateTime(2025, 12, 10, 14, 30, 0, DateTimeKind.Utc);
        var host = CreateHostBuilder(specificRunTime).Build();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));

        // Act
        await host.StartAsync(cts.Token);
        await Task.Delay(TimeSpan.FromSeconds(5));
        await host.StopAsync(cts.Token);

        // Assert
        var reportFiles = Directory.GetFiles(_testOutputDirectory, "PowerPosition_*.csv");
        Assert.That(reportFiles.Length, Is.GreaterThan(0));

        var reportFileName = Path.GetFileName(reportFiles[0]);
        Assert.That(reportFileName, Does.Contain("20251210"), 
            "Report filename should contain date from configured RunTime");
    }

    [Test]
    public async Task WhenEnvironmentVariable_SetThenOverridesConfig()
    {
        // Arrange
        var envRunTime = "2025-12-11T10:00:00Z";
        Environment.SetEnvironmentVariable("DOTNET_RUNTIME", envRunTime);

        try
        {
            var host = CreateHostBuilder(DateTime.UtcNow).Build();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));

            // Act
            await host.StartAsync(cts.Token);
            await Task.Delay(TimeSpan.FromSeconds(5));
            await host.StopAsync(cts.Token);

            // Assert
            var reportFiles = Directory.GetFiles(_testOutputDirectory, "PowerPosition_*.csv");
            Assert.That(reportFiles.Length, Is.GreaterThan(0));

            var reportFileName = Path.GetFileName(reportFiles[0]);
            Assert.That(reportFileName, Does.Contain("20251211"),
                "Report filename should use date from environment variable");
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTNET_RUNTIME", null);
        }
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Creates a host builder with test-specific configuration and optional service overrides
    /// </summary>
    /// <param name="runTime">The runtime to use for extractions</param>
    /// <param name="mockPowerService">Optional mock PowerService for testing failures</param>
    /// <param name="intervalMinutes">Optional interval override for scheduling tests</param>
    /// <param name="retryAttempts">Optional retry attempts override for error handling tests</param>
    private IHostBuilder CreateHostBuilder(
        DateTime runTime,
        Mock<IPowerService>? mockPowerService = null,
        int? intervalMinutes = null,
        int? retryAttempts = null)
    {
        var configValues = new Dictionary<string, string>
        {
            { "PowerPositionSettings:RunTime", runTime.ToString("o") },
            { "PowerPositionSettings:OutputDirectory", _testOutputDirectory },
            { "PowerPositionSettings:AuditDirectory", _testAuditDirectory },
            { "PowerPositionSettings:DlqDirectory", _testDlqDirectory },
            { "PowerPositionSettings:TimeZoneId", "Europe/London" },
            { "PowerPositionSettings:IntervalMinutes", (intervalMinutes ?? 1).ToString() },
            { "PowerPositionSettings:RetryAttempts", (retryAttempts ?? 3).ToString() },
            { "PowerPositionSettings:RetryDelaySeconds", "1" }
        };

        var host = Host.CreateDefaultBuilder()
            .ConfigureLogging(APPNAME, skipLogging: true)
            .RegisterServices()
            .ConfigureAppConfiguration(c => c.AddInMemoryCollection(configValues!))
            .ConfigureServices(svc =>
            {
                // Replace PowerService with mock if provided
                if (mockPowerService != null)
                {
                    svc.RemoveService<IPowerTradeProvider>();
                    svc.AddSingleton<IPowerTradeProvider>(sp =>
                    {
                        var logger = sp.GetRequiredService<ILogger<PowerTradeProvider>>();
                        return new PowerTradeProvider(mockPowerService.Object, logger);
                    });
                }

                // Enable console logging for tests
                svc.AddLogging(builder =>
                {
                    builder.AddConsole();
                    builder.SetMinimumLevel(LogLevel.Debug);
                });
            });

        return host;
    }

    private const string APPNAME = "power-position-tracker-tests";

    #endregion
}
