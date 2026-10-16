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

    // What the Run value starts Pitboard with, as Windows signs the person in.
    public const string SignIn = "--sign-in";

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

        // A click's arguments reach only the copy that registered, and nobody asked for the other two.
        return Unasked(given) ? Start.Dropped : Start.Redirect;
    }

    public static bool OpensWindow(IEnumerable<string> arguments) =>
        !Unasked(arguments.ToHashSet(StringComparer.OrdinalIgnoreCase));

    private static bool Unasked(HashSet<string> given) =>
        given.Contains(SignIn) || given.Contains(Updated) || given.Contains(Notified);
}
