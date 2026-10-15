using System.Globalization;

namespace Pitboard.Windows;

// As the macOS app draws a timer: minutes and seconds, hours first from an hour on, and nought once it has passed.
public static class Countdown
{
    public static string Left(long until, DateTimeOffset now)
    {
        var left = TimeSpan.FromSeconds(Math.Max(until - now.ToUnixTimeSeconds(), 0));
        return left.TotalHours >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)left.TotalHours}:{left.Minutes:00}:{left.Seconds:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{left.Minutes}:{left.Seconds:00}");
    }

    public static bool Ended(long until, DateTimeOffset now) => now.ToUnixTimeSeconds() >= until;
}
