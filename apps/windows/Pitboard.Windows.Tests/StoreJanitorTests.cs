using Pitboard.Core;

namespace Pitboard.Windows.Tests;

[TestClass]
public sealed class StoreJanitorTests
{
    private readonly Folders _folders = new();
    private readonly List<(string Store, bool Deleted)> _told = [];

    private void Tell(string store, bool deleted) => _told.Add((store, deleted));

    private StoreJanitor Janitor() => new(_folders);

    [TestMethod]
    public void AStoreWithNoFolderIsDeletedAtOnce()
    {
        Janitor().Delete([new StoreDeletion("a", 1)], [], Tell);

        Assert.AreEqual(("a", true), _told.Single());
        Assert.IsEmpty(_folders.Marked);
    }

    // On the runners the folder outlived the browser process that marked it, and went as the next one started.
    [TestMethod]
    public void AStoreIsMarkedOnceAndSweptOnceItsBrowserProcessHasExited()
    {
        _folders.There.Add("a");
        var janitor = Janitor();

        janitor.Delete([new StoreDeletion("a", 1)], [], Tell);
        janitor.Delete([new StoreDeletion("a", 2)], ["a"], Tell);
        _folders.Running = false;
        janitor.Delete([new StoreDeletion("a", 3)], ["a"], Tell);

        Assert.AreEqual("a", _folders.Marked.Single());
        Assert.AreEqual(1, _folders.Sweeps);
        CollectionAssert.AreEqual(new[] { ("a", false), ("a", false), ("a", true) }, _told);
    }

    // Marked at an earlier launch: a start of this one deletes its folder, and making its profile first could make it
    // again, as the start had already deleted it.
    [TestMethod]
    public void AStoreHeldAtAnEarlierLaunchIsSweptNotMarkedAgain()
    {
        _folders.There.Add("a");
        _folders.Pending.Add("a");

        Janitor().Delete([new StoreDeletion("a", 1)], ["A"], Tell);

        Assert.IsEmpty(_folders.Marked);
        Assert.AreEqual(1, _folders.Sweeps);
        Assert.AreEqual(("a", true), _told.Single());
    }

    // A browser process started since its mark that left the folder: the mark may not have taken.
    [TestMethod]
    public void AFolderAStartLeftIsMarkedAgain()
    {
        _folders.There.Add("a");
        _folders.Starts = 1;
        _folders.Running = true;

        Janitor().Delete([new StoreDeletion("a", 1)], ["a"], Tell);

        Assert.AreEqual("a", _folders.Marked.Single());
        Assert.AreEqual(0, _folders.Sweeps);
        Assert.AreEqual(("a", false), _told.Single());
    }

    [TestMethod]
    public void EachAskIsAnsweredOnceHoweverManySnapshotsListIt()
    {
        var janitor = Janitor();

        janitor.Delete([new StoreDeletion("a", 1)], [], Tell);
        janitor.Delete([new StoreDeletion("a", 1), new StoreDeletion("b", 2)], [], Tell);

        CollectionAssert.AreEqual(new[] { ("a", true), ("b", true) }, _told);
    }

    // A Runtime that kept profiles elsewhere would have every folder look absent, and a forgotten account's sign-in stay.
    [TestMethod]
    public void AFolderThatLooksAbsentIsDeletedOnlyOnceAPageHasShownWhereFoldersAre()
    {
        _folders.LayoutKnown = false;
        var janitor = Janitor();

        janitor.Delete([new StoreDeletion("a", 1)], [], Tell);
        _folders.LayoutKnown = true;
        janitor.Delete([new StoreDeletion("a", 2)], ["a"], Tell);

        Assert.AreEqual("a", _folders.Marked.Single());
        CollectionAssert.AreEqual(new[] { ("a", false), ("a", true) }, _told);
    }

    // Answered all the same, or the model would wait on the ask for good.
    [TestMethod]
    public void AStoreThatCouldNotBeMarkedIsHeldAndTriedAgain()
    {
        _folders.There.Add("a");
        _folders.Refusing = true;
        var janitor = Janitor();

        janitor.Delete([new StoreDeletion("a", 1)], [], Tell);
        _folders.Refusing = false;
        janitor.Delete([new StoreDeletion("a", 2)], ["a"], Tell);

        Assert.AreEqual("a", _folders.Marked.Single());
        CollectionAssert.AreEqual(new[] { ("a", false), ("a", false) }, _told);
    }

    // Profiles as WebView2 keeps them: marking one starts a browser process, and a start deletes what was marked before.
    private sealed class Folders : IProfileFolders
    {
        public HashSet<string> There { get; } = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> Pending { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<string> Marked { get; } = [];

        public int Sweeps { get; private set; }

        public bool Refusing { get; set; }

        public bool LayoutKnown { get; set; } = true;

        public int Starts { get; set; }

        public bool Running { get; set; }

        public bool Exists(string store) => There.Contains(store);

        public Task MarkDeleted(string store)
        {
            if (Refusing)
            {
                throw new InvalidOperationException("the browser process would not start");
            }

            Start();
            Marked.Add(store);
            Pending.Add(store);
            return Task.CompletedTask;
        }

        public Task Sweep(Func<bool> gone)
        {
            Sweeps++;
            Start();
            return Task.CompletedTask;
        }

        private void Start()
        {
            if (!Running)
            {
                Starts++;
                There.ExceptWith(Pending);
                Pending.Clear();
                Running = true;
            }
        }
    }
}
