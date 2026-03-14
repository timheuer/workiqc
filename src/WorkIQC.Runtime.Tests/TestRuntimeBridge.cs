using WorkIQC.Runtime.Abstractions.Models;
using WorkIQC.Runtime.Sdk;

namespace WorkIQC.Runtime.Tests;

internal sealed class TestRuntimeBridge : ICopilotRuntimeBridge
{
    public Func<SessionConfiguration, CancellationToken, Task<string>> OnCreateSessionAsync { get; set; } =
        (_, _) => Task.FromResult("session-test");

    public Func<string, string?, CancellationToken, Task<bool>> OnResumeSessionAsync { get; set; } =
        (_, _, _) => Task.FromResult(true);

    public Func<string, CancellationToken, Task<IReadOnlyList<CopilotModelDescriptor>>> OnListAvailableModelsAsync { get; set; } =
        (_, _) => Task.FromResult<IReadOnlyList<CopilotModelDescriptor>>(Array.Empty<CopilotModelDescriptor>());

    public Func<string, CancellationToken, Task<SessionState>> OnGetSessionStateAsync { get; set; } =
        (sessionId, _) => Task.FromResult(new SessionState
        {
            SessionId = sessionId,
            Status = SessionStatus.Ready,
            CreatedAt = DateTimeOffset.UtcNow
        });

    public Func<string, CancellationToken, Task> OnDisposeSessionAsync { get; set; } =
        (_, _) => Task.CompletedTask;

    public Func<SendMessageRequest, CancellationToken, Task<SendMessageResponse>> OnSendMessageAsync { get; set; } =
        (request, _) => Task.FromResult(new SendMessageResponse
        {
            SessionId = request.SessionId ?? "session-test",
            MessageId = "message-test"
        });

    public Func<string, CancellationToken, IAsyncEnumerable<StreamingDelta>> OnStreamResponseAsync { get; set; } =
        (_, _) => EmptyDeltas();

    public Func<string, CancellationToken, IAsyncEnumerable<ToolEvent>> OnObserveToolEventsAsync { get; set; } =
        (_, _) => EmptyToolEvents();

    public int ResumeSessionCallCount { get; private set; }

    public int DisposeSessionCallCount { get; private set; }

    public Task<string> CreateSessionAsync(SessionConfiguration config, CancellationToken cancellationToken = default)
        => OnCreateSessionAsync(config, cancellationToken);

    public Task<bool> ResumeSessionAsync(string sessionId, string? modelId = null, CancellationToken cancellationToken = default)
    {
        ResumeSessionCallCount++;
        return OnResumeSessionAsync(sessionId, modelId, cancellationToken);
    }

    public Task<IReadOnlyList<CopilotModelDescriptor>> ListAvailableModelsAsync(string workspacePath, CancellationToken cancellationToken = default)
        => OnListAvailableModelsAsync(workspacePath, cancellationToken);

    public Task<SessionState> GetSessionStateAsync(string sessionId, CancellationToken cancellationToken = default)
        => OnGetSessionStateAsync(sessionId, cancellationToken);

    public Task DisposeSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        DisposeSessionCallCount++;
        return OnDisposeSessionAsync(sessionId, cancellationToken);
    }

    public Task<SendMessageResponse> SendMessageAsync(SendMessageRequest request, CancellationToken cancellationToken = default)
        => OnSendMessageAsync(request, cancellationToken);

    public IAsyncEnumerable<StreamingDelta> StreamResponseAsync(string sessionId, CancellationToken cancellationToken = default)
        => OnStreamResponseAsync(sessionId, cancellationToken);

    public IAsyncEnumerable<ToolEvent> ObserveToolEventsAsync(string sessionId, CancellationToken cancellationToken = default)
        => OnObserveToolEventsAsync(sessionId, cancellationToken);

    private static async IAsyncEnumerable<StreamingDelta> EmptyDeltas()
    {
        await Task.CompletedTask;
        yield break;
    }

    private static async IAsyncEnumerable<ToolEvent> EmptyToolEvents()
    {
        await Task.CompletedTask;
        yield break;
    }
}
