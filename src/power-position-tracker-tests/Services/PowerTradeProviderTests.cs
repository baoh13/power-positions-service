using Moq;
using AutoFixture;
using Services;
using power_position_tracker.Services;
using Microsoft.Extensions.Logging;
using NodaTime;

namespace power_position_tracker_tests.Services;

[TestFixture]
public class PowerTradeProviderTests
{
    private Mock<IPowerService> _mockPowerService;
    private Mock<ILogger<PowerTradeProvider>> _mockLogger;
    private Fixture _fixture;
    private PowerTradeProvider _sut;

    [SetUp]
    public void Setup()
    {
        _fixture = new Fixture();
        _mockPowerService = new Mock<IPowerService>();
        _mockLogger = new Mock<ILogger<PowerTradeProvider>>();
        _sut = new PowerTradeProvider(_mockPowerService.Object, _mockLogger.Object);
    }

    [Test]
    public async Task GetTradesAsync_WhenPowerServiceReturnsValidTrades_ReturnsTradeCollection()
    {
        // Arrange
        var targetDate = new LocalDate(2025, 12, 10);
        var expectedDateTime = new DateTime(2025, 12, 10); // PowerService expects DateTime
        var expectedTrades = CreateSampleTrades(2);
        
        _mockPowerService
            .Setup(ps => ps.GetTradesAsync(expectedDateTime))
            .ReturnsAsync(expectedTrades);

        // Act
        var result = await _sut.GetTradesAsync(targetDate);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Count(), Is.EqualTo(2));
        _mockPowerService.Verify(ps => ps.GetTradesAsync(expectedDateTime), Times.Once);
    }

    [Test]
    public async Task GetTradesAsync_WhenPowerServiceReturnsNull_ReturnsEmptyList()
    {
        // Arrange
        var targetDate = new LocalDate(2025, 12, 10);
        var expectedDateTime = new DateTime(2025, 12, 10);
        
        _mockPowerService
            .Setup(ps => ps.GetTradesAsync(expectedDateTime))
            .ReturnsAsync((IEnumerable<PowerTrade>)null);

        // Act
        var result = await _sut.GetTradesAsync(targetDate);

        // Assert
        Assert.That(result, Is.Empty);
        _mockPowerService.Verify(ps => ps.GetTradesAsync(expectedDateTime), Times.Once);
    }

    [Test]
    public async Task GetTradesAsync_WhenPowerServiceReturnsEmptyCollection_ReturnsEmptyCollection()
    {
        // Arrange
        var targetDate = new LocalDate(2025, 12, 10);
        var expectedDateTime = new DateTime(2025, 12, 10);
        var emptyTrades = new List<PowerTrade>();
        
        _mockPowerService
            .Setup(ps => ps.GetTradesAsync(expectedDateTime))
            .ReturnsAsync(emptyTrades);

        // Act
        var result = await _sut.GetTradesAsync(targetDate);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Count(), Is.EqualTo(0));
    }

    [Test]
    public async Task GetTradesAsync_WhenPowerServiceReturnsMultipleTrades_ReturnsMaterializedList()
    {
        // Arrange
        var targetDate = new LocalDate(2025, 12, 10);
        var expectedDateTime = new DateTime(2025, 12, 10);
        var expectedTrades = CreateSampleTrades(5);
        
        _mockPowerService
            .Setup(ps => ps.GetTradesAsync(expectedDateTime))
            .ReturnsAsync(expectedTrades);

        // Act
        var result = await _sut.GetTradesAsync(targetDate);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.InstanceOf<List<PowerTrade>>());
        Assert.That(result.Count(), Is.EqualTo(5));
    }

    [Test]
    public async Task GetTradesAsync_WhenCalled_LogsRetrievalInformation()
    {
        // Arrange
        var targetDate = new LocalDate(2025, 12, 10);
        var expectedDateTime = new DateTime(2025, 12, 10);
        var expectedTrades = CreateSampleTrades(3);
        
        _mockPowerService
            .Setup(ps => ps.GetTradesAsync(expectedDateTime))
            .ReturnsAsync(expectedTrades);

        // Act
        await _sut.GetTradesAsync(targetDate);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Retrieving power trades for date")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.AtLeastOnce);
    }

    [Test]
    public async Task GetTradesAsync_WhenSuccessful_LogsTradeCount()
    {
        // Arrange
        var targetDate = new LocalDate(2025, 12, 10);
        var expectedDateTime = new DateTime(2025, 12, 10);
        var expectedTrades = CreateSampleTrades(3);
        
        _mockPowerService
            .Setup(ps => ps.GetTradesAsync(expectedDateTime))
            .ReturnsAsync(expectedTrades);

        // Act
        await _sut.GetTradesAsync(targetDate);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Retrieved") && v.ToString().Contains("trades")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once);
    }

    [Test]
    public void GetTradesAsync_WhenPowerServiceThrowsPowerServiceException_LogsErrorAndPropagatesException()
    {
        // Arrange
        var targetDate = new LocalDate(2025, 12, 10);
        var expectedDateTime = new DateTime(2025, 12, 10);
        var expectedException = new PowerServiceException("Error retrieving power volumes");
        
        _mockPowerService
            .Setup(ps => ps.GetTradesAsync(expectedDateTime))
            .ThrowsAsync(expectedException);

        // Act & Assert
        var ex = Assert.ThrowsAsync<PowerServiceException>(
            async () => await _sut.GetTradesAsync(targetDate));
        
        Assert.That(ex.Message, Is.EqualTo("Error retrieving power volumes"));
        
        // Verify error was logged
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("PowerService error retrieving trades")),
                It.Is<Exception>(e => e == expectedException),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once);
    }

    [Test]
    public async Task GetTradesAsync_WithDifferentDates_CallsPowerServiceWithCorrectDate()
    {
        // Arrange
        var localDate1 = new LocalDate(2025, 12, 10);
        var localDate2 = new LocalDate(2025, 12, 11);
        var expectedDateTime1 = new DateTime(2025, 12, 10);
        var expectedDateTime2 = new DateTime(2025, 12, 11);
        
        _mockPowerService
            .Setup(ps => ps.GetTradesAsync(It.IsAny<DateTime>()))
            .ReturnsAsync(CreateSampleTrades(1));

        // Act
        await _sut.GetTradesAsync(localDate1);
        await _sut.GetTradesAsync(localDate2);

        // Assert
        _mockPowerService.Verify(ps => ps.GetTradesAsync(expectedDateTime1), Times.Once);
        _mockPowerService.Verify(ps => ps.GetTradesAsync(expectedDateTime2), Times.Once);
    }

    [Test]
    public async Task GetTradesAsync_WhenCalledMultipleTimes_EachCallGoesToPowerService()
    {
        // Arrange
        var targetDate = new LocalDate(2025, 12, 10);
        var expectedDateTime = new DateTime(2025, 12, 10);
        _mockPowerService
            .Setup(ps => ps.GetTradesAsync(expectedDateTime))
            .ReturnsAsync(CreateSampleTrades(2));

        // Act
        await _sut.GetTradesAsync(targetDate);
        await _sut.GetTradesAsync(targetDate);
        await _sut.GetTradesAsync(targetDate);

        // Assert
        _mockPowerService.Verify(ps => ps.GetTradesAsync(expectedDateTime), Times.Exactly(3));
    }

    private IEnumerable<PowerTrade> CreateSampleTrades(int count)
    {
        var trades = new List<PowerTrade>();
        
        for (int i = 0; i < count; i++)
        {
            var periods = new PowerPeriod[24];
            for (int p = 0; p < 24; p++)
            {
                var powerPeriod = _fixture.Create<PowerPeriod>();
                powerPeriod.Period = p + 1;
                
                periods[p] = powerPeriod;
            }
            
            var tradeDate = _fixture.Create<DateTime>();
            trades.Add(PowerTrade.Create(tradeDate, 24));
        }
        
        return trades;
    }
}
