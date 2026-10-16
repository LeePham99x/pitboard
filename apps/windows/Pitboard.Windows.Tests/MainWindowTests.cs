using Microsoft.UI.Windowing;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using Pitboard.Core;
using Pitboard.Views;

namespace Pitboard.Windows.Tests;

[TestClass]
public sealed class MainWindowTests
{
    private static MainWindow Made(Snapshot snapshot) =>
        new(
            new AppModel(new StandInModel(snapshot)), new StartAtSignIn(new FixtureStartupItem()), new NoUpdates(), "0.0.0",
            _ => { }, new WindowPlacement(Path.Combine(Path.GetTempPath(), $"pitboard-placement-{Guid.NewGuid()}.json")));

    // As the WinUI Gallery's main window keeps.
    [UITestMethod]
    public async Task TheWindowIsNeverSmallerThan640By500()
    {
        var window = await Views.Activated(Made(Snapshots.Read(1)));
        try
        {
            var scale = window.Content.XamlRoot.RasterizationScale;
            var presenter = (OverlappedPresenter)window.AppWindow.Presenter;

            Assert.AreEqual((int)Math.Ceiling(640 * scale), presenter.PreferredMinimumWidth);
            Assert.AreEqual((int)Math.Ceiling(500 * scale), presenter.PreferredMinimumHeight);
        }
        finally
        {
            window.Close();
        }
    }
}
