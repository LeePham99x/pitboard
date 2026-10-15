using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Pitboard.Core;

namespace Pitboard.Views;

// One ContentDialog shows per thread at a time, so each of the window's dialogs waits for the one before it.
public sealed class DialogHost(Func<XamlRoot?> root)
{
    private Task _turn = Task.CompletedTask;

    public ContentDialog? Open { get; private set; }

    // Null when the dialog never showed: withdrawn before its turn, or no window to show it in.
    public async Task<ContentDialogResult?> Show(ContentDialog dialog, CancellationToken withdrawn = default)
    {
        var before = _turn;
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _turn = done.Task;
        try
        {
            await before;
            if (withdrawn.IsCancellationRequested || root() is not { } shownIn)
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
