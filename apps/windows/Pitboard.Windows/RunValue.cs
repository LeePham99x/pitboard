using Microsoft.Win32;

namespace Pitboard.Windows;

// The values of the person's own Run key, each a command Windows starts as they sign in.
public interface IRunKey
{
    string? Value(string name);

    byte[]? Approval(string name);

    void Write(string name, string command);

    void Remove(string name);
}

public sealed class RegistryRunKey : IRunKey
{
    public const string Path = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public string? Value(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(Path);
        return key?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
    }

    public byte[]? Approval(string name) => null;

    public void Write(string name, string command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(Path, writable: true);
        key.SetValue(name, command, RegistryValueKind.String);
    }

    public void Remove(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(Path, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }
}

// Start at sign-in as an installed Release build keeps it: the Run value Pitboard, naming this Pitboard.exe and nothing else.
public sealed class RunValue(Build build, string exe, IRunKey key) : IStartupItem
{
    public const string Name = "Pitboard";

    // What the Run key's documentation allows a command line.
    public const int Longest = 260;

    public string? Refusal => build is Build.Debug ? "A Debug build never starts when you sign in to Windows." : null;

    public static string Command(string exe) => $"\"{exe}\" {Launching.SignIn}";

    // Whatever its arguments, as an uninstall finds a value this Pitboard.exe wrote.
    public static bool Names(string? command, string exe) =>
        command is not null
            && Launching.Arguments(command).FirstOrDefault() is { } program
            && string.Equals(program, exe, StringComparison.OrdinalIgnoreCase);

    // Paths are compared as Windows compares them, without case.
    public bool Read() =>
        Refusal is null && string.Equals(key.Value(Name), Command(exe), StringComparison.OrdinalIgnoreCase);

    public bool TurnedOff() => false;

    public void Write(bool starts)
    {
        if (Refusal is { } refusal)
        {
            throw new InvalidOperationException(refusal);
        }

        if (!starts)
        {
            if (Read())
            {
                key.Remove(Name);
            }

            return;
        }

        var command = Command(exe);
        if (command.Length > Longest)
        {
            throw new InvalidOperationException(
                $"Windows starts a program when you sign in only from a path of up to {Longest - Command("").Length} characters, and Pitboard’s is longer.");
        }

        key.Write(Name, command);
    }
}
