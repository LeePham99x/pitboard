using System.ComponentModel;
using Pitboard.Core;

namespace Pitboard.Windows;

public sealed partial class ActivityViewModel(AppModel model) : INotifyPropertyChanged
{
    private ActivityShown? _shown;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ActivityLine[] Lines => model.Machine.Activity.Lines;

    public bool Listed => model.Machine.Activity.Empty is null;

    public bool ShowsEmpty => !Listed;

    public string EmptyTitle => model.Machine.Activity.Empty?.Title ?? "";

    public string EmptyDetail => model.Machine.Activity.Empty?.Detail ?? "";

    public void Listen()
    {
        model.PropertyChanged -= OnModelChanged;
        model.PropertyChanged += OnModelChanged;
        _shown = null;
        Show();
    }

    public void Stop() => model.PropertyChanged -= OnModelChanged;

    // Every visit, and Refresh: the log is read each time the page is shown.
    public void Read() => model.Send(new Intent.PaneShown(Pane.Activity));

    private void OnModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(AppModel.Machine))
        {
            Show();
        }
    }

    // The machine's other parts change too, and a list told again goes back to its top.
    private void Show()
    {
        var activity = model.Machine.Activity;
        if (Structural.Equal(activity, _shown))
        {
            return;
        }

        _shown = activity;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }
}
