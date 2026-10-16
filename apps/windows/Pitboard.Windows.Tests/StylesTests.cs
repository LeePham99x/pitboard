using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;

namespace Pitboard.Windows.Tests;

[TestClass]
public sealed class StylesTests
{
    private static readonly string[] Secondary = ["SecondaryTextBlockStyle", "SecondaryCaptionTextBlockStyle"];

    private static Style Found(string key)
    {
        Assert.IsTrue(Application.Current.Resources.TryGetValue(key, out var found), $"{key} is not among the app's resources");
        return (Style)found;
    }

    // A view made in code takes them from the app's resources, where a window it is not in yet finds them (run 38825074643).
    [UITestMethod]
    public void EachSharedStyleIsFoundBeforeAnyWindowShows()
    {
        var secondary = ((SolidColorBrush)Application.Current.Resources["TextFillColorSecondaryBrush"]).Color;
        foreach (var key in Secondary)
        {
            var text = new TextBlock { Text = "Detail", Style = Found(key) };

            Assert.AreEqual(secondary, ((SolidColorBrush)text.Foreground).Color, key);
            Assert.AreEqual(TextWrapping.Wrap, text.TextWrapping, key);
        }

        var title = new TextBlock { Text = "Accounts", Style = Found("PageTitleTextBlockStyle") };
        Assert.AreEqual(AutomationHeadingLevel.Level1, AutomationProperties.GetHeadingLevel(title));
    }
}
