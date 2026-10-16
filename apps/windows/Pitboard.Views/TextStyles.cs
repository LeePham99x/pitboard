using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Markup;

namespace Pitboard.Views;

// Text a view makes in code, in the theme's secondary brush as SettingsPane's Explanatory is. A ThemeResource follows
// the theme; Application.Resources' indexer found no theme brush before Pitboard's first window showed (run 38825074643).
internal static class TextStyles
{
    [ThreadStatic]
    private static Style? _secondary;

    [ThreadStatic]
    private static Style? _secondaryCaption;

    public static Style Secondary => _secondary ??= Load(basedOn: null);

    public static Style SecondaryCaption => _secondaryCaption ??= Load("CaptionTextBlockStyle");

    // Styles belong to the thread that loads them.
    private static Style Load(string? basedOn) =>
        (Style)XamlReader.Load(
            "<Style xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" TargetType=\"TextBlock\""
            + (basedOn is null ? "" : $" BasedOn=\"{{StaticResource {basedOn}}}\"")
            + "><Setter Property=\"Foreground\" Value=\"{ThemeResource TextFillColorSecondaryBrush}\" />"
            + "<Setter Property=\"TextWrapping\" Value=\"Wrap\" /></Style>");
}
