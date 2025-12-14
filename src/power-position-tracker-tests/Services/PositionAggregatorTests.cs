using Moq;
using AutoFixture;
using Services;
using power_position_tracker.Services;
using power_position_tracker.Services.Interfaces;
using Microsoft.Extensions.Logging;
using NodaTime;

namespace power_position_tracker_tests.Services;

[TestFixture]
public class PositionAggregatorTests
{
    private Mock<ILocalTimeProvider> _mockLocalTimeProvider;
    private Mock<ILogger<PositionAggregator>> _mockLogger;
    private Fixture _fixture;
    private PositionAggregator _sut;
    private DateTimeZone _londonTimeZone;

    [SetUp]
    public void Setup()
    {
        _fixture = new Fixture();
        _mockLocalTimeProvider = new Mock<ILocalTimeProvider>();
        _mockLogger = new Mock<ILogger<PositionAggregator>>();
        _londonTimeZone = DateTimeZoneProviders.Tzdb["Europe/London"];

        _mockLocalTimeProvider.Setup(ltp => ltp.TimeZone).Returns(_londonTimeZone);

        _sut = new PositionAggregator(_mockLocalTimeProvider.Object, _mockLogger.Object);
    }

    [Test]
    public void Aggregate_WhenTradesIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        IEnumerable<PowerTrade> trades = null;
        var targetDate = new LocalDate(2025, 12, 10);

        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() => _sut.Aggregate(trades, targetDate));
        Assert.That(ex.ParamName, Is.EqualTo("trades"));
        Assert.That(ex.Message, Does.Contain("Trades collection cannot be null"));
    }

    [Test]
    public void Aggregate_WhenPeriodCountIsNotDivisibleBy24_ThrowsInvalidOperationException()
    {
        // Arrange
        var targetDate = new LocalDate(2025, 12, 10);
        var trades = CreateTradesWithPeriodCount(23); // Invalid: not divisible by 24

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => _sut.Aggregate(trades, targetDate));
        Assert.That(ex.Message, Does.Contain("Expected period count to be a multiple of 24"));
        Assert.That(ex.Message, Does.Contain("23 periods"));
    }

    [Test]
    public void Aggregate_WhenPeriodCountIs25_ThrowsInvalidOperationException()
    {
        // Arrange
        var targetDate = new LocalDate(2025, 12, 10);
        var trades = CreateTradesWithPeriodCount(25); // Invalid: not divisible by 24

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => _sut.Aggregate(trades, targetDate));
        Assert.That(ex.Message, Does.Contain("Expected period count to be a multiple of 24"));
        Assert.That(ex.Message, Does.Contain("25 periods"));
    }

    [Test]
    public void Aggregate_WhenSingleTradeWith24Periods_ReturnsAggregatedPositions()
    {
        // Arrange
        var targetDate = new LocalDate(2025, 12, 10);
        var tradingDayStart = new LocalDateTime(2025, 12, 9, 23, 0).InZoneStrictly(_londonTimeZone);
        var trades = CreateSampleTrades(1);

        _mockLocalTimeProvider.Setup(ltp => ltp.GetTradingDayStart(targetDate))
            .Returns(tradingDayStart);

        SetupPeriodToZonedDateTimeMock(tradingDayStart);

        // Act
        var result = _sut.Aggregate(trades, targetDate).ToList();

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Count, Is.EqualTo(24));
        Assert.That(result.Select(r => r.Period), Is.Ordered);
        _mockLocalTimeProvider.Verify(ltp => ltp.GetTradingDayStart(targetDate), Times.Once);
    }

    [Test]
    public void Aggregate_WhenMultipleTradesWithSamePeriod_SumsVolumes()
    {
        // Arrange
        var targetDate = new LocalDate(2025, 12, 10);
        var tradingDayStart = new LocalDateTime(2025, 12, 9, 23, 0).InZoneStrictly(_londonTimeZone);

        // Create trades with known volumes for easy verification
        var trade1 = CreateTradeWithVolumes(100.0);
        var trade2 = CreateTradeWithVolumes(50.0);
        var trade3 = CreateTradeWithVolumes(25.0);
        var trades = new List<PowerTrade> { trade1, trade2, trade3 };

        _mockLocalTimeProvider.Setup(ltp => ltp.GetTradingDayStart(targetDate))
            .Returns(tradingDayStart);

        SetupPeriodToZonedDateTimeMock(tradingDayStart);

        // Act
        var result = _sut.Aggregate(trades, targetDate).ToList();

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Count, Is.EqualTo(24));
        
        // Each period should have volume = 100 + 50 + 25 = 175
        foreach (var position in result)
        {
            Assert.That(position.Volume, Is.EqualTo(175.0));
        }
    }

    [Test]
    public void Aggregate_WhenMultipleTradesWithDifferentVolumes_AggregatesCorrectly()
    {
        // Arrange
        var targetDate = new LocalDate(2025, 12, 10);
        var tradingDayStart = new LocalDateTime(2025, 12, 9, 23, 0).InZoneStrictly(_londonTimeZone);

        // Create two trades with specific volumes per period
        var trade1 = PowerTrade.Create(new DateTime(2025, 12, 10), 24);
        var trade2 = PowerTrade.Create(new DateTime(2025, 12, 10), 24);

        // Set specific volumes for each period in trade1
        for (int i = 0; i < 24; i++)
        {
            trade1.Periods[i].Period = i + 1;
            trade1.Periods[i].Volume = (i + 1) * 10.0; // 10, 20, 30, ..., 240
        }

        // Set specific volumes for each period in trade2
        for (int i = 0; i < 24; i++)
        {
            trade2.Periods[i].Period = i + 1;
            trade2.Periods[i].Volume = (i + 1) * 5.0; // 5, 10, 15, ..., 120
        }

        var trades = new List<PowerTrade> { trade1, trade2 };

        _mockLocalTimeProvider.Setup(ltp => ltp.GetTradingDayStart(targetDate))
            .Returns(tradingDayStart);

        SetupPeriodToZonedDateTimeMock(tradingDayStart);

        // Act
        var result = _sut.Aggregate(trades, targetDate).ToList();

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Count, Is.EqualTo(24));

        for (int i = 0; i < 24; i++)
        {
            var expectedVolume = (i + 1) * 10.0 + (i + 1) * 5.0; // Sum of both trades
            Assert.That(result[i].Volume, Is.EqualTo(expectedVolume));
            Assert.That(result[i].Period, Is.EqualTo(i + 1));
        }
    }

    [Test]
    public void Aggregate_WhenTradesHave48Periods_AggregatesSuccessfully()
    {
        // Arrange - Two trades, each with 24 periods = 48 total (divisible by 24)
        var targetDate = new LocalDate(2025, 12, 10);
        var tradingDayStart = new LocalDateTime(2025, 12, 9, 23, 0).InZoneStrictly(_londonTimeZone);
        var trades = CreateSampleTrades(2);

        _mockLocalTimeProvider.Setup(ltp => ltp.GetTradingDayStart(targetDate))
            .Returns(tradingDayStart);

        SetupPeriodToZonedDateTimeMock(tradingDayStart);

        // Act
        var result = _sut.Aggregate(trades, targetDate).ToList();

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Count, Is.EqualTo(24));
    }

    [Test]
    public void Aggregate_WithEmptyTradesCollection_ThrowsInvalidOperationException()
    {
        // Arrange
        var targetDate = new LocalDate(2025, 12, 10);
        var trades = new List<PowerTrade>(); // Empty collection - 0 periods

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => _sut.Aggregate(trades, targetDate));
        Assert.That(ex.Message, Does.Contain("Expected period count to be a multiple of 24"));
        Assert.That(ex.Message, Does.Contain("0 periods"));
    }

    [Test]
    public void Aggregate_ResultsAreOrderedByPeriod()
    {
        // Arrange
        var targetDate = new LocalDate(2025, 12, 10);
        var tradingDayStart = new LocalDateTime(2025, 12, 9, 23, 0).InZoneStrictly(_londonTimeZone);

        // Create trade with periods in random order
        var trade = PowerTrade.Create(new DateTime(2025, 12, 10), 24);
        var random = new Random(42);
        var shuffledPeriods = trade.Periods.OrderBy(x => random.Next()).ToArray();
        
        // Reassign shuffled periods
        for (int i = 0; i < 24; i++)
        {
            trade.Periods[i] = shuffledPeriods[i];
        }

        var trades = new List<PowerTrade> { trade };

        _mockLocalTimeProvider.Setup(ltp => ltp.GetTradingDayStart(targetDate))
            .Returns(tradingDayStart);

        SetupPeriodToZonedDateTimeMock(tradingDayStart);

        // Act
        var result = _sut.Aggregate(trades, targetDate).ToList();

        // Assert
        Assert.That(result.Select(r => r.Period), Is.Ordered);
        for (int i = 0; i < 24; i++)
        {
            Assert.That(result[i].Period, Is.EqualTo(i + 1));
        }
    }

    [Test]
    public void Aggregate_UsesLocalTimeProviderForTimeConversion()
    {
        // Arrange
        var targetDate = new LocalDate(2025, 12, 10);
        var tradingDayStart = new LocalDateTime(2025, 12, 9, 23, 0).InZoneStrictly(_londonTimeZone);
        var trades = CreateSampleTrades(1);

        _mockLocalTimeProvider.Setup(ltp => ltp.GetTradingDayStart(targetDate))
            .Returns(tradingDayStart);

        SetupPeriodToZonedDateTimeMock(tradingDayStart);

        // Act
        _sut.Aggregate(trades, targetDate);

        // Assert
        _mockLocalTimeProvider.Verify(ltp => ltp.GetTradingDayStart(targetDate), Times.Once);
        _mockLocalTimeProvider.Verify(ltp => ltp.PeriodToZonedDateTime(tradingDayStart, It.IsAny<int>()), Times.Exactly(24));
        _mockLocalTimeProvider.Verify(ltp => ltp.FormatToLocalTime(It.IsAny<ZonedDateTime>()), Times.Exactly(24));
    }

    [Test]
    public void Aggregate_FormatsLocalTimeCorrectly()
    {
        // Arrange
        var targetDate = new LocalDate(2025, 12, 10);
        var tradingDayStart = new LocalDateTime(2025, 12, 9, 23, 0).InZoneStrictly(_londonTimeZone);
        var trades = CreateSampleTrades(1);

        _mockLocalTimeProvider.Setup(ltp => ltp.GetTradingDayStart(targetDate))
            .Returns(tradingDayStart);

        // Setup to return specific formatted times for each period
        for (int period = 1; period <= 24; period++)
        {
            var hour = (22 + period) % 24; // Starting at 23:00
            var expectedTime = $"{hour:D2}:00";
            var periodTime = tradingDayStart.Plus(Duration.FromHours(period - 1));
            
            _mockLocalTimeProvider.Setup(ltp => ltp.PeriodToZonedDateTime(tradingDayStart, period))
                .Returns(periodTime);
            _mockLocalTimeProvider.Setup(ltp => ltp.FormatToLocalTime(periodTime))
                .Returns(expectedTime);
        }

        // Act
        var result = _sut.Aggregate(trades, targetDate).ToList();

        // Assert
        Assert.That(result.Count, Is.EqualTo(24));
        for (int i = 0; i < 24; i++)
        {
            Assert.That(result[i].LocalTime, Is.Not.Null.And.Not.Empty);
        }
    }

    [Test]
    public void Aggregate_WithNegativeVolumes_HandlesCorrectly()
    {
        // Arrange
        var targetDate = new LocalDate(2025, 12, 10);
        var tradingDayStart = new LocalDateTime(2025, 12, 9, 23, 0).InZoneStrictly(_londonTimeZone);

        var trade1 = CreateTradeWithVolumes(100.0);
        var trade2 = CreateTradeWithVolumes(-30.0); // Negative volume
        var trades = new List<PowerTrade> { trade1, trade2 };

        _mockLocalTimeProvider.Setup(ltp => ltp.GetTradingDayStart(targetDate))
            .Returns(tradingDayStart);

        SetupPeriodToZonedDateTimeMock(tradingDayStart);

        // Act
        var result = _sut.Aggregate(trades, targetDate).ToList();

        // Assert
        Assert.That(result.Count, Is.EqualTo(24));
        foreach (var position in result)
        {
            Assert.That(position.Volume, Is.EqualTo(70.0)); // 100 + (-30) = 70
        }
    }

    [Test]
    public void Aggregate_WithZeroVolumes_ReturnsZeroSums()
    {
        // Arrange
        var targetDate = new LocalDate(2025, 12, 10);
        var tradingDayStart = new LocalDateTime(2025, 12, 9, 23, 0).InZoneStrictly(_londonTimeZone);
        var trade = CreateTradeWithVolumes(0.0);
        var trades = new List<PowerTrade> { trade };

        _mockLocalTimeProvider.Setup(ltp => ltp.GetTradingDayStart(targetDate))
            .Returns(tradingDayStart);

        SetupPeriodToZonedDateTimeMock(tradingDayStart);

        // Act
        var result = _sut.Aggregate(trades, targetDate).ToList();

        // Assert
        Assert.That(result.Count, Is.EqualTo(24));
        foreach (var position in result)
        {
            Assert.That(position.Volume, Is.EqualTo(0.0));
        }
    }

    [Test]
    public void Aggregate_WithLargeNumberOfTrades_AggregatesCorrectly()
    {
        // Arrange
        var targetDate = new LocalDate(2025, 12, 10);
        var tradingDayStart = new LocalDateTime(2025, 12, 9, 23, 0).InZoneStrictly(_londonTimeZone);
        
        // Create 100 trades, each with 24 periods of volume 1.0
        var trades = Enumerable.Range(0, 100)
            .Select(_ => CreateTradeWithVolumes(1.0))
            .ToList();

        _mockLocalTimeProvider.Setup(ltp => ltp.GetTradingDayStart(targetDate))
            .Returns(tradingDayStart);

        SetupPeriodToZonedDateTimeMock(tradingDayStart);

        // Act
        var result = _sut.Aggregate(trades, targetDate).ToList();

        // Assert
        Assert.That(result.Count, Is.EqualTo(24));
        foreach (var position in result)
        {
            Assert.That(position.Volume, Is.EqualTo(100.0)); // 100 trades * 1.0
        }
    }

    [Test]
    public void Aggregate_EnsuresAllPeriodsFrom1To24ArePresent()
    {
        // Arrange
        var targetDate = new LocalDate(2025, 12, 10);
        var tradingDayStart = new LocalDateTime(2025, 12, 9, 23, 0).InZoneStrictly(_londonTimeZone);
        var trades = CreateSampleTrades(1);

        _mockLocalTimeProvider.Setup(ltp => ltp.GetTradingDayStart(targetDate))
            .Returns(tradingDayStart);

        SetupPeriodToZonedDateTimeMock(tradingDayStart);

        // Act
        var result = _sut.Aggregate(trades, targetDate).ToList();

        // Assert
        var periods = result.Select(r => r.Period).ToList();
        Assert.That(periods, Is.EquivalentTo(Enumerable.Range(1, 24)));
    }

    // Helper methods
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
            var trade = PowerTrade.Create(tradeDate, 24);
            for (int p = 0; p < 24; p++)
            {
                trade.Periods[p] = periods[p];
            }
            
            trades.Add(trade);
        }
        
        return trades;
    }

    private PowerTrade CreateTradeWithVolumes(double volume)
    {
        var trade = PowerTrade.Create(new DateTime(2025, 12, 10), 24);
        for (int i = 0; i < 24; i++)
        {
            trade.Periods[i].Period = i + 1;
            trade.Periods[i].Volume = volume;
        }
        return trade;
    }

    private IEnumerable<PowerTrade> CreateTradesWithPeriodCount(int periodCount)
    {
        var trade = PowerTrade.Create(new DateTime(2025, 12, 10), periodCount);
        for (int i = 0; i < periodCount; i++)
        {
            trade.Periods[i].Period = i + 1;
            trade.Periods[i].Volume = _fixture.Create<double>();
        }
        return new List<PowerTrade> { trade };
    }

    private void SetupPeriodToZonedDateTimeMock(ZonedDateTime tradingDayStart)
    {
        for (int period = 1; period <= 24; period++)
        {
            var periodTime = tradingDayStart.Plus(Duration.FromHours(period - 1));
            var hour = periodTime.Hour;
            var formattedTime = $"{hour:D2}:00";

            _mockLocalTimeProvider.Setup(ltp => ltp.PeriodToZonedDateTime(tradingDayStart, period))
                .Returns(periodTime);
            _mockLocalTimeProvider.Setup(ltp => ltp.FormatToLocalTime(periodTime))
                .Returns(formattedTime);
        }
    }
}
