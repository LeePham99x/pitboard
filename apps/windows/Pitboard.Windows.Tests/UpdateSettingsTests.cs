namespace Pitboard.Windows.Tests;

[TestClass]
public sealed class UpdateSettingsTests
{
    private readonly string _folder = Directory.CreateTempSubdirectory("pitboard-updates-").FullName;

    [TestCleanup]
    public void RemoveFolder() => Directory.Delete(_folder, recursive: true);

    [TestMethod]
    public void WhatWinSparkleWritesTheNextRunReads()
    {
        var file = Path.Combine(_folder, "Pitboard App", "updates.json");
        var settings = new UpdateSettings(file);

        Assert.IsNull(settings.Read("CheckForUpdates"));
        settings.Write("CheckForUpdates", "1");
        settings.Write("SkipThisVersion", "1.0.1");
        settings.Delete("SkipThisVersion");

        var next = new UpdateSettings(file);
        Assert.AreEqual("1", next.Read("CheckForUpdates"));
        Assert.IsNull(next.Read("SkipThisVersion"));
    }

    [TestMethod]
    public void AFileItCannotReadHoldsNothing()
    {
        var file = Path.Combine(_folder, "updates.json");
        File.WriteAllText(file, "{ not json");
        var settings = new UpdateSettings(file);

        Assert.IsNull(settings.Read("CheckForUpdates"));
        settings.Write("CheckForUpdates", "0");
        Assert.AreEqual("0", new UpdateSettings(file).Read("CheckForUpdates"));
    }
}
