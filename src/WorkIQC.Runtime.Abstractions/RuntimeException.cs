using System;

namespace WorkIQC.Runtime.Abstractions;

public class RuntimeException : Exception
{
    public RuntimeException(string message, string? errorCode = null)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public RuntimeException(string message, Exception innerException, string? errorCode = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }

    public string? ErrorCode { get; }
}

public class SessionNotFoundException : RuntimeException
{
    public SessionNotFoundException(string sessionId)
        : base($"Session '{sessionId}' not found or expired.", errorCode: "runtime.session.not-found")
    {
        SessionId = sessionId;
    }

    public string SessionId { get; }
}

public class BootstrapException : RuntimeException
{
    public BootstrapException(string message, string? errorCode = null)
        : base(message, errorCode)
    {
    }

    public BootstrapException(string message, Exception innerException, string? errorCode = null)
        : base(message, innerException, errorCode)
    {
    }
}

public sealed class UnsupportedRuntimeActionException : RuntimeException
{
    public UnsupportedRuntimeActionException(
        string actionName,
        string capabilityName,
        string message,
        string? resolution = null,
        string? errorCode = null)
        : base(message, errorCode ?? "runtime.unsupported-action")
    {
        ActionName = actionName;
        CapabilityName = capabilityName;
        Resolution = resolution;
    }

    public string ActionName { get; }

    public string CapabilityName { get; }

    public string? Resolution { get; }
}
