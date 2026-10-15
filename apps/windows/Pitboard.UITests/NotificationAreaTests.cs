using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;

namespace Pitboard.UITests;

// CF3: the fresh standard user's icon reaches Explorer, and Explorer's clicks reach Pitboard.
[TestClass]
[Elevated(ConditionMode.Exclude)]
[ExplorerTakesIcons]
public sealed class NotificationAreaTests
{
    // Not the Release build's, whose GUID Explorer would then tie to this test's path.
    private static readonly Guid TestIcon = new("e05d7be5-9ae7-44dd-b273-01ba52314037");

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void ThisUserAddsAnIconByAGuidAndByAWindow()
    {
        var window = NotificationArea.OwnerWindow();
        Assert.AreNotEqual(0, window, "CF3: this user could not make a window to own an icon");
        try
        {
            var byWindow = NotificationArea.Data(window, null);
            var addedByWindow = NotificationArea.Send(NotificationArea.NimAdd, ref byWindow);
            TestContext.WriteLine($"CF3: Shell_NotifyIconW NIM_ADD by window and id: {addedByWindow}");
            if (addedByWindow)
            {
                byWindow.Version = 4;
                TestContext.WriteLine($"CF3: NIM_SETVERSION 4: {NotificationArea.Send(NotificationArea.NimSetVersion, ref byWindow)}");
                NotificationArea.Send(NotificationArea.NimDelete, ref byWindow);
            }

            var byGuid = NotificationArea.Data(window, TestIcon);
            var addedByGuid = NotificationArea.Send(NotificationArea.NimAdd, ref byGuid);
            TestContext.WriteLine($"CF3: Shell_NotifyIconW NIM_ADD by GUID: {addedByGuid}");
            if (addedByGuid)
            {
                NotificationArea.Send(NotificationArea.NimDelete, ref byGuid);
            }

            Assert.IsTrue(addedByWindow, "CF3: Explorer refused this user's icon named by its window and id");
            Assert.IsTrue(addedByGuid, "CF3: Explorer refused this user's icon named by a GUID");
        }
        finally
        {
            NotificationArea.Close(window);
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
}
