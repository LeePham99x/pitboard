using System.Runtime.InteropServices;
using Windows.ApplicationModel;
using Windows.Management.Deployment;

namespace Pitboard.Share;

// The MSI's custom actions, with this program's folder as the package's external location.
internal static class Registration
{
    private const string PackageFile = "PitboardShare.msix";

    public static async Task<int> RegisterAsync()
    {
        var folder = AppContext.BaseDirectory;
        var options = new AddPackageOptions { ExternalLocationUri = new Uri(folder) };
        try
        {
            var result = await new PackageManager().AddPackageByUriAsync(new Uri(Path.Combine(folder, PackageFile)), options);
            return result.IsRegistered ? 0 : 1;
        }
        catch (Exception failed) when (failed is COMException or UnauthorizedAccessException)
        {
            return 1;
        }
    }

    // This program carries the package's identity, so the removal may end it before it returns.
    public static async Task<int> UnregisterAsync()
    {
        string package;
        try
        {
            package = Package.Current.Id.FullName;
        }
        catch (InvalidOperationException)
        {
            return 0;
        }

        try
        {
            await new PackageManager().RemovePackageAsync(package);
            return 0;
        }
        catch (Exception failed) when (failed is COMException or UnauthorizedAccessException)
        {
            return 1;
        }
    }
}
