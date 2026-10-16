using System.Runtime.InteropServices;
using Windows.Management.Deployment;

namespace Pitboard.Windows;

// The user's registrations of PitboardShare's sparse package.
public interface ISharePackages
{
    IReadOnlyList<(string FullName, string? Location)> Registered();

    // 0, or the error Windows gave the removal.
    int Remove(string fullName);
}

// What the MSI's removal and rollback ask of Pitboard.exe --unregister-share. Not PitboardShare.exe: RemovePackageAsync
// ends every process of the package it removes, and a process without the identity finds no package through it.
public static class SharePackage
{
    public const string Name = "PitboardShare";

    // Only the package whose external location is this install, so another copy's stays registered.
    public static int Unregister(ISharePackages packages, string folder)
    {
        ArgumentNullException.ThrowIfNull(packages);
        var failed = 0;
        foreach (var (fullName, location) in packages.Registered())
        {
            if (location is not null && string.Equals(Trimmed(location), Trimmed(folder), StringComparison.OrdinalIgnoreCase))
            {
                var removed = packages.Remove(fullName);
                failed = removed != 0 ? removed : failed;
            }
        }

        return failed;
    }

    private static string Trimmed(string folder) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
}

public sealed class SystemSharePackages : ISharePackages
{
    private readonly PackageManager _manager = new();

    public IReadOnlyList<(string FullName, string? Location)> Registered() =>
        _manager.FindPackagesForUser("")
            .Where(package => package.Id.Name == SharePackage.Name)
            .Select(package => (package.Id.FullName, (string?)package.EffectiveExternalPath))
            .ToList();

    public int Remove(string fullName)
    {
        try
        {
            var result = _manager.RemovePackageAsync(fullName).AsTask().GetAwaiter().GetResult();
            return result.ExtendedErrorCode?.HResult ?? 0;
        }
        catch (Exception refused) when (refused is COMException or UnauthorizedAccessException)
        {
            return refused.HResult;
        }
    }
}
