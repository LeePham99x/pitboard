using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Pitboard.Core;

namespace Pitboard.Views;

// One ContentDialog shows per thread at a time, so each dialog of any of the thread's windows waits for the one before
// it.
public sealed class DialogHost(Func<XamlRoot?> root)
{
    [ThreadStatic]
    private static Task? _turn;

    private bool _withdrawn;

    public ContentDialog? Open { get; private set; }

    // As its window closes: the dialog it shows goes, and none waiting shows.
    public void Withdraw()
    {
        _withdrawn = true;
        Open?.Hide();
    }

    // Null when the dialog never showed: withdrawn before its turn, or no window to show it in.
    public async Task<ContentDialogResult?> Show(ContentDialog dialog, CancellationToken withdrawn = default)
    {
        var before = _turn ?? Task.CompletedTask;
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _turn = done.Task;
        try
        {
            await before;
            if (_withdrawn || withdrawn.IsCancellationRequested || root() is not { } shownIn)
            {
                return null;
            }

            dialog.XamlRoot = shownIn;
            // As the WinUI Gallery styles a ContentDialog it makes in code.
            if (dialog.Style is null && Application.Current.Resources.TryGetValue("DefaultContentDialogStyle", out var style))
            {
                dialog.Style = style as Style;
            }

            Open = dialog;
            using (withdrawn.Register(dialog.Hide))
            {
                return await dialog.ShowAsync();
            }
        }
        finally
        {
            Open = null;
            done.SetResult();
        }
    }

    public async Task<bool> Confirm(Question question) =>
        await Show(new ContentDialog
        {
            Title = question.Title,
            Content = question.Message,
            PrimaryButtonText = question.Confirm,
            CloseButtonText = "Cancel",
        }) == ContentDialogResult.Primary;
}
