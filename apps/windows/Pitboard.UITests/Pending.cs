namespace Pitboard.UITests;

// The W item each fixture world waits for on Windows, as its Rust fixture test is ignored for it.
internal static class Pending
{
    public const string W17 = "W17: finding programs on Windows, without which these machines have no Claude Code";

    public const string W23a = "W23a: Claude Code's Credential Manager store, which a machine in memory plays";

    public const string W23b = "W23b: Claude Code's login left in a file behind its Credential Manager store";

    public const string W25 = "W25: Task Scheduler, which renews parked logins daily";

    public const string WWarm = "W-warm: warming up Claude Code accounts on Windows";
}
