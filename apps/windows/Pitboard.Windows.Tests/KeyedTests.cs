namespace Pitboard.Windows.Tests;

[TestClass]
public sealed class KeyedTests
{
    private sealed record Item(string Key, string Text);

    private sealed class Row(Item item)
    {
        public string Key { get; } = item.Key;

        public string Text { get; set; } = item.Text;
    }

    private static void Sync(List<Row> rows, params Item[] items) =>
        Keyed.Sync(rows, items, item => item.Key, row => row.Key, item => new Row(item), (row, item) => row.Text = item.Text);

    [TestMethod]
    public void ARowTheModelStillListsIsKeptAndUpdated()
    {
        var rows = new List<Row>();
        Sync(rows, new("work", "work 42%"), new("spare", "spare 10%"));
        var work = rows[0];

        Sync(rows, new("work", "work 43%"), new("spare", "spare 10%"));

        Assert.AreSame(work, rows[0]);
        Assert.AreEqual("work 43%", work.Text);
    }

    [TestMethod]
    public void RowsComeAndGoAndMoveAsTheModelSays()
    {
        var rows = new List<Row>();
        Sync(rows, new("a", ""), new("b", ""), new("c", ""));
        var c = rows[2];

        Sync(rows, new("c", ""), new("d", ""), new("a", ""));

        Assert.AreEqual("c, d, a", string.Join(", ", rows.Select(row => row.Key)));
        Assert.AreSame(c, rows[0]);
    }

    [TestMethod]
    public void NothingListedLeavesNoRow()
    {
        var rows = new List<Row>();
        Sync(rows, new("a", ""), new("b", ""));

        Sync(rows);

        Assert.IsEmpty(rows);
    }
}
