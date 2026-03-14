using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WorkIQC.App.Models;
using WorkIQC.App.Services;
using WorkIQC.Persistence;
using WorkIQC.Persistence.Models;
using WorkIQC.Persistence.Services;
using WorkIQC.Runtime.Abstractions;
using WorkIQC.Runtime.Abstractions.Models;

namespace WorkIQC.App.Tests;

[TestClass]
public sealed class ChatShellServiceTests
{
    [TestMethod]
    public async Task LoadShellAsync_WhenHistoryIsEmpty_ReturnsEmptyConversationsWithBootstrapContext()
    {
        await using var fixture = await ChatShellServiceFixture.CreateAsync();
        fixture.Bootstrap.RuntimeDependenciesReport = CreateUnavailableReport("runtime-prerequisites", "Node.js is unavailable.");
        fixture.Bootstrap.AuthenticationReport = CreateCompletedAuthenticationHandoff();

        var state = await fixture.Service.LoadShellAsync();

        Assert.AreEqual("Local history", state.ConnectionBadgeText);
        Assert.IsEmpty(state.Conversations);
        StringAssert.Contains(state.SidebarFooterText, "Saved conversations reopen from local history");
        StringAssert.Contains(state.SidebarFooterText, "Node.js is unavailable.");
    }

    [TestMethod]
    public async Task LoadShellAsync_WhenBootstrapIsReady_UsesBootstrapReadyBadge()
    {
        await using var fixture = await ChatShellServiceFixture.CreateAsync();

        var state = await fixture.Service.LoadShellAsync();

        Assert.AreEqual("Bootstrap ready", state.ConnectionBadgeText);
        StringAssert.Contains(state.SidebarFooterText, "Saved conversations reopen from local history");
        StringAssert.Contains(state.SidebarFooterText, "the next send can create or resume a live WorkICQ-backed session");
        Assert.IsFalse(state.SetupState.RequiresUserAction);
    }

    [TestMethod]
    public async Task LoadShellAsync_WhenBootstrapThrows_DoesNotAbortLaunch()
    {
        await using var fixture = await ChatShellServiceFixture.CreateAsync();
        fixture.Bootstrap.RuntimeDependenciesException = new RuntimeException("bootstrap exploded");

        var state = await fixture.Service.LoadShellAsync();

        Assert.AreEqual("Local history", state.ConnectionBadgeText);
        StringAssert.Contains(state.SidebarFooterText, "WorkICQ bootstrap check failed: bootstrap exploded");
    }

    [TestMethod]
    public async Task LoadShellAsync_WhenHistoryExists_ReturnsPersistedConversationsOrderedByUpdatedAt()
    {
        await using var fixture = await ChatShellServiceFixture.CreateAsync();
        var older = await fixture.CreateConversationAsync("Older thread");
        await fixture.AddMessageAsync(older.Id, "assistant", "First line\r\nSecond line");
        await fixture.SetSessionIdAsync(older.Id, "session-older");

        var draft = await fixture.CreateConversationAsync("Fresh draft");

        var newest = await fixture.CreateConversationAsync("Newest thread");
        await fixture.AddMessageAsync(newest.Id, "user", "How do we keep **markdown** literal?");
        await fixture.AddMessageAsync(newest.Id, "assistant", "Use plain text until rendering arrives.");
        await fixture.SetSessionIdAsync(newest.Id, "session-newest");

        await fixture.SetConversationUpdatedAtAsync(older.Id, DateTime.UtcNow.AddHours(-2));
        await fixture.SetConversationUpdatedAtAsync(draft.Id, DateTime.UtcNow.AddHours(-1));
        await fixture.SetConversationUpdatedAtAsync(newest.Id, DateTime.UtcNow);

        var state = await fixture.Service.LoadShellAsync();

        Assert.AreEqual("Bootstrap ready", state.ConnectionBadgeText);
        CollectionAssert.AreEqual(
            new[] { newest.Id, draft.Id, older.Id },
            state.Conversations.Select(conversation => conversation.Id).ToArray());
        Assert.AreEqual("Use plain text until rendering arrives.", state.Conversations[0].Preview);
        Assert.IsFalse(state.Conversations[0].IsDraft);
        Assert.AreEqual("session-newest", state.Conversations[0].SessionId);
        Assert.AreEqual("Waiting for your first question", state.Conversations[1].Preview);
        Assert.IsTrue(state.Conversations[1].IsDraft);
        Assert.AreEqual("First line Second line", state.Conversations[2].Preview);
    }

