using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NodaTime;
using power_position_tracker;
using power_position_tracker.Services;

namespace power_position_tracker_tests.Services;

[TestFixture]
public class ExecutionAuditLoggerTests
{
    private string _testAuditDirectory = null!;
    private DateTimeZone _londonTimeZone = null!;

    [SetUp]
    public void SetUp()
    {
        _testAuditDirectory = Path.Combine(Path.GetTempPath(), $"AuditTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testAuditDirectory);
        _londonTimeZone = DateTimeZoneProviders.Tzdb["Europe/London"];
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_testAuditDirectory))
        {
            Directory.Delete(_testAuditDirectory, recursive: true);
        }
    }

    [Test]
    public async Task LogExtractionCompletionAsync_CreatesAuditFileWithHeader()
    {
        // Arrange
        var settings = CreateSettings();
        var logger = new ExecutionAuditLogger(settings, CreateLogger());

        var startTime = CreateZonedDateTime(2025, 12, 10, 14, 0, 0);
        var endTime = CreateZonedDateTime(2025, 12, 10, 14, 0, 5);
        var targetDate = new LocalDate(2025, 12, 10);

        // Act
        await logger.LogExtractionCompletionAsync(startTime, endTime, targetDate, "Done", 1);

        // Assert
        var expectedFileName = "ExecutionAudit_20251210.csv";
        var filePath = Path.Combine(_testAuditDirectory, expectedFileName);

        Assert.That(File.Exists(filePath), Is.True, $"Audit file should exist at {filePath}");

        var lines = await File.ReadAllLinesAsync(filePath);
        Assert.That(lines.Length, Is.EqualTo(2)); // Header + 1 data row

        Assert.That(lines[0], Is.EqualTo("StartTimeLocal,EndTimeLocal,TargetDate,DurationSeconds,Status,Attempt,ErrorMessage,ReportFileName"));
    }

    [Test]
    public async Task LogExtractionCompletionAsync_WritesCorrectCsvFormat()
    {
        // Arrange
        var settings = CreateSettings();
        var logger = new ExecutionAuditLogger(settings, CreateLogger());

        var startTime = CreateZonedDateTime(2025, 12, 10, 14, 0, 0);
        var endTime = CreateZonedDateTime(2025, 12, 10, 14, 0, 5);
        var targetDate = new LocalDate(2025, 12, 10);

        // Act
        await logger.LogExtractionCompletionAsync(
            startTime, 
            endTime, 
            targetDate, 
            "Done", 
            1, 
            errorMessage: null, 
            reportFileName: "PowerPosition_20251210_1400.csv");

        // Assert
        var filePath = Path.Combine(_testAuditDirectory, "ExecutionAudit_20251210.csv");
        var lines = await File.ReadAllLinesAsync(filePath);

        var dataLine = lines[1];
        var fields = dataLine.Split(',');

        Assert.That(fields.Length, Is.EqualTo(8));
        Assert.That(fields[0], Is.EqualTo("2025-12-10 14:00:00")); // StartTimeLocal
        Assert.That(fields[1], Is.EqualTo("2025-12-10 14:00:05")); // EndTimeLocal
        Assert.That(fields[2], Is.EqualTo("2025-12-10")); // TargetDate
        Assert.That(fields[3], Is.EqualTo("5.00")); // DurationSeconds
        Assert.That(fields[4], Is.EqualTo("Done")); // Status
        Assert.That(fields[5], Is.EqualTo("1")); // Attempt
        Assert.That(fields[6], Is.EqualTo("")); // ErrorMessage (empty)
        Assert.That(fields[7], Is.EqualTo("PowerPosition_20251210_1400.csv")); // ReportFileName
    }

    [Test]
    public async Task LogExtractionCompletionAsync_EscapesCsvSpecialCharacters()
    {
        // Arrange
        var settings = CreateSettings();
        var logger = new ExecutionAuditLogger(settings, CreateLogger());

        var startTime = CreateZonedDateTime(2025, 12, 10, 14, 0, 0);
        var endTime = CreateZonedDateTime(2025, 12, 10, 14, 0, 5);
        var targetDate = new LocalDate(2025, 12, 10);

        var errorMessage = "Error: Connection failed, \"timeout\" occurred\nRetrying...";

        // Act
        await logger.LogExtractionCompletionAsync(startTime, endTime, targetDate, "Failed", 2, errorMessage);

        // Assert
        var filePath = Path.Combine(_testAuditDirectory, "ExecutionAudit_20251210.csv");
        var content = await File.ReadAllTextAsync(filePath);

        // Error message should be quoted and internal quotes doubled
        Assert.That(content, Does.Contain("\"Error: Connection failed, \"\"timeout\"\" occurred"));
    }

    [Test]
    public async Task LogExtractionCompletionAsync_AppendsToExistingFile()
    {
        // Arrange
        var settings = CreateSettings();
        var logger = new ExecutionAuditLogger(settings, CreateLogger());

        var startTime1 = CreateZonedDateTime(2025, 12, 10, 14, 0, 0);
        var endTime1 = CreateZonedDateTime(2025, 12, 10, 14, 0, 5);
        var targetDate = new LocalDate(2025, 12, 10);

        var startTime2 = CreateZonedDateTime(2025, 12, 10, 14, 5, 0);
        var endTime2 = CreateZonedDateTime(2025, 12, 10, 14, 5, 3);

        // Act
        await logger.LogExtractionCompletionAsync(startTime1, endTime1, targetDate, "Done", 1);
        await logger.LogExtractionCompletionAsync(startTime2, endTime2, targetDate, "Failed", 2, "Test error");

        // Assert
        var filePath = Path.Combine(_testAuditDirectory, "ExecutionAudit_20251210.csv");
        var lines = await File.ReadAllLinesAsync(filePath);

        Assert.That(lines.Length, Is.EqualTo(3)); // Header + 2 data rows
    }

    [Test]
    public async Task LogExtractionCompletionAsync_CreatesSeparateFilesForDifferentDays()
    {
        // Arrange
        var settings = CreateSettings();
        var logger = new ExecutionAuditLogger(settings, CreateLogger());

        var startTime1 = CreateZonedDateTime(2025, 12, 10, 23, 55, 0);
        var endTime1 = CreateZonedDateTime(2025, 12, 10, 23, 55, 5);
        var targetDate1 = new LocalDate(2025, 12, 10);

        var startTime2 = CreateZonedDateTime(2025, 12, 11, 0, 5, 0);
        var endTime2 = CreateZonedDateTime(2025, 12, 11, 0, 5, 3);
        var targetDate2 = new LocalDate(2025, 12, 11);

        // Act
        await logger.LogExtractionCompletionAsync(startTime1, endTime1, targetDate1, "Done", 1);
        await logger.LogExtractionCompletionAsync(startTime2, endTime2, targetDate2, "Done", 1);

        // Assert
        var file1 = Path.Combine(_testAuditDirectory, "ExecutionAudit_20251210.csv");
        var file2 = Path.Combine(_testAuditDirectory, "ExecutionAudit_20251211.csv");

        Assert.That(File.Exists(file1), Is.True);
        Assert.That(File.Exists(file2), Is.True);

        var lines1 = await File.ReadAllLinesAsync(file1);
        var lines2 = await File.ReadAllLinesAsync(file2);

        Assert.That(lines1.Length, Is.EqualTo(2)); // Header + 1 data row
        Assert.That(lines2.Length, Is.EqualTo(2)); // Header + 1 data row
    }

    [Test]
    public async Task LogExtractionCompletionAsync_HandlesMultipleRetriesWithDifferentStatuses()
    {
        // Arrange
        var settings = CreateSettings();
        var logger = new ExecutionAuditLogger(settings, CreateLogger());

        var targetDate = new LocalDate(2025, 12, 10);
        var startTime = CreateZonedDateTime(2025, 12, 10, 14, 0, 0);

        // Act - Simulate retry sequence
        await logger.LogExtractionCompletionAsync(
            startTime, 
            CreateZonedDateTime(2025, 12, 10, 14, 0, 5), 
            targetDate, 
            "RetryAttempt", 
            1, 
            "Connection timeout");

        await logger.LogExtractionCompletionAsync(
            startTime, 
            CreateZonedDateTime(2025, 12, 10, 14, 0, 25), 
            targetDate, 
            "RetryAttempt", 
            2, 
            "Connection timeout");

        await logger.LogExtractionCompletionAsync(
            startTime, 
            CreateZonedDateTime(2025, 12, 10, 14, 0, 45), 
            targetDate, 
            "Done", 
            3);

        // Assert
        var filePath = Path.Combine(_testAuditDirectory, "ExecutionAudit_20251210.csv");
        var lines = await File.ReadAllLinesAsync(filePath);

        Assert.That(lines.Length, Is.EqualTo(4)); // Header + 3 data rows

        Assert.That(lines[1], Does.Contain("RetryAttempt,1"));
        Assert.That(lines[2], Does.Contain("RetryAttempt,2"));
        Assert.That(lines[3], Does.Contain("Done,3"));
    }

    [Test]
    public async Task LogExtractionCompletionAsync_HandlesBstTransition()
    {
        // Arrange
        var settings = CreateSettings();
        var logger = new ExecutionAuditLogger(settings, CreateLogger());

        // March 30, 2025: BST transition (clocks go forward at 01:00)
        var startTime = CreateZonedDateTime(2025, 3, 30, 0, 55, 0);
        var endTime = CreateZonedDateTime(2025, 3, 30, 2, 5, 0); // After transition
        var targetDate = new LocalDate(2025, 3, 30);

        // Act
        await logger.LogExtractionCompletionAsync(startTime, endTime, targetDate, "Done", 1);

        // Assert
        var filePath = Path.Combine(_testAuditDirectory, "ExecutionAudit_20250330.csv");
        var lines = await File.ReadAllLinesAsync(filePath);

        Assert.That(lines.Length, Is.EqualTo(2));

        var dataLine = lines[1];
        Assert.That(dataLine, Does.Contain("2025-03-30 00:55:00")); // GMT
        Assert.That(dataLine, Does.Contain("2025-03-30 02:05:00")); // BST (1 hour skipped)
    }

    [Test]
    public void LogExtractionCompletionAsync_ThrowsWhenAttemptIsZero()
    {
        // Arrange
        var settings = CreateSettings();
        var logger = new ExecutionAuditLogger(settings, CreateLogger());

        var startTime = CreateZonedDateTime(2025, 12, 10, 14, 0, 0);
        var endTime = CreateZonedDateTime(2025, 12, 10, 14, 0, 5);
        var targetDate = new LocalDate(2025, 12, 10);

        // Act & Assert
        Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            async () => await logger.LogExtractionCompletionAsync(startTime, endTime, targetDate, "Done", 0));
    }

    [Test]
    public void LogExtractionCompletionAsync_ThrowsWhenStatusIsEmpty()
    {
        // Arrange
        var settings = CreateSettings();
        var logger = new ExecutionAuditLogger(settings, CreateLogger());

        var startTime = CreateZonedDateTime(2025, 12, 10, 14, 0, 0);
        var endTime = CreateZonedDateTime(2025, 12, 10, 14, 0, 5);
        var targetDate = new LocalDate(2025, 12, 10);

        // Act & Assert
        Assert.ThrowsAsync<ArgumentException>(
            async () => await logger.LogExtractionCompletionAsync(startTime, endTime, targetDate, "", 1));
    }

    [Test]
    public async Task LogExtractionCompletionAsync_IsThreadSafe()
    {
        // Arrange
        var settings = CreateSettings();
        var logger = new ExecutionAuditLogger(settings, CreateLogger());

        var targetDate = new LocalDate(2025, 12, 10);
        var tasks = new List<Task>();

        // Act - Simulate concurrent writes
        for (int i = 0; i < 10; i++)
        {
            var attempt = i + 1;
            var startTime = CreateZonedDateTime(2025, 12, 10, 14, i, 0);
            var endTime = CreateZonedDateTime(2025, 12, 10, 14, i, 5);

            tasks.Add(logger.LogExtractionCompletionAsync(startTime, endTime, targetDate, "Done", attempt));
        }

        await Task.WhenAll(tasks);

        // Assert
        var filePath = Path.Combine(_testAuditDirectory, "ExecutionAudit_20251210.csv");
        var lines = await File.ReadAllLinesAsync(filePath);

        Assert.That(lines.Length, Is.EqualTo(11)); // Header + 10 data rows
    }

    [Test]
    public async Task LogExtractionCompletionAsync_CalculatesDurationCorrectly()
    {
        // Arrange
        var settings = CreateSettings();
        var logger = new ExecutionAuditLogger(settings, CreateLogger());

        var startTime = CreateZonedDateTime(2025, 12, 10, 14, 0, 0);
        var endTime = CreateZonedDateTime(2025, 12, 10, 14, 1, 33); // 93 seconds later
        var targetDate = new LocalDate(2025, 12, 10);

        // Act
        await logger.LogExtractionCompletionAsync(startTime, endTime, targetDate, "Done", 1);

        // Assert
        var filePath = Path.Combine(_testAuditDirectory, "ExecutionAudit_20251210.csv");
        var lines = await File.ReadAllLinesAsync(filePath);

        var dataLine = lines[1];
        var fields = dataLine.Split(',');

        Assert.That(fields[3], Is.EqualTo("93.00")); // DurationSeconds
    }

    private IOptions<PowerPositionSettings> CreateSettings()
    {
        return Options.Create(new PowerPositionSettings
        {
            AuditDirectory = _testAuditDirectory,
            OutputDirectory = "C:\\Output",
            TimeZoneId = "Europe/London"
        });
    }

    private ILogger<ExecutionAuditLogger> CreateLogger()
    {
        return new LoggerFactory().CreateLogger<ExecutionAuditLogger>();
    }

    private ZonedDateTime CreateZonedDateTime(int year, int month, int day, int hour, int minute, int second)
    {
        var localDateTime = new LocalDateTime(year, month, day, hour, minute, second);
        return localDateTime.InZoneLeniently(_londonTimeZone);
    }
}
