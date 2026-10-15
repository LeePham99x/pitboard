namespace Pitboard.Windows;

// The navigations of a page and its frames under way, by WebView2's id, for telling whose a download is: WebView2 names
// no frame in DownloadStarting. A download the page's own navigation cannot account for counts as a frame's, so it is
// asked about more often than on macOS, never less.
public sealed class PageNavigations
{
    private readonly Dictionary<ulong, string> _page = [];
    private readonly Dictionary<ulong, string> _frames = [];

    // A redirect starts again under the same id.
    public void Starting(ulong id, string address, bool frame, bool stopped)
    {
        var under = frame ? _frames : _page;
        if (stopped)
        {
            under.Remove(id);
        }
        else
        {
            under[id] = address;
        }
    }

    public void Completed(ulong id, bool frame) => (frame ? _frames : _page).Remove(id);

    public bool MainFrame(string address) =>
        _page.ContainsValue(address) && !_frames.ContainsValue(address);
}
