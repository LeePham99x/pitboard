using System.Globalization;

namespace Pitboard.Windows.Tests;

[TestClass]
public sealed class WindowsLocalTimeTests
{
    // Friday 15 January 2027, 08:00 UTC.
    private const long At = 1_800_000_000;

    private static WindowsLocalTime In(string culture) =>
        new(new CultureInfo(culture, useUserOverride: false), TimeZoneInfo.Utc);

    // Newer ICU data puts a narrow no-break space before AM, which \s takes too.
    [TestMethod]
    public void AnAmericanReadsTwelveHours()
    {
        var time = In("en-US");

        StringAssert.Matches(time.Clock(At, withWeekday: false), new(@"^8:00\sAM$"));
        StringAssert.Matches(time.Clock(At, withWeekday: true), new(@"^Fri 8:00\sAM$"));
        StringAssert.Matches(time.DateAndTime(At), new(@"^1/15/2027 8:00\sAM$"));
    }

    [TestMethod]
    public void ABritishReaderReadsTwentyFourHours()
    {
        var time = In("en-GB");

        Assert.AreEqual("08:00", time.Clock(At, withWeekday: false));
        Assert.AreEqual("Fri 08:00", time.Clock(At, withWeekday: true));
        Assert.AreEqual("15/01/2027 08:00", time.DateAndTime(At));
    }

    // As Windows' regional format lets a person set their own short time.
    [TestMethod]
    public void APersonsOwnShortTimeIsTheClocks()
    {
        var culture = (CultureInfo)new CultureInfo("en-US", useUserOverride: false).Clone();
        culture.DateTimeFormat.ShortTimePattern = "HH:mm";
        var time = new WindowsLocalTime(culture, TimeZoneInfo.Utc);

        Assert.AreEqual("08:00", time.Clock(At, withWeekday: false));
        Assert.AreEqual("Fri 08:00", time.Clock(At, withWeekday: true));
    }

    [TestMethod]
    public void ADayIsTheOneInThePersonsTimeZone()
    {
        var east = TimeZoneInfo.CreateCustomTimeZone("UTC+02", TimeSpan.FromHours(2), "UTC+02", "UTC+02");
        var before = At + (13 * 3600) + (30 * 60);
        var after = before + 3600;
        var culture = new CultureInfo("en-GB", useUserOverride: false);

        Assert.IsTrue(new WindowsLocalTime(culture, TimeZoneInfo.Utc).SameDay(before, after));
        Assert.IsFalse(new WindowsLocalTime(culture, east).SameDay(before, after));
        Assert.AreEqual("Sat 00:30", new WindowsLocalTime(culture, east).Clock(after, withWeekday: true));
    }
}
