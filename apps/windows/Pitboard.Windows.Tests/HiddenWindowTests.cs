using System.Runtime.InteropServices;

namespace Pitboard.Windows.Tests;

[TestClass]
public sealed partial class HiddenWindowTests
{
    private const uint ParentOf = 1;
    private const uint Close = 0x0010;
    private const uint EndSession = 0x0016;

    private static HiddenWindow Made(
        Action? sessionEnding = null, Action? closing = null, Action<IconEvent>? icon = null, Action? taskbarCreated = null) =>
        new(sessionEnding ?? (() => { }), closing ?? (() => { }), icon ?? (_ => { }), taskbarCreated ?? (() => { }));

    // As Explorer tells every top-level window once it starts again.
    [TestMethod]
    public void TheIconIsAddedAgainWhenTheTaskbarIsCreated()
    {
        var area = new NotifyIconTests.StandInArea();
        NotifyIcon? icon = null;
        var hidden = Made(taskbarCreated: () => icon?.Readd());
        icon = new NotifyIcon(area, hidden.Handle, 0, null);
        icon.Show("Pitboard");

        SendMessage(hidden.Handle, RegisterWindowMessage("TaskbarCreated"), 0, 0);

        Assert.AreEqual("Add, SetVersion, Add, SetVersion", string.Join(", ", area.Sent.Select(sent => sent.Message)));
        Assert.AreEqual(hidden.Handle, area.Sent[2].Icon.Window);
    }

    // A message-only window's parent is HWND_MESSAGE's, and no broadcast reaches it.
    [TestMethod]
    public void TheWindowIsTopLevel()
    {
        var hidden = Made();

        Assert.AreEqual(GetDesktopWindow(), GetAncestor(hidden.Handle, ParentOf));
    }

    [TestMethod]
    public void TheIconsClicksReachTheApp()
    {
        var heard = new List<IconEvent>();
        var hidden = Made(icon: heard.Add);

        SendMessage(hidden.Handle, NotifyIcon.CallbackMessage, 40 | (1000 << 16), 0x007B | (1 << 16));
        SendMessage(hidden.Handle, NotifyIcon.CallbackMessage, 40 | (1000 << 16), 0x0400 | (1 << 16));
        SendMessage(hidden.Handle, NotifyIcon.CallbackMessage, 40 | (1000 << 16), 0x0200 | (1 << 16));

        Assert.AreEqual("Menu { X = 40, Y = 1000 }, Activated { }", string.Join(", ", heard));
    }

    // As taskkill without /f asks.
    [TestMethod]
    public void ClosingTheWindowEndsPitboard()
    {
        var closed = 0;
        var hidden = Made(closing: () => closed++);

        SendMessage(hidden.Handle, Close, 0, 0);

        Assert.AreEqual(1, closed);
    }

    [TestMethod]
    public void OnlyASessionThatEndsStopsTheModel()
    {
        var ended = 0;
        var hidden = Made(sessionEnding: () => ended++);

        SendMessage(hidden.Handle, EndSession, 0, 0);
        SendMessage(hidden.Handle, EndSession, 1, 0);

        Assert.AreEqual(1, ended);
    }

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    private static partial nint SendMessage(nint window, uint message, nuint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "RegisterWindowMessageW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint RegisterWindowMessage(string name);

    [LibraryImport("user32.dll")]
    private static partial nint GetAncestor(nint window, uint flags);

    [LibraryImport("user32.dll")]
    private static partial nint GetDesktopWindow();
}
