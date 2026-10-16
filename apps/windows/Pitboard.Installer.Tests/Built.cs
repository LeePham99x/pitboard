using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Pitboard.Installer.Tests;

internal static partial class Built
{
    // The MSI windows-app built.
    public static string Msi => Path(nameof(Msi), "PitboardMsi", File.Exists);

    // The folder Pitboard.exe lies in, in the administrative image msiexec /a extracted from it.
    public static string Image => Path(nameof(Image), "PitboardMsiImage", Directory.Exists);

    // Whether the MSI was built with PitboardShare.exe, as it is when its package is signed.
    public static bool ShareSigned =>
        typeof(Built).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Any(metadata => metadata.Key == "PitboardShareSigned" && metadata.Value == "true");

    // The version Cargo.toml names, as the MSI was given it.
    public static string CargoVersion()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            var manifest = System.IO.Path.Combine(folder.FullName, "Cargo.toml");
            var version = File.Exists(manifest) ? WorkspaceVersion().Match(File.ReadAllText(manifest)) : Match.Empty;
            if (version.Success)
            {
                return version.Groups[1].Value;
            }
        }

        throw new InvalidOperationException($"no workspace Cargo.toml above {AppContext.BaseDirectory}");
    }

    public static int Major(string version) =>
        int.Parse(version.Split('.')[0], CultureInfo.InvariantCulture);

    private static string Path(string what, string key, Func<string, bool> exists)
    {
        var path = typeof(Built).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(metadata => metadata.Key == key)?.Value;
        return string.IsNullOrEmpty(path) || !exists(path)
            ? throw new InvalidOperationException($"no {what} at '{path}': publish the tests with -p:{key}=<its path>")
            : path;
    }

    [GeneratedRegex(@"^\[workspace\.package\]$[^\[]*?^version = ""([^""]+)""", RegexOptions.Multiline)]
    private static partial Regex WorkspaceVersion();
}
