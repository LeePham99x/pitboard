using Pitboard.Core;

namespace Pitboard.Windows.Tests;

[TestClass]
public sealed class StoreJanitorTests
{
    private readonly HashSet<string> _folders = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _marked = [];
    private readonly List<(string Store, bool Deleted)> _told = [];
    private bool _refusing;

    private StoreJanitor Janitor() => new(
        _folders.Contains,
        store =>
        {
            if (_refusing)
            {
                throw new InvalidOperationException("the browser process would not start");
            }

            _marked.Add(store);
            return Task.CompletedTask;
        });

    private void Tell(string store, bool deleted) => _told.Add((store, deleted));

    [TestMethod]
    public void AStoreWithNoFolderIsDeletedAtOnce()
    {
        Janitor().Delete([new StoreDeletion("a", 1)], Tell);

        Assert.AreEqual(("a", true), _told.Single());
        Assert.IsEmpty(_marked);
    }

    // WebView2 deletes the folder as its browser process exits.
    [TestMethod]
    public void AStoreIsMarkedOnceAndHeldUntilItsFolderGoes()
    {
        _folders.Add("a");
        var janitor = Janitor();

        janitor.Delete([new StoreDeletion("a", 1)], Tell);
        janitor.Delete([new StoreDeletion("a", 2)], Tell);
        _folders.Remove("a");
        janitor.Delete([new StoreDeletion("a", 3)], Tell);

        Assert.AreEqual("a", _marked.Single());
        CollectionAssert.AreEqual(new[] { ("a", false), ("a", false), ("a", true) }, _told);
    }

    [TestMethod]
    public void EachAskIsAnsweredOnceHoweverManySnapshotsListIt()
    {
        var janitor = Janitor();

        janitor.Delete([new StoreDeletion("a", 1)], Tell);
        janitor.Delete([new StoreDeletion("a", 1), new StoreDeletion("b", 2)], Tell);

        CollectionAssert.AreEqual(new[] { ("a", true), ("b", true) }, _told);
    }

    // Answered all the same, or the model would wait on the ask for good.
    [TestMethod]
    public void AStoreThatCouldNotBeMarkedIsHeldAndTriedAgain()
    {
        _folders.Add("a");
        _refusing = true;
        var janitor = Janitor();

        janitor.Delete([new StoreDeletion("a", 1)], Tell);
        _refusing = false;
        janitor.Delete([new StoreDeletion("a", 2)], Tell);

        Assert.AreEqual("a", _marked.Single());
        CollectionAssert.AreEqual(new[] { ("a", false), ("a", false) }, _told);
    }
}
