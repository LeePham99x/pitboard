using System.Text.Json;

namespace Pitboard.Windows;

// WinSparkle's own settings, in the app's data folder where it would use the registry, so an uninstall
// that removes the folder leaves none behind. WinSparkle calls it from threads of its own.
public sealed class UpdateSettings(string file)
{
    private readonly Lock _lock = new();
    private Dictionary<string, string>? _values;

    public string? Read(string name)
    {
        lock (_lock)
        {
            return Values().GetValueOrDefault(name);
        }
    }

    public void Write(string name, string value)
    {
        lock (_lock)
        {
            Values()[name] = value;
            Save();
        }
    }

    public void Delete(string name)
    {
        lock (_lock)
        {
            if (Values().Remove(name))
            {
                Save();
            }
        }
    }

    private Dictionary<string, string> Values()
    {
        if (_values is null)
        {
            try
            {
                _values = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file)) ?? [];
            }
            catch (Exception unread) when (unread is IOException or UnauthorizedAccessException or JsonException)
            {
                _values = [];
            }
        }

        return _values;
    }

    // Thrown through WinSparkle's native frames, a failure would end the process: an unwritten value lasts this run.
    private void Save()
    {
        var written = file + ".new";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(written, JsonSerializer.Serialize(_values));
            File.Move(written, file, overwrite: true);
        }
        catch (Exception unwritten) when (unwritten is IOException or UnauthorizedAccessException)
        {
        }
    }
}
