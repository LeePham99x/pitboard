using System.Runtime.InteropServices;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;

namespace Pitboard.UITests;

// CF3: the fresh standard user's icon reaches the job user's Explorer, and Explorer's clicks reach Pitboard.
[TestClass]
[Elevated(ConditionMode.Exclude)]
public sealed partial class NotificationAreaTests
{
    private const uint NimAdd = 0;
    private const uint NimDelete = 2;
    private const uint NimSetVersion = 4;
    private const uint NifTip = 0x04;
    private const uint NifGuid = 0x20;
    private const uint NifShowTip = 0x80;

    // Not the Release build's, whose GUID Explorer would then tie to this test's path.
    private static readonly Guid TestIcon = new("e05d7be5-9ae7-44dd-b273-01ba52314037");

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void ThisUserAddsAnIconByAGuidAndByAWindow()
    {
        var window = CreateWindowEx(0, "STATIC", "CF3 probe", 0, 0, 0, 0, 0, 0, 0, 0, 0);
        Assert.AreNotEqual(0, window, "CF3: this user could not make a window to own an icon");
        try
        {
            var byWindow = Data(window, null);
            var addedByWindow = ShellNotifyIcon(NimAdd, ref byWindow);
            TestContext.WriteLine($"CF3: Shell_NotifyIconW NIM_ADD by window and id: {addedByWindow}");
            if (addedByWindow)
            {
                byWindow.Version = 4;
                TestContext.WriteLine($"CF3: NIM_SETVERSION 4: {ShellNotifyIcon(NimSetVersion, ref byWindow)}");
                ShellNotifyIcon(NimDelete, ref byWindow);
            }

            var byGuid = Data(window, TestIcon);
            var addedByGuid = ShellNotifyIcon(NimAdd, ref byGuid);
            TestContext.WriteLine($"CF3: Shell_NotifyIconW NIM_ADD by GUID: {addedByGuid}");
            if (addedByGuid)
            {
                ShellNotifyIcon(NimDelete, ref byGuid);
            }

            Assert.IsTrue(addedByWindow, "CF3: Explorer refused this user's icon named by its window and id");
            Assert.IsTrue(addedByGuid, "CF3: Explorer refused this user's icon named by a GUID");
        }
        finally
        {
            DestroyWindow(window);
        }
    }

    [TestMethod]
    public void PitboardsIconOpensTheWindowAndTheMenu()
    {
        using var launched = Launched.Start(Builds.Debug, fixture: "firstLaunch");
        var window = launched.Window("Pitboard");
        window.Close();
        Assert.IsTrue(Retry.WhileTrue(() => window.IsAvailable, TimeSpan.FromSeconds(10)).Result, "the window stayed open");
        Assert.IsNotNull(NotificationArea.Taskbar(launched.Automation), "CF3: this user's UI Automation finds no taskbar");

        var icon = NotificationArea.Icon(launched.Automation);
        Assert.IsNotNull(icon, "CF3: Pitboard's icon is not in Explorer's UI Automation tree, on the taskbar or in its overflow");
        TestContext.WriteLine($"CF3: the icon is '{icon.Name}' in {NotificationArea.Where(icon)}");

        icon.Click();
        Assert.IsTrue(
            Retry.WhileFalse(() => Opened(launched), TimeSpan.FromSeconds(10)).Result,
            "CF3: a click on the icon did not open the window: NIN_SELECT did not reach the hidden window");

        launched.Icon().RightClick();
        launched.Find(found => found.ByName("Open Pitboard"), "menu after a right click on the icon: WM_CONTEXTMENU did not reach the hidden window");
        Keyboard.Press(VirtualKeyShort.ESCAPE);
        Assert.IsTrue(launched.Gone(found => found.ByName("Open Pitboard")), "Esc left the menu open");
    }

    private static bool Opened(Launched launched) =>
        FlaUI.Core.Application.Attach(launched.Process).GetAllTopLevelWindows(launched.Automation)
            .Any(shown => shown.Title == "Pitboard");

    private static unsafe NotifyIconData Data(nint window, Guid? item)
    {
        var data = new NotifyIconData
        {
            Size = (uint)sizeof(NotifyIconData),
            Window = window,
            Id = 1,
            Flags = NifTip | NifShowTip | (item is null ? 0 : NifGuid),
            Item = item ?? Guid.Empty,
        };
        "CF3 probe".AsSpan().CopyTo(new Span<char>(data.Tip, 128));
        return data;
    }

    // NOTIFYICONDATAW as 64-bit Windows lays it out.
    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct NotifyIconData
    {
        public uint Size;
        public nint Window;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public nint Icon;
        public fixed char Tip[128];
        public uint State;
        public uint StateMask;
        public fixed char Info[256];
        public uint Version;
        public fixed char InfoTitle[64];
        public uint InfoFlags;
        public Guid Item;
        public nint BalloonIcon;
    }

    [LibraryImport("shell32.dll", EntryPoint = "Shell_NotifyIconW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShellNotifyIcon(uint message, ref NotifyIconData data);

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateWindowEx(
        uint extendedStyle, string className, string name, uint style, int x, int y, int width, int height,
        nint parent, nint menu, nint instance, nint parameter);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint window);
}
