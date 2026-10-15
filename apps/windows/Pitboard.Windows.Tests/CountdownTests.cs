namespace Pitboard.Windows.Tests;

[TestClass]
public sealed class CountdownTests
{
    private const long At = 1_800_000_000;

    private static DateTimeOffset Second(long epoch) => DateTimeOffset.FromUnixTimeSeconds(epoch);

    [TestMethod]
    public void MinutesAndSecondsUnderAnHour()
    {
        Assert.AreEqual("2:05", Countdown.Left(At + 125, Second(At)));
        Assert.AreEqual("59:59", Countdown.Left(At + 3599, Second(At)));
    }

    [TestMethod]
    public void HoursFirstFromAnHourOn()
    {
        Assert.AreEqual("1:00:05", Countdown.Left(At + 3605, Second(At)));
    }

    [TestMethod]
    public void ItStopsAtNought()
    {
        Assert.AreEqual("0:00", Countdown.Left(At, Second(At + 30)));
        Assert.IsTrue(Countdown.Ended(At, Second(At)));
        Assert.IsFalse(Countdown.Ended(At + 1, Second(At)));
    }
}
