namespace Pitboard.Windows.Tests;

// WebView2 names no frame that started a download, so it is the page's own only while the page's navigation to its
// address is under way.
[TestClass]
public sealed class PageNavigationsTests
{
    private const string File = "https://chatgpt.com/files/report.pdf";

    [TestMethod]
    public void ADownloadIsThePagesOnlyWhileItsNavigationThereIsUnderWay()
    {
        var navigations = new PageNavigations();
        Assert.IsFalse(navigations.MainFrame(File), "with no navigation");

        navigations.Starting(1, File, frame: false, stopped: false);
        Assert.IsTrue(navigations.MainFrame(File));
        Assert.IsFalse(navigations.MainFrame("https://chatgpt.com/files/other.pdf"));

        navigations.Completed(1, frame: false);
        Assert.IsFalse(navigations.MainFrame(File), "once it completed");
    }

    [TestMethod]
    public void ARedirectMovesTheNavigationAndOneStoppedIsNotUnderWay()
    {
        var navigations = new PageNavigations();

        navigations.Starting(1, "https://chatgpt.com/r", frame: false, stopped: false);
        navigations.Starting(1, File, frame: false, stopped: false);
        Assert.IsTrue(navigations.MainFrame(File));
        Assert.IsFalse(navigations.MainFrame("https://chatgpt.com/r"));

        navigations.Starting(1, "https://example.com/x.pdf", frame: false, stopped: true);
        Assert.IsFalse(navigations.MainFrame(File), "redirected away and stopped");
        Assert.IsFalse(navigations.MainFrame("https://example.com/x.pdf"));
    }

    // A frame's navigation to the same address leaves no telling them apart, so the download is the frame's.
    [TestMethod]
    public void AFramesNavigationToTheSameAddressMakesTheDownloadAFrames()
    {
        var navigations = new PageNavigations();
        navigations.Starting(1, File, frame: false, stopped: false);
        navigations.Starting(1, File, frame: true, stopped: false);

        Assert.IsFalse(navigations.MainFrame(File));

        navigations.Completed(1, frame: true);
        Assert.IsTrue(navigations.MainFrame(File));
    }
}
