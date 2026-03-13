using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace WorkIQC.Runtime.Tests;

internal static class TestHelpers
{
    public static async Task<TException> ThrowsAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException exception)
        {
            return exception;
        }

        throw new AssertFailedException($"Expected exception of type {typeof(TException).Name}.");
    }
}
