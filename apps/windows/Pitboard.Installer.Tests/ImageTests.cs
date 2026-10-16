namespace Pitboard.Installer.Tests;

// The administrative image msiexec /a extracted, laid out as an install lays out %LOCALAPPDATA%\Programs\Pitboard.
[TestClass]
public sealed class ImageTests
{
    [TestMethod]
    public void TheImageHoldsTheAppAndItsCommandLineInBin()
    {
        foreach (var file in new[] { "Pitboard.exe", "Pitboard.dll", "pitboard_ffi.dll", "WinSparkle.dll", "Pitboard.ico" })
        {
            Assert.IsTrue(File.Exists(Path.Combine(Built.Image, file)), file);
        }

        var bin = Path.Combine(Built.Image, "bin");
        Assert.AreEqual("pitboard.exe", string.Join(", ", Directory.GetFiles(bin).Select(Path.GetFileName)));
        CollectionAssert.AreNotEqual(
            File.ReadAllBytes(Path.Combine(Built.Image, "Pitboard.exe")), File.ReadAllBytes(Path.Combine(bin, "pitboard.exe")));
        Assert.IsEmpty(Directory.GetFiles(Built.Image, "*.pdb", SearchOption.AllDirectories));
    }

    [TestMethod]
    public void AnUnsignedImageHasNoShareTarget()
    {
        Assert.IsEmpty(Directory.GetFiles(Built.Image, "PitboardShare.*"));
        Assert.IsFalse(File.Exists(Path.Combine(Built.Image, "pitboard_share_ffi.dll")));
    }
}
