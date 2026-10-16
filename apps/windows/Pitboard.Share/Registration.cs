using System.Runtime.InteropServices;
using Windows.Management.Deployment;

namespace Pitboard.Share;

// The MSI's registration, with this program's folder as the package's external location. Pitboard.exe removes it.
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
}
