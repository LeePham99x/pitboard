using System.Reflection;
using Pitboard.Core;

namespace Pitboard.Windows;

public sealed record SettingsLink(string Title, string Url);

// What the settings page says and sends beyond the model's words, by the macOS Settings' rules.
public static class SettingsPage
{
    private const int MinutesInADay = 24 * 60;

    public const string StartupNote = "Settings > Apps > Startup can turn it off too.";

    public const string NoUpdateKey =
        "This copy of Pitboard can’t update itself: it was built from source and carries no update key. "
        + "A copy from a release keeps itself up to date.";

    public const string BetaNote = "A beta updates itself to 1.0.0 once that is out. Install a later beta by hand.";

    public const string NewTerminals = "Pitboard’s installer puts pitboard on your PATH, which terminals opened after it ran see.";

    public const string WhatItDoes =
        "The command line does everything the app does, and more: pitboard status in a script, and pitboard repair "
        + "for a parked login Pitboard’s records have lost track of.";

    public const string Copyright = "Copyright 2026 the Pitboard authors";

    public static readonly SettingsLink[] Links =
    [
        new("usepitboard.com", "https://usepitboard.com"),
        new("Source code on GitHub", "https://github.com/datlechin/pitboard"),
        new("Report a problem", "https://github.com/datlechin/pitboard/issues"),
        new("Apache License 2.0", "https://github.com/datlechin/pitboard/blob/main/LICENSE"),
    ];

    // The .NET SDK adds the commit to an assembly's informational version after a plus.
    public static string Version(Assembly assembly) =>
        Version(assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    public static string Version(string? informational) => (informational ?? "").Split('+')[0];

    public static string Named(string version) => version.Length == 0 ? "Pitboard" : $"Pitboard {version}";

    public static bool IsBeta(string version) => version.Contains('-', StringComparison.Ordinal);

    public static string[] UpdatesNotes(IUpdates updates, string version) =>
        [.. updates.Available ? [] : new[] { NoUpdateKey }, .. IsBeta(version) ? new[] { BetaNote } : []];

    public static string? TerminalNote(CommandLineShown shown) => shown.Found is FoundCommandLine.Nowhere ? NewTerminals : null;

    // 24:00, the end of a day, shows as midnight, as the macOS pickers show it.
    public static TimeSpan TimeOfDay(ushort minutes) => TimeSpan.FromMinutes(minutes % MinutesInADay);

    public static ushort Minutes(TimeSpan time) => (ushort)(((time.Hours * 60) + time.Minutes) % MinutesInADay);

    // A NumberBox holds NaN once its text is cleared.
    public static byte? Share(double value, AutoSwitchShown auto) =>
        double.IsNaN(value) ? null : (byte)Math.Clamp(Math.Round(value), auto.Lowest, auto.Highest);
}
