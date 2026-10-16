using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.RestartManager;

namespace Pitboard.Windows;

// The Restart Manager calls that ask an app to quit. Tests stand in for them: a session lives under the user's HKCU.
internal interface IRestartManager
{
    WIN32_ERROR StartSession(out uint session);

    WIN32_ERROR Register(uint session, RM_UNIQUE_PROCESS process);

    WIN32_ERROR Shutdown(uint session, uint flags);

    WIN32_ERROR EndSession(uint session);
}

internal sealed class SystemRestartManager : IRestartManager
{
    public WIN32_ERROR StartSession(out uint session)
    {
        Span<char> key = stackalloc char[(int)PInvoke.CCH_RM_SESSION_KEY + 1];
        return PInvoke.RmStartSession(out session, key);
    }

    public WIN32_ERROR Register(uint session, RM_UNIQUE_PROCESS process) =>
        PInvoke.RmRegisterResources(session, default, [process], default);

    public WIN32_ERROR Shutdown(uint session, uint flags) => PInvoke.RmShutdown(session, flags, null!);

    public WIN32_ERROR EndSession(uint session) => PInvoke.RmEndSession(session);
}
