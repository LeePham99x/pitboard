using System.Runtime.InteropServices;

namespace Pitboard.UITests;

// The clipboard's text, as any program on this desktop reads it.
internal static partial class ClipboardText
{
    private const uint UnicodeText = 13;

    public static string? Read()
    {
        // Another program may hold the clipboard open for a moment.
        for (var tries = 0; tries < 40; tries++)
        {
            if (OpenClipboard(0))
            {
                try
                {
                    var handle = GetClipboardData(UnicodeText);
                    if (handle == 0)
                    {
                        return null;
                    }

                    try
                    {
                        return Marshal.PtrToStringUni(GlobalLock(handle));
                    }
                    finally
                    {
                        GlobalUnlock(handle);
                    }
                }
                finally
                {
                    CloseClipboard();
                }
            }

            Thread.Sleep(50);
        }

        throw new AssertFailedException("another program kept the clipboard open");
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenClipboard(nint owner);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseClipboard();

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint GetClipboardData(uint format);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint GlobalLock(nint memory);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalUnlock(nint memory);
}
