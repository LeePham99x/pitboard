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

        // The MSI starts Pitboard after an update, which a copy already running has no use for.
        return given.Contains(Updated) ? Start.Dropped : Start.Redirect;
    }
}
