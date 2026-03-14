using Microsoft.UI.Xaml;
using WorkIQC.App.Models;
using WorkIQC.App.Services;
using WorkIQC.App.ViewModels;

namespace WorkIQC.App.Tests;

[TestClass]
public sealed class MainPageViewModelTests
{
    [TestMethod]
    public void ComposerHintText_UsesEnterToSendAndShiftEnterForNewLine()
    {
        var viewModel = new MainPageViewModel(new FakeChatShellService(CreateShellState()));

        Assert.AreEqual("Press Enter to send. Shift+Enter adds a new line.", viewModel.ComposerHintText);
    }

    [TestMethod]
    public async Task InitializeAsync_WhenAccessibleModelsExist_LoadsModelPickerOptions()
    {
        var service = new FakeChatShellService(CreateShellState())
        {
            AvailableModels =
            [
                new ShellModelOption("gpt-5", "GPT-5"),
                new ShellModelOption("claude-sonnet-4.5", "Claude Sonnet 4.5")
            ]
        };
        var viewModel = new MainPageViewModel(service);

        await viewModel.InitializeAsync();

        Assert.HasCount(2, viewModel.AvailableModels);
        Assert.AreEqual(Visibility.Visible, viewModel.ModelPickerVisibility);
        Assert.AreEqual("gpt-5", viewModel.SelectedModelId);
        Assert.IsNotNull(viewModel.SelectedModelOption);
        Assert.AreEqual("gpt-5", viewModel.SelectedModelOption.Id);
    }

    [TestMethod]
    public async Task SendAsync_WhenModelIsSelected_ForwardsSelectedModelToShellService()
    {
        var conversation = CreateConversation(
            "thread-1",
            "Model thread",
            DateTime.Now.AddMinutes(-5),
            "Previous answer",
            new ShellMessageSnapshot(ChatRole.Assistant, "WorkICQ", "Previous answer", DateTime.Now.AddMinutes(-5)));
        ShellSendRequest? capturedRequest = null;
        var service = new FakeChatShellService(CreateShellState(conversation))
        {
            AvailableModels = [new ShellModelOption("gpt-5", "GPT-5")],
            OnSendAsync = (request, _) =>
            {
                capturedRequest = request;
                return Task.FromResult(new ShellSendResponse(
                    conversation.Id,
                    conversation.Title,
                    IsPersisted: true,
                    IsDraft: false,
                    SessionId: "session-thread-1",
                    ConnectionBadgeText: "WorkICQ runtime",
                    SidebarFooterText: "Streaming from selected model.",
                    ResponseStream: SingleChunkResponseStream("Done."),
                    ActivityStream: EmptyActivityStream()));
            }
        };
        var viewModel = new MainPageViewModel(service);

        await viewModel.InitializeAsync();
        viewModel.SelectedModelId = "gpt-5";
        viewModel.ComposerText = "Use selected model";

        await viewModel.SendAsync();
        await WaitForConditionAsync(() => capturedRequest is not null);

        Assert.IsNotNull(capturedRequest);
        Assert.AreEqual("gpt-5", capturedRequest.ModelId);
    }

    [TestMethod]
    public async Task ShowSettings_AndReturnToConversation_PreservesSelectedThread()
    {
        var shellState = CreateShellState(
            CreateConversation(
                "thread-1",
                "Newest thread",
                DateTime.Now.AddMinutes(-5),
                "Latest answer",
                new ShellMessageSnapshot(ChatRole.User, "You", "Question", DateTime.Now.AddMinutes(-6)),
                new ShellMessageSnapshot(ChatRole.Assistant, "WorkICQ", "Latest answer", DateTime.Now.AddMinutes(-5))),
            CreateConversation(
                "thread-2",
                "Older thread",
                DateTime.Now.AddHours(-1),
                "Earlier preview",
                new ShellMessageSnapshot(ChatRole.Assistant, "WorkICQ", "Earlier preview", DateTime.Now.AddHours(-1))));
        var service = new FakeChatShellService(shellState);
        var viewModel = new MainPageViewModel(service);

        await viewModel.InitializeAsync();
        var selectedId = viewModel.SelectedConversationId;
        var transcript = viewModel.Messages.Select(message => message.Content).ToArray();

        viewModel.ShowSettings();

        Assert.IsTrue(viewModel.IsSettingsViewActive);
        Assert.AreEqual(Visibility.Visible, viewModel.SettingsSurfaceVisibility);
        Assert.AreEqual(Visibility.Collapsed, viewModel.ConversationSurfaceVisibility);

        viewModel.ReturnToConversation();

        Assert.IsFalse(viewModel.IsSettingsViewActive);
        Assert.AreEqual(selectedId, viewModel.SelectedConversationId);
        Assert.AreEqual(Visibility.Collapsed, viewModel.SettingsSurfaceVisibility);
        Assert.AreEqual(Visibility.Visible, viewModel.ConversationSurfaceVisibility);
        CollectionAssert.AreEqual(transcript, viewModel.Messages.Select(message => message.Content).ToArray());
    }

