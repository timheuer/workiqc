using WorkIQC.Runtime.Abstractions.Models;

namespace WorkIQC.Runtime.Tests;

[TestClass]
public sealed class MessageOrchestratorTests
{
    private readonly TestRuntimeBridge _bridge = new();
    private readonly MessageOrchestrator _orchestrator;

    public MessageOrchestratorTests()
    {
        _orchestrator = new MessageOrchestrator(_bridge);
    }

    [TestMethod]
    public async Task SendMessageAsync_RejectsInvalidRequest()
    {
        var exception = await TestHelpers.ThrowsAsync<ArgumentException>(() =>
            _orchestrator.SendMessageAsync(new SendMessageRequest
            {
                ConversationId = "conversation-1",
                UserMessage = "   "
            }));

        Assert.AreEqual("UserMessage", exception.ParamName);
    }

    [TestMethod]
    public async Task SendMessageAsync_DelegatesToRuntimeBridge()
    {
        SendMessageRequest? capturedRequest = null;
        _bridge.OnSendMessageAsync = (request, _) =>
        {
            capturedRequest = request;
            return Task.FromResult(new SendMessageResponse
            {
                SessionId = request.SessionId!,
                MessageId = "message-42"
            });
        };

        var response = await _orchestrator.SendMessageAsync(new SendMessageRequest
        {
            ConversationId = "conversation-1",
            SessionId = "session-1",
            UserMessage = "hello"
        });

        Assert.AreEqual("message-42", response.MessageId);
        Assert.AreEqual("session-1", capturedRequest!.SessionId);
    }

    [TestMethod]
    public async Task StreamResponseAsync_YieldsRuntimeBridgeDeltas()
    {
        _bridge.OnStreamResponseAsync = (_, _) => Stream("hello ", "world");

        var response = await ReadAllAsync(_orchestrator.StreamResponseAsync("session-1"));

        Assert.AreEqual("hello world", response);
    }

    [TestMethod]
    public async Task ObserveToolEventsAsync_YieldsRuntimeBridgeEvents()
    {
        _bridge.OnObserveToolEventsAsync = (_, _) => Observe(
            new ToolEvent
            {
                ToolName = "workiq",
                EventType = ToolEventType.Started,
                StatusMessage = "workiq started"
            },
            new ToolEvent
            {
                ToolName = "workiq",
                EventType = ToolEventType.Completed,
                StatusMessage = "workiq completed"
            });

        var events = new List<ToolEvent>();
        await foreach (var toolEvent in _orchestrator.ObserveToolEventsAsync("session-1"))
        {
            events.Add(toolEvent);
        }

        Assert.HasCount(2, events);
        Assert.AreEqual(ToolEventType.Completed, events[^1].EventType);
    }

    private static async Task<string> ReadAllAsync(IAsyncEnumerable<StreamingDelta> stream)
    {
        var builder = new System.Text.StringBuilder();
        await foreach (var delta in stream)
        {
            builder.Append(delta.Content);
        }

        return builder.ToString();
    }

    private static async IAsyncEnumerable<StreamingDelta> Stream(params string[] chunks)
    {
        foreach (var chunk in chunks)
        {
            yield return new StreamingDelta { Content = chunk, IsComplete = false };
            await Task.Yield();
        }
    }

    private static async IAsyncEnumerable<ToolEvent> Observe(params ToolEvent[] toolEvents)
    {
        foreach (var toolEvent in toolEvents)
        {
            yield return toolEvent;
            await Task.Yield();
        }
    }
}
