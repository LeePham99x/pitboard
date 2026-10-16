using Microsoft.UI.Dispatching;

namespace Pitboard.Windows;

// An exception that ends a DispatcherQueue callback fails fast in CoreMessagingXP.dll and reaches no handler, so a
// callback writes it out first, where the UI tests read why Pitboard ended.
public static class Ui
{
    public static DispatcherQueueHandler Guarded(Action callback, TextWriter? said = null) =>
        () =>
        {
            try
            {
                callback();
            }
            catch (Exception thrown)
            {
                (said ?? Console.Error).WriteLine(Said(thrown));
                throw;
            }
        };

    // C#/WinRT keeps WinUI's own words, such as the XAML file it could not read, apart from the message.
    private static string Said(Exception thrown) =>
        thrown.Data["RestrictedDescription"] is string restricted ? $"{thrown}{Environment.NewLine}{restricted}" : thrown.ToString();
}