    [TestMethod]
    public async Task SelectConversationAsync_WhenSettingsAreOpen_ReturnsToConversationSurface()
    {
        var newest = CreateConversation(
            "thread-1",
            "Newest thread",
            DateTime.Now.AddMinutes(-5),
            "Latest answer",
            new ShellMessageSnapshot(ChatRole.Assistant, "WorkICQ", "Latest answer", DateTime.Now.AddMinutes(-5)));
        var older = CreateConversation(
            "thread-2",
            "Older thread",
            DateTime.Now.AddHours(-1),
            "Earlier preview",
            new ShellMessageSnapshot(ChatRole.Assistant, "WorkICQ", "Earlier preview", DateTime.Now.AddHours(-1)));
        var viewModel = new MainPageViewModel(new FakeChatShellService(CreateShellState(newest, older)));

        await viewModel.InitializeAsync();
        viewModel.ShowSettings();

        await viewModel.SelectConversationAsync(older.Id);

        Assert.IsFalse(viewModel.IsSettingsViewActive);
        Assert.AreEqual(older.Id, viewModel.SelectedConversationId);
        Assert.AreEqual(Visibility.Collapsed, viewModel.SettingsSurfaceVisibility);
        Assert.AreEqual(Visibility.Visible, viewModel.ConversationSurfaceVisibility);
        CollectionAssert.AreEqual(new[] { "Earlier preview" }, viewModel.Messages.Select(message => message.Content).ToArray());
    }

    [TestMethod]
    public async Task DeleteConversationAsync_WhenSelectedConversationIsRemoved_SelectsNextConversationAndDeletesPersistedState()
    {
        var newest = CreateConversation(
            "thread-1",
            "Newest thread",
            DateTime.Now.AddMinutes(-5),
            "Latest answer",
            new ShellMessageSnapshot(ChatRole.Assistant, "WorkICQ", "Latest answer", DateTime.Now.AddMinutes(-5)));
        var older = CreateConversation(
            "thread-2",
            "Older thread",
            DateTime.Now.AddHours(-1),
            "Earlier preview",
            new ShellMessageSnapshot(ChatRole.Assistant, "WorkICQ", "Earlier preview", DateTime.Now.AddHours(-1)));
        var service = new FakeChatShellService(CreateShellState(newest, older));
        var viewModel = new MainPageViewModel(service);

        await viewModel.InitializeAsync();

        await viewModel.DeleteConversationAsync(newest.Id);

        CollectionAssert.AreEqual(new[] { newest.Id }, service.DeletedConversationIds.ToArray());
        Assert.AreEqual(older.Id, viewModel.SelectedConversationId);
        Assert.HasCount(1, viewModel.SidebarItems);
        CollectionAssert.AreEqual(new[] { "Earlier preview" }, viewModel.Messages.Select(message => message.Content).ToArray());
    }

