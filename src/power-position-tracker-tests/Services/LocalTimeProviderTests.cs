using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NodaTime;
using power_position_tracker;
using power_position_tracker.Services;

namespace power_position_tracker_tests.Services;

[TestFixture]
public class LocalTimeProviderTests
{
    private LocalTimeProvider _localTimeProvider;
    private Mock<ILogger<LocalTimeProvider>> _loggerMock;
    private Mock<IOptions<PowerPositionSettings>> _settingsMock;

    [SetUp]
    public void Setup()
    {
        _loggerMock = new Mock<ILogger<LocalTimeProvider>>();
        _settingsMock = new Mock<IOptions<PowerPositionSettings>>();

        _settingsMock.Setup(s => s.Value).Returns(new PowerPositionSettings
        {
            TimeZoneId = "Europe/London"
        });

        _localTimeProvider = new LocalTimeProvider(_loggerMock.Object, _settingsMock.Object);
    }

    [Test]
    public void Constructor_NullSettings_ThrowsArgumentNullException()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
        {
            var provider = new LocalTimeProvider(_loggerMock.Object, null);
        });
        Assert.That(ex.ParamName, Is.EqualTo("settings"));
    }

    [Test]
    public void Constructor_CustomTimeZone_UsesConfiguredZone()
    {
        // Arrange
        var customTimeZoneId = "America/New_York";
        _settingsMock.Setup(s => s.Value).Returns(new PowerPositionSettings
        {
            TimeZoneId = customTimeZoneId
        });
        // Act
        var provider = new LocalTimeProvider(_loggerMock.Object, _settingsMock.Object);
        // Assert
        Assert.That(provider.TimeZone.Id, Is.EqualTo(customTimeZoneId));
    }

    [Test]
    public void ToLocalTime_BSTDay_ConvertsCorrectly()
    {
        // Arrange - May 08, 2024 at 12:00 UTC
        var utcDateTime = new DateTime(2024, 5, 8, 12, 0, 0, DateTimeKind.Utc);

        // Act
        var result = _localTimeProvider.ToLocalTime(utcDateTime);

        // Assert
        Assert.That(result.Zone.Id, Is.EqualTo("Europe/London"));
        Assert.That(result.LocalDateTime.Date, Is.EqualTo(new LocalDate(2024, 5, 8)));
        Assert.That(result.LocalDateTime.TimeOfDay, Is.EqualTo(new LocalTime(13, 0))); // BST is UTC+1
    }

    [Test]
    public void ToLocalTime_GMTDay_ConvertsCorrectly()
    {
        // Arrange - May 08, 2024 at 12:00 UTC
        var utcDateTime = new DateTime(2024, 11, 3, 6, 0, 0, DateTimeKind.Utc);

        // Act
        var result = _localTimeProvider.ToLocalTime(utcDateTime);

        // Assert
        Assert.That(result.Zone.Id, Is.EqualTo("Europe/London"));
        Assert.That(result.LocalDateTime.Date, Is.EqualTo(new LocalDate(2024, 11, 3)));
        Assert.That(result.LocalDateTime.TimeOfDay, Is.EqualTo(new LocalTime(6, 0))); // GMT is UTC+0
    }

    [Test]
    public void GetTradingDayStart_NormalWinterDay_Succeeds()
    {
        // Arrange - December 10, 2024 (GMT, No DST)
        var targetDate = new LocalDate(2024, 12, 10);

        // Act - Should succeed: 23:00 on Dec 9, 2024 exists and is unambiguous
        var result = _localTimeProvider.GetTradingDayStart(targetDate);

        // Assert
        Assert.That(result.LocalDateTime.Date, Is.EqualTo(new LocalDate(2024, 12, 9)));
        Assert.That(result.LocalDateTime.TimeOfDay, Is.EqualTo(new LocalTime(23, 0)));
        Assert.That(result.Zone.Id, Is.EqualTo("Europe/London"));
    }

    [Test]
    public void GetTradingDayStart_NormalSummerDay_Succeeds()
    {
        // Arrange - June 11, 2024 (BST active, DST in effect)
        var targetDate = new LocalDate(2024, 6, 11);

        // Act - Should succeed: 23:00 on June 10, 2024 exists and is unambiguous
        var result = _localTimeProvider.GetTradingDayStart(targetDate);

        // Assert
        Assert.That(result.LocalDateTime.Date, Is.EqualTo(new LocalDate(2024, 6, 10)));
        Assert.That(result.LocalDateTime.TimeOfDay, Is.EqualTo(new LocalTime(23, 0)));
        Assert.That(result.Zone.Id, Is.EqualTo("Europe/London"));
    }

    [Test]
    public void GetTradingDayStart_SpringForwardDay_Succeeds() 
    {
        // Arrange - March 31, 2024 (Clocks jump forward at 01:00)
        // Trading day : starts at 23:00 on March 30, 2024 (GMT) through 22:00 on March 31, 2024 (BST)
        var targetDate = new LocalDate(2024, 3, 31);

        // Act - Should succeed: 23:00 on March 30, 2024 is before the BST transition
        var result = _localTimeProvider.GetTradingDayStart(targetDate);

        // Assert
        Assert.That(result.LocalDateTime.Date, Is.EqualTo(new LocalDate(2024, 3, 30)));
        Assert.That(result.LocalDateTime.TimeOfDay, Is.EqualTo(new LocalTime(23, 0)));

        // Verify it's in GMT (offset is 0 hours before BST kicks in)
        Assert.That(result.Offset, Is.EqualTo(Offset.Zero)); // GMT = UTC+0
    }

    [Test]
    public void GetTradingDayStart_FallBackDay_Succeeds()
    {
        // Arrange - October 27, 2024 (Clocks go back at 02:00)
        // Trading day: starts at 23:00 on October 26, 2024 (BST) through 22:00 on October 27, 2024 (GMT)
        var targetDate = new LocalDate(2024, 10, 27);

        // Act - Should succeed: 23:00 on October 26, 2024 is before the GMT transition
        var result = _localTimeProvider.GetTradingDayStart(targetDate);

        // Assert
        Assert.That(result.LocalDateTime.Date, Is.EqualTo(new LocalDate(2024, 10, 26)));
        Assert.That(result.LocalDateTime.TimeOfDay, Is.EqualTo(new LocalTime(23, 0)));

        // Verify it's in BST (offset is +1 hour before GMT kicks in)
        Assert.That(result.Offset, Is.EqualTo(Offset.FromHours(1))); // BST = UTC+1
    }

    [Test]
    public void GetTradingDayStart_AtStrictly_SkippedTime_ThrowsSkippedException()
    {
        // Arrange - March 31, 2024 at 01:30 (this time doesnt exist)
        var londonZone = DateTimeZoneProviders.Tzdb["Europe/London"];
        var skippedTime = new LocalDateTime(2024, 3, 31, 1, 30); // 01:30 on March 31, 2024 is skipped

        // Act & Assert - Expect SkippedTimeException when trying to create ZonedDateTime
        var ex = Assert.Throws<SkippedTimeException>(() =>
        {
            londonZone.AtStrictly(skippedTime);
        });

        Assert.That(ex.LocalDateTime, Is.EqualTo(skippedTime));
        Assert.That(ex.Zone, Is.EqualTo(londonZone));
    }

    [Test]
    public void AtStrictly_AmbiguosTime_ThrowsAmbiguousException()
    {
        var londonZone = DateTimeZoneProviders.Tzdb["Europe/London"];
        var ambiguousTime = new LocalDateTime(2024, 10, 27, 1, 30); // 01:30 on Oct 27, occurs twice

        // Act & Assert - AtStrictly should throw AmbiguousTimeException
        var ex = Assert.Throws<AmbiguousTimeException>(() =>
        {
            londonZone.AtStrictly(ambiguousTime);
        });

        // Verify exception details
        Assert.That(ex.EarlierMapping.LocalDateTime, Is.EqualTo(ambiguousTime));
        Assert.That(ex.LaterMapping.LocalDateTime, Is.EqualTo(ambiguousTime));
        Assert.That(ex.Zone, Is.EqualTo(londonZone));
    }

    [Test]
    public void AtLenientLy_SkippedTime_ResolvesToLaterMapping()
    {
        // Arrange - March 31, 2024 at 01:30 (this time is skipped)
        var londonZone = DateTimeZoneProviders.Tzdb["Europe/London"];
        var skippedTime = new LocalDateTime(2024, 3, 31, 1, 30); // 01:30 on March 31, 2024 is skipped

        // Act - Atleniently should resolve to the first valid time after the gap (02:30 BST)
        var result = londonZone.AtLeniently(skippedTime);

        // Assert
        Assert.That(result.LocalDateTime.Date, Is.EqualTo(new LocalDate(2024, 3, 31)));
        Assert.That(result.LocalDateTime.TimeOfDay, Is.EqualTo(new LocalTime(2, 30))); // Resolved to 02:30
    }

    [Test]
    public void AtLeniently_AmbuguousTime_ResolvesToEarlierMapping()
    {
        // Arrange - October 27, 2024 at 01:30 (this time is ambiguous)
        var londonZone = DateTimeZoneProviders.Tzdb["Europe/London"];
        var ambiguousTime = new LocalDateTime(2024, 10, 27, 1, 30); // 01:30 on Oct 27, occurs twice

        // Act - AtLeniently should resolve to the earlier mapping (BST)
        var result = londonZone.AtLeniently(ambiguousTime);

        // Assert
        Assert.That(result.LocalDateTime.Date, Is.EqualTo(new LocalDate(2024, 10, 27)));
        Assert.That(result.LocalDateTime.TimeOfDay, Is.EqualTo(new LocalTime(1, 30)));
    }

    [Test]
    public void PeriodToZonedDateTime_NormalDay_MapsAllPeriods()
    {
        // Arrange - June 11, 2024 (BST active)
        var targetDate = new LocalDate(2024, 6, 11);
        var tradingDayStart = _localTimeProvider.GetTradingDayStart(targetDate);

        // Act & Assert - Verify all 24 periods map correctly
        for (int period = 1; period <= 24; period++)
        {
            var periodTime = _localTimeProvider.PeriodToZonedDateTime(tradingDayStart, period);
            var expectedHour = (23 + period - 1)%24;
            Assert.That(periodTime.LocalDateTime.TimeOfDay.Hour, Is.EqualTo(expectedHour), $"Period {period} hour mismatch");
        }
    }

    [Test]
    public void PeriodToZonedDateTime_SpringForwardDay_HandlesGapCorrectly()
    {
        // Arrange - March 31, 2024 (spring forward)
        var targetDate = new LocalDate(2024, 3, 31);
        var tradingDayStart = _localTimeProvider.GetTradingDayStart(targetDate);
        // trading day starts at 23:00 on March 30, 2024 (GMT)

        // Act - Map periods during the spring forward transition
        var period1 = _localTimeProvider.PeriodToZonedDateTime(tradingDayStart, 1); // 23:00 Mar 30
        var period2 = _localTimeProvider.PeriodToZonedDateTime(tradingDayStart, 2); // 00:00 Mar 31
        var period3 = _localTimeProvider.PeriodToZonedDateTime(tradingDayStart, 3); // 02:00 Mar 31
        var period4 = _localTimeProvider.PeriodToZonedDateTime(tradingDayStart, 4); // 03:00 Mar 31

        // Assert
        Assert.That(period1.LocalDateTime.TimeOfDay.Hour, Is.EqualTo(23));
        Assert.That(period1.Offset, Is.EqualTo(Offset.Zero)); // GMT
        Assert.That(period2.LocalDateTime.TimeOfDay.Hour, Is.EqualTo(0));
        Assert.That(period2.Offset, Is.EqualTo(Offset.Zero)); // GMT
        Assert.That(period3.LocalDateTime.TimeOfDay.Hour, Is.EqualTo(2));
        Assert.That(period3.Offset, Is.EqualTo(Offset.FromHours(1))); // BST
        Assert.That(period4.LocalDateTime.TimeOfDay.Hour, Is.EqualTo(3));
        Assert.That(period4.Offset, Is.EqualTo(Offset.FromHours(1))); // BST
    }

    [Test]
    public void PeriodToZonedDateTime_FallbackDay_HandlesOverlapCorrectly()
    {
        // Arrange - Oct 27, 2024 (fall back)
        var targetDate = new LocalDate(2024, 10, 27);
        var tradingDayStart = _localTimeProvider.GetTradingDayStart(targetDate);
        // trading day starts at 23:00 on Oct 26, 2024 (BST)

        // Act - Map periods during the fall back transition
        var period1 = _localTimeProvider.PeriodToZonedDateTime(tradingDayStart, 1); // 23:00 Oct 26
        var period2 = _localTimeProvider.PeriodToZonedDateTime(tradingDayStart, 2); // 00:00 Oct 27
        var period3 = _localTimeProvider.PeriodToZonedDateTime(tradingDayStart, 3); // 01:00 Oct 27
        var period4 = _localTimeProvider.PeriodToZonedDateTime(tradingDayStart, 4); // 01:00 Oct 27 (second occurrence)
        var period5 = _localTimeProvider.PeriodToZonedDateTime(tradingDayStart, 5); // 02:00 Oct 27 

        // Assert
        Assert.That(period1.LocalDateTime.TimeOfDay.Hour, Is.EqualTo(23));
        Assert.That(period1.Offset, Is.EqualTo(Offset.FromHours(1))); // BST
        Assert.That(period2.LocalDateTime.TimeOfDay.Hour, Is.EqualTo(0));
        Assert.That(period2.Offset, Is.EqualTo(Offset.FromHours(1))); // BST
        Assert.That(period3.LocalDateTime.TimeOfDay.Hour, Is.EqualTo(1));
        Assert.That(period3.Offset, Is.EqualTo(Offset.FromHours(1))); // BST
        Assert.That(period4.LocalDateTime.TimeOfDay.Hour, Is.EqualTo(1));
        Assert.That(period4.Offset, Is.EqualTo(Offset.FromHours(0))); // GMT
        Assert.That(period5.LocalDateTime.TimeOfDay.Hour, Is.EqualTo(2));
        Assert.That(period5.Offset, Is.EqualTo(Offset.FromHours(0))); // GMT
    }

    [Test]
    public void PeriodToZonedDateTime_InvalidPeriod_ThrowsException()
    {
        // Arrange 
        var targetDate = new LocalDate(2024, 12, 10);
        var tradingDayStart = _localTimeProvider.GetTradingDayStart(targetDate);

        // Act & Asseret - Period 0 should throw
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            _localTimeProvider.PeriodToZonedDateTime(tradingDayStart, 0);
        });

        // Act & Asseret - Period 25 should throw
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            _localTimeProvider.PeriodToZonedDateTime(tradingDayStart, 25);
        });
    }

    [Test]
    public void FormatLocalTime_VariousTimes_FormatsCorrectly()
    {
        // Arrange
        var londonZone = DateTimeZoneProviders.Tzdb["Europe/London"];
        var time1 = londonZone.AtStrictly(new LocalDateTime(2024, 12, 10, 9, 5));   // 09:05
        var time2 = londonZone.AtStrictly(new LocalDateTime(2024, 6, 11, 15, 30));  // 15:30
        var time3 = londonZone.AtStrictly(new LocalDateTime(2024, 3, 31, 23, 45));  // 23:45

        // Act
        var formatted1 = _localTimeProvider.FormatToLocalTime(time1);
        var formatted2 = _localTimeProvider.FormatToLocalTime(time2);
        var formatted3 = _localTimeProvider.FormatToLocalTime(time3);

        // Assert
        Assert.That(formatted1, Is.EqualTo("09:05"));
        Assert.That(formatted2, Is.EqualTo("15:30"));
        Assert.That(formatted3, Is.EqualTo("23:45"));
    }
}
