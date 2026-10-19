namespace Pitboard.Windows.Tests;

[TestClass]
public sealed class IconImageTests
{
    // Microsoft's high DPI guidance loads an image again for a new DPI, and Explorer stretches one of another size.
    [TestMethod]
    public void TheImageIsLoadedAgainOnlyAtANewSize()
    {
        var size = 16;
        var done = new List<string>();
        var image = new IconImage(
            () => size,
            wanted =>
            {
                done.Add($"load {wanted}");
                return wanted;
            },
            before => done.Add($"destroy {before}"));

        image.Follow(shown => done.Add($"show {shown}"));
        size = 24;
        image.Follow(shown => done.Add($"show {shown}"));

        Assert.AreEqual("load 16, load 24, show 24, destroy 16", string.Join(", ", done));
        Assert.AreEqual((nint)24, image.Handle);
    }
}
