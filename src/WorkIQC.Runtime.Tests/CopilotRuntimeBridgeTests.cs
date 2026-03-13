using WorkIQC.Runtime.Abstractions.Models;
using WorkIQC.Runtime.Sdk;

namespace WorkIQC.Runtime.Tests;

[TestClass]
public sealed class CopilotRuntimeBridgeTests
{
    [TestMethod]
    public async Task StreamResponseAsync_WhenToolBackedTurnCompletes_PrefersFinalAssistantMessage()
    {
        var session = new FakeCopilotSdkSession("session-1");
        var bridge = CreateBridge(session);
        var sessionId = await bridge.CreateSessionAsync(CreateConfig());
        await bridge.SendMessageAsync(new SendMessageRequest
        {
            ConversationId = "conversation-1",
            SessionId = sessionId,
            UserMessage = "What changed?"
        });

        var responseTask = ReadAllAsync(bridge.StreamResponseAsync(sessionId));

        session.Emit(new AssistantMessageDeltaRuntimeEvent("Checking local WorkIQ data first."));
        session.Emit(new ToolStartedRuntimeEvent("workiq", "Querying WorkIQ…"));
        session.Emit(new AssistantMessageCompletedRuntimeEvent("Here is the actual WorkIQ answer."));
        session.Emit(new SessionIdleRuntimeEvent());

        var response = await responseTask;

        Assert.AreEqual("Here is the actual WorkIQ answer.", response);
    }

    [TestMethod]
    public async Task StreamResponseAsync_WhenCompletedAssistantMessageIsMissing_UsesBufferedDeltas()
    {
        var session = new FakeCopilotSdkSession("session-1");
        var bridge = CreateBridge(session);
        var sessionId = await bridge.CreateSessionAsync(CreateConfig());
        await bridge.SendMessageAsync(new SendMessageRequest
        {
            ConversationId = "conversation-1",
            SessionId = sessionId,
            UserMessage = "Summarize it."
        });

        var responseTask = ReadAllAsync(bridge.StreamResponseAsync(sessionId));

        session.Emit(new AssistantMessageDeltaRuntimeEvent("Buffered "));
        session.Emit(new AssistantMessageDeltaRuntimeEvent("assistant answer."));
        session.Emit(new SessionIdleRuntimeEvent());

        var response = await responseTask;

        Assert.AreEqual("Buffered assistant answer.", response);
    }

    [TestMethod]
    public async Task StreamResponseAsync_WhenToolCallXmlIsStreamedBeforeToolExecution_StripsMarkupAndShowsPostToolAnswer()
    {
        var session = new FakeCopilotSdkSession("session-1");
        var bridge = CreateBridge(session);
        var sessionId = await bridge.CreateSessionAsync(CreateConfig());
        await bridge.SendMessageAsync(new SendMessageRequest
        {
            ConversationId = "conversation-1",
            SessionId = sessionId,
            UserMessage = "Who are my direct reports?"
        });

        var responseTask = ReadAllAsync(bridge.StreamResponseAsync(sessionId));

        session.Emit(new AssistantMessageDeltaRuntimeEvent("<tool_call> <tool_name>ask_work_iq</tool_name> <tool_input>{\"question\": \"Who are my direct reports?\"}</tool_input> </tool_call>"));
        session.Emit(new AssistantMessageCompletedRuntimeEvent("<tool_call> <tool_name>ask_work_iq</tool_name> <tool_input>{\"question\": \"Who are my direct reports?\"}</tool_input> </tool_call>"));
        session.Emit(new ToolStartedRuntimeEvent("workiq", "Querying WorkIQ…"));
        session.Emit(new ToolCompletedRuntimeEvent("workiq", "WorkIQ returned data."));
        session.Emit(new AssistantMessageDeltaRuntimeEvent("Your direct reports are Alice, Bob, and Carol."));
        session.Emit(new AssistantMessageCompletedRuntimeEvent("Your direct reports are Alice, Bob, and Carol."));
        session.Emit(new SessionIdleRuntimeEvent());

        var response = await responseTask;

        Assert.AreEqual("Your direct reports are Alice, Bob, and Carol.", response);
    }

    [TestMethod]
    public async Task StreamResponseAsync_WhenToolExecutesButNoPostToolAnswer_ReturnsEmptyInsteadOfToolMarkup()
    {
        var session = new FakeCopilotSdkSession("session-1");
        var bridge = CreateBridge(session);
        var sessionId = await bridge.CreateSessionAsync(CreateConfig());
        await bridge.SendMessageAsync(new SendMessageRequest
        {
            ConversationId = "conversation-1",
            SessionId = sessionId,
            UserMessage = "Who are my direct reports?"
        });

        var responseTask = ReadAllAsync(bridge.StreamResponseAsync(sessionId));

        session.Emit(new AssistantMessageDeltaRuntimeEvent("<tool_call> <tool_name>ask_work_iq</tool_name> </tool_call>"));
        session.Emit(new ToolStartedRuntimeEvent("workiq", "Querying WorkIQ…"));
        session.Emit(new ToolCompletedRuntimeEvent("workiq", "WorkIQ returned data."));
        session.Emit(new SessionIdleRuntimeEvent());

        var response = await responseTask;

        Assert.AreEqual(string.Empty, response);
    }

    private static CopilotRuntimeBridge CreateBridge(FakeCopilotSdkSession session)
        => new(new FakeCopilotSdkClientFactory(session));

    private static SessionConfiguration CreateConfig()
        => new()
        {
            WorkspacePath = @"D:\temp\workspace",
            McpConfigPath = @"D:\temp\workspace\.copilot\mcp-config.json"
        };

    private static async Task<string> ReadAllAsync(IAsyncEnumerable<StreamingDelta> stream)
    {
        var builder = new System.Text.StringBuilder();
        await foreach (var delta in stream)
        {
            builder.Append(delta.Content);
        }

        return builder.ToString();
    }

    private sealed class FakeCopilotSdkClientFactory(FakeCopilotSdkSession session) : ICopilotSdkClientFactory
    {
        public ICopilotSdkClient Create(string workspacePath) => new FakeCopilotSdkClient(session);
    }

    private sealed class FakeCopilotSdkClient(FakeCopilotSdkSession session) : ICopilotSdkClient
    {
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<ICopilotSdkSession> CreateSessionAsync(SessionConfiguration config, CancellationToken cancellationToken = default)
            => Task.FromResult<ICopilotSdkSession>(session);

        public Task<ICopilotSdkSession> ResumeSessionAsync(string sessionId, CancellationToken cancellationToken = default)
            => Task.FromResult<ICopilotSdkSession>(session);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeCopilotSdkSession : ICopilotSdkSession
    {
        private readonly List<Action<CopilotSessionEvent>> _handlers = [];

        public FakeCopilotSdkSession(string sessionId)
        {
            SessionId = sessionId;
        }

        public string SessionId { get; }

        public IDisposable Subscribe(Action<CopilotSessionEvent> handler)
        {
            _handlers.Add(handler);
            return new Subscription(_handlers, handler);
        }

        public Task<string> SendAsync(string prompt, CancellationToken cancellationToken = default)
            => Task.FromResult("message-1");

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void Emit(CopilotSessionEvent sessionEvent)
        {
            foreach (var handler in _handlers.ToArray())
            {
                handler(sessionEvent);
            }
        }

        private sealed class Subscription(List<Action<CopilotSessionEvent>> handlers, Action<CopilotSessionEvent> handler) : IDisposable
        {
            public void Dispose() => handlers.Remove(handler);
        }
    }
}
