using WorkIQC.Runtime.Abstractions.Models;

namespace WorkIQC.Runtime.Sdk;

internal interface ICopilotRuntimeBridge
{
    Task<string> CreateSessionAsync(SessionConfiguration config, CancellationToken cancellationToken = default);
    Task<bool> ResumeSessionAsync(string sessionId, string? modelId = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CopilotModelDescriptor>> ListAvailableModelsAsync(string workspacePath, CancellationToken cancellationToken = default);
    Task<SessionState> GetSessionStateAsync(string sessionId, CancellationToken cancellationToken = default);
    Task DisposeSessionAsync(string sessionId, CancellationToken cancellationToken = default);
    Task<SendMessageResponse> SendMessageAsync(SendMessageRequest request, CancellationToken cancellationToken = default);
    IAsyncEnumerable<StreamingDelta> StreamResponseAsync(string sessionId, CancellationToken cancellationToken = default);
    IAsyncEnumerable<ToolEvent> ObserveToolEventsAsync(string sessionId, CancellationToken cancellationToken = default);
}

internal interface ICopilotSdkClientFactory
{
    ICopilotSdkClient Create(string workspacePath);
}

internal interface ICopilotSdkClient : IAsyncDisposable
{
    Task StartAsync(CancellationToken cancellationToken = default);
    Task<ICopilotSdkSession> CreateSessionAsync(SessionConfiguration config, CancellationToken cancellationToken = default);
    Task<ICopilotSdkSession> ResumeSessionAsync(string sessionId, string? modelId = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CopilotModelDescriptor>> ListAvailableModelsAsync(CancellationToken cancellationToken = default);
}

internal interface ICopilotSdkSession : IAsyncDisposable
{
    string SessionId { get; }
    IDisposable Subscribe(Action<CopilotSessionEvent> handler);
    Task<string> SendAsync(string prompt, CancellationToken cancellationToken = default);
}

internal abstract record CopilotSessionEvent;

internal sealed record AssistantMessageDeltaRuntimeEvent(string Content) : CopilotSessionEvent;

internal sealed record AssistantMessageCompletedRuntimeEvent(string Content) : CopilotSessionEvent;

internal sealed record ToolStartedRuntimeEvent(string ToolName, string? StatusMessage = null) : CopilotSessionEvent;

internal sealed record ToolProgressRuntimeEvent(string ToolName, string? StatusMessage = null) : CopilotSessionEvent;

internal sealed record ToolCompletedRuntimeEvent(string ToolName, string? StatusMessage = null) : CopilotSessionEvent;

internal sealed record ToolFailedRuntimeEvent(string ToolName, string ErrorMessage) : CopilotSessionEvent;

internal sealed record SessionIdleRuntimeEvent : CopilotSessionEvent;

internal sealed record SessionErrorRuntimeEvent(string Message) : CopilotSessionEvent;
