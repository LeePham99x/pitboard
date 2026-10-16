using Microsoft.Web.WebView2.Core;

namespace Pitboard.Windows.Tests;

// WebView2 names no frame that started a download. On both runners a navigation that became one completed first,
// unsuccessful and ConnectionAborted, and DownloadStarting followed (run 38825074643).
[TestClass]
public sealed class PageNavigationsTests
{
    private const string File = "https://chatgpt.com/files/report.pdf";

    private const CoreWebView2WebErrorStatus Aborted = CoreWebView2WebErrorStatus.ConnectionAborted;

    [TestMethod]
    public void APagesNavigationThatBecameADownloadMakesItThePages()
    {
        var navigations = new PageNavigations();
        Assert.IsFalse(navigations.MainFrame(File), "with no navigation");

        navigations.Starting(1, File, frame: false, stopped: false);
        Assert.IsTrue(navigations.MainFrame(File), "under way");
        Assert.IsFalse(navigations.MainFrame("https://chatgpt.com/files/other.pdf"));

        navigations.Completed(1, frame: false, succeeded: false, Aborted);
        Assert.IsTrue(navigations.MainFrame(File), "completed as one that became a download does");

        navigations.Starting(2, "https://chatgpt.com/", frame: false, stopped: false);
        Assert.IsFalse(navigations.MainFrame(File), "once the page navigated again");
    }

    [TestMethod]
    public void APageThatLoadedOrFailedOtherwiseStartedNoDownload()
    {
        var navigations = new PageNavigations();

        navigations.Starting(1, File, frame: false, stopped: false);
        navigations.Completed(1, frame: false, succeeded: true, CoreWebView2WebErrorStatus.Unknown);
        Assert.IsFalse(navigations.MainFrame(File), "loaded");

        navigations.Starting(2, File, frame: false, stopped: false);
        navigations.Completed(2, frame: false, succeeded: false, CoreWebView2WebErrorStatus.HostNameNotResolved);
        Assert.IsFalse(navigations.MainFrame(File), "failed for another reason");
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
        navigations.Starting(2, File, frame: true, stopped: false);
        Assert.IsFalse(navigations.MainFrame(File), "both under way");

        navigations.Completed(1, frame: false, succeeded: false, Aborted);
        navigations.Completed(2, frame: true, succeeded: false, Aborted);
        Assert.IsFalse(navigations.MainFrame(File), "both became downloads");

        navigations.Starting(3, "https://example.com/frame", frame: true, stopped: false);
        Assert.IsFalse(navigations.MainFrame(File), "another frame navigated");
    }
}
