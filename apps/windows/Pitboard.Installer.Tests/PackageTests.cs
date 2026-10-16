using System.Globalization;
using System.Text.RegularExpressions;
using WixToolset.Dtf.WindowsInstaller;

namespace Pitboard.Installer.Tests;

[TestClass]
public sealed class PackageTests
{
    // One for the x64 and the ARM64 MSI, so an install of either replaces the other: CI's two legs each check theirs.
    private const string UpgradeCode = "{9D967FF2-F54D-4D63-9B26-51D7E75F62FC}";

    private const string FullUninstall = "REMOVE = \"ALL\" AND NOT UPGRADINGPRODUCTCODE";

    private const int Deferred = 0x400;
    private const int IgnoreExitCode = 0x40;
    private const int AsyncNoWait = 0xC0;
    private const int ExeFromInstalledFile = 18;

    private static Database Open() => new(Built.Msi, DatabaseOpenMode.ReadOnly);

    private static List<string?[]> Rows(Database database, string query, int columns)
    {
        using var view = database.OpenView(query);
        view.Execute();
        var rows = new List<string?[]>();
        foreach (var record in view)
        {
            using (record)
            {
                rows.Add(Enumerable.Range(1, columns).Select(field => record.IsNull(field) ? null : record.GetString(field)).ToArray());
            }
        }

        return rows;
    }

    private static string? Property(Database database, string name) =>
        Rows(database, $"SELECT `Value` FROM `Property` WHERE `Property` = '{name}'", 1).SingleOrDefault()?[0];

    [TestMethod]
    public void ItIsPitboardsForOneAccountWithOneUpgradeCode()
    {
        using var database = Open();

        Assert.AreEqual("Pitboard", Property(database, "ProductName"));
        Assert.AreEqual("Pitboard", Property(database, "Manufacturer"));
        Assert.AreEqual(UpgradeCode, Property(database, "UpgradeCode"));
        Assert.AreEqual("2", Property(database, "ALLUSERS"));
        Assert.AreEqual("1", Property(database, "MSIINSTALLPERUSER"));
        var conditions = Rows(database, "SELECT `Condition` FROM `LaunchCondition`", 1).Select(row => row[0]).ToList();
        CollectionAssert.Contains(conditions, "NOT ALLUSERS OR ACTION = \"ADMIN\"");
    }

    // Beta N of X.Y.Z is X.Y.Z.N, and a beta and its final upgrade each other, since Windows Installer compares three fields.
    [TestMethod]
    public void ItCarriesCargoTomlsVersionAndReplacesTheSameVersion()
    {
        using var database = Open();
        var expected = Regex.Replace(Built.CargoVersion(), @"^(\d+\.\d+\.\d+)-beta\.(\d+)$", "$1.$2");

        Assert.AreEqual(expected, Property(database, "ProductVersion"));
        var upgrades = Rows(database, "SELECT `UpgradeCode`, `VersionMax`, `Attributes` FROM `Upgrade` WHERE `VersionMax` IS NOT NULL", 3);
        var upgrade = upgrades.Single();
        Assert.AreEqual(UpgradeCode, upgrade[0]);
        Assert.AreEqual(expected, upgrade[1]);
        const int versionMaxInclusive = 0x200;
        Assert.AreEqual(versionMaxInclusive, int.Parse(upgrade[2]!, CultureInfo.InvariantCulture) & versionMaxInclusive);
    }

    [TestMethod]
    public void RestartManagerIsLeftOn()
    {
        using var database = Open();

        Assert.IsNull(Property(database, "MSIRESTARTMANAGERCONTROL"));
        Assert.IsNull(Property(database, "MSIDISABLERMRESTART"));
    }

    [TestMethod]
    public void TheCommandLineGoesOnTheUserPathAndComesOffItOnUninstall()
    {
        using var database = Open();

        var row = Rows(database, "SELECT `Name`, `Value` FROM `Environment`", 2).Single();
        var name = row[0]!;
        var flags = name[..^name.TrimStart('=', '+', '-', '!', '*').Length];
        Assert.AreEqual("PATH", name[flags.Length..], ignoreCase: true);
        StringAssert.Contains(flags, "=", "it is set on an install");
        StringAssert.Contains(flags, "-", "it goes on the uninstall");
        Assert.IsFalse(flags.Contains('*', StringComparison.Ordinal), "the machine's PATH");
        Assert.AreEqual("[~];[INSTALLFOLDER]bin", row[1]);
    }

    [TestMethod]
    public void PitboardLinksOpenTheInstalledPitboardExe()
    {
        using var database = Open();
        var rows = Rows(database, "SELECT `Root`, `Key`, `Name`, `Value` FROM `Registry`", 4);

        string? Value(string key, string? name) =>
            rows.Single(row => row[1] == key && row[2] == name)[3];

        Assert.AreEqual("URL:Pitboard", Value(@"Software\Classes\pitboard", null));
        Assert.IsNull(Value(@"Software\Classes\pitboard", "URL Protocol"));
        Assert.AreEqual("\"[INSTALLFOLDER]Pitboard.exe\" \"%1\"", Value(@"Software\Classes\pitboard\shell\open\command", null));
    }

