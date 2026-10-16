using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Pitboard.Windows;

// What SparkleUpdates asks of WinSparkle, which tests replace.
internal interface ISparkle
{
    // 0 where WinSparkle refuses the key, which then keeps none and installs an update nobody signed.
    int SetKey(string key);

    // Before Start, as WinSparkle takes its configuration.
    void Configure(string feed, string version, UpdateSettings settings, SparkleEvents events);

    bool ChecksAutomatically { get; set; }

    void Start();

    void Check();

    void Stop();
}

// Each runs on a thread of WinSparkle's, never the app's.
internal sealed record SparkleEvents(Func<bool> MayShutDown, Action ShutDown, Action Found, Action NotFound, Action Skipped);

// WinSparkle.dll 0.9.4 through its C API, which holds one updater per process.
internal sealed unsafe partial class WinSparkle : ISparkle
{
    private const string Library = "WinSparkle.dll";

    private static SparkleEvents? _events;
    private static UpdateSettings? _settings;

    public bool ChecksAutomatically
    {
        get => GetAutomaticCheckForUpdates() != 0;
        set => SetAutomaticCheckForUpdates(value ? 1 : 0);
    }

    public int SetKey(string key) => SetEdDsaPublicKey(key);

    public void Configure(string feed, string version, UpdateSettings settings, SparkleEvents events)
    {
        _events = events;
        _settings = settings;
        var methods = new ConfigMethods { Read = &ReadSetting, Write = &WriteSetting, Delete = &DeleteSetting };
        SetConfigMethods(&methods);
        SetAppDetails("Pitboard", "Pitboard", version);
        SetAppBuildVersion(version);
        SetAppcastUrl(feed);
        SetCanShutdownCallback(&MayShutDown);
        SetShutdownRequestCallback(&ShutDown);
        SetDidFindUpdateCallback(&Found);
        SetDidNotFindUpdateCallback(&NotFound);
        SetUpdateSkippedCallback(&Skipped);
    }

    public void Start() => Init();

    public void Check() => CheckUpdateWithUi();

    public void Stop() => Cleanup();

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int MayShutDown() => _events?.MayShutDown() == true ? 1 : 0;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void ShutDown() => _events?.ShutDown();

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Found() => _events?.Found();

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void NotFound() => _events?.NotFound();

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Skipped() => _events?.Skipped();

    // length counts UTF-16 units, the null included.
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int ReadSetting(byte* name, char* value, nuint length, void* data)
    {
        if (_settings?.Read(Marshal.PtrToStringUTF8((nint)name) ?? "") is not { } read || (nuint)read.Length >= length)
        {
            return 0;
        }

        read.CopyTo(new Span<char>(value, read.Length));
        value[read.Length] = '\0';
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void WriteSetting(byte* name, char* value, void* data) =>
        _settings?.Write(Marshal.PtrToStringUTF8((nint)name) ?? "", new string(value));

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void DeleteSetting(byte* name, void* data) => _settings?.Delete(Marshal.PtrToStringUTF8((nint)name) ?? "");

    [StructLayout(LayoutKind.Sequential)]
    private struct ConfigMethods
    {
        public delegate* unmanaged[Cdecl]<byte*, char*, nuint, void*, int> Read;
        public delegate* unmanaged[Cdecl]<byte*, char*, void*, void> Write;
        public delegate* unmanaged[Cdecl]<byte*, void*, void> Delete;
        public void* UserData;
    }

    [LibraryImport(Library, EntryPoint = "win_sparkle_set_eddsa_public_key", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial int SetEdDsaPublicKey(string key);

    [LibraryImport(Library, EntryPoint = "win_sparkle_set_config_methods")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial void SetConfigMethods(ConfigMethods* methods);

    [LibraryImport(Library, EntryPoint = "win_sparkle_set_app_details", StringMarshalling = StringMarshalling.Utf16)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial void SetAppDetails(string company, string app, string version);

    [LibraryImport(Library, EntryPoint = "win_sparkle_set_app_build_version", StringMarshalling = StringMarshalling.Utf16)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial void SetAppBuildVersion(string build);

    [LibraryImport(Library, EntryPoint = "win_sparkle_set_appcast_url", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial void SetAppcastUrl(string url);

    [LibraryImport(Library, EntryPoint = "win_sparkle_set_automatic_check_for_updates")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial void SetAutomaticCheckForUpdates(int state);

    [LibraryImport(Library, EntryPoint = "win_sparkle_get_automatic_check_for_updates")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial int GetAutomaticCheckForUpdates();

    [LibraryImport(Library, EntryPoint = "win_sparkle_set_can_shutdown_callback")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial void SetCanShutdownCallback(delegate* unmanaged[Cdecl]<int> callback);

    [LibraryImport(Library, EntryPoint = "win_sparkle_set_shutdown_request_callback")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial void SetShutdownRequestCallback(delegate* unmanaged[Cdecl]<void> callback);

    [LibraryImport(Library, EntryPoint = "win_sparkle_set_did_find_update_callback")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial void SetDidFindUpdateCallback(delegate* unmanaged[Cdecl]<void> callback);

    [LibraryImport(Library, EntryPoint = "win_sparkle_set_did_not_find_update_callback")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial void SetDidNotFindUpdateCallback(delegate* unmanaged[Cdecl]<void> callback);

    [LibraryImport(Library, EntryPoint = "win_sparkle_set_update_skipped_callback")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial void SetUpdateSkippedCallback(delegate* unmanaged[Cdecl]<void> callback);

    [LibraryImport(Library, EntryPoint = "win_sparkle_init")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial void Init();

    [LibraryImport(Library, EntryPoint = "win_sparkle_check_update_with_ui")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial void CheckUpdateWithUi();

    [LibraryImport(Library, EntryPoint = "win_sparkle_cleanup")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial void Cleanup();
}
