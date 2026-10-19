using Pitboard.Core;

namespace Pitboard.Windows;

public interface IRenewalTask
{
    void Remove(string exe);
}

// The core alone names and writes the task, and takes it away only where it runs the command line beside exe.
public sealed class CoreRenewalTask(Dictionary<string, string> environment) : IRenewalTask
{
    public void Remove(string exe) => _ = PitboardFfiMethods.UnscheduleApp(environment, exe);
}
