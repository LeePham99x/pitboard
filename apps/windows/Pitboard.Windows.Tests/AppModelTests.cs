using System.Reflection;
using Pitboard.Core;

namespace Pitboard.Windows.Tests;

[TestClass]
public sealed class AppModelTests
{
    [TestMethod]
    public void ASnapshotNoNewerThanTheOneShownIsDropped()
    {
        var model = new AppModel(Snapshots.Read(2));
        var told = new List<string?>();
        model.PropertyChanged += (_, changed) => told.Add(changed.PropertyName);

        Assert.IsEmpty(model.Apply(Snapshots.Read(1) with { Reading = true }));
        Assert.IsEmpty(model.Apply(Snapshots.Read(2) with { Reading = true }));

        Assert.AreEqual(2UL, model.Revision);
        Assert.IsFalse(model.Reading);
        Assert.IsEmpty(told);
    }

    // Every list in the newer snapshot is a new array holding what the shown one held.
    [TestMethod]
    public void OnlyThePartsThatChangedAreAssignedAndTold()
    {
        var model = new AppModel(Snapshots.Read(1));
        var status = model.Status;
        var told = new List<string?>();
        model.PropertyChanged += (_, changed) => told.Add(changed.PropertyName);

        var assigned = model.Apply(Snapshots.Read(3) with { Reading = true });

        Assert.AreEqual("Revision, Reading", string.Join(", ", assigned));
        Assert.AreEqual("Revision, Reading", string.Join(", ", told));
        Assert.AreSame(status, model.Status);
        Assert.IsTrue(model.Reading);
    }

    [TestMethod]
    public void AChangeInsideAListIsAChangeOfItsPart()
    {
        var model = new AppModel(Snapshots.Read(1));
        var read = Snapshots.Read(2);
        var status = read.Status!;
        var renamed = status with { Accounts = [status.Accounts[0] with { Label = "office" }] };

        var assigned = model.Apply(read with { Status = renamed });

        Assert.AreEqual("Revision, Status", string.Join(", ", assigned));
        Assert.AreEqual("office", model.Status!.Accounts[0].Label);
    }

    [TestMethod]
    public void TheModelShowsEveryPartOfASnapshot()
    {
        var snapshot = Snapshots.Read(1);
        var model = new AppModel(snapshot);

        foreach (var part in typeof(Snapshot).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var shown = typeof(AppModel).GetProperty(part.Name);
            Assert.IsNotNull(shown, part.Name);
            Assert.AreEqual(part.PropertyType, shown.PropertyType, part.Name);
            Assert.AreEqual(part.GetValue(snapshot), shown.GetValue(model), part.Name);
        }
    }
}
