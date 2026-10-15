# Checks the Windows app as a Release publish lays it out, for CI and the release alike: no
# fixture in any of its programs, Pitboard.exe without a package identity, and the core built
# for this machine with its C runtime inside it. It reads the files and runs none of them.
param(
    [Parameter(Mandatory)] [string] $Folder,
    [Parameter(Mandatory)] [ValidateSet('x86_64-pc-windows-msvc', 'aarch64-pc-windows-msvc')] [string] $Triple
)
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path -LiteralPath $Folder).Path
$exe = Join-Path $root 'Pitboard.exe'
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw "no Pitboard.exe in $root" }

# no-fixture-text.sh's words, as UTF-8 in native code and as UTF-16 in a .NET assembly's strings.
$fixtureText = 'A Pitboard fixture page', 'dana@work.example', 'PITBOARD_TEST_CLAUDE_DESKTOP'
$programs = @(Get-ChildItem -LiteralPath $root -Recurse -File -Include '*.exe', '*.dll')
foreach ($program in $programs) {
    $bytes = [IO.File]::ReadAllBytes($program.FullName)
    $narrow = [Text.Encoding]::Latin1.GetString($bytes)
    $wide = [Text.Encoding]::Unicode.GetString($bytes) + [Text.Encoding]::Unicode.GetString($bytes, 1, $bytes.Length - 1)
    foreach ($text in $fixtureText) {
        if ($narrow.Contains($text) -or $wide.Contains($text)) {
            throw "$($program.Name) holds a fixture's text, '$text': a Release build has none"
        }
    }
}
# The core's own metadata names the variable; only a Debug build of the app reads it.
foreach ($assembly in 'Pitboard.dll', 'Pitboard.Windows.dll') {
    $bytes = [IO.File]::ReadAllBytes((Join-Path $root $assembly))
    if ([Text.Encoding]::Unicode.GetString($bytes).Contains('PITBOARD_FIXTURE')) {
        throw "$assembly reads PITBOARD_FIXTURE: it was built as Debug"
    }
}
Write-Output "None of the $($programs.Count) programs and libraries holds a fixture."

# Only PitboardShare.exe carries a package identity, through an <msix> element in its manifest.
Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

public static class PitboardManifest
{
    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibraryExW(string path, IntPtr file, uint flags);
    [DllImport("kernel32", SetLastError = true)]
    private static extern IntPtr FindResourceW(IntPtr module, IntPtr name, IntPtr type);
    [DllImport("kernel32", SetLastError = true)]
    private static extern IntPtr LoadResource(IntPtr module, IntPtr resource);
    [DllImport("kernel32", SetLastError = true)]
    private static extern IntPtr LockResource(IntPtr data);
    [DllImport("kernel32", SetLastError = true)]
    private static extern uint SizeofResource(IntPtr module, IntPtr resource);
    [DllImport("kernel32", SetLastError = true)]
    private static extern bool FreeLibrary(IntPtr module);

    // LOAD_LIBRARY_AS_DATAFILE | LOAD_LIBRARY_AS_IMAGE_RESOURCE: mapped to be read, never run.
    public static string Of(string path)
    {
        IntPtr module = LoadLibraryExW(path, IntPtr.Zero, 0x2 | 0x20);
        if (module == IntPtr.Zero) throw new Win32Exception();
        try
        {
            // RT_MANIFEST, CREATEPROCESS_MANIFEST_RESOURCE_ID
            IntPtr found = FindResourceW(module, (IntPtr)1, (IntPtr)24);
            if (found == IntPtr.Zero) return null;
            IntPtr data = LockResource(LoadResource(module, found));
            byte[] bytes = new byte[SizeofResource(module, found)];
            Marshal.Copy(data, bytes, 0, bytes.Length);
            return Encoding.UTF8.GetString(bytes);
        }
        finally
        {
            FreeLibrary(module);
        }
    }
}
'@
$manifest = [PitboardManifest]::Of($exe)
if (-not $manifest) { throw 'Pitboard.exe has no manifest: app.manifest did not reach it' }
if ($manifest -match '<(\w+:)?msix\b') {
    Write-Output $manifest
    throw 'Pitboard.exe asks for a package identity: only PitboardShare.exe may carry one'
}
Write-Output 'Pitboard.exe carries no package identity.'

$machine = @{ 'x86_64-pc-windows-msvc' = 'AMD64'; 'aarch64-pc-windows-msvc' = 'ARM64' }[$Triple]
$readobj = Get-Command llvm-readobj -ErrorAction SilentlyContinue
$readobj = if ($readobj) { $readobj.Source } else { "$env:ProgramFiles\LLVM\bin\llvm-readobj.exe" }
foreach ($library in 'pitboard_ffi.dll') {
    $path = Join-Path $root $library
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "no $library beside Pitboard.exe" }
    $headers = & $readobj --file-headers $path | Out-String
    if ($LASTEXITCODE -ne 0) { throw "llvm-readobj could not read $library" }
    if ($headers -notmatch "Machine: IMAGE_FILE_MACHINE_$machine\b") {
        Write-Output $headers
        throw "$library is not a library for $machine"
    }
    $imports = & $readobj --coff-imports $path | Out-String
    if ($LASTEXITCODE -ne 0) { throw "llvm-readobj could not read $library's imports" }
    if ($imports -match '(?i)vcruntime|api-ms-win-crt') {
        Write-Output $imports
        throw "$library takes its C runtime from a DLL: it was built without +crt-static"
    }
    Write-Output "$library is for $machine, with the C runtime inside it."
}
exit 0
