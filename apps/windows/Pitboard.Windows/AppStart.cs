namespace Pitboard.Windows;

public sealed record AppStart(
    Identity Identity, Dictionary<string, string> Environment, string? Fixture, string DataFolder);
