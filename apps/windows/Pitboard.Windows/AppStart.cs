namespace Pitboard.Windows;

public sealed record AppStart(
    Identity Identity, Dictionary<string, string> Environment, string? Fixture, string DataFolder)
{
    public IReadOnlyList<string> Arguments { get; init; } = [];

    public string? Link => Launching.Link(Arguments, Identity.LinkScheme);
}