    [TestMethod]
    public async Task CreateConversationAsync_CreatesPersistedDraftSnapshot()
    {
        await using var fixture = await ChatShellServiceFixture.CreateAsync();

        var snapshot = await fixture.Service.CreateConversationAsync();

        Assert.AreEqual("New chat", snapshot.Title);
        Assert.AreEqual("Waiting for your first question", snapshot.Preview);
        Assert.IsTrue(snapshot.IsPersisted);
        Assert.IsTrue(snapshot.IsDraft);
        Assert.IsEmpty(snapshot.Messages);
        Assert.IsNotNull(await fixture.GetConversationAsync(snapshot.Id));
    }

    [TestMethod]
    public async Task DeleteConversationAsync_RemovesConversationMessagesAndSession()
    {
        await using var fixture = await ChatShellServiceFixture.CreateAsync();
        var conversation = await fixture.CreateConversationAsync("Disposable thread");
        await fixture.AddMessageAsync(conversation.Id, "user", "Delete me");
        await fixture.SetSessionIdAsync(conversation.Id, "session-delete");

        await fixture.Service.DeleteConversationAsync(conversation.Id);

        Assert.IsNull(await fixture.GetConversationAsync(conversation.Id));
        Assert.IsNull(await fixture.GetSessionIdAsync(conversation.Id));
    }

    [TestMethod]
    public async Task SendAsync_WhenRuntimeIsUnavailable_CreatesConversationAndPersistsBlockingTranscript()
    {
        await using var fixture = await ChatShellServiceFixture.CreateAsync();
        fixture.Bootstrap.RuntimeDependenciesReport = CreateUnavailableReport("runtime-prerequisites", "Copilot CLI missing.");
        fixture.Bootstrap.AuthenticationReport = CreateCompletedAuthenticationHandoff();

        var response = await fixture.Service.SendAsync(new ShellSendRequest(
            "missing-conversation",
            "Markdown fallback",
            "Show **markdown** and `code` literally",
            SessionId: null));
        var assistant = await ReadAllAsync(response.ResponseStream);
        var persistedConversation = await fixture.GetConversationAsync(response.ConversationId);

        Assert.AreEqual("Runtime blocked", response.ConnectionBadgeText);
        StringAssert.Contains(response.SidebarFooterText, "Copilot CLI missing.");
        StringAssert.Contains(assistant, "## Runtime request blocked");
        StringAssert.Contains(assistant, "**markdown**");
        StringAssert.Contains(assistant, "`code`");
        Assert.IsFalse(assistant.Contains("## Placeholder response for", StringComparison.Ordinal));
        Assert.IsNotNull(persistedConversation);
        Assert.AreEqual("Markdown fallback", persistedConversation!.Title);
        var persistedMessages = persistedConversation.Messages.OrderBy(message => message.Timestamp).ToArray();
        Assert.IsGreaterThanOrEqualTo(persistedMessages.Length, 2);
        Assert.AreEqual("Show **markdown** and `code` literally", persistedMessages[0].Content);
        Assert.AreEqual(
            assistant.TrimEnd().ReplaceLineEndings(Environment.NewLine),
            persistedMessages[^1].Content.ReplaceLineEndings(Environment.NewLine));
    }

    [TestMethod]
    public async Task SendAsync_WhenWorkplacePromptRuntimeIsUnavailable_ReturnsBlockingResponseInsteadOfPlaceholder()
    {
        await using var fixture = await ChatShellServiceFixture.CreateAsync();
        fixture.Bootstrap.RuntimeDependenciesReport = CreateUnavailableReport("runtime-prerequisites", "Copilot CLI missing.");
        fixture.Bootstrap.AuthenticationReport = CreateCompletedAuthenticationHandoff();

        var response = await fixture.Service.SendAsync(new ShellSendRequest(
            "workplace-blocked",
            "Direct reports",
            "Who are my direct reports?",
            SessionId: null));
        var assistant = await ReadAllAsync(response.ResponseStream);
        var persistedConversation = await fixture.GetConversationAsync(response.ConversationId);

        Assert.AreEqual("WorkICQ blocked", response.ConnectionBadgeText);
        StringAssert.Contains(response.SidebarFooterText, "requires a live WorkICQ call");
        StringAssert.Contains(response.SidebarFooterText, "Copilot CLI missing.");
        StringAssert.Contains(assistant, "## WorkICQ request blocked");
        StringAssert.Contains(assistant, "direct reports");
        Assert.IsFalse(assistant.Contains("## Placeholder response for", StringComparison.Ordinal));
        Assert.AreEqual(0, fixture.SessionCoordinator.CreateSessionCallCount);
        Assert.IsEmpty(fixture.MessageOrchestrator.SendRequests);
        Assert.AreEqual(
            assistant.TrimEnd().ReplaceLineEndings(Environment.NewLine),
            persistedConversation!.Messages.OrderBy(message => message.Timestamp).Last().Content.ReplaceLineEndings(Environment.NewLine));
    }

