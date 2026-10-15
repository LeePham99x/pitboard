using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;
using Pitboard.Core;

namespace Pitboard.Windows;

// Every window's downloads, as the macOS app's DownloadCenter keeps them: each saved under the name
// download_destination gives, never over a file or another download's name, asked about first where the policy says,
// and told to the model from its start to its end. WebView2's own download dialog lists them.
public sealed class Downloads(string folder, Action<Intent> send)
{
    private readonly List<Running> _live = [];

    // Counted here rather than from a snapshot: a download started a moment before Quit is under way before the
    // snapshot that lists it.
    public Question? QuitQuestion => PitboardFfiMethods.DownloadsQuitQuestion((uint)_live.Count);

    // The stores of the windows whose downloads are under way, the newest first.
    public IEnumerable<string> Stores => _live.Select(running => running.Store).Reverse();

    // Holds WebView2 until it is answered, so nothing is saved before the person says yes.
    public async Task Start(
        string store, CoreWebView2DownloadStartingEventArgs args, bool ask, Func<AlertText, Task<bool>> allow)
    {
        var deferral = args.GetDeferral();
        try
        {
            var operation = args.DownloadOperation;
            var name = Path.GetFileName(args.ResultFilePath);
            var running = new Running(Guid.NewGuid().ToString(), store, operation);
            _live.Add(running);
            send(new Intent.DownloadStarted(running.Id, store, name));
            if (ask && !await allow(PitboardFfiMethods.DownloadQuestion(name, PitboardFfiMethods.DownloadHost(operation.Uri))))
            {
                args.Cancel = true;
                End(running, new DownloadEnd.Cancelled());
                return;
            }

            running.File = PitboardFfiMethods.DownloadDestination(
                folder, name, [.. _live.Select(other => other.File).OfType<string>()]);
            args.ResultFilePath = running.File;
            operation.StateChanged += (sender, _) => Changed(running, sender);
            send(new Intent.DownloadSaving(running.Id, running.File));
        }
        finally
        {
            deferral.Complete();
        }
    }

    // Quitting stops every download, and none resumes.
    public void StopAll()
    {
        foreach (var running in _live.ToArray())
        {
            try
            {
                running.Operation.Cancel();
            }
            catch (COMException)
            {
                // Its browser process has gone, and the download with it.
            }

            End(running, new DownloadEnd.Cancelled());
        }
    }

    // A paused download is interrupted and can resume, and is still under way.
    private void Changed(Running running, CoreWebView2DownloadOperation operation)
    {
        switch (operation.State)
        {
            case CoreWebView2DownloadState.Completed:
                End(running, new DownloadEnd.Finished());
                break;
            case CoreWebView2DownloadState.Interrupted when !operation.CanResume:
                End(running, operation.InterruptReason == CoreWebView2DownloadInterruptReason.UserCanceled
                    ? new DownloadEnd.Cancelled()
                    : new DownloadEnd.Failed(operation.InterruptReason.ToString()));
                break;
        }
    }

    private void End(Running running, DownloadEnd end)
    {
        if (_live.Remove(running))
        {
            send(new Intent.DownloadEnded(running.Id, end));
        }
    }

    private sealed class Running(string id, string store, CoreWebView2DownloadOperation operation)
    {
        public string Id => id;

        public string Store => store;

        public CoreWebView2DownloadOperation Operation => operation;

        public string? File { get; set; }
    }
}
