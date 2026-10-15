namespace Pitboard.Windows.Tests;

[TestClass]
public sealed class IconEventTests
{
    private const nint Select = 0x0400;
    private const nint KeySelect = 0x0401;
    private const nint ContextMenu = 0x007B;
    private const nint MouseMove = 0x0200;
    private const nint PopupOpen = 0x0406;

    // NOTIFYICON_VERSION_4: the event in lParam's low word, the icon's id in its high word.
    private static nint LParam(nint happened) => happened | (1 << 16);

    private static nuint At(short x, short y) => (nuint)(ushort)x | ((nuint)(ushort)y << 16);

    [TestMethod]
    public void AClickOrEnterOnTheIconActivatesIt()
    {
        Assert.AreEqual(new IconEvent.Activated(), IconEvent.Read(At(1800, 1060), LParam(Select)));
        Assert.AreEqual(new IconEvent.Activated(), IconEvent.Read(At(1800, 1060), LParam(KeySelect)));
    }

    [TestMethod]
    public void ARightClickOrTheMenuKeyAsksForTheMenuWhereItPoints()
    {
        Assert.AreEqual(new IconEvent.Menu(1800, 1060), IconEvent.Read(At(1800, 1060), LParam(ContextMenu)));
    }

    // A screen left of or above the main one has negative coordinates.
    [TestMethod]
    public void ThePointIsSignedForAScreenLeftOfTheMainOne()
    {
        Assert.AreEqual(new IconEvent.Menu(-1700, -20), IconEvent.Read(At(-1700, -20), LParam(ContextMenu)));
    }

    [TestMethod]
    public void HoveringAndTheRestAreLeftAlone()
    {
        Assert.IsNull(IconEvent.Read(At(1800, 1060), LParam(MouseMove)));
        Assert.IsNull(IconEvent.Read(At(1800, 1060), LParam(PopupOpen)));
    }
}
