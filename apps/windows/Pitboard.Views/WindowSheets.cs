using System.ComponentModel;
using Pitboard.Windows;
using Windows.System;

namespace Pitboard.Views;

// The model keeps the sheet; this one goes however it goes, and the model is told.
public sealed partial class WindowSheets : IDisposable
{
    private readonly AppModel _model;
    private readonly DialogHost _dialogs;
    private readonly Action<Uri> _open;
    private string? _dismissed;

    public WindowSheets(AppModel model, DialogHost dialogs, Action<Uri>? open = null)
    {
        _model = model;
        _dialogs = dialogs;
        _open = open ?? (page => _ = Launcher.LaunchUriAsync(page));
        model.PropertyChanged += OnModelChanged;
        Follow();
    }

    public AccountSheet? Shown { get; private set; }

    public void Dispose()
    {
        _model.PropertyChanged -= OnModelChanged;
        Shown?.Withdraw();
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(AppModel.Sheet))
        {
            Follow();
        }
    }

    // Closed by the person, a sheet stays down until the model has heard and moved on.
    private void Follow()
    {
        var key = _model.Sheet is { } sheet ? SheetPage.Key(sheet) : null;
        if (key != _dismissed)
        {
            _dismissed = null;
        }

        if (key == Shown?.Key || (key is not null && key == _dismissed))
        {
            return;
        }

        Shown?.Withdraw();
        Shown = key is null ? null : new AccountSheet(_model, key, _open);
        if (Shown is { } shown)
        {
            Present(shown);
        }
    }

    private async void Present(AccountSheet sheet)
    {
        var answer = await _dialogs.Show(sheet, sheet.Withdrawn);
        var closed = answer is not null && !sheet.Withdrawn.IsCancellationRequested;
        if (Shown == sheet)
        {
            Shown = null;
            _dismissed = closed ? sheet.Key : null;
        }

        var told = closed ? sheet.Dismissed() : [];
        sheet.Dispose();
        foreach (var intent in told)
        {
            _model.Send(intent);
        }
    }
}
