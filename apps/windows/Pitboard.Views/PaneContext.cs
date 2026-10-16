using Pitboard.Core;
using Pitboard.Windows;
using Windows.System;

namespace Pitboard.Views;

// What the main window navigates each of its pages with: a page made by its frame takes no arguments.
public sealed record PaneContext(AppModel Model, DialogHost Dialogs, StartAtSignIn StartAtSignIn, IUpdates Updates, string Version)
{
    public Action<WindowAccount>? OpenWindow { get; init; }

    public Action<Uri> Open { get; init; } = page => _ = Launcher.LaunchUriAsync(page);

    public TimeProvider Time { get; init; } = TimeProvider.System;
}
