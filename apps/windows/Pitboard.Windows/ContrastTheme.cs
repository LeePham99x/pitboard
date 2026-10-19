using Microsoft.UI;
using Microsoft.UI.System;

namespace Pitboard.Windows;

// Whether Windows shows a contrast theme, as a window's caption follows it.
public interface IContrastTheme
{
    // Raised on any thread.
    event EventHandler? Changed;

    bool Shown { get; }
}

// The Windows App SDK's ThemeSettings, which tells a Win32 app of a contrast theme.
public sealed class SystemContrast : IContrastTheme
{
    private readonly ThemeSettings _settings;

    public SystemContrast(WindowId window)
    {
        _settings = ThemeSettings.CreateForWindowId(window);
        _settings.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? Changed;

    public bool Shown => _settings.HighContrast;
}