    [TestMethod]
    public void TheStartMenuShortcutCarriesPitboardsAppUserModelId()
    {
        using var database = Open();

        var shortcut = Rows(database, "SELECT `Shortcut`, `Directory_`, `Target` FROM `Shortcut`", 3).Single();
        Assert.AreEqual("ProgramMenuFolder", shortcut[1]);
        Assert.AreEqual("[INSTALLFOLDER]Pitboard.exe", shortcut[2]);
        var property = Rows(database, "SELECT `Shortcut_`, `PropertyKey`, `PropVariantValue` FROM `MsiShortcutProperty`", 3).Single();
        Assert.AreEqual($"{shortcut[0]} System.AppUserModel.ID Pitboard.Pitboard", string.Join(' ', property));
    }

    [TestMethod]
    public void OnlyAFullUninstallUnregistersAndRemovesTheAccountWindowsData()
    {
        using var database = Open();

        var unregister = Rows(database, "SELECT `Type`, `Source`, `Target` FROM `CustomAction` WHERE `Action` = 'Unregister'", 3).Single();
        var type = int.Parse(unregister[0]!, CultureInfo.InvariantCulture);
        Assert.AreEqual(ExeFromInstalledFile, type & 0x3F);
        Assert.AreEqual(Deferred, type & Deferred);
        Assert.AreEqual(IgnoreExitCode, type & IgnoreExitCode);
        Assert.AreEqual("PitboardExe --unregister", string.Join(' ', unregister[1..]));
        Assert.AreEqual(FullUninstall, Condition(database, "Unregister"));

        var removed = Rows(database, "SELECT `Property`, `InstallMode`, `Condition` FROM `Wix4RemoveFolderEx`", 3).Single();
        Assert.AreEqual($"PITBOARDAPPDATA 2 {FullUninstall}", string.Join(' ', removed));
        var folder = Rows(database, "SELECT `Target` FROM `CustomAction` WHERE `Source` = 'PITBOARDAPPDATA'", 1).Single();
        Assert.AreEqual("[LocalAppDataFolder]Pitboard App", folder[0]);
    }

    [TestMethod]
    public void AnUpdateStartsPitboardAgainOnlyWhenItsInstallAsks()
    {
        using var database = Open();

        var launch = Rows(database, "SELECT `Type`, `Source`, `Target` FROM `CustomAction` WHERE `Action` = 'LaunchAfterInstall'", 3).Single();
        var type = int.Parse(launch[0]!, CultureInfo.InvariantCulture);
        Assert.AreEqual(ExeFromInstalledFile, type & 0x3F);
        Assert.AreEqual(AsyncNoWait, type & AsyncNoWait);
        Assert.AreEqual(0, type & Deferred);
        Assert.AreEqual("PitboardExe --updated", string.Join(' ', launch[1..]));
        Assert.AreEqual("LAUNCHAFTERINSTALL = \"1\" AND NOT REMOVE", Condition(database, "LaunchAfterInstall"));
        Assert.IsGreaterThan(Sequence(database, "InstallFinalize"), Sequence(database, "LaunchAfterInstall"));
    }

    // A RemoveRegistry row acts on an install, so the Run value goes through --unregister instead.
    [TestMethod]
    public void NoRowWritesTheMachinesRegistryOrRemovesTheRunValue()
    {
        using var database = Open();

        var roots = Rows(database, "SELECT `Root` FROM `Registry`", 1).Select(row => row[0]).Distinct().ToList();
        Assert.AreEqual("-1", string.Join(", ", roots), "HKMU alone, which a per-user install writes under HKCU");
        if (database.Tables.Contains("RemoveRegistry"))
        {
            var removed = Rows(database, "SELECT `Root`, `Key` FROM `RemoveRegistry`", 2);
            Assert.IsFalse(removed.Any(row => row[1]!.EndsWith(@"CurrentVersion\Run", StringComparison.OrdinalIgnoreCase)));
            Assert.IsFalse(removed.Any(row => row[0] == "2"));
        }
    }

    private static string? Condition(Database database, string action) =>
        Rows(database, $"SELECT `Condition` FROM `InstallExecuteSequence` WHERE `Action` = '{action}'", 1).Single()[0];

    private static int Sequence(Database database, string action) =>
        int.Parse(
            Rows(database, $"SELECT `Sequence` FROM `InstallExecuteSequence` WHERE `Action` = '{action}'", 1).Single()[0]!,
            CultureInfo.InvariantCulture);
}