    [TestMethod]
    public async Task SendAsync_WhenStoredSessionExists_ReusesItAndPreservesMarkdownContent()
    {
        await using var fixture = await ChatShellServiceFixture.CreateAsync();
        var conversation = await fixture.CreateConversationAsync("Runtime markdown");
        await fixture.SetSessionIdAsync(conversation.Id, "stored-session");
        fixture.SessionCoordinator.OnCreateSessionAsync = (_, _) => throw new AssertFailedException("Existing session should be reused.");
        fixture.SessionCoordinator.OnResumeSessionAsync = (sessionId, _) =>
        {
            Assert.AreEqual("stored-session", sessionId);
            return Task.FromResult(true);
        };

        const string markdown = """
            ## Result
            
            - plain-text markdown
            - `inline code`
            
            ```csharp
            Console.WriteLine("hello");
            ```
            """;

        fixture.MessageOrchestrator.OnSendMessageAsync = (request, _) =>
            Task.FromResult(new SendMessageResponse { SessionId = request.SessionId!, MessageId = "message-1" });
        fixture.MessageOrchestrator.OnStreamResponseAsync = (_, _) => Stream(markdown);

        var response = await fixture.Service.SendAsync(new ShellSendRequest(
            conversation.Id,
            "Runtime markdown",
            "Ship the transcript",
            SessionId: null));
        var assistant = await ReadAllAsync(response.ResponseStream);
        var persistedConversation = await fixture.GetConversationAsync(conversation.Id);

        Assert.AreEqual("stored-session", fixture.MessageOrchestrator.SendRequests.Single().SessionId);
        Assert.AreEqual(0, fixture.SessionCoordinator.CreateSessionCallCount);
        Assert.AreEqual(1, fixture.SessionCoordinator.ResumeSessionCallCount);
        Assert.AreEqual(markdown.ReplaceLineEndings(Environment.NewLine), assistant.ReplaceLineEndings(Environment.NewLine));
        Assert.AreEqual(
            markdown.ReplaceLineEndings(Environment.NewLine),
            persistedConversation!.Messages.OrderBy(message => message.Timestamp).Last().Content.ReplaceLineEndings(Environment.NewLine));
    }

    [TestMethod]
    public async Task SendAsync_WhenWorkplacePromptRuntimeIsReady_UsesLiveRuntimePath()
    {
        await using var fixture = await ChatShellServiceFixture.CreateAsync();
        var conversation = await fixture.CreateConversationAsync("Org lookup");
        fixture.SessionCoordinator.OnCreateSessionAsync = (_, _) => Task.FromResult("live-session");
        fixture.MessageOrchestrator.OnSendMessageAsync = (_, _) =>
            Task.FromResult(new SendMessageResponse { SessionId = "live-session", MessageId = "message-WorkICQ" });
        fixture.MessageOrchestrator.OnStreamResponseAsync = (_, _) => Stream("- Sam Carter", "\n- Priya Patel");

        var response = await fixture.Service.SendAsync(new ShellSendRequest(
            conversation.Id,
            "Org lookup",
            "Who are my direct reports?",
            SessionId: null));
        var assistant = await ReadAllAsync(response.ResponseStream);

        Assert.AreEqual("WorkICQ runtime", response.ConnectionBadgeText);
        Assert.AreEqual(1, fixture.SessionCoordinator.CreateSessionCallCount);
        Assert.HasCount(1, fixture.MessageOrchestrator.SendRequests);
        Assert.AreEqual("Who are my direct reports?", fixture.MessageOrchestrator.SendRequests.Single().UserMessage);
        Assert.AreEqual("- Sam Carter\n- Priya Patel", assistant);
    }

