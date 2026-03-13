using System.Runtime.CompilerServices;
using WorkIQC.Runtime.Abstractions;
using WorkIQC.Runtime.Abstractions.Models;
using WorkIQC.Runtime.Sdk;

namespace WorkIQC.Runtime;

public sealed class MessageOrchestrator : IMessageOrchestrator
{
    private readonly ICopilotRuntimeBridge _runtimeBridge;

    public MessageOrchestrator()
        : this(CopilotRuntimeBridge.Shared)
    {
    }

    internal MessageOrchestrator(ICopilotRuntimeBridge runtimeBridge)
    {
        _runtimeBridge = runtimeBridge;
    }

    public Task<SendMessageResponse> SendMessageAsync(SendMessageRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();
        return _runtimeBridge.SendMessageAsync(request, cancellationToken);
    }

    public IAsyncEnumerable<StreamingDelta> StreamResponseAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ValidateSessionId(sessionId);
        cancellationToken.ThrowIfCancellationRequested();
        return _runtimeBridge.StreamResponseAsync(sessionId, cancellationToken);
    }

    public IAsyncEnumerable<ToolEvent> ObserveToolEventsAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ValidateSessionId(sessionId);
        cancellationToken.ThrowIfCancellationRequested();
        return _runtimeBridge.ObserveToolEventsAsync(sessionId, cancellationToken);
    }

    private static void ValidateSessionId(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("Session identifier is required.", nameof(sessionId));
        }
    }
}
