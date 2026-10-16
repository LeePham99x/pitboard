using Pitboard.Core;

namespace Pitboard.Windows;

// Which stores go, and when, is the model's, in AccountWindowsShown.Deleting. WebView2 deletes a profile's folder only
// as its browser process exits, so a store is told deleted once its folder has gone, and held until then: the model
// asks again after its next read. Where the folder is, is read from WebView2's own ProfilePath before an absent one
// counts, as the Runtime updates itself.
public sealed class StoreJanitor(Func<string, bool> exists, Func<string, Task> markDeleted, Func<bool> layoutKnown)
{
    // Each ask once, however many snapshots list it before the model has heard how it went.
    private readonly HashSet<StoreDeletion> _taken = [];
    private readonly HashSet<string> _marked = new(StringComparer.OrdinalIgnoreCase);

    public void Delete(IReadOnlyList<StoreDeletion> asked, Action<string, bool> told)
    {
        _taken.IntersectWith(asked);
        foreach (var ask in asked)
        {
            if (_taken.Add(ask))
            {
                _ = Answer(ask.Store, told);
            }
        }
    }

    private async Task Answer(string store, Action<string, bool> told)
    {
        bool There() => !layoutKnown() || exists(store);
        if (There() && _marked.Add(store))
        {
            try
            {
                await markDeleted(store);
            }
            // Told as held all the same, or the model would wait on this ask for good.
            catch (Exception)
            {
                _marked.Remove(store);
            }
        }

        told(store, !There());
    }
}
