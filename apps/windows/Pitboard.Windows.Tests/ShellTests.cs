namespace Pitboard.Windows.Tests;

[TestClass]
public sealed class ShellTests
{
    // The tests point LOCALAPPDATA at a scratch folder, which must not move the app's.
    [TestMethod]
    public void TheAppsFolderIsTheKnownFolderWhateverLocalAppDataSays()
    {
        var was = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        var scratch = Directory.CreateTempSubdirectory("pitboard-local-").FullName;
        try
        {
            Environment.SetEnvironmentVariable("LOCALAPPDATA", scratch);

            var known = Shell.LocalAppData();

            Assert.AreNotEqual(scratch, known);
            Assert.IsTrue(Path.IsPathFullyQualified(known), known);
        }
        finally
        {
            Environment.SetEnvironmentVariable("LOCALAPPDATA", was);
            Directory.Delete(scratch);
        }
    }
}
