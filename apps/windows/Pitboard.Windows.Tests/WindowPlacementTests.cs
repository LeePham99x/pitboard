using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using WinRT.Interop;

namespace Pitboard.Windows.Tests;

[TestClass]
public sealed class WindowPlacementTests
{
    private static string File() => Path.Combine(Path.GetTempPath(), $"pitboard-placement-{Guid.NewGuid()}.json");

    [TestMethod]
    public void APlacementIsReadAsItWasSaved()
    {
        var file = File();
        try
        {
            var placement = new WindowPlacement(file);
            var saved = new Placement(Left: -1600, Top: 40, Right: -200, Bottom: 980, Maximized: true);

            placement.Save(saved);

            Assert.AreEqual(saved, new WindowPlacement(file).Saved());
        }
        finally
        {
            System.IO.File.Delete(file);
        }
    }

    // The window then opens at its default size, in the middle of the screen.
    [TestMethod]
    public void AMissingOrUnreadableFileGivesNoPlacement()
    {
        var file = File();
        try
        {
            Assert.IsNull(new WindowPlacement(file).Saved(), "a missing file");
            foreach (var written in new[] { "{\"Left\":", "[]", "{}", "{\"Left\":10,\"Top\":10,\"Right\":10,\"Bottom\":10}" })
            {
                System.IO.File.WriteAllText(file, written);
                Assert.IsNull(new WindowPlacement(file).Saved(), written);
            }
        }
        finally
        {
            System.IO.File.Delete(file);
        }
    }

    // Closed while minimized, a window opens again as it would have come back from the taskbar.
    [UITestMethod]
    public async Task AMinimizedWindowIsSavedByTheStateItRestoresTo()
    {
        var maximized = await Views.Activated(new Window { Content = new Grid() });
        var restored = await Views.Activated(new Window { Content = new Grid() });
        try
        {
            var presenter = (OverlappedPresenter)maximized.AppWindow.Presenter;
            presenter.Maximize();
            presenter.Minimize();
            ((OverlappedPresenter)restored.AppWindow.Presenter).Minimize();

            Assert.IsTrue(WindowPlacement.Of(WindowNative.GetWindowHandle(maximized))?.Maximized);
            var normal = WindowPlacement.Of(WindowNative.GetWindowHandle(restored));
            Assert.IsFalse(normal?.Maximized);
            Assert.IsTrue(normal!.Right > normal.Left && normal.Bottom > normal.Top, $"{normal}");
        }
        finally
        {
            maximized.Close();
            restored.Close();
        }
    }
}
