using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NodaTime;
using power_position_tracker;
using power_position_tracker.Models;
using power_position_tracker.Services;

namespace power_position_tracker_tests.Services;

[TestFixture]
public class ReportWriterTests
{
    private ReportWriter _sut;
    private Mock<ILogger<ReportWriter>> _loggerMock;
    private Mock<IOptions<PowerPositionSettings>> _settingsMock;
    private string _testOutputDirectory;
    private DateTimeZone _londonTimeZone;

    [SetUp]
    public void Setup()
    {
        _loggerMock = new Mock<ILogger<ReportWriter>>();
        _settingsMock = new Mock<IOptions<PowerPositionSettings>>();
        _londonTimeZone = DateTimeZoneProviders.Tzdb["Europe/London"];

        // Use a unique directory for each test run to avoid conflicts
        _testOutputDirectory = Path.Combine(Path.GetTempPath(), $"PowerPositionTests_{Guid.NewGuid()}");

        var settings = new PowerPositionSettings
        {
            OutputDirectory = _testOutputDirectory
        };

        _settingsMock.Setup(s => s.Value).Returns(settings);

        _sut = new ReportWriter(_loggerMock.Object, _settingsMock.Object);
    }

    [TearDown]
    public void Teardown()
    {
        _sut.Dispose();

        // Clean up test directory
        if (Directory.Exists(_testOutputDirectory))
        {
            Directory.Delete(_testOutputDirectory, true);
        }
    }