    [TestMethod]
    public async Task DeleteConversationAsync_WhenLastConversationIsRemoved_ClearsSelectionAndTranscript()
    {
        var onlyConversation = CreateConversation(
            "thread-1",
            "Only thread",
            DateTime.Now.AddMinutes(-5),
            "Lonely preview",
            new ShellMessageSnapshot(ChatRole.Assistant, "WorkICQ", "Lonely preview", DateTime.Now.AddMinutes(-5)));
        var service = new FakeChatShellService(CreateShellState(onlyConversation));
        var viewModel = new MainPageViewModel(service);

        await viewModel.InitializeAsync();
        await viewModel.DeleteConversationAsync(onlyConversation.Id);

        CollectionAssert.AreEqual(new[] { onlyConversation.Id }, service.DeletedConversationIds.ToArray());
        Assert.IsNull(viewModel.SelectedConversationId);
        Assert.IsEmpty(viewModel.SidebarItems);
        Assert.IsEmpty(viewModel.Messages);
        Assert.AreEqual(Visibility.Visible, viewModel.EmptyStateVisibility);
    }

    [TestMethod]
    public async Task InitializeAsync_WhenAuthenticationIsReady_ShowsCompletedAuthState()
    {
        var viewModel = new MainPageViewModel(new FakeChatShellService(CreateShellState()));

        await viewModel.InitializeAsync();

        Assert.AreEqual("Completed", viewModel.AuthStepStatusText);
        StringAssert.Contains(viewModel.AuthStepDescription, "Copilot auth handoff was recorded locally.");
    }

    [TestMethod]
    public async Task InitializeAsync_WhenEulaIsAccepted_ShowsVerifiedConsentCopy()
    {
        var viewModel = new MainPageViewModel(new FakeChatShellService(CreateShellState()));

        await viewModel.InitializeAsync();

        StringAssert.Contains(viewModel.EulaStepDescription, "Verified during WorkICQ bootstrap");
    }

    [TestMethod]
    public void SetupCopy_UsesBootstrapLanguageForConsentAndAuth()
    {
        var setupState = new ShellSetupState(
            RequiresUserAction: true,
            CanAttemptRuntime: false,
            IsEulaAccepted: false,
            IsAuthenticationHandoffStarted: false,
            SummaryText: "WorkICQ bootstrap still needs attention before the next live session: consent is required.",
            WorkIQPackageReference: "@microsoft/WorkICQ",
            WorkspacePath: "C:\\WorkIQC",
            McpConfigPath: "C:\\WorkIQC\\.copilot\\mcp-config.json",
            EulaUrl: "https://github.com/microsoft/work-iq-mcp",
            EulaMarkerPath: "C:\\WorkIQC\\eula.json",
            AuthenticationMarkerPath: "C:\\WorkIQC\\auth.json",
            AuthenticationCommandLine: "copilot login",
            Blockers: [new SetupCheckItem("Consent is required.", false)],
            Prerequisites: Array.Empty<SetupCheckItem>());
        var viewModel = new MainPageViewModel(new FakeChatShellService(CreateShellStateWithSetup(setupState)));

        Assert.AreEqual("Complete WorkICQ bootstrap", viewModel.SetupTitle);
        StringAssert.Contains(viewModel.EulaStepDescription, "complete the WorkICQ consent bootstrap before the first live session");
        StringAssert.Contains(viewModel.AuthStepDescription, "during bootstrap before the first live WorkICQ session");
    }

    [TestMethod]
    public async Task SendAsync_WhenWorkICQRequestIsBlocked_ShowsBlockingStatusInsteadOfPlaceholderStatus()
    {
        var conversation = CreateConversation(
            "thread-1",
            "Org lookup",
            DateTime.Now.AddMinutes(-5),
            "Latest answer",
            new ShellMessageSnapshot(ChatRole.Assistant, "WorkICQ", "Latest answer", DateTime.Now.AddMinutes(-5)));
        var shellState = CreateShellState(conversation);
        var streamGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new FakeChatShellService(shellState)
        {
            OnSendAsync = (_, _) => Task.FromResult(new ShellSendResponse(
                conversation.Id,
                conversation.Title,
                IsPersisted: true,
                IsDraft: false,
                SessionId: "session-thread-1",
                ConnectionBadgeText: "WorkICQ blocked",
                SidebarFooterText: "This workplace request requires a live WorkICQ call.",
                ResponseStream: CreateBlockedResponseStream(streamGate.Task),
                ActivityStream: EmptyActivityStream()))
        };
        var viewModel = new MainPageViewModel(service);

        await viewModel.InitializeAsync();
        viewModel.ComposerText = "Who are my direct reports?";

        var sendTask = viewModel.SendAsync();
        await WaitForConditionAsync(() => viewModel.ComposerStatusText == "Showing WorkICQ blocking error…");

        Assert.AreEqual("Showing WorkICQ blocking error…", viewModel.ComposerStatusText);

        streamGate.SetResult();
        await sendTask;
    }

