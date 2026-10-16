namespace Pitboard.UITests;

[TestClass]
public sealed class Runner
{
    // A GitHub-hosted runner is thrown away after the job.
    [AssemblyInitialize]
    public static void OnlyOnAGitHubHostedRunner(TestContext context)
    {
        if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true"
            || Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT") != "github-hosted")
        {
            throw new InvalidOperationException(
                "Pitboard.UITests runs only on a GitHub-hosted runner: it moves the mouse, types into whatever has focus and replaces the clipboard");
        }
    }

    [AssemblyCleanup]
    public static void RemoveTheRunsFolders() => Launched.RemoveWeb();
}
