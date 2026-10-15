namespace Pitboard.Windows;

public enum Build
{
    Debug,
    Release,
}

public sealed record Identity(Build Build)
{
    public string Aumid => Build is Build.Debug ? "Pitboard.Pitboard.Debug" : "Pitboard.Pitboard";

    public string LinkScheme => Build is Build.Debug ? "pitboard-debug" : "pitboard";

    public string DataFolderName => Build is Build.Debug ? "Pitboard App Debug" : "Pitboard App";

    public string InstanceKey(string? fixtureFolder) =>
        (Build, fixtureFolder) switch
        {
            (Build.Release, _) => "Pitboard",
            (Build.Debug, null) => "Pitboard Debug",
            (Build.Debug, var folder) => $"Pitboard Debug {folder}",
            _ => throw new ArgumentOutOfRangeException(nameof(fixtureFolder)),
        };

    // Beside Pitboard's folder, never in it: neither PITBOARD_HOME nor pitboard uninstall reaches it.
    public string DataFolder(string localAppData, string? fixtureFolder) =>
        fixtureFolder ?? Path.Combine(localAppData, DataFolderName);
}
