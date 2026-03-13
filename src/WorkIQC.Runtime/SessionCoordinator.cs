using WorkIQC.Runtime.Abstractions;
using WorkIQC.Runtime.Abstractions.Models;
using WorkIQC.Runtime.Sdk;

namespace WorkIQC.Runtime;

public sealed class SessionCoordinator : ISessionCoordinator
{
    private readonly ICopilotRuntimeBridge _runtimeBridge;

    public SessionCoordinator()
        : this(CopilotRuntimeBridge.Shared)
    {
    }

    internal SessionCoordinator(ICopilotRuntimeBridge runtimeBridge)
    {
        _runtimeBridge = runtimeBridge;
    }

    public Task<string> CreateSessionAsync(SessionConfiguration config, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(config);
        config.Validate();
        return _runtimeBridge.CreateSessionAsync(config, cancellationToken);
    }

    public Task<bool> ResumeSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateSessionId(sessionId, nameof(sessionId));

        return _runtimeBridge.ResumeSessionAsync(sessionId, cancellationToken);
    }

    public Task<SessionState> GetSessionStateAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateSessionId(sessionId, nameof(sessionId));

        return _runtimeBridge.GetSessionStateAsync(sessionId, cancellationToken);
    }

    public Task DisposeSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateSessionId(sessionId, nameof(sessionId));

        return _runtimeBridge.DisposeSessionAsync(sessionId, cancellationToken);
    }

    private static void ValidateSessionId(string sessionId, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("Session identifier is required.", parameterName);
        }
    }
}