    [Test]
    public void WhenLoggerIsNullThenThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new ReportWriter(null!, _settingsMock.Object));
    }

    [Test]
    public void WhenSettingsIsNullThenThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new ReportWriter(_loggerMock.Object, null!));
    }

    [Test]
    public void WhenOutputDirectoryIsNullThenThrowsArgumentException()
    {
        // Arrange
        var settings = new PowerPositionSettings
        {
            OutputDirectory = null!
        };
        _settingsMock.Setup(s => s.Value).Returns(settings);

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            new ReportWriter(_loggerMock.Object, _settingsMock.Object));
    }

    [Test]
    public void WhenOutputDirectoryIsEmptyThenThrowsArgumentException()
    {
        // Arrange
        var settings = new PowerPositionSettings
        {
            OutputDirectory = ""
        };
        _settingsMock.Setup(s => s.Value).Returns(settings);

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            new ReportWriter(_loggerMock.Object, _settingsMock.Object));
    }

    [Test]
    public void WhenOutputDirectoryIsWhitespaceThenThrowsArgumentException()
    {
        // Arrange
        var settings = new PowerPositionSettings
        {
            OutputDirectory = "   "
        };
        _settingsMock.Setup(s => s.Value).Returns(settings);

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            new ReportWriter(_loggerMock.Object, _settingsMock.Object));
    }

    [Test]
    public async Task WriteReportAsync_PositionsIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var extractionTime = new LocalDateTime(2025, 12, 10, 14, 30, 0).InZoneLeniently(_londonTimeZone);

        // Act & Assert
        var ex = Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await _sut.WriteReportAsync(null!, extractionTime));

        Assert.That(ex.ParamName, Is.EqualTo("positions"));
    }

    [Test]
    public async Task WriteReportAsync_ValidPositions_WritesCorrectCsvFile()
    {
        // Arrange
        var extractionTime = new LocalDateTime(2025, 12, 10, 14, 30, 0).InZoneLeniently(_londonTimeZone);
        var positions = CreateTestPositions();

        // Act
        var filePath = await _sut.WriteReportAsync(positions, extractionTime);

        // Assert
        Assert.That(File.Exists(filePath), Is.True);
        Assert.That(filePath, Does.EndWith("PowerPosition_20251210_1430.csv"));

        var content = await File.ReadAllTextAsync(filePath);
        var lines = content.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.That(lines.Length, Is.EqualTo(25)); // Header + 24 data rows
        Assert.That(lines[0], Is.EqualTo("LocalTime,Volume"));
        Assert.That(lines[1], Is.EqualTo("23:00,150.00"));
        Assert.That(lines[2], Is.EqualTo("00:00,150.00"));
    }

    [Test]
    public async Task WriteReportAsync_ValidPositions_ReturnsCorrectFilePath()
    {
        // Arrange
        var extractionTime = new LocalDateTime(2025, 12, 10, 14, 30, 0).InZoneLeniently(_londonTimeZone);
        var positions = CreateTestPositions();

        // Act
        var filePath = await _sut.WriteReportAsync(positions, extractionTime);

        // Assert
        Assert.That(filePath, Does.Contain(_testOutputDirectory));
        Assert.That(filePath, Does.EndWith("PowerPosition_20251210_1430.csv"));
    }

    [Test]
    public async Task WriteReportAsync_ValidPositions_OrdersByPeriod()
    {
        // Arrange
        var extractionTime = new LocalDateTime(2025, 12, 10, 14, 30, 0).InZoneLeniently(_londonTimeZone);
        
        // Create positions in non-sequential order
        var positions = new List<AggregatedPosition>
        {
            new AggregatedPosition("22:00", 80.0, 24),
            new AggregatedPosition("23:00", 150.0, 1),
            new AggregatedPosition("01:00", 150.0, 3),
            new AggregatedPosition("00:00", 150.0, 2)
        };

        // Act
        var filePath = await _sut.WriteReportAsync(positions, extractionTime);

        // Assert
        var content = await File.ReadAllTextAsync(filePath);
        var lines = content.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        // Verify ordering by period (23:00 first, then 00:00, 01:00, 22:00 last)
        Assert.That(lines[1], Is.EqualTo("23:00,150.00"));
        Assert.That(lines[2], Is.EqualTo("00:00,150.00"));
        Assert.That(lines[3], Is.EqualTo("01:00,150.00"));
        Assert.That(lines[4], Is.EqualTo("22:00,80.00"));
    }

    [Test]
    public async Task WriteReportAsync_PositionCountIsNot24_LogsWarning()
    {
        // Arrange
        var extractionTime = new LocalDateTime(2025, 12, 10, 14, 30, 0).InZoneLeniently(_londonTimeZone);
        var positions = new List<AggregatedPosition>
        {
            new AggregatedPosition("23:00", 150.0, 1),
            new AggregatedPosition("00:00", 150.0, 2)
        };

        // Act
        await _sut.WriteReportAsync(positions, extractionTime);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Expected 24 aggregated positions, but received 2")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Test]
    public async Task WriteReportAsync_WriteSucceeds_LogsInformation()
    {
        // Arrange
        var extractionTime = new LocalDateTime(2025, 12, 10, 14, 30, 0).InZoneLeniently(_londonTimeZone);
        var positions = CreateTestPositions();

        // Act
        await _sut.WriteReportAsync(positions, extractionTime);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Report written successfully")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Test]
    public async Task WriteReportAsync_NegativeVolumes_FormatsCorrectly()
    {
        // Arrange
        var extractionTime = new LocalDateTime(2025, 12, 10, 14, 30, 0).InZoneLeniently(_londonTimeZone);
        var positions = new List<AggregatedPosition>
        {
            new AggregatedPosition("23:00", 150.0, 1),
            new AggregatedPosition("00:00", -20.5, 2)
        };

        // Act
        var filePath = await _sut.WriteReportAsync(positions, extractionTime);

        // Assert
        var content = await File.ReadAllTextAsync(filePath);
        var lines = content.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.That(lines[1], Is.EqualTo("23:00,150.00"));
        Assert.That(lines[2], Is.EqualTo("00:00,-20.50"));
    }

    [Test]
    public async Task WriteReportAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        // Arrange
        var extractionTime = new LocalDateTime(2025, 12, 10, 14, 30, 0).InZoneLeniently(_londonTimeZone);
        var positions = CreateTestPositions();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        Assert.ThrowsAsync<TaskCanceledException>(async () =>
            await _sut.WriteReportAsync(positions, extractionTime, cts.Token));
    }

    [Test]
    public async Task WriteReportAsync_WhenMultipleConcurrentWrites_HandlesThreadSafely()
    {
        // Arrange
        var extractionTime1 = new LocalDateTime(2025, 12, 10, 14, 30, 0).InZoneLeniently(_londonTimeZone);
        var extractionTime2 = new LocalDateTime(2025, 12, 10, 14, 35, 0).InZoneLeniently(_londonTimeZone);
        var positions1 = CreateTestPositions();
        var positions2 = CreateTestPositions();

        // Act
        var task1 = _sut.WriteReportAsync(positions1, extractionTime1);
        var task2 = _sut.WriteReportAsync(positions2, extractionTime2);

        await Task.WhenAll(task1, task2);

        // Assert
        Assert.That(File.Exists(task1.Result), Is.True);
        Assert.That(File.Exists(task2.Result), Is.True);
        Assert.That(task1.Result, Is.Not.EqualTo(task2.Result));
    }

    [Test]
    public void WriteReportAsync_DisposedThen_ObjectDisposedException()
    {
        // Arrange
        var extractionTime = new LocalDateTime(2025, 12, 10, 14, 30, 0).InZoneLeniently(_londonTimeZone);
        var positions = CreateTestPositions();
        _sut.Dispose();

        // Act & Assert
        Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await _sut.WriteReportAsync(positions, extractionTime));
    }

    [Test]
    public void WriteReportAsync_DisposedMultipleTimes_DoesNotThrow()
    {
        // Arrange & Act & Assert
        Assert.DoesNotThrow(() =>
        {
            _sut.Dispose();
            _sut.Dispose();
        });
    }

    [Test]
    public async Task WriteReportAsync_FileNameContainsSpecialCharactersInDate_FormatsCorrectly()
    {
        // Arrange
        var extractionTime = new LocalDateTime(2025, 1, 5, 9, 5, 0).InZoneLeniently(_londonTimeZone);
        var positions = CreateTestPositions();

        // Act
        var filePath = await _sut.WriteReportAsync(positions, extractionTime);

        // Assert
        Assert.That(filePath, Does.EndWith("PowerPosition_20250105_0905.csv"));
    }

    [Test]
    public async Task WriteReportAsync_EmptyPositionList_WritesHeaderOnly()
    {
        // Arrange
        var extractionTime = new LocalDateTime(2025, 12, 10, 14, 30, 0).InZoneLeniently(_londonTimeZone);
        var positions = new List<AggregatedPosition>();

        // Act
        var filePath = await _sut.WriteReportAsync(positions, extractionTime);

        // Assert
        var content = await File.ReadAllTextAsync(filePath);
        var lines = content.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.That(lines.Length, Is.EqualTo(1)); // Header only
        Assert.That(lines[0], Is.EqualTo("LocalTime,Volume"));
    }

    [Test]
    public async Task WriteReportAsync_VolumeHasDecimalPlaces_FormatsToTwoDecimals()
    {
        // Arrange
        var extractionTime = new LocalDateTime(2025, 12, 10, 14, 30, 0).InZoneLeniently(_londonTimeZone);
        var positions = new List<AggregatedPosition>
        {
            new AggregatedPosition("23:00", 150.123456, 1),
            new AggregatedPosition("00:00", 80.999, 2)
        };

        // Act
        var filePath = await _sut.WriteReportAsync(positions, extractionTime);

        // Assert
        var content = await File.ReadAllTextAsync(filePath);
        var lines = content.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.That(lines[1], Is.EqualTo("23:00,150.12"));
        Assert.That(lines[2], Is.EqualTo("00:00,81.00"));
    }

    private List<AggregatedPosition> CreateTestPositions()
    {
        var positions = new List<AggregatedPosition>();

        // Periods 1-11 with volume 150.0
        for (int i = 1; i <= 11; i++)
        {
            var hour = (i + 22) % 24; // Period 1 = 23:00, Period 2 = 00:00, etc.
            positions.Add(new AggregatedPosition($"{hour:D2}:00", 150.0, i));
        }

        // Periods 12-24 with volume 80.0
        for (int i = 12; i <= 24; i++)
        {
            var hour = (i + 22) % 24;
            positions.Add(new AggregatedPosition($"{hour:D2}:00", 80.0, i));
        }

        return positions;
    }
}
