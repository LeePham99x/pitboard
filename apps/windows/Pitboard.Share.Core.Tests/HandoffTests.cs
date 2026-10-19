using System.ComponentModel;

namespace Pitboard.Share.Core.Tests;

[TestClass]
public sealed class HandoffTests
{
    private const string Link = "pitboard://open?url=https%3A%2F%2Fchatgpt.com%2Fc%2Fx%3Fy%3D1%23z";

    [TestMethod]
    public void PitboardExeBesideItGetsTheLinkAsItsOnlyArgument()
    {
        var folder = Path.Combine(Path.GetTempPath(), "pitboard-install");

        var start = Handoff.StartInfo(folder, Link);

        Assert.AreEqual(Path.Combine(folder, "Pitboard.exe"), start.FileName);
        CollectionAssert.AreEqual(new[] { Link }, start.ArgumentList.ToArray());
        Assert.AreEqual("", start.Arguments);
        Assert.IsFalse(start.UseShellExecute);
    }

    [TestMethod]
    public void AFolderWithoutPitboardExeStartsNothing()
    {
        var folder = Directory.CreateTempSubdirectory("pitboard-share-tests-");
        try
        {
            Assert.ThrowsExactly<Win32Exception>(() => Handoff.Start(folder.FullName, Link));
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }
}