    [TestMethod]
    public async Task SendAsync_WhenBootstrapIsReadyWithoutSession_CreatesSessionAndStoresResolvedSessionId()
    {
        await using var fixture = await ChatShellServiceFixture.CreateAsync();
        var conversation = await fixture.CreateConversationAsync("Live handoff");
        SessionConfiguration? capturedConfig = null;
        fixture.Bootstrap.WorkspaceResult = CreateWorkspace("@microsoft/WorkICQ");
        fixture.SessionCoordinator.OnCreateSessionAsync = (config, _) =>
        {
            capturedConfig = config;
            return Task.FromResult("created-session");
        };
        fixture.MessageOrchestrator.OnSendMessageAsync = (_, _) =>
            Task.FromResult(new SendMessageResponse { SessionId = "resolved-session", MessageId = "message-2" });
        fixture.MessageOrchestrator.OnStreamResponseAsync = (_, _) => Stream("First ", "second");

        var response = await fixture.Service.SendAsync(new ShellSendRequest(
            conversation.Id,
            "Live handoff",
            "Send it live",
            SessionId: null));
        var assistant = await ReadAllAsync(response.ResponseStream);

        Assert.AreEqual("resolved-session", response.SessionId);
        Assert.AreEqual("WorkICQ runtime", response.ConnectionBadgeText);
        Assert.AreEqual("First second", assistant);
        StringAssert.Contains(response.SidebarFooterText, "active reply is coming from the WorkICQ runtime");
        Assert.AreEqual(1, fixture.SessionCoordinator.CreateSessionCallCount);
        Assert.AreEqual(0, fixture.SessionCoordinator.ResumeSessionCallCount);
        Assert.IsNotNull(capturedConfig);
        Assert.IsNull(capturedConfig!.WorkIQVersion);
        CollectionAssert.AreEqual(WorkIQRuntimeDefaults.SessionAllowedToolNames.ToArray(), capturedConfig.AllowedTools.ToArray());
        Assert.AreEqual(
            "The app name WorkIQC identifies the desktop shell only. It is not a knowledge source.",
            capturedConfig.SystemGuidance["app-identity"]);
        StringAssert.Contains(capturedConfig.SystemGuidance["answer-sources"], "Never answer from local history");
        Assert.AreEqual("WorkICQ-first", capturedConfig.SystemGuidance["tool-posture"]);
        StringAssert.Contains(capturedConfig.SystemGuidance["current-user"], "currently authenticated Copilot/WorkICQ user");
        StringAssert.Contains(capturedConfig.SystemGuidance["principal-resolution"], "Only ask the user for their own name or work email if the WorkICQ tool explicitly reports");
        StringAssert.Contains(capturedConfig.SystemGuidance["eula-recovery"], "complete the WorkICQ consent bootstrap in Settings");
        StringAssert.Contains(capturedConfig.SystemGuidance["allowed-tools"], "ask_work_iq");
        CollectionAssert.AreEqual(new[] { "*" }, capturedConfig.AllowedTools.ToArray());
        Assert.AreEqual("resolved-session", await fixture.GetSessionIdAsync(conversation.Id));
    }

    [TestMethod]
    public async Task LoadShellAsync_WhenSetupMarkersAreMissing_ExposesFirstRunSetupState()
    {
        await using var fixture = await ChatShellServiceFixture.CreateAsync();
        fixture.Bootstrap.EulaReport = new EulaAcceptanceReport
        {
            Status = EulaAcceptanceStatus.ActionRequired,
            MarkerPath = Path.Combine(Path.GetTempPath(), "workiqc-tests", "eula-accepted.json"),
            Resolution = "Complete the WorkICQ consent bootstrap from Settings so the app can verify consent."
        };
        fixture.Bootstrap.AuthenticationReport = new AuthenticationHandoffReport
        {
            Status = AuthenticationHandoffStatus.ActionRequired,
            MarkerPath = Path.Combine(Path.GetTempPath(), "workiqc-tests", "auth-handoff.json"),
            LoginCommand = "copilot login",
            Resolution = "Launch Copilot sign-in from the setup card."
        };

        var state = await fixture.Service.LoadShellAsync();

        Assert.IsTrue(state.SetupState.RequiresUserAction);
        StringAssert.Contains(state.SetupState.AuthenticationCommandLine, "copilot login");
        StringAssert.Contains(state.SetupState.Blockers[0].Text, "WorkICQ consent bootstrap");
    }

    [TestMethod]
    public async Task AcceptWorkIqTermsAsync_AndRecordAuthenticationHandoffAsync_RefreshSetupState()
    {
        await using var fixture = await ChatShellServiceFixture.CreateAsync();
        fixture.Bootstrap.EulaReport = new EulaAcceptanceReport
        {
            Status = EulaAcceptanceStatus.ActionRequired,
            MarkerPath = Path.Combine(Path.GetTempPath(), "workiqc-tests", "eula-accepted.json")
        };
        fixture.Bootstrap.AuthenticationReport = new AuthenticationHandoffReport
        {
            Status = AuthenticationHandoffStatus.ActionRequired,
            MarkerPath = Path.Combine(Path.GetTempPath(), "workiqc-tests", "auth-handoff.json"),
            LoginCommand = "copilot login"
        };

        var afterEula = await fixture.Service.AcceptWorkIqTermsAsync();
        var afterAuth = await fixture.Service.RecordAuthenticationHandoffAsync("copilot login");

        Assert.IsTrue(afterEula.IsEulaAccepted);
        Assert.IsTrue(afterAuth.IsAuthenticationHandoffStarted);
        Assert.AreEqual(1, fixture.Bootstrap.AcceptEulaCallCount);
        StringAssert.Contains(afterAuth.Prerequisites.Single(item => item.Text.StartsWith("copilot.auth:", StringComparison.Ordinal)).Text, "Copilot sign-in handoff was recorded");
    }

