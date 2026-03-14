using WorkIQC.Runtime.Abstractions.Models;

namespace WorkIQC.Runtime.Tests;

[TestClass]
public sealed class SessionCoordinatorTests
{
    private readonly TestRuntimeBridge _bridge = new();
    private readonly SessionCoordinator _coordinator;

    public SessionCoordinatorTests()
    {
        _coordinator = new SessionCoordinator(_bridge);
    }

    [TestMethod]
    public async Task CreateSessionAsync_RejectsInvalidConfiguration()
    {
        var exception = await TestHelpers.ThrowsAsync<ArgumentException>(() =>
            _coordinator.CreateSessionAsync(new SessionConfiguration
            {
                WorkspacePath = "relative-workspace",
                McpConfigPath = @"D:\temp\workspace\.copilot\mcp-config.json"
            }));

        Assert.AreEqual("WorkspacePath", exception.ParamName);
    }

    [TestMethod]
    public async Task CreateSessionAsync_DelegatesToRuntimeBridge()
    {
        SessionConfiguration? capturedConfig = null;
        _bridge.OnCreateSessionAsync = (config, _) =>
        {
            capturedConfig = config;
            return Task.FromResult("session-123");
        };

        var sessionId = await _coordinator.CreateSessionAsync(new SessionConfiguration
        {
            WorkspacePath = @"D:\temp\workspace",
            McpConfigPath = @"D:\temp\workspace\.copilot\mcp-config.json"
        });

        Assert.AreEqual("session-123", sessionId);
        Assert.IsNotNull(capturedConfig);
    }

    [TestMethod]
    public async Task ResumeSessionAsync_DelegatesToRuntimeBridge()
    {
        _bridge.OnResumeSessionAsync = (sessionId, modelId, _) => Task.FromResult(sessionId == "session-1" && modelId == "gpt-5");

        var resumed = await _coordinator.ResumeSessionAsync("session-1", "gpt-5");

        Assert.IsTrue(resumed);
        Assert.AreEqual(1, _bridge.ResumeSessionCallCount);
    }

    [TestMethod]
    public async Task ListAvailableModelsAsync_DelegatesToRuntimeBridge()
    {
        _bridge.OnListAvailableModelsAsync = (workspacePath, _) => Task.FromResult<IReadOnlyList<CopilotModelDescriptor>>(
        [
            new("gpt-5", "GPT-5")
        ]);

        var models = await _coordinator.ListAvailableModelsAsync(@"D:\temp\workspace");

        Assert.HasCount(1, models);
        Assert.AreEqual("gpt-5", models[0].Id);
    }

    [TestMethod]
    public async Task GetSessionStateAsync_ReturnsRuntimeBridgeState()
    {
        _bridge.OnGetSessionStateAsync = (sessionId, _) => Task.FromResult(new SessionState
        {
            SessionId = sessionId,
            Status = SessionStatus.Ready,
            CreatedAt = DateTimeOffset.UtcNow
        });

        var state = await _coordinator.GetSessionStateAsync("session-1");

        Assert.AreEqual(SessionStatus.Ready, state.Status);
        Assert.AreEqual("session-1", state.SessionId);
    }

    [TestMethod]
    public async Task DisposeSessionAsync_DelegatesToRuntimeBridge()
    {
        await _coordinator.DisposeSessionAsync("session-1");

        Assert.AreEqual(1, _bridge.DisposeSessionCallCount);
    }
}
