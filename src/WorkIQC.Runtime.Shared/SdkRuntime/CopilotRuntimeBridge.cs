using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Threading.Channels;
using WorkIQC.Runtime.Abstractions;
using WorkIQC.Runtime.Abstractions.Models;

namespace WorkIQC.Runtime.Sdk;

internal sealed class CopilotRuntimeBridge : ICopilotRuntimeBridge
{
    private readonly ICopilotSdkClientFactory _clientFactory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, WorkspaceHost> _workspaceHosts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RuntimeSessionHandle> _sessions = new(StringComparer.Ordinal);

    public static CopilotRuntimeBridge Shared { get; } = new(new GitHubCopilotSdkClientFactory());

    internal CopilotRuntimeBridge(ICopilotSdkClientFactory clientFactory)
    {
        _clientFactory = clientFactory;
    }

    public async Task<string> CreateSessionAsync(SessionConfiguration config, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.Validate();

        try
        {
            var host = await GetOrCreateWorkspaceHostAsync(config.WorkspacePath, cancellationToken).ConfigureAwait(false);
            var sdkSession = await host.Client.CreateSessionAsync(config, cancellationToken).ConfigureAwait(false);

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var handle = RegisterSession(host, sdkSession, WorkIQTextUtilities.NormalizeModelId(config.ModelId));
                WriteDiagnostic("session.create", $"Created runtime session '{handle.SessionId}' for workspace '{config.WorkspacePath}' using model '{handle.State.ModelId ?? "<default>"}'.");
                return handle.SessionId;
            }
            finally
            {
                _gate.Release();
            }
        }
        catch (RuntimeException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new RuntimeException(
                "Copilot session creation failed before WorkIQC could establish a live session.",
                exception,
                errorCode: "runtime.session.create.failed");
        }
    }

    public async Task<bool> ResumeSessionAsync(string sessionId, string? modelId = null, CancellationToken cancellationToken = default)
    {
        ValidateSessionId(sessionId);
        var normalizedModelId = WorkIQTextUtilities.NormalizeModelId(modelId);

        if (_sessions.TryGetValue(sessionId, out var activeHandle))
        {
            if (string.Equals(activeHandle.State.ModelId, normalizedModelId, StringComparison.OrdinalIgnoreCase))
            {
                WriteDiagnostic("session.resume", $"Session '{sessionId}' is already active in-process with model '{normalizedModelId ?? "<default>"}'.");
                return true;
            }

            var resumedActiveSession = await activeHandle.WorkspaceHost.Client.ResumeSessionAsync(sessionId, normalizedModelId, cancellationToken).ConfigureAwait(false);
            RuntimeSessionHandle? priorHandle = null;

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_sessions.TryGetValue(sessionId, out priorHandle))
                {
                    priorHandle.Dispose();
                    _sessions.Remove(sessionId);
                    priorHandle.WorkspaceHost.SessionIds.TryRemove(sessionId, out _);
                }

                RegisterSession(activeHandle.WorkspaceHost, resumedActiveSession, normalizedModelId);
            }
            finally
            {
                _gate.Release();
            }

            if (priorHandle is not null)
            {
                await priorHandle.Session.DisposeAsync().ConfigureAwait(false);
            }

            WriteDiagnostic("session.resume", $"Updated active runtime session '{sessionId}' to model '{normalizedModelId ?? "<default>"}'.");
            return true;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var host in _workspaceHosts.Values)
            {
                try
                {
                    var sdkSession = await host.Client.ResumeSessionAsync(sessionId, normalizedModelId, cancellationToken).ConfigureAwait(false);
                    RegisterSession(host, sdkSession, normalizedModelId);
                    WriteDiagnostic("session.resume", $"Resumed runtime session '{sessionId}' from workspace '{host.WorkspacePath}' with model '{normalizedModelId ?? "<default>"}'.");
                    return true;
                }
                catch (RuntimeException exception) when (LooksMissing(exception))
                {
                    continue;
                }
            }

            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<CopilotModelDescriptor>> ListAvailableModelsAsync(string workspacePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workspacePath))
        {
            throw new ArgumentException("Workspace path is required.", nameof(workspacePath));
        }

        var host = await GetOrCreateWorkspaceHostAsync(workspacePath, cancellationToken).ConfigureAwait(false);
        return await host.Client.ListAvailableModelsAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<SessionState> GetSessionStateAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateSessionId(sessionId);

        if (_sessions.TryGetValue(sessionId, out var handle))
        {
            return Task.FromResult(handle.State with { });
        }

        return Task.FromResult(new SessionState
        {
            SessionId = sessionId,
            Status = SessionStatus.Failed,
            ErrorCode = "runtime.session.not-found",
            ErrorMessage = "The stored Copilot session is not active in the current app process and could not be resumed yet."
        });
    }

    public async Task DisposeSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ValidateSessionId(sessionId);

        RuntimeSessionHandle? handle;
        WorkspaceHost? workspaceHost;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_sessions.TryGetValue(sessionId, out handle))
            {
                return;
            }

            workspaceHost = handle.WorkspaceHost;
            _sessions.Remove(sessionId);
            workspaceHost.SessionIds.TryRemove(sessionId, out _);
            handle.Dispose();
        }
        finally
        {
            _gate.Release();
        }

        await handle!.Session.DisposeAsync().ConfigureAwait(false);
        WriteDiagnostic("session.dispose", $"Disposed runtime session '{sessionId}'.");

        if (workspaceHost!.SessionIds.IsEmpty)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (workspaceHost.SessionIds.IsEmpty && _workspaceHosts.Remove(workspaceHost.WorkspacePath))
                {
                    await workspaceHost.Client.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    public async Task<SendMessageResponse> SendMessageAsync(SendMessageRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();

        var sessionId = request.SessionId;
        ValidateSessionId(sessionId);

        if (!_sessions.TryGetValue(sessionId!, out var handle))
        {
            throw new SessionNotFoundException(sessionId!);
        }

        RuntimeTurn turn;
        lock (handle.SyncRoot)
        {
            if (handle.CurrentTurn is { IsCompleted: false })
            {
                throw new RuntimeException(
                    "A Copilot turn is already in flight for this conversation. Wait for the current stream to settle before sending another prompt.",
                    errorCode: "runtime.message.in-flight");
            }

            turn = new RuntimeTurn();
            handle.CurrentTurn = turn;
            handle.State = handle.State with
            {
                Status = SessionStatus.Processing,
                ErrorCode = null,
                ErrorMessage = null,
                LastActivityAt = DateTimeOffset.UtcNow
            };
        }

        try
        {
            var messageId = await handle.Session.SendAsync(request.UserMessage, cancellationToken).ConfigureAwait(false);
            turn.MessageId = messageId;
            WriteDiagnostic("turn.send", $"Session '{handle.SessionId}' accepted prompt and returned message '{messageId}'.");

            return new SendMessageResponse
            {
                SessionId = handle.SessionId,
                MessageId = messageId
            };
        }
        catch (Exception exception)
        {
            var runtimeException = exception as RuntimeException
                ?? new RuntimeException(
                    "Copilot accepted the session but could not accept the user turn.",
                    exception,
                    errorCode: "runtime.message.send.failed");

            lock (handle.SyncRoot)
            {
                handle.State = handle.State with
                {
                    Status = SessionStatus.Failed,
                    ErrorCode = runtimeException.ErrorCode,
                    ErrorMessage = runtimeException.Message,
                    LastActivityAt = DateTimeOffset.UtcNow
                };
            }

            turn.Complete(runtimeException);
            throw runtimeException;
        }
    }

    public async IAsyncEnumerable<StreamingDelta> StreamResponseAsync(
        string sessionId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ValidateSessionId(sessionId);

        var turn = GetCurrentTurn(sessionId, "assistant stream");
        await foreach (var delta in turn.ReadDeltasAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return delta;
        }
    }

    public async IAsyncEnumerable<ToolEvent> ObserveToolEventsAsync(
        string sessionId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ValidateSessionId(sessionId);

        var turn = GetCurrentTurn(sessionId, "tool event stream");
        await foreach (var toolEvent in turn.ReadToolEventsAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return toolEvent;
        }
    }

    private RuntimeTurn GetCurrentTurn(string sessionId, string streamName)
    {
        if (!_sessions.TryGetValue(sessionId, out var handle))
        {
            throw new SessionNotFoundException(sessionId);
        }

        lock (handle.SyncRoot)
        {
            if (handle.CurrentTurn is null)
            {
                throw new RuntimeException(
                    $"No {streamName} is available for session '{sessionId}' because no message has been sent in this app process yet.",
                    errorCode: "runtime.message.no-active-turn");
            }

            return handle.CurrentTurn;
        }
    }

    private async Task<WorkspaceHost> GetOrCreateWorkspaceHostAsync(string workspacePath, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_workspaceHosts.TryGetValue(workspacePath, out var existingHost))
            {
                return existingHost;
            }

            var client = _clientFactory.Create(workspacePath);
            await client.StartAsync(cancellationToken).ConfigureAwait(false);
            var host = new WorkspaceHost(workspacePath, client);
            _workspaceHosts.Add(workspacePath, host);
            return host;
        }
        finally
        {
            _gate.Release();
        }
    }

    private RuntimeSessionHandle RegisterSession(WorkspaceHost host, ICopilotSdkSession sdkSession, string? modelId)
    {
        if (_sessions.TryGetValue(sdkSession.SessionId, out var existing))
        {
            existing.Dispose();
            _sessions.Remove(sdkSession.SessionId);
            host.SessionIds.TryRemove(sdkSession.SessionId, out _);
        }

        var handle = new RuntimeSessionHandle(host, sdkSession);
        handle.Subscription = sdkSession.Subscribe(evt => OnSessionEvent(handle, evt));
        handle.State = new SessionState
        {
            SessionId = sdkSession.SessionId,
            Status = SessionStatus.Ready,
            ModelId = modelId,
            CreatedAt = DateTimeOffset.UtcNow,
            LastActivityAt = DateTimeOffset.UtcNow
        };

        _sessions[sdkSession.SessionId] = handle;
        host.SessionIds.TryAdd(sdkSession.SessionId, 0);
        return handle;
    }

    private static void OnSessionEvent(RuntimeSessionHandle handle, CopilotSessionEvent sessionEvent)
    {
        var timestamp = DateTimeOffset.UtcNow;
        RuntimeTurn? turn;

        lock (handle.SyncRoot)
        {
            turn = handle.CurrentTurn;
            handle.State = handle.State with { LastActivityAt = timestamp };
        }

        switch (sessionEvent)
        {
            case AssistantMessageDeltaRuntimeEvent delta when turn is not null:
                turn.TryWriteAssistantDelta(delta.Content);
                break;

            case AssistantMessageCompletedRuntimeEvent completed when turn is not null:
                turn.FinalAssistantContent = completed.Content;
                WriteDiagnostic("turn.complete", $"Session '{handle.SessionId}' produced a completed assistant message ({completed.Content.Length} chars).");
                break;

            case ToolStartedRuntimeEvent toolStarted when turn is not null:
                turn.HasToolActivity = true;
                turn.ResetForToolExecution();
                WriteDiagnostic("tool.started", $"Session '{handle.SessionId}' tool '{toolStarted.ToolName}' started. Pre-tool buffers cleared.");
                turn.TryWriteToolEvent(CreateToolEvent(toolStarted.ToolName, ToolEventType.Started, toolStarted.StatusMessage, null, timestamp));
                break;

            case ToolProgressRuntimeEvent toolProgress when turn is not null:
                turn.HasToolActivity = true;
                turn.TryWriteToolEvent(CreateToolEvent(toolProgress.ToolName, ToolEventType.Progress, toolProgress.StatusMessage, null, timestamp));
                break;

            case ToolCompletedRuntimeEvent toolCompleted when turn is not null:
                turn.HasToolActivity = true;
                WriteDiagnostic("tool.completed", $"Session '{handle.SessionId}' tool '{toolCompleted.ToolName}' completed.");
                turn.TryWriteToolEvent(CreateToolEvent(toolCompleted.ToolName, ToolEventType.Completed, toolCompleted.StatusMessage, null, timestamp));
                break;

            case ToolFailedRuntimeEvent toolFailed when turn is not null:
                turn.HasToolActivity = true;
                WriteDiagnostic("tool.failed", $"Session '{handle.SessionId}' tool '{toolFailed.ToolName}' failed: {toolFailed.ErrorMessage}");
                turn.TryWriteToolEvent(CreateToolEvent(toolFailed.ToolName, ToolEventType.Failed, null, toolFailed.ErrorMessage, timestamp));
                break;

            case SessionErrorRuntimeEvent error:
                var runtimeException = new RuntimeException(error.Message, errorCode: "runtime.session.event-error");
                lock (handle.SyncRoot)
                {
                    handle.State = handle.State with
                    {
                        Status = SessionStatus.Failed,
                        ErrorCode = runtimeException.ErrorCode,
                        ErrorMessage = runtimeException.Message,
                        LastActivityAt = timestamp
                    };
                }

                turn?.Complete(runtimeException);
                break;

            case SessionIdleRuntimeEvent:
                lock (handle.SyncRoot)
                {
                    handle.State = handle.State with
                    {
                        Status = SessionStatus.Ready,
                        ErrorCode = null,
                        ErrorMessage = null,
                        LastActivityAt = timestamp
                    };
                }

                turn?.Complete();
                break;
        }
    }

    private static ToolEvent CreateToolEvent(
        string toolName,
        ToolEventType eventType,
        string? statusMessage,
        string? errorMessage,
        DateTimeOffset timestamp)
        => new()
        {
            ToolName = toolName,
            EventType = eventType,
            StatusMessage = statusMessage,
            ErrorMessage = errorMessage,
            Timestamp = timestamp
        };

    private static void ValidateSessionId(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("Session identifier is required.", nameof(sessionId));
        }
    }

    private static bool LooksMissing(RuntimeException exception)
        => string.Equals(exception.ErrorCode, "runtime.session.not-found", StringComparison.Ordinal)
            || exception.Message.Contains("not found", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("No session", StringComparison.OrdinalIgnoreCase);

    private sealed class WorkspaceHost
    {
        public WorkspaceHost(string workspacePath, ICopilotSdkClient client)
        {
            WorkspacePath = workspacePath;
            Client = client;
        }

        public string WorkspacePath { get; }

        public ICopilotSdkClient Client { get; }

        public ConcurrentDictionary<string, byte> SessionIds { get; } = new(StringComparer.Ordinal);
    }

    private sealed class RuntimeSessionHandle
    {
        public RuntimeSessionHandle(WorkspaceHost workspaceHost, ICopilotSdkSession session)
        {
            WorkspaceHost = workspaceHost;
            Session = session;
            State = new SessionState
            {
                SessionId = session.SessionId,
                Status = SessionStatus.Initializing
            };
        }

        public object SyncRoot { get; } = new();

        public WorkspaceHost WorkspaceHost { get; }

        public ICopilotSdkSession Session { get; }

        public IDisposable? Subscription { get; set; }

        public RuntimeTurn? CurrentTurn { get; set; }

        public SessionState State { get; set; }

        public string SessionId => Session.SessionId;

        public void Dispose() => Subscription?.Dispose();
    }

    private sealed class RuntimeTurn
    {
        private readonly Channel<StreamingDelta> _deltas = Channel.CreateUnbounded<StreamingDelta>();
        private readonly Channel<ToolEvent> _toolEvents = Channel.CreateUnbounded<ToolEvent>();
        private readonly StringBuilder _bufferedAssistantContent = new();
        private int _completed;

        public string? MessageId { get; set; }

        public bool HasToolActivity { get; set; }

        public string? FinalAssistantContent { get; set; }

        public bool IsCompleted => Volatile.Read(ref _completed) != 0;

        public void ResetForToolExecution()
        {
            if (_bufferedAssistantContent.Length > 0)
            {
                var thinkingText = _bufferedAssistantContent.ToString().Trim();
                if (thinkingText.Length > 0)
                {
                    TryWriteToolEvent(new ToolEvent
                    {
                        ToolName = "thinking",
                        EventType = ToolEventType.Progress,
                        StatusMessage = thinkingText,
                        Timestamp = DateTimeOffset.UtcNow
                    });
                }
            }

            _bufferedAssistantContent.Clear();
            FinalAssistantContent = null;
        }

        public bool TryWriteAssistantDelta(string? content, bool isComplete = false)
        {
            if (string.IsNullOrEmpty(content))
            {
                return false;
            }

            _bufferedAssistantContent.Append(content);
            return true;
        }

        public bool TryWriteToolEvent(ToolEvent toolEvent) => _toolEvents.Writer.TryWrite(toolEvent);

        public async IAsyncEnumerable<StreamingDelta> ReadDeltasAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var delta in _deltas.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return delta;
            }
        }

        public async IAsyncEnumerable<ToolEvent> ReadToolEventsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var toolEvent in _toolEvents.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return toolEvent;
            }
        }

        public void Complete(Exception? error = null)
        {
            if (Interlocked.Exchange(ref _completed, 1) != 0)
            {
                return;
            }

            TryWriteCompletionDelta();

            _deltas.Writer.TryComplete(error);
            _toolEvents.Writer.TryComplete(error);
        }

        private void TryWriteCompletionDelta()
        {
            var completedMessage = FinalAssistantContent?.Trim();
            if (string.IsNullOrWhiteSpace(completedMessage))
            {
                if (_bufferedAssistantContent.Length == 0)
                {
                    return;
                }
                completedMessage = _bufferedAssistantContent.ToString().Trim();
            }

            completedMessage = StripToolCallMarkup(completedMessage);

            if (string.IsNullOrWhiteSpace(completedMessage))
            {
                return;
            }

            _deltas.Writer.TryWrite(new StreamingDelta
            {
                Content = completedMessage,
                IsComplete = true
            });
        }

        private static string StripToolCallMarkup(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return content;
            }

            var cleaned = System.Text.RegularExpressions.Regex.Replace(
                content,
                @"<tool_call>.*?</tool_call>",
                string.Empty,
                System.Text.RegularExpressions.RegexOptions.Singleline);

            return cleaned.Trim();
        }
    }

    private static void WriteDiagnostic(string stage, string message)
        => Trace.WriteLine($"[{DateTimeOffset.Now:O}] [WorkIQC.RuntimeBridge] [{stage}] {message}");

}
