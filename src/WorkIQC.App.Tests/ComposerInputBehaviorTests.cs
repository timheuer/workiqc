using Windows.System;
using Windows.UI.Core;
using WorkIQC.App.Views;

namespace WorkIQC.App.Tests;

[TestClass]
public sealed class ComposerInputBehaviorTests
{
    [TestMethod]
    public void ShouldSendOnKeyDown_WhenEnterIsPressedWithoutShift()
    {
        Assert.IsTrue(ComposerInputBehavior.ShouldSendOnKeyDown(VirtualKey.Enter, CoreVirtualKeyStates.None));
    }

    [TestMethod]
    public void ShouldSendOnKeyDown_WhenShiftEnterIsPressed_ReturnsFalse()
    {
        Assert.IsFalse(ComposerInputBehavior.ShouldSendOnKeyDown(VirtualKey.Enter, CoreVirtualKeyStates.Down));
    }

    [TestMethod]
    public void ShouldSendOnKeyDown_WhenAnotherKeyIsPressed_ReturnsFalse()
    {
        Assert.IsFalse(ComposerInputBehavior.ShouldSendOnKeyDown(VirtualKey.A, CoreVirtualKeyStates.None));
    }
}
