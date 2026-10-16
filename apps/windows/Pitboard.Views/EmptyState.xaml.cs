using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Pitboard.Views;

// What a page shows in place of a list with nothing in it: a mark or a ring, a title, why, and one thing to do.
public sealed partial class EmptyState : UserControl
{
    public static readonly DependencyProperty GlyphProperty =
        DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(EmptyState), new PropertyMetadata(""));

    public static readonly DependencyProperty BusyProperty =
        DependencyProperty.Register(nameof(Busy), typeof(bool), typeof(EmptyState), new PropertyMetadata(false));

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(EmptyState), new PropertyMetadata(""));

    public static readonly DependencyProperty DetailProperty =
        DependencyProperty.Register(nameof(Detail), typeof(string), typeof(EmptyState), new PropertyMetadata(""));

    public static readonly DependencyProperty ChoiceProperty =
        DependencyProperty.Register(nameof(Choice), typeof(string), typeof(EmptyState), new PropertyMetadata(""));

    public static readonly DependencyProperty IsChoiceEnabledProperty =
        DependencyProperty.Register(nameof(IsChoiceEnabled), typeof(bool), typeof(EmptyState), new PropertyMetadata(true));

    public static readonly DependencyProperty ProminentProperty = DependencyProperty.Register(
        nameof(Prominent), typeof(bool), typeof(EmptyState),
        new PropertyMetadata(false, (shown, _) => ((EmptyState)shown).ShowProminence()));

    public EmptyState() => InitializeComponent();

    public event RoutedEventHandler? Chosen;

    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public bool Busy
    {
        get => (bool)GetValue(BusyProperty);
        set => SetValue(BusyProperty, value);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Detail
    {
        get => (string)GetValue(DetailProperty);
        set => SetValue(DetailProperty, value);
    }

    public string Choice
    {
        get => (string)GetValue(ChoiceProperty);
        set => SetValue(ChoiceProperty, value);
    }

    public bool IsChoiceEnabled
    {
        get => (bool)GetValue(IsChoiceEnabledProperty);
        set => SetValue(IsChoiceEnabledProperty, value);
    }

    // The page's one thing to do is its accent button; a retry is not.
    public bool Prominent
    {
        get => (bool)GetValue(ProminentProperty);
        set => SetValue(ProminentProperty, value);
    }

    public static Visibility MarkShown(string glyph, bool busy) =>
        !busy && !string.IsNullOrEmpty(glyph) ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility Said(string text) => string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;

    private void ShowProminence()
    {
        if (Prominent)
        {
            ChoiceButton.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        }
        else
        {
            ChoiceButton.ClearValue(StyleProperty);
        }
    }

    private void OnChoice(object sender, RoutedEventArgs args) => Chosen?.Invoke(this, args);
}
