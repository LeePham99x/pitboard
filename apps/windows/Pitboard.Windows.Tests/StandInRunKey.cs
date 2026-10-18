namespace Pitboard.Windows.Tests;

// The person's Run key in memory: tests never write the registry.
internal sealed class StandInRunKey : IRunKey
{
    public Dictionary<string, string> Values { get; } = [];

    // StartupApproved\Run, which Settings > Apps > Startup writes.
    public Dictionary<string, byte[]> Approvals { get; } = [];

    public List<string> Asked { get; } = [];

    public Exception? Refuses { get; set; }

    public Exception? ReadRefuses { get; set; }

    public string? Value(string name)
    {
        Asked.Add($"value {name}");
        if (ReadRefuses is { } refused)
        {
            throw refused;
        }

        return Values.GetValueOrDefault(name);
    }

    public byte[]? Approval(string name)
    {
        Asked.Add($"approval {name}");
        return Approvals.GetValueOrDefault(name);
    }

    public void Write(string name, string command)
    {
        Asked.Add($"write {name}");
        if (Refuses is { } refused)
        {
            throw refused;
        }

        Values[name] = command;
    }

    public void Remove(string name)
    {
        Asked.Add($"remove {name}");
        Values.Remove(name);
    }
}
