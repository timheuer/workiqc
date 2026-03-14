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

    public Task<bool> ResumeSessionAsync(string sessionId, string? modelId = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateSessionId(sessionId, nameof(sessionId));

        return _runtimeBridge.ResumeSessionAsync(sessionId, modelId, cancellationToken);
    }

    public Task<IReadOnlyList<CopilotModelDescriptor>> ListAvailableModelsAsync(string workspacePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(workspacePath))
        {
            throw new ArgumentException("Workspace path is required.", nameof(workspacePath));
        }

        return _runtimeBridge.ListAvailableModelsAsync(workspacePath, cancellationToken);
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
