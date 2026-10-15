using Pitboard.Core;

namespace Pitboard.Windows;

public enum Start
{
    Run,
    Redirect,
    Dropped,
    Refused,
    Unregister,
}

public static class Launching
{
    public const string Unregister = "--unregister";

    public const string Updated = "--updated";

    // What COM starts Pitboard.exe with for a click on its notification, as Register writes it.
    public const string Notified = "----AppNotificationActivated:";

    public static Start Decide(IEnumerable<string> arguments, Func<ReadFailure?> refusal, Func<bool> holdsKey)
    {
        var given = arguments.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (given.Contains(Unregister))
        {
            return Start.Unregister;
        }

        if (refusal() is not null)
        {
            return Start.Refused;
        }

        if (holdsKey())
        {
            return Start.Run;
        }

        // The MSI starts Pitboard after an update, which a copy already running has no use for. A click's
        // arguments reach only a copy that registers, so a second copy has none to hand over.
        return given.Contains(Updated) || given.Contains(Notified) ? Start.Dropped : Start.Redirect;
    }
}
