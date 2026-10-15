namespace Pitboard.Core.Tests;

[TestClass]
public sealed class BindingsTests
{
    private static readonly string[] Codes = ["claude", "codex"];

    // The first call checks every checksum against the library, so one that answers proves they agree.
    [TestMethod]
    public void TheBindingsLoadTheCoreAndAgreeWithIt()
    {
        var sites = PitboardFfiMethods.Sites();

        CollectionAssert.AreEqual(Codes, sites.Select(site => site.Provider).ToArray());
    }

    [TestMethod]
    public void TheBindingsOfferTheModelAndNothingOlder()
    {
        var exported = typeof(PitboardModel).Assembly.GetExportedTypes().Where(type => !type.IsNested);
        // uniffi-bindgen-cs writes an exported object as a class with an interface named I and its name.
        var objects = exported
            .Where(type => type.IsClass && type.GetInterface("I" + type.Name) is not null)
            .Select(type => type.Name)
            .Order(StringComparer.Ordinal);
        string[] older =
        [
            "Pitboard", "SignIn", "Settings", "PitboardException", "Cause", "Switched", "Switch",
            "Adoption", "Enrolled", "EnrolledAs", "Changed", "Holding", "Remedy", "Diagnosis", "Change",
            "SignInView", "Check", "Renewed",
        ];
        var stillOffered = exported.Select(type => type.Name).Intersect(older).Order(StringComparer.Ordinal);
        string[] olderCalls =
        [
            "WindowsOf", "ForgetMessage", "Tools", "CommandLinePlaces", "SignInView", "FindCommandLine",
            "HomeDirectory", "LimitColumn", "LimitName", "UsageLevel", "Resets", "Runway", "ParkedLife",
            "RenewalNote", "DoctorSummary", "SameReset",
        ];
        var stillCalled = typeof(PitboardFfiMethods).GetMethods()
            .Select(method => method.Name)
            .Intersect(olderCalls)
            .Order(StringComparer.Ordinal);

        Assert.AreEqual(nameof(PitboardModel), string.Join(", ", objects));
        Assert.AreEqual("", string.Join(", ", stillOffered));
        Assert.AreEqual("", string.Join(", ", stillCalled));
    }

    [TestMethod]
    [OSCondition(ConditionMode.Exclude, OperatingSystems.Windows, IgnoreMessage = "W17: finding programs on Windows")]
    public void TheAppAsksTheCoreWhereThingsAreAndWhatRuns()
    {
        var environment = new Dictionary<string, string> { ["HOME"] = "/home/x" };

        Assert.AreEqual("/home/x/.pitboard", PitboardFfiMethods.PitboardDirectory(environment));
        Assert.IsNull(PitboardFfiMethods.AppCommandLine("/home/x/pitboard"));
        Assert.IsFalse(PitboardFfiMethods.CanRun("/nowhere/at/all"));
    }

    [TestMethod]
    public void TheWindowsAppKeysItsRecordsByItsDirectoryInAnyCase()
    {
        var key = PitboardFfiMethods.WindowsRecordsKey(@"c:\users\x\appdata\local\pitboard\");

        Assert.AreEqual(@"C:\USERS\X\APPDATA\LOCAL\PITBOARD", key);
        Assert.AreEqual(key, PitboardFfiMethods.WindowsRecordsKey(@"\\?\C:\Users\X\AppData\Local\Pitboard"));
    }
}
