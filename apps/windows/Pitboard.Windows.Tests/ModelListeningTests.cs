using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;

namespace Pitboard.Windows.Tests;

[TestClass]
public sealed class ModelListeningTests
{
    // Told from a thread of the test's own, as the model tells from one of its own.
    [TestMethod]
    public void EachSnapshotReachesTheAppsThreadInTheOrderItWasTold()
    {
        var queue = UITestMethodAttribute.DispatcherQueue!;
        var listening = new ModelListening(queue);
        var delivered = new List<string>();
        using var all = new CountdownEvent(3);
        listening.Deliver = snapshot =>
        {
            delivered.Add(queue.HasThreadAccess ? $"{snapshot.Revision}" : $"{snapshot.Revision} elsewhere");
            all.Signal();
        };

        listening.Changed(Snapshots.Read(1));
        listening.Changed(Snapshots.Read(3));
        listening.Changed(Snapshots.Read(2));

        Assert.IsTrue(all.Wait(TimeSpan.FromSeconds(10)), "not every snapshot was delivered");
        Assert.AreEqual("1, 3, 2", string.Join(", ", delivered));
    }
}
