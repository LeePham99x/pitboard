using System.Runtime.InteropServices;

namespace Pitboard.UITests;

// CF1: on GitHub's runners the tests' user works in the job user's session, where a WinUI window fails to
// load, so a test of Pitboard's window runs only in a session of the user's own.
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
internal sealed partial class OwnSessionAttribute : ConditionBaseAttribute
{
    private const int WtsUserName = 5;

    public OwnSessionAttribute()
        : base(ConditionMode.Include) =>
        IgnoreMessage = "CF1: Pitboard's window fails to load in another user's session, as on GitHub's runners";

    public override bool IsConditionMet => string.Equals(SessionUser(), Environment.UserName, StringComparison.OrdinalIgnoreCase);

    public override string GroupName => "OwnSession";

    private static string? SessionUser()
    {
        if (!WTSQuerySessionInformation(0, -1, WtsUserName, out var name, out _))
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStringUni(name);
        }
        finally
        {
            WTSFreeMemory(name);
        }
    }

    [LibraryImport("wtsapi32.dll", EntryPoint = "WTSQuerySessionInformationW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WTSQuerySessionInformation(nint server, int session, int infoClass, out nint buffer, out uint bytes);

    [LibraryImport("wtsapi32.dll")]
    private static partial void WTSFreeMemory(nint memory);
}
