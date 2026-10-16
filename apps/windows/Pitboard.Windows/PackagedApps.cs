using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Pitboard.Core;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Diagnostics.ToolHelp;
using Windows.Win32.System.RestartManager;
using Windows.Win32.System.Threading;
using Windows.Win32.UI.Shell;

namespace Pitboard.Windows;

// What WindowsApps asks of Windows: the process list, Restart Manager and the activation manager.
internal static class PackagedApps
{
    private delegate WIN32_ERROR Named(SafeHandle process, ref uint length, Span<char> name);

    public static IReadOnlyList<AppProcess> Listed()
    {
        var owners = WindowOwners();
        using var snapshot = PInvoke.CreateToolhelp32Snapshot_SafeHandle(CREATE_TOOLHELP_SNAPSHOT_FLAGS.TH32CS_SNAPPROCESS, 0);
        if (snapshot.IsInvalid)
        {
            throw new PlatformException.Failed($"Windows did not list its processes: {Marshal.GetLastPInvokeErrorMessage()}");
        }

        var listed = new List<AppProcess>();
        var entry = new PROCESSENTRY32W { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32W>() };
        for (var more = PInvoke.Process32FirstW(snapshot, ref entry); more; more = PInvoke.Process32NextW(snapshot, ref entry))
        {
            var (family, aumid) = Package(entry.th32ProcessID);
            listed.Add(new AppProcess(entry.th32ProcessID, entry.th32ParentProcessID, family, aumid, owners.Contains(entry.th32ProcessID)));
        }

        return listed;
    }

    // Without RmForceShutdown, and on a thread of its own: RmShutdown returns once the app has ended or declined.
    public static Task<WIN32_ERROR> Close(uint id)
    {
        using var process = Opened(id);
        if (process is null)
        {
            return Task.FromResult(WIN32_ERROR.ERROR_SUCCESS);
        }

        if (!PInvoke.GetProcessTimes(process, out var started, out _, out _, out _))
        {
            throw new PlatformException.Failed($"Windows did not say when process {id} started: {Marshal.GetLastPInvokeErrorMessage()}");
        }

        Span<char> key = stackalloc char[(int)PInvoke.CCH_RM_SESSION_KEY + 1];
        Succeeded(PInvoke.RmStartSession(out var session, key), "start a Restart Manager session");
        try
        {
            Succeeded(
                PInvoke.RmRegisterResources(session, default, [new RM_UNIQUE_PROCESS { dwProcessId = id, ProcessStartTime = started }], default),
                $"register process {id} with Restart Manager");
        }
        catch
        {
            PInvoke.RmEndSession(session);
            throw;
        }

        return Task.Factory.StartNew(
            () =>
            {
                try
                {
                    return PInvoke.RmShutdown(session, 0, null!);
                }
                finally
                {
                    PInvoke.RmEndSession(session);
                }
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    // The activation manager is a Shell object, and the model calls from a thread COM knows nothing of.
    public static void Activate(string aumid)
    {
        Exception? failed = null;
        var thread = new Thread(() =>
        {
            IApplicationActivationManager? manager = null;
            try
            {
                manager = (IApplicationActivationManager)new ApplicationActivationManager();
                manager.ActivateApplication(aumid, null!, ACTIVATEOPTIONS.AO_NONE, out _);
            }
            catch (Exception refused)
            {
                failed = refused;
            }
            finally
            {
                // In its own apartment, before the thread that made it ends.
                if (manager is not null)
                {
                    Marshal.FinalReleaseComObject(manager);
                }
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failed is not null)
        {
            throw new PlatformException.Failed($"Windows did not open {aumid}: {failed.Message}");
        }
    }

    // Hidden ones too, as an app in the notification area keeps.
    private static HashSet<uint> WindowOwners()
    {
        var owners = new HashSet<uint>();
        PInvoke.EnumWindows(
            (window, _) =>
            {
                PInvoke.GetWindowThreadProcessId(window, out var owner);
                owners.Add(owner);
                return true;
            },
            default);
        return owners;
    }

    // Another user's process, or one that has ended, has neither.
    private static (string? Family, string? Aumid) Package(uint id)
    {
        using var process = Opened(id);
        var family = process is null ? null : Read(process, PInvoke.GetPackageFamilyName);
        return (family, family is null ? null : Read(process!, PInvoke.GetApplicationUserModelId));
    }

    private static SafeFileHandle? Opened(uint id)
    {
        var process = PInvoke.OpenProcess_SafeHandle(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, id);
        if (!process.IsInvalid)
        {
            return process;
        }

        process.Dispose();
        return null;
    }

    // APPMODEL_ERROR_NO_PACKAGE or APPMODEL_ERROR_NO_APPLICATION where there is none.
    private static string? Read(SafeHandle process, Named named)
    {
        var length = 0u;
        if (named(process, ref length, default) != WIN32_ERROR.ERROR_INSUFFICIENT_BUFFER)
        {
            return null;
        }

        var name = new char[length];
        return named(process, ref length, name) == WIN32_ERROR.ERROR_SUCCESS ? new string(name, 0, (int)length - 1) : null;
    }

    private static void Succeeded(WIN32_ERROR result, string what)
    {
        if (result != WIN32_ERROR.ERROR_SUCCESS)
        {
            throw new PlatformException.Failed($"Windows did not {what}: {Marshal.GetPInvokeErrorMessage((int)result)}");
        }
    }
}
