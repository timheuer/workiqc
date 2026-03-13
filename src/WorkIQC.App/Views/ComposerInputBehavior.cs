using Windows.System;
using Windows.UI.Core;

namespace WorkIQC.App.Views;

internal static class ComposerInputBehavior
{
    public static bool ShouldSendOnKeyDown(VirtualKey key, CoreVirtualKeyStates shiftState)
        => key == VirtualKey.Enter && !shiftState.HasFlag(CoreVirtualKeyStates.Down);
}
