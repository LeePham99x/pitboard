namespace Pitboard.UITests;

// CF3: on GitHub's runners Explorer is the job user's and takes no icon of the test user's, so a test that
// clicks Pitboard's icon runs only where Explorer takes it, and the session's step A2 clicks it.
[AttributeUsage(AttributeTargets.Class)]
internal sealed class ExplorerTakesIconsAttribute : ConditionBaseAttribute
{
    public ExplorerTakesIconsAttribute()
        : base(ConditionMode.Include) =>
        IgnoreMessage = "CF3: Explorer takes no notification-area icon of this user's, as on GitHub's runners";

    public override bool IsConditionMet => NotificationArea.TakesIcons;

    public override string GroupName => "ExplorerTakesIcons";
}