    [TestMethod]
    public async Task LoadShellAsync_WhenAuthenticationIsReady_ReportsItAsPrerequisiteFeedback()
    {
        await using var fixture = await ChatShellServiceFixture.CreateAsync();
        fixture.Bootstrap.AuthenticationReport = new AuthenticationHandoffReport
        {
            Status = AuthenticationHandoffStatus.Completed,
            MarkerPath = Path.Combine(Path.GetTempPath(), "workiqc-tests", "auth-handoff.json"),
            LoginCommand = "copilot login",
            Details = "Copilot sign-in handoff was recorded via 'copilot login' (recorded 3/14/2026 9:41 AM). This does not verify that the live WorkICQ session can resolve the signed-in principal. Evidence is stored at 'auth-handoff.json'."
        };

        var state = await fixture.Service.LoadShellAsync();

        Assert.IsFalse(state.SetupState.RequiresUserAction);
        StringAssert.Contains(
            state.SetupState.Prerequisites.Single(item => item.Text.StartsWith("copilot.auth:", StringComparison.Ordinal)).Text,
            "Copilot sign-in handoff was recorded");
        StringAssert.Contains(state.SetupState.SummaryText, "WorkICQ bootstrap is ready.");
    }

    [TestMethod]
    public async Task SendAsync_WhenRuntimeSendFails_ReturnsBlockingPlanWithRuntimeBlockedBadge()
    {
        await using var fixture = await ChatShellServiceFixture.CreateAsync();
        var conversation = await fixture.CreateConversationAsync("Send failure");
        fixture.SessionCoordinator.OnCreateSessionAsync = (_, _) => Task.FromResult("created-session");
        fixture.MessageOrchestrator.OnSendMessageAsync = (_, _) =>
            Task.FromResult(new SendMessageResponse
            {
                SessionId = "created-session",
                MessageId = "message-3",
                ErrorMessage = "Gateway timeout"
            });

        var response = await fixture.Service.SendAsync(new ShellSendRequest(
            conversation.Id,
            "Send failure",
            "Trigger a send failure",
            SessionId: null));
        var assistant = await ReadAllAsync(response.ResponseStream);

        Assert.AreEqual("Runtime blocked", response.ConnectionBadgeText);
        StringAssert.Contains(response.SidebarFooterText, "Copilot SDK handoff failed before the request could run.");
        StringAssert.Contains(response.SidebarFooterText, "Gateway timeout");
        StringAssert.Contains(assistant, "## Runtime request blocked");
    }