    private static ShellBootstrapState CreateShellState(params ShellConversationSnapshot[] conversations)
        => CreateShellStateWithSetup(
            new ShellSetupState(
                RequiresUserAction: false,
                CanAttemptRuntime: true,
                IsEulaAccepted: true,
                IsAuthenticationHandoffStarted: true,
                SummaryText: "Ready",
                WorkIQPackageReference: "@microsoft/WorkICQ",
                WorkspacePath: "C:\\WorkIQC",
                McpConfigPath: "C:\\WorkIQC\\.copilot\\mcp-config.json",
                EulaUrl: "https://github.com/microsoft/work-iq-mcp",
                EulaMarkerPath: "C:\\WorkIQC\\eula.json",
                AuthenticationMarkerPath: "C:\\WorkIQC\\auth.json",
                AuthenticationCommandLine: "copilot login",
                Blockers: Array.Empty<SetupCheckItem>(),
                Prerequisites: Array.Empty<SetupCheckItem>()),
            conversations);

    private static ShellBootstrapState CreateShellStateWithSetup(ShellSetupState setupState, params ShellConversationSnapshot[] conversations)
        => new(
            conversations,
            "Local history",
            "Saved threads are available.",
            setupState);

    private static ShellConversationSnapshot CreateConversation(
        string id,
        string title,
        DateTime updatedAt,
        string preview,
        params ShellMessageSnapshot[] messages)
        => new(
            id,
            title,
            preview,
            updatedAt,
            IsPersisted: true,
            IsDraft: false,
            SessionId: $"session-{id}",
            Messages: messages);

    private static async Task WaitForConditionAsync(Func<bool> condition, int attempts = 30, int delayMilliseconds = 25)
    {
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(delayMilliseconds);
        }

        Assert.Fail("Timed out waiting for condition.");
    }

    private static async IAsyncEnumerable<string> CreateBlockedResponseStream(Task gateTask)
    {
        await gateTask;
        yield return "## WorkICQ request blocked";
    }

    private static IAsyncEnumerable<string> EmptyActivityStream()
    {
        return Stream();

        static async IAsyncEnumerable<string> Stream()
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private static async IAsyncEnumerable<string> SingleChunkResponseStream(string chunk)
    {
        yield return chunk;
        await Task.Yield();
    }

    private sealed class FakeChatShellService : IChatShellService
    {
        public FakeChatShellService(ShellBootstrapState shellState)
        {
            ShellState = shellState;
        }

        public ShellBootstrapState ShellState { get; }

        public List<string> DeletedConversationIds { get; } = new();

        public IReadOnlyList<ShellModelOption> AvailableModels { get; set; } = Array.Empty<ShellModelOption>();

        public Func<ShellSendRequest, CancellationToken, Task<ShellSendResponse>> OnSendAsync { get; set; } =
            (_, _) => throw new NotSupportedException();

        public Task<ShellBootstrapState> LoadShellAsync(int recentLimit = 12, CancellationToken cancellationToken = default)
            => Task.FromResult(ShellState);

        public Task<IReadOnlyList<ShellModelOption>> GetAvailableModelsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(AvailableModels);

        public Task<ShellConversationSnapshot> CreateConversationAsync(string? title = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task DeleteConversationAsync(string conversationId, CancellationToken cancellationToken = default)
        {
            DeletedConversationIds.Add(conversationId);
            return Task.CompletedTask;
        }

        public Task<ShellSendResponse> SendAsync(ShellSendRequest request, CancellationToken cancellationToken = default)
            => OnSendAsync(request, cancellationToken);

        public Task<ShellSetupState> RefreshSetupAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(ShellState.SetupState);

        public Task<ShellSetupState> AcceptWorkIqTermsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(ShellState.SetupState);

        public Task<ShellSetupState> RecordAuthenticationHandoffAsync(string? loginCommand = null, CancellationToken cancellationToken = default)
            => Task.FromResult(ShellState.SetupState);
    }
}
