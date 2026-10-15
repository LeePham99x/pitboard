using System.Security;

namespace Pitboard.Windows;

// What starts Pitboard as the person signs in, which a fixture replaces so nothing is registered with Windows.
public interface IStartupItem
{
    // Why this copy never starts at sign-in, or null where it may.
    string? Refusal { get; }

    bool Read();

    void Write(bool starts);
}

// The settings' start at sign-in, as the macOS app's OpenAtLogin: what Windows has, and why a change did not happen.
public sealed class StartAtSignIn(IStartupItem item)
{
    public event EventHandler? Changed;

    // A Debug build's own writer refuses, so only a fixture's stand-in turns it on there.
    public static StartAtSignIn For(AppStart start, string exe)
    {
#if DEBUG
        if (start.Fixture is not null)
        {
            return new(new FixtureStartupItem());
        }
#endif
        return new(new RunValue(start.Identity.Build, exe, new RegistryRunKey()));
    }

    public bool On { get; private set; }

    public string? Failed { get; private set; }

    public string? Refusal => item.Refusal;

    // Read again each time the settings show it, since another program can change the Run key.
    public void Read()
    {
        try
        {
            On = item.Read();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException)
        {
            On = false;
            Failed = error.Message;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Set(bool on)
    {
        Failed = null;
        try
        {
            item.Write(on);
        }
        catch (Exception error) when (error is InvalidOperationException or IOException or UnauthorizedAccessException or SecurityException)
        {
            Failed = error.Message;
        }

        Read();
    }
}

#if DEBUG
// Starts nothing, so a fixture's settings turn start at sign-in on and off as the macOS fixture's login item does.
public sealed class FixtureStartupItem : IStartupItem
{
    private bool _on;

    public string? Refusal => null;

    public bool Read() => _on;

    public void Write(bool starts) => _on = starts;
}
#endif