    [TestMethod]
    public async Task SendAsync_WhenWorkplacePromptRuntimeSendFails_ReturnsBlockingResponse()
    {
        await using var fixture = await ChatShellServiceFixture.CreateAsync();
        var conversation = await fixture.CreateConversationAsync("Workplace send failure");
        fixture.SessionCoordinator.OnCreateSessionAsync = (_, _) => Task.FromResult("created-session");
        fixture.MessageOrchestrator.OnSendMessageAsync = (_, _) =>
            Task.FromResult(new SendMessageResponse
            {
                SessionId = "created-session",
                MessageId = "message-3",
                ErrorMessage = "Gateway timeout"
            });

        var response = await fixture.Service.SendAsync(new ShellSendRequest(
            conversation.Id,
            "Workplace send failure",
            "Who are my direct reports?",
            SessionId: null));
        var assistant = await ReadAllAsync(response.ResponseStream);

        Assert.AreEqual("WorkICQ blocked", response.ConnectionBadgeText);
        StringAssert.Contains(response.SidebarFooterText, "live WorkICQ handoff failed before the request could run");
        StringAssert.Contains(response.SidebarFooterText, "Gateway timeout");
        StringAssert.Contains(assistant, "## WorkICQ request blocked");
        Assert.IsFalse(assistant.Contains("## Placeholder response for", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task SendAsync_WhenRuntimeStreamIsEmpty_UsesBlockingResponseAndPersistsIt()
    {
        await using var fixture = await ChatShellServiceFixture.CreateAsync();
        var conversation = await fixture.CreateConversationAsync("Empty runtime");
        fixture.SessionCoordinator.OnCreateSessionAsync = (_, _) => Task.FromResult("created-session");
        fixture.MessageOrchestrator.OnSendMessageAsync = (_, _) =>
            Task.FromResult(new SendMessageResponse { SessionId = "created-session", MessageId = "message-4" });
        fixture.MessageOrchestrator.OnStreamResponseAsync = (_, _) => Stream(string.Empty);

        var response = await fixture.Service.SendAsync(new ShellSendRequest(
            conversation.Id,
            "Empty runtime",
            "Explain theme switching",
            SessionId: null));
        var assistant = await ReadAllAsync(response.ResponseStream);
        var persistedConversation = await fixture.GetConversationAsync(conversation.Id);

        StringAssert.StartsWith(assistant, "## Runtime request blocked");
        StringAssert.Contains(assistant, "finished without returning answer content");
        Assert.AreEqual(
            assistant.TrimEnd().ReplaceLineEndings(Environment.NewLine),
            persistedConversation!.Messages.OrderBy(message => message.Timestamp).Last().Content.ReplaceLineEndings(Environment.NewLine));
    }

    [TestMethod]
    public async Task SendAsync_WhenWorkplacePromptRuntimeStreamIsEmpty_ReturnsBlockingResponse()
    {
        await using var fixture = await ChatShellServiceFixture.CreateAsync();
        var conversation = await fixture.CreateConversationAsync("Empty workplace runtime");
        fixture.SessionCoordinator.OnCreateSessionAsync = (_, _) => Task.FromResult("created-session");
        fixture.MessageOrchestrator.OnSendMessageAsync = (_, _) =>
            Task.FromResult(new SendMessageResponse { SessionId = "created-session", MessageId = "message-4" });
        fixture.MessageOrchestrator.OnStreamResponseAsync = (_, _) => Stream(string.Empty);

        var response = await fixture.Service.SendAsync(new ShellSendRequest(
            conversation.Id,
            "Empty workplace runtime",
            "Who are my direct reports?",
            SessionId: null));
        var assistant = await ReadAllAsync(response.ResponseStream);

        Assert.AreEqual("WorkICQ runtime", response.ConnectionBadgeText);
        StringAssert.Contains(assistant, "## WorkICQ request blocked");
        StringAssert.Contains(assistant, "finished without returning answer content");
        Assert.IsFalse(assistant.Contains("## Placeholder response for", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task SendAsync_WhenUnsupportedRuntimeActionOccurs_IncludesResolutionInFooter()
    {
        await using var fixture = await ChatShellServiceFixture.CreateAsync();
        var conversation = await fixture.CreateConversationAsync("Unsupported runtime");
        fixture.SessionCoordinator.OnCreateSessionAsync = (_, _) =>
            throw new UnsupportedRuntimeActionException(
                actionName: "CreateSessionAsync",
                capabilityName: "copilot-session-management",
                message: "Session orchestration is not ready.",
                resolution: "Keep the shell blocked for this release.",
                errorCode: "runtime.session.create.unsupported");

        var response = await fixture.Service.SendAsync(new ShellSendRequest(
            conversation.Id,
            "Unsupported runtime",
            "Stay safe",
            SessionId: null));
        var assistant = await ReadAllAsync(response.ResponseStream);

        Assert.AreEqual("Runtime blocked", response.ConnectionBadgeText);
        StringAssert.Contains(response.SidebarFooterText, "Session orchestration is not ready.");
        StringAssert.Contains(response.SidebarFooterText, "Keep the shell blocked for this release.");
        StringAssert.Contains(assistant, "## Runtime request blocked");
    }

    private static RuntimeReadinessReport CreateReadyReport(string subject)
        => new()
        {
            Subject = subject,
            Dependencies =
            [
                new DependencyCheckResult
                {
                    Name = "dependency",
                    IsAvailable = true,
                    ResolvedPath = @"C:\tools\dependency.exe",
                    Details = "Dependency is available."
                }
            ],
            Capabilities =
            [
                new RuntimeCapability
                {
                    Name = "capability",
                    Status = RuntimeCapabilityStatus.Available,
                    Details = "Capability is available."
                }
            ]
        };

    private static RuntimeReadinessReport CreateUnavailableReport(string subject, string details)
        => new()
        {
            Subject = subject,
            Dependencies =
            [
                new DependencyCheckResult
                {
                    Name = "dependency",
                    IsAvailable = false,
                    Details = details
                }
            ]
        };

    private static WorkspaceInitializationResult CreateWorkspace(string packageReference = "@microsoft/WorkICQ")
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "workiqc-tests", Guid.NewGuid().ToString("N"));
        var copilotPath = Path.Combine(workspaceRoot, ".copilot");
        Directory.CreateDirectory(copilotPath);
        return new WorkspaceInitializationResult
        {
            WorkspacePath = workspaceRoot,
            CopilotDirectoryPath = copilotPath,
            McpConfigPath = Path.Combine(copilotPath, "mcp-config.json"),
            WorkIQPackageReference = packageReference,
            UsesLatestWorkIQPackage = string.Equals(packageReference, WorkIQRuntimeDefaults.PackageReference, StringComparison.OrdinalIgnoreCase),
            ConfigWasWritten = true
        };
    }

    private static EulaAcceptanceReport CreateAcceptedEula()
        => new()
        {
            Status = EulaAcceptanceStatus.Accepted,
            MarkerPath = Path.Combine(Path.GetTempPath(), "workiqc-tests", "eula-accepted.json"),
            Details = "Accepted."
        };

    private static AuthenticationHandoffReport CreateCompletedAuthenticationHandoff()
        => new()
        {
            Status = AuthenticationHandoffStatus.Completed,
            MarkerPath = Path.Combine(Path.GetTempPath(), "workiqc-tests", "auth-handoff.json"),
            LoginCommand = "copilot login",
            Details = "Copilot sign-in handoff was recorded via 'copilot login'. This does not verify that the live WorkICQ session can resolve the signed-in principal."
        };

    private static async Task<string> ReadAllAsync(IAsyncEnumerable<string> stream, CancellationToken cancellationToken = default)
    {
        var chunks = new List<string>();
        await foreach (var chunk in stream.WithCancellation(cancellationToken))
        {
            chunks.Add(chunk);
        }

        return string.Concat(chunks);
    }

    private static async IAsyncEnumerable<StreamingDelta> Stream(params string[] chunks)
    {
        foreach (var chunk in chunks)
        {
            yield return new StreamingDelta { Content = chunk, IsComplete = false };
            await Task.Yield();
        }
    }

    private sealed class ChatShellServiceFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly ServiceProvider _serviceProvider;

        private ChatShellServiceFixture(
            SqliteConnection connection,
            ServiceProvider serviceProvider,
            ChatShellService service,
            TestCopilotBootstrap bootstrap,
            TestSessionCoordinator sessionCoordinator,
            TestMessageOrchestrator messageOrchestrator)
        {
            _connection = connection;
            _serviceProvider = serviceProvider;
            Service = service;
            Bootstrap = bootstrap;
            SessionCoordinator = sessionCoordinator;
            MessageOrchestrator = messageOrchestrator;
        }

        public ChatShellService Service { get; }

        public TestCopilotBootstrap Bootstrap { get; }

        public TestSessionCoordinator SessionCoordinator { get; }

        public TestMessageOrchestrator MessageOrchestrator { get; }

        public static async Task<ChatShellServiceFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var services = new ServiceCollection();
            services.AddDbContext<WorkIQDbContext>(options => options.UseSqlite(connection));
            services.AddScoped<IConversationService, ConversationService>();

            var serviceProvider = services.BuildServiceProvider(validateScopes: true);
            await using (var scope = serviceProvider.CreateAsyncScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<WorkIQDbContext>();
                await context.Database.EnsureCreatedAsync();
            }

            var bootstrap = new TestCopilotBootstrap();
            var sessionCoordinator = new TestSessionCoordinator();
            var messageOrchestrator = new TestMessageOrchestrator();
            var service = new ChatShellService(
                serviceProvider.GetRequiredService<IServiceScopeFactory>(),
                bootstrap,
                sessionCoordinator,
                messageOrchestrator);

            return new ChatShellServiceFixture(connection, serviceProvider, service, bootstrap, sessionCoordinator, messageOrchestrator);
        }

        public async Task<Conversation> CreateConversationAsync(string title)
        {
            await using var scope = _serviceProvider.CreateAsyncScope();
            var conversationService = scope.ServiceProvider.GetRequiredService<IConversationService>();
            return await conversationService.CreateConversationAsync(title);
        }

        public async Task AddMessageAsync(string conversationId, string role, string content)
        {
            await using var scope = _serviceProvider.CreateAsyncScope();
            var conversationService = scope.ServiceProvider.GetRequiredService<IConversationService>();
            await conversationService.AddMessageAsync(conversationId, role, content);
        }

        public async Task SetSessionIdAsync(string conversationId, string sessionId)
        {
            await using var scope = _serviceProvider.CreateAsyncScope();
            var conversationService = scope.ServiceProvider.GetRequiredService<IConversationService>();
            await conversationService.SetCopilotSessionIdAsync(conversationId, sessionId);
        }

        public async Task<string?> GetSessionIdAsync(string conversationId)
        {
            await using var scope = _serviceProvider.CreateAsyncScope();
            var conversationService = scope.ServiceProvider.GetRequiredService<IConversationService>();
            return await conversationService.GetCopilotSessionIdAsync(conversationId);
        }

        public async Task<Conversation?> GetConversationAsync(string conversationId)
        {
            await using var scope = _serviceProvider.CreateAsyncScope();
            var conversationService = scope.ServiceProvider.GetRequiredService<IConversationService>();
            return await conversationService.GetConversationAsync(conversationId);
        }

        public async Task SetConversationUpdatedAtAsync(string conversationId, DateTime updatedAt)
        {
            await using var scope = _serviceProvider.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<WorkIQDbContext>();
            var conversation = await context.Conversations.SingleAsync(item => item.Id == conversationId);
            conversation.UpdatedAt = updatedAt;
            await context.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await _serviceProvider.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class TestCopilotBootstrap : ICopilotBootstrap
    {
        public RuntimeReadinessReport RuntimeDependenciesReport { get; set; } = CreateReadyReport("runtime-prerequisites");

        public RuntimeReadinessReport WorkICQReport { get; set; } = CreateReadyReport("WorkICQ-prerequisites");

        public WorkspaceInitializationResult WorkspaceResult { get; set; } = CreateWorkspace();

        public EulaAcceptanceReport EulaReport { get; set; } = CreateAcceptedEula();

        public AuthenticationHandoffReport AuthenticationReport { get; set; } = CreateCompletedAuthenticationHandoff();

        public Exception? RuntimeDependenciesException { get; set; }

        public Exception? WorkICQException { get; set; }

        public Exception? InitializeWorkspaceException { get; set; }

        public Exception? EulaException { get; set; }

        public Exception? AuthenticationException { get; set; }

        public int AcceptEulaCallCount { get; private set; }

        public Task<RuntimeReadinessReport> EnsureRuntimeDependenciesAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (RuntimeDependenciesException is not null)
            {
                throw RuntimeDependenciesException;
            }

            return Task.FromResult(RuntimeDependenciesReport);
        }

        public Task<RuntimeReadinessReport> EnsureWorkIQAvailableAsync(string? version = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (WorkICQException is not null)
            {
                throw WorkICQException;
            }

            return Task.FromResult(WorkICQReport);
        }

        public Task<WorkspaceInitializationResult> InitializeWorkspaceAsync(string? workspacePath = null, string? version = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (InitializeWorkspaceException is not null)
            {
                throw InitializeWorkspaceException;
            }

            return Task.FromResult(WorkspaceResult);
        }

        public Task<EulaAcceptanceReport> VerifyEulaAcceptanceAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (EulaException is not null)
            {
                throw EulaException;
            }

            return Task.FromResult(EulaReport);
        }

        public Task<EulaAcceptanceReport> AcceptEulaAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AcceptEulaCallCount++;
            if (EulaException is not null)
            {
                throw EulaException;
            }

            EulaReport = CreateAcceptedEula();
            return Task.FromResult(EulaReport);
        }

        public Task<AuthenticationHandoffReport> VerifyAuthenticationHandoffAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (AuthenticationException is not null)
            {
                throw AuthenticationException;
            }

            return Task.FromResult(AuthenticationReport);
        }

        public Task<AuthenticationHandoffReport> RecordAuthenticationHandoffAsync(string? loginCommand = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AuthenticationReport = CreateCompletedAuthenticationHandoff() with
            {
                LoginCommand = loginCommand ?? "copilot login"
            };
            return Task.FromResult(AuthenticationReport);
        }
    }

