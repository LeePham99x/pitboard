using Windows.Win32;

namespace Pitboard.Windows;

public abstract record IconEvent
{
    private IconEvent()
    {
    }

    // A left click, or Enter or Space on the icon.
    public sealed record Activated : IconEvent;

    // A right click, or the menu key or Shift+F10 on the icon, at the point on the screen it gives.
    public sealed record Menu(int X, int Y) : IconEvent;

    // Under NOTIFYICON_VERSION_4 the event is lParam's low word and its anchor wParam's two words, signed.
    public static IconEvent? Read(nuint wParam, nint lParam) =>
        (uint)(lParam & 0xFFFF) switch
        {
            PInvoke.NIN_SELECT or PInvoke.NIN_SELECT | PInvoke.NINF_KEY => new Activated(),
            PInvoke.WM_CONTEXTMENU => new Menu((short)(wParam & 0xFFFF), (short)((wParam >> 16) & 0xFFFF)),
            _ => null,
        };
}
