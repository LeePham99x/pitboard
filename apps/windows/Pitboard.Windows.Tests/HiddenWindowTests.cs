using System.Runtime.InteropServices;

namespace Pitboard.Windows.Tests;

[TestClass]
public sealed partial class HiddenWindowTests
{
    private const uint ParentOf = 1;
    private const uint Close = 0x0010;
    private const uint EndSession = 0x0016;
    private const uint SettingChange = 0x001A;
    private const uint DisplayChange = 0x007E;
    private const uint DpiChanged = 0x02E0;
    private const nint CloseApp = 0x1;

    private static HiddenWindow Made(
        Action? sessionEnding = null, Action? closing = null, Action<IconEvent>? icon = null, Action? taskbarCreated = null,
        Action? displayChanged = null) =>
        new(
            sessionEnding ?? (() => { }), closing ?? (() => { }), icon ?? (_ => { }), taskbarCreated ?? (() => { }),
            displayChanged ?? (() => { }));

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

    // Each can come with a new scale for the primary display, whose taskbar holds the icon, and the icon drawn for the
    // scale before blurs.
    [TestMethod]
    public void AChangeOfTheDisplaysReachesTheApp()
    {
        var changes = 0;
        var hidden = Made(displayChanged: () => changes++);

        SendMessage(hidden.Handle, DisplayChange, 32, 0);
        SendMessage(hidden.Handle, DpiChanged, 144 | (144 << 16), 0);
        SendMessage(hidden.Handle, SettingChange, 0, 0);

        Assert.AreEqual(3, changes);
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

    // As Restart Manager asks a program to end, to replace its files, and then waits for it to exit.
    [TestMethod]
    public void RestartManagersEndOfSessionEndsPitboard()
    {
        var (closed, ended) = (0, 0);
        var hidden = Made(sessionEnding: () => ended++, closing: () => closed++);

        SendMessage(hidden.Handle, EndSession, 1, CloseApp);

        Assert.AreEqual((1, 0), (closed, ended));
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