    private sealed class TestSessionCoordinator : ISessionCoordinator
    {
        public Func<SessionConfiguration, CancellationToken, Task<string>> OnCreateSessionAsync { get; set; } = (_, _) => Task.FromResult("created-session");

        public Func<string, CancellationToken, Task<bool>> OnResumeSessionAsync { get; set; } = (_, _) => Task.FromResult(true);

        public int CreateSessionCallCount { get; private set; }

        public int ResumeSessionCallCount { get; private set; }

        public Task<string> CreateSessionAsync(SessionConfiguration config, CancellationToken cancellationToken = default)
        {
            CreateSessionCallCount++;
            return OnCreateSessionAsync(config, cancellationToken);
        }

        public Task<bool> ResumeSessionAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            ResumeSessionCallCount++;
            return OnResumeSessionAsync(sessionId, cancellationToken);
        }

        public Task<SessionState> GetSessionStateAsync(string sessionId, CancellationToken cancellationToken = default)
            => Task.FromResult(new SessionState
            {
                SessionId = sessionId,
                Status = SessionStatus.Ready,
                CreatedAt = DateTimeOffset.UtcNow
            });

        public Task DisposeSessionAsync(string sessionId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class TestMessageOrchestrator : IMessageOrchestrator
    {
        public Func<SendMessageRequest, CancellationToken, Task<SendMessageResponse>> OnSendMessageAsync { get; set; } =
            (request, _) => Task.FromResult(new SendMessageResponse
            {
                SessionId = request.SessionId ?? "resolved-session",
                MessageId = "message-0"
            });

        public Func<string, CancellationToken, IAsyncEnumerable<StreamingDelta>> OnStreamResponseAsync { get; set; } =
            (_, _) => Stream("ready");

        public List<SendMessageRequest> SendRequests { get; } = [];

        public Task<SendMessageResponse> SendMessageAsync(SendMessageRequest request, CancellationToken cancellationToken = default)
        {
            SendRequests.Add(request);
            return OnSendMessageAsync(request, cancellationToken);
        }

        public IAsyncEnumerable<StreamingDelta> StreamResponseAsync(string sessionId, CancellationToken cancellationToken = default)
            => OnStreamResponseAsync(sessionId, cancellationToken);

        public async IAsyncEnumerable<ToolEvent> ObserveToolEventsAsync(string sessionId, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.CompletedTask;
            yield break;
        }
    }
}
