namespace Pitboard.Windows.Tests;

[TestClass]
public sealed class SharePackageTests
{
    private const string Installed = @"C:\Users\Dana Lee\AppData\Local\Programs\Pitboard\";

    [TestMethod]
    public void TheUninstallRemovesThePackageWhoseExternalLocationIsThisInstallInAnyCase()
    {
        var packages = new StandInSharePackages(
            ("PitboardShare_1.0.0.1000_neutral__abc", @"C:\USERS\DANA LEE\APPDATA\LOCAL\PROGRAMS\PITBOARD"),
            ("PitboardShare_0.9.0.1000_neutral__def", @"C:\Users\Dana Lee\Builds\Pitboard"));

        var failed = SharePackage.Unregister(packages, Installed);

        Assert.AreEqual(0, failed);
        Assert.AreEqual("remove PitboardShare_1.0.0.1000_neutral__abc", string.Join(", ", packages.Asked));
    }

    // Windows read no external location, as for a package registered with another folder than this one.
    [TestMethod]
    public void APackageWithNoExternalLocationIsLeftAlone()
    {
        var packages = new StandInSharePackages(("PitboardShare_1.0.0.1000_neutral__abc", null));

        Assert.AreEqual(0, SharePackage.Unregister(packages, Installed));
        Assert.IsEmpty(packages.Asked);
    }

    [TestMethod]
    public void ARemovalWindowsRefusesGivesItsError()
    {
        const int packagesInUse = unchecked((int)0x80073D02);
        var packages = new StandInSharePackages(("PitboardShare_1.0.0.1000_neutral__abc", Installed)) { Refuses = packagesInUse };

        Assert.AreEqual(packagesInUse, SharePackage.Unregister(packages, Installed));
    }

    private sealed class StandInSharePackages(params (string FullName, string? Location)[] registered) : ISharePackages
    {
        public List<string> Asked { get; } = [];

        public int Refuses { get; init; }

        public IReadOnlyList<(string FullName, string? Location)> Registered() => registered;

        public int Remove(string fullName)
        {
            Asked.Add($"remove {fullName}");
            return Refuses;
        }
    }
}
