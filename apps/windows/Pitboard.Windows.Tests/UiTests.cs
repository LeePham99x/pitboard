namespace Pitboard.Windows.Tests;

[TestClass]
public sealed class UiTests
{
    [TestMethod]
    public void AGuardedCallbackWritesWhatItThrowsAndThrowsIt()
    {
        var said = new StringWriter();
        var thrown = new InvalidOperationException("The text associated with this error code could not be found.");
        thrown.Data["RestrictedDescription"] = "Cannot locate resource from 'ms-appx:///Pitboard.Views/AccountsPane.xaml'.";
        var callback = Ui.Guarded(() => throw thrown, said);

        Assert.AreSame(thrown, Assert.Throws<InvalidOperationException>(() => callback()));
        StringAssert.Contains(said.ToString(), "The text associated with this error code could not be found.");
        StringAssert.Contains(said.ToString(), "Cannot locate resource from 'ms-appx:///Pitboard.Views/AccountsPane.xaml'.");
    }

    // Where every first chance exception was written, those Pitboard catches buried the one that ended it.
    [TestMethod]
    public void AGuardedCallbackWritesNoExceptionItCaught()
    {
        var said = new StringWriter();
        var ran = false;

        Ui.Guarded(
            () =>
            {
                try
                {
                    throw new IOException("The file is held.");
                }
                catch (IOException)
                {
                    ran = true;
                }
            },
            said)();

        Assert.IsTrue(ran);
        Assert.AreEqual("", said.ToString());
    }
}
