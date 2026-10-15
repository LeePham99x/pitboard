using System.Security.Principal;

namespace Pitboard.UITests;

// On CI the job's user is elevated and the test user is not: each run takes the tests made for it.
[AttributeUsage(AttributeTargets.Class)]
internal sealed class ElevatedAttribute : ConditionBaseAttribute
{
    public ElevatedAttribute(ConditionMode mode)
        : base(mode) =>
        IgnoreMessage = mode is ConditionMode.Include ? "runs as an elevated user only" : "runs as a standard user only";

    public override bool IsConditionMet
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    public override string GroupName => "Elevated";
}
