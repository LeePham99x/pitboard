using System.Runtime.InteropServices;

namespace Pitboard.Windows.Tests;

// The COM apartment of the window's thread, which WebView2's loader needs single-threaded (APTTYPE 0 or 3): as the
// app starts, and before each test that runs on that thread.
internal static partial class Apartments
{
    private static readonly List<string> Startup = [];
    private static string? _lastSingle;
    private static string? _firstMulti;

    public static string Said =>
        $"{string.Join(", ", Startup)}, last single-threaded before {_lastSingle ?? "no test"}, "
        + $"first multithreaded before {_firstMulti ?? "no test"}";

    public static string Now()
    {
        var read = CoGetApartmentType(out var type, out var qualifier);
        return read == 0 ? $"APTTYPE {type}, qualifier {qualifier}" : $"CoGetApartmentType 0x{read:X8}";
    }

    public static void Read(string when) => Startup.Add($"{when} {Now()}");

    public static void Before(string test)
    {
        if (UnitTestApp.Window?.DispatcherQueue.HasThreadAccess != true || CoGetApartmentType(out var type, out _) != 0)
        {
            return;
        }

        if (type is 0 or 3)
        {
            _lastSingle = test;
        }
        else
        {
            _firstMulti ??= test;
        }
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoGetApartmentType(out int type, out int qualifier);
}
