using System.Diagnostics;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using WorkIQC.App.Models;
using WorkIQC.Persistence;
using WorkIQC.Persistence.Models;
using WorkIQC.Persistence.Services;
using WorkIQC.Runtime.Abstractions;
using WorkIQC.Runtime.Abstractions.Models;

namespace WorkIQC.App.Services;

public sealed class ChatShellService : IChatShellService
{
    private const string LocalHistoryConnectionBadge = "Local history";
    private const string RuntimeSetupConnectionBadge = "Runtime setup";
    private const string BootstrapReadyConnectionBadge = "Bootstrap ready";
    private const string RuntimeConnectionBadge = "WorkICQ runtime";
    private const string WorkIqBlockedConnectionBadge = "WorkICQ blocked";
    private const string LocalHistorySidebarFooter = "Saved conversations reopen from local history, but new prompts still go through the runtime path when it is ready.";
    private const string RuntimeSidebarFooter = "The active reply is coming from the WorkICQ runtime. Local history only keeps the thread and session resume hints.";
    private const string WorkIqBlockingSidebarFooter = "This prompt requires a live WorkICQ call. The app did not answer from local history, saved org data, or placeholder story content.";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ICopilotBootstrap _copilotBootstrap;
    private readonly ISessionCoordinator _sessionCoordinator;
    private readonly IMessageOrchestrator _messageOrchestrator;

    public ChatShellService(
        IServiceScopeFactory scopeFactory,
        ICopilotBootstrap copilotBootstrap,
        ISessionCoordinator sessionCoordinator,
        IMessageOrchestrator messageOrchestrator)
    {
        _scopeFactory = scopeFactory;
        _copilotBootstrap = copilotBootstrap;
        _sessionCoordinator = sessionCoordinator;
        _messageOrchestrator = messageOrchestrator;
    }

    public async Task<ShellBootstrapState> LoadShellAsync(int recentLimit = 12, CancellationToken cancellationToken = default)
    {
        var bootstrapSummary = await TryGetBootstrapSummaryAsync(cancellationToken);
        WriteDiagnostic("bootstrap.load", $"Setup ready: {bootstrapSummary.CanAttemptRuntime}; badge: {bootstrapSummary.ConnectionBadgeText}.");

        using var scope = _scopeFactory.CreateScope();
        var conversationService = scope.ServiceProvider.GetRequiredService<IConversationService>();
        var recentConversations = await conversationService.GetRecentConversationsAsync(recentLimit);
        WriteDiagnostic("history.load", $"Loaded {recentConversations.Count} conversation(s) from local history.");
        if (recentConversations.Count == 0)
        {
            return new ShellBootstrapState(
                Array.Empty<ShellConversationSnapshot>(),
                ResolveConnectionBadge(LocalHistoryConnectionBadge, bootstrapSummary),
                ComposeStatusText(LocalHistorySidebarFooter, bootstrapSummary.SidebarStatusText),
                bootstrapSummary.SetupState);
        }

        var snapshots = new List<ShellConversationSnapshot>(recentConversations.Count);
        foreach (var conversation in recentConversations.OrderByDescending(item => item.UpdatedAt))
        {
            var messages = await conversationService.GetMessagesAsync(conversation.Id);
            var sessionId = await conversationService.GetCopilotSessionIdAsync(conversation.Id);
            snapshots.Add(MapConversation(conversation, messages, sessionId));
        }

        return new ShellBootstrapState(
            snapshots,
            ResolveConnectionBadge(LocalHistoryConnectionBadge, bootstrapSummary),
            ComposeStatusText(LocalHistorySidebarFooter, bootstrapSummary.SidebarStatusText),
            bootstrapSummary.SetupState);
    }

    public async Task<ShellConversationSnapshot> CreateConversationAsync(string? title = null, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var conversationService = scope.ServiceProvider.GetRequiredService<IConversationService>();
        var conversation = await conversationService.CreateConversationAsync(title ?? "New chat");

        return new ShellConversationSnapshot(
            conversation.Id,
            conversation.Title,
            "Waiting for your first question",
            conversation.UpdatedAt.ToLocalTime(),
            IsPersisted: true,
            IsDraft: true,
            SessionId: null,
            Messages: Array.Empty<ShellMessageSnapshot>());
    }

    public async Task<IReadOnlyList<ShellModelOption>> GetAvailableModelsAsync(CancellationToken cancellationToken = default)
    {
        var bootstrapSummary = await TryGetBootstrapSummaryAsync(cancellationToken);
        if (!bootstrapSummary.CanAttemptRuntime)
        {
            return Array.Empty<ShellModelOption>();
        }

        try
        {
            var models = await _sessionCoordinator.ListAvailableModelsAsync(bootstrapSummary.Workspace.WorkspacePath, cancellationToken);
            return models
                .Select(model => new ShellModelOption(
                    model.Id,
                    string.Equals(model.DisplayName, model.Id, StringComparison.OrdinalIgnoreCase)
                        ? model.DisplayName
                        : $"{model.DisplayName} ({model.Id})"))
                .ToArray();
        }
        catch (Exception exception)
        {
            WriteDiagnostic("models.load", $"Accessible model discovery failed: {exception.Message}");
            return Array.Empty<ShellModelOption>();
        }
    }

    public async Task DeleteConversationAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();

        using var scope = _scopeFactory.CreateScope();
        var conversationService = scope.ServiceProvider.GetRequiredService<IConversationService>();
        await conversationService.DeleteConversationAsync(conversationId);
    }

    public async Task<ShellSendResponse> SendAsync(ShellSendRequest request, CancellationToken cancellationToken = default)
    {
        var normalizedModelId = WorkIQTextUtilities.NormalizeModelId(request.ModelId);
        WriteDiagnostic(
            "send.start",
            $"Conversation '{request.ConversationId}' sending WorkICQ-only prompt with stored session '{request.SessionId ?? "<none>"}', model '{normalizedModelId ?? "<default>"}'. Prompt={WorkIQTextUtilities.SummarizePrompt(request.Prompt)}");
        using var scope = _scopeFactory.CreateScope();
        var conversationService = scope.ServiceProvider.GetRequiredService<IConversationService>();

        var conversation = await conversationService.GetConversationAsync(request.ConversationId)
            ?? await conversationService.CreateConversationAsync(request.ConversationTitle);

        if (!string.Equals(conversation.Title, request.ConversationTitle, StringComparison.Ordinal))
        {
            conversation.Title = request.ConversationTitle;
            await conversationService.UpdateConversationAsync(conversation);
        }

        await conversationService.AddMessageAsync(conversation.Id, "user", request.Prompt);

        var runtimePlan = await CreateRuntimePlanAsync(conversationService, conversation.Id, request.Prompt, request.SessionId, normalizedModelId, cancellationToken);
        WriteDiagnostic(
            "send.plan",
            $"Conversation '{conversation.Id}' resolved to badge '{runtimePlan.ConnectionBadgeText}' with session '{runtimePlan.SessionId ?? "<none>"}'.");
        return new ShellSendResponse(
            conversation.Id,
            conversation.Title,
            IsPersisted: true,
            IsDraft: false,
            runtimePlan.SessionId,
            runtimePlan.ConnectionBadgeText,
            runtimePlan.SidebarFooterText,
            runtimePlan.ResponseStream,
            runtimePlan.ActivityStream);
    }

    public async Task<ShellSetupState> RefreshSetupAsync(CancellationToken cancellationToken = default)
        => (await TryGetBootstrapSummaryAsync(cancellationToken)).SetupState;

    public async Task<ShellSetupState> AcceptWorkIqTermsAsync(CancellationToken cancellationToken = default)
    {
        await _copilotBootstrap.AcceptEulaAsync(cancellationToken);
        return await RefreshSetupAsync(cancellationToken);
    }

    public async Task<ShellSetupState> RecordAuthenticationHandoffAsync(string? loginCommand = null, CancellationToken cancellationToken = default)
    {
        await _copilotBootstrap.RecordAuthenticationHandoffAsync(loginCommand, cancellationToken);
        return await RefreshSetupAsync(cancellationToken);
    }

    private async Task<RuntimePlan> CreateRuntimePlanAsync(
        IConversationService conversationService,
        string conversationId,
        string prompt,
        string? sessionId,
        string? normalizedModelId,
        CancellationToken cancellationToken)
    {
        BootstrapSummary? bootstrapSummary = null;

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            sessionId = await conversationService.GetCopilotSessionIdAsync(conversationId);
        }

        try
        {
            bootstrapSummary = await TryGetBootstrapSummaryAsync(cancellationToken);
            WriteDiagnostic(
                "runtime.availability",
                $"Runtime ready={bootstrapSummary.CanAttemptRuntime}; workiqOnlyShell=true; workspace='{bootstrapSummary.Workspace.WorkspacePath}'; mcp='{bootstrapSummary.Workspace.McpConfigPath}'.");
            if (!bootstrapSummary.CanAttemptRuntime)
            {
                WriteDiagnostic(
                    "runtime.plan",
                    "Bootstrap blockers remain, so this turn is blocked until the live WorkICQ path is available.");
                return CreateBlockedPlan(
                    conversationId,
                    prompt,
                    sessionId,
                    ComposeStatusText(bootstrapSummary.SidebarStatusText, bootstrapSummary.SetupState.Blockers.FirstOrDefault()?.Text));
            }

            var sessionConfiguration = new SessionConfiguration
            {
                WorkspacePath = bootstrapSummary.Workspace.WorkspacePath,
                McpConfigPath = bootstrapSummary.Workspace.McpConfigPath,
                ModelId = normalizedModelId,
                AllowedTools = WorkIQRuntimeDefaults.SessionAllowedToolNames,
                SystemGuidance = new Dictionary<string, string>
                {
                    ["app-identity"] = "The app name WorkIQC identifies the desktop shell only. It is not a knowledge source.",
                    ["answer-sources"] = "Use live Copilot SDK reasoning and the allowed WorkICQ MCP tool path only. Never answer from local history, sample conversations, placeholder content, or setup metadata.",
                    ["tool-posture"] = "WorkICQ-required",
                    ["tool-requirement"] = "Invoke a WorkICQ MCP tool on every user turn before producing any final answer. If no WorkICQ tool can be used, do not answer the question and explicitly say that WorkICQ execution is required for this shell.",
                    ["current-user"] = "For first-person workplace requests such as 'my direct reports', 'my manager', 'my meetings', or 'who reports to me', treat 'me', 'my', and 'I' as the currently authenticated Copilot/WorkICQ user.",
                    ["principal-resolution"] = "Invoke WorkICQ before answering first-person workplace questions. Only ask the user for their own name or work email if the WorkICQ tool explicitly reports that the signed-in principal could not be resolved or multiple people matched.",
                    ["calendar-routing"] = "Calendar and schedule prompts such as 'what is my schedule on Wednesday' must still invoke WorkICQ before any answer is produced.",
                    ["eula-recovery"] = "If WorkICQ reports that EULA acceptance is required, stop and tell the user to complete the WorkICQ consent bootstrap in Settings before retrying the request.",
                    ["allowed-tools"] = "All WorkICQ MCP tools (ask_work_iq, accept_eula, and any future additions)",
                    ["ui-contract"] = "Stream markdown-rich assistant text and keep WorkICQ usage explicit."
                }
            };

            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                if (await ShouldCreateFreshSessionForModelAsync(sessionId, normalizedModelId, cancellationToken))
                {
                    WriteDiagnostic("runtime.model", $"Starting a fresh runtime session because stored session '{sessionId}' is not safe to reuse for model '{normalizedModelId}'.");
                    await TryDisposeSessionAsync(sessionId, cancellationToken);
                    sessionId = null;
                }
                else
                {
                    WriteDiagnostic("runtime.resume", $"Attempting to resume persisted session '{sessionId}' with model '{normalizedModelId ?? "<default>"}'.");
                    var resumed = await _sessionCoordinator.ResumeSessionAsync(sessionId, normalizedModelId, cancellationToken);
                    if (!resumed)
                    {
                        WriteDiagnostic("runtime.resume", $"Stored session '{sessionId}' could not be resumed. Creating a fresh session.");
                        sessionId = null;
                    }
                }
            }

            sessionId ??= await _sessionCoordinator.CreateSessionAsync(sessionConfiguration, cancellationToken);
            WriteDiagnostic("runtime.session", $"Using live runtime session '{sessionId}'.");

            await conversationService.SetCopilotSessionIdAsync(conversationId, sessionId);
            WriteDiagnostic("runtime.persist", $"Stored session resume metadata for conversation '{conversationId}' as '{sessionId}'.");
            WriteDiagnostic(
                "runtime.dispatch",
                $"Dispatching prompt to Copilot SDK session '{sessionId}' with model '{normalizedModelId ?? "<default>"}', allowed tools [{string.Join(", ", sessionConfiguration.AllowedTools)}], and MCP config '{sessionConfiguration.McpConfigPath}'.");

            var runtimePrompt = BuildRuntimePrompt(prompt);

            var sendResponse = await _messageOrchestrator.SendMessageAsync(
                new SendMessageRequest
                {
                    ConversationId = conversationId,
                    UserMessage = runtimePrompt,
                    SessionId = sessionId
                },
                cancellationToken);

            if (!sendResponse.Success)
            {
                WriteDiagnostic(
                    "runtime.send",
                    $"Runtime send failed before streaming. Code: {sendResponse.ErrorCode ?? "<none>"}; message: {sendResponse.ErrorMessage ?? "<none>"}.");
                return CreateBlockedPlan(
                    conversationId,
                    prompt,
                    sessionId,
                    ComposeStatusText(
                        "The live WorkICQ handoff failed before the request could run.",
                        sendResponse.ErrorMessage ?? sendResponse.ErrorCode));
            }

            var resolvedSessionId = sendResponse.SessionId;
            await conversationService.SetCopilotSessionIdAsync(conversationId, resolvedSessionId);
            WriteDiagnostic(
                "runtime.send",
                $"Runtime accepted turn for session '{resolvedSessionId}' and message '{sendResponse.MessageId}'. Resume metadata updated for conversation '{conversationId}'.");

            var turnMonitor = StartTurnMonitor(resolvedSessionId, cancellationToken);

            return new RuntimePlan(
                resolvedSessionId,
                RuntimeConnectionBadge,
                RuntimeSidebarFooter,
                StreamRuntimeResponseAsync(conversationId, prompt, resolvedSessionId, turnMonitor, cancellationToken),
                StreamRuntimeActivityAsync(turnMonitor, cancellationToken));
        }
        catch (RuntimeException exception)
        {
            WriteDiagnostic("runtime.error", exception.Message);
            var resolution = exception is UnsupportedRuntimeActionException unsupported
                ? unsupported.Resolution
                : null;

            return CreateBlockedPlan(
                conversationId,
                prompt,
                sessionId,
                ComposeStatusText(bootstrapSummary?.SidebarStatusText, exception.Message, resolution));
        }
        catch (Exception exception)
        {
            WriteDiagnostic("runtime.error", $"Unexpected runtime handoff failure: {exception}");
            return CreateBlockedPlan(
                conversationId,
                prompt,
                sessionId,
                ComposeStatusText(
                    bootstrapSummary?.SidebarStatusText,
                    $"Unexpected live WorkICQ failure: {exception.Message}"));
        }
    }

    private RuntimePlan CreateBlockedPlan(
        string conversationId,
        string prompt,
        string? sessionId,
        string blockingReason)
    {
        return new RuntimePlan(
            sessionId,
            WorkIqBlockedConnectionBadge,
            ComposeStatusText(
                WorkIqBlockingSidebarFooter,
                blockingReason),
            StreamBlockingResponseAsync(conversationId, prompt, blockingReason),
            EmptyActivityStream());
    }

    private async Task<bool> ShouldCreateFreshSessionForModelAsync(string sessionId, string? normalizedModelId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(normalizedModelId))
        {
            return false;
        }

        var state = await _sessionCoordinator.GetSessionStateAsync(sessionId, cancellationToken);
        if (string.Equals(state.ErrorCode, "runtime.session.not-found", StringComparison.Ordinal))
        {
            return true;
        }

        return !string.Equals(state.ModelId, normalizedModelId, StringComparison.OrdinalIgnoreCase);
    }

    private async Task TryDisposeSessionAsync(string sessionId, CancellationToken cancellationToken)
    {
        try
        {
            await _sessionCoordinator.DisposeSessionAsync(sessionId, cancellationToken);
        }
        catch (RuntimeException exception)
        {
            WriteDiagnostic("runtime.dispose", $"Session '{sessionId}' cleanup failed during model switch: {exception.Message}");
        }
    }

    private async Task<BootstrapSummary> GetBootstrapSummaryAsync(CancellationToken cancellationToken)
    {
        var workspace = await _copilotBootstrap.InitializeWorkspaceAsync(StorageHelper.GetWorkspacePath(), cancellationToken: cancellationToken);
        var dependencies = await _copilotBootstrap.EnsureRuntimeDependenciesAsync(cancellationToken);
        var workIq = await _copilotBootstrap.EnsureWorkIQAvailableAsync(cancellationToken: cancellationToken);
        var eula = await _copilotBootstrap.VerifyEulaAcceptanceAsync(cancellationToken);
        var auth = await _copilotBootstrap.VerifyAuthenticationHandoffAsync(cancellationToken);

        var blockers = dependencies.Dependencies
            .Where(static dependency => !dependency.IsAvailable)
            .Select(dependency => new SetupCheckItem(dependency.Details ?? $"{dependency.Name} is unavailable.", false))
            .Concat(dependencies.Capabilities.Where(static capability => capability.Status != RuntimeCapabilityStatus.Available)
                .Select(capability => new SetupCheckItem(capability.Resolution ?? capability.Details ?? $"{capability.Name} requires attention.", false)))
            .Concat(workIq.Capabilities.Where(static capability => capability.Status != RuntimeCapabilityStatus.Available)
                .Select(capability => new SetupCheckItem(capability.Resolution ?? capability.Details ?? $"{capability.Name} requires attention.", false)))
            .ToList();

        if (!eula.CanProceed)
        {
            blockers.Add(new SetupCheckItem(eula.Resolution ?? eula.Details ?? "WorkICQ EULA acceptance is still required.", false));
        }

        if (!auth.CanProceed)
        {
            blockers.Add(new SetupCheckItem(auth.Resolution ?? auth.Details ?? "Copilot authentication handoff is still required.", false));
        }

        var prerequisites = dependencies.Dependencies
            .Select(dependency => new SetupCheckItem(
                dependency.IsAvailable
                    ? $"{dependency.Name}: ready ({dependency.ResolvedPath ?? "resolved"})"
                    : $"{dependency.Name}: {dependency.Details ?? "missing"}",
                dependency.IsAvailable))
            .Concat(
                workIq.Capabilities.Select(capability =>
                    new SetupCheckItem(
                        $"{capability.Name}: {(capability.Status == RuntimeCapabilityStatus.Available ? "ready" : capability.Resolution ?? capability.Details ?? "attention required")}",
                        capability.Status == RuntimeCapabilityStatus.Available)))
            .Append(new SetupCheckItem(
                eula.CanProceed
                    ? $"WorkICQ.eula: accepted ({eula.MarkerPath})"
                    : $"WorkICQ.eula: {eula.Details ?? eula.Resolution ?? "attention required"}",
                eula.CanProceed))
            .Append(new SetupCheckItem(
                auth.CanProceed
                    ? $"copilot.auth: ready ({auth.Details ?? auth.MarkerPath})"
                    : $"copilot.auth: {auth.Resolution ?? auth.Details ?? "attention required"}",
                auth.CanProceed))
            .ToList();

        var statusText = blockers.Count > 0
            ? $"WorkICQ bootstrap still needs attention before the next live session: {blockers[0].Text}"
            : "WorkICQ bootstrap is ready. Saved history only reopens the thread; the next send can create or resume a live WorkICQ-backed session.";

        WriteDiagnostic(
            "bootstrap.summary",
            $"Ready: {blockers.Count == 0}; blockers: {blockers.Count}; workspace: {workspace.WorkspacePath}.");

        var setupState = new ShellSetupState(
            RequiresUserAction: blockers.Count > 0,
            CanAttemptRuntime: blockers.Count == 0,
            IsEulaAccepted: eula.CanProceed,
            IsAuthenticationHandoffStarted: auth.CanProceed,
            SummaryText: statusText,
            WorkIQPackageReference: workspace.WorkIQPackageReference,
            WorkspacePath: workspace.WorkspacePath,
            McpConfigPath: workspace.McpConfigPath,
            EulaUrl: WorkIQRuntimeDefaults.EulaUrl,
            EulaMarkerPath: eula.MarkerPath,
            AuthenticationMarkerPath: auth.MarkerPath,
            AuthenticationCommandLine: auth.LoginCommand,
            Blockers: blockers,
            Prerequisites: prerequisites);

        return new BootstrapSummary(
            blockers.Count > 0 ? RuntimeSetupConnectionBadge : BootstrapReadyConnectionBadge,
            statusText,
            blockers.Count == 0,
            workspace,
            setupState);
    }

    private async Task<BootstrapSummary> TryGetBootstrapSummaryAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await GetBootstrapSummaryAsync(cancellationToken);
        }
        catch (RuntimeException exception)
        {
            WriteDiagnostic("bootstrap.error", exception.ToString());
            return new BootstrapSummary(
                RuntimeSetupConnectionBadge,
                $"WorkICQ bootstrap check failed: {exception.Message}",
                false,
                CreateWorkspacePlaceholder(),
                CreateSetupPlaceholder($"WorkICQ bootstrap check failed: {exception.Message}"));
        }
        catch (Exception exception)
        {
            WriteDiagnostic("bootstrap.error", exception.ToString());
            return new BootstrapSummary(
                RuntimeSetupConnectionBadge,
                $"WorkICQ bootstrap check failed unexpectedly: {exception.Message}",
                false,
                CreateWorkspacePlaceholder(),
                CreateSetupPlaceholder($"WorkICQ bootstrap check failed unexpectedly: {exception.Message}"));
        }
    }

    private async IAsyncEnumerable<string> StreamRuntimeResponseAsync(
        string conversationId,
        string prompt,
        string sessionId,
        WorkIqTurnMonitor turnMonitor,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        var bufferedChunks = new List<string>();

        await using var enumerator = _messageOrchestrator
            .StreamResponseAsync(sessionId, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);

        RuntimeException? runtimeFailure = null;

        while (true)
        {
            StreamingDelta delta;

            try
            {
                if (!await enumerator.MoveNextAsync())
                {
                    break;
                }
                delta = enumerator.Current;
            }
            catch (RuntimeException exception)
            {
                runtimeFailure = exception;
                break;
            }

            if (string.IsNullOrEmpty(delta.Content))
            {
                continue;
            }

            if (!turnMonitor.HasObservedWorkIqTool)
            {
                bufferedChunks.Add(delta.Content);
                continue;
            }

            foreach (var bufferedChunk in ReleaseBufferedChunks(bufferedChunks, builder))
            {
                yield return bufferedChunk;
            }

            builder.Append(delta.Content);
            yield return delta.Content;
        }

        await turnMonitor.ObservationTask;

        if (!turnMonitor.HasObservedWorkIqTool)
        {
            WriteDiagnostic("runtime.stream", "Discarding assistant output because the turn completed without any WorkICQ tool invocation.");
            var blockingResponse = BuildMissingWorkIqToolResponse(prompt);
            foreach (var chunk in StreamChunks(blockingResponse))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(32, cancellationToken);
                yield return chunk;
            }

            await PersistAssistantMessageAsync(conversationId, blockingResponse, cancellationToken);
            yield break;
        }

        foreach (var bufferedChunk in ReleaseBufferedChunks(bufferedChunks, builder))
        {
            yield return bufferedChunk;
        }

        if (runtimeFailure is not null)
        {
            if (builder.Length == 0)
            {
                var fallbackResponse = BuildBlockingResponse(prompt, $"The live WorkICQ stream ended before any answer arrived. {runtimeFailure.Message}");

                WriteDiagnostic(
                    "runtime.stream",
                    "Runtime stream ended before producing an assistant answer. Returning a blocking error instead of placeholder content.");

                foreach (var chunk in StreamChunks(fallbackResponse))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await Task.Delay(32, cancellationToken);
                    builder.Append(chunk);
                    yield return chunk;
                }

                await PersistAssistantMessageAsync(conversationId, builder.ToString().Trim(), cancellationToken);
                yield break;
            }

            var failureNote = $"{Environment.NewLine}{Environment.NewLine}_Live WorkICQ execution stopped early: {runtimeFailure.Message}_";
            builder.Append(failureNote);
            WriteDiagnostic("runtime.stream", $"Runtime stream ended early after {builder.Length} chars: {runtimeFailure.Message}");
            yield return failureNote;

            await PersistAssistantMessageAsync(conversationId, builder.ToString().Trim(), cancellationToken);
            yield break;
        }

        var assistantContent = builder.ToString().Trim();
        if (assistantContent.Length == 0)
        {
            WriteDiagnostic(
                "runtime.stream",
                "Runtime finished without assistant content. Returning a blocking error instead of placeholder content.");
            assistantContent = BuildBlockingResponse(prompt, "The live WorkICQ runtime finished without returning answer content.");
            foreach (var chunk in StreamChunks(assistantContent))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(32, cancellationToken);
                yield return chunk;
            }
        }

        await PersistAssistantMessageAsync(conversationId, assistantContent, cancellationToken);
        WriteDiagnostic("runtime.stream", $"Persisted assistant transcript for conversation '{conversationId}' ({assistantContent.Length} chars).");
    }

    private WorkIqTurnMonitor StartTurnMonitor(string sessionId, CancellationToken cancellationToken)
    {
        var turnMonitor = new WorkIqTurnMonitor();
        turnMonitor.AttachObservationTask(ObserveRuntimeActivityAsync(sessionId, turnMonitor, cancellationToken));
        return turnMonitor;
    }

    private async Task ObserveRuntimeActivityAsync(string sessionId, WorkIqTurnMonitor turnMonitor, CancellationToken cancellationToken)
    {
        await using var enumerator = _messageOrchestrator
            .ObserveToolEventsAsync(sessionId, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);

        try
        {
            while (true)
            {
                ToolEvent toolEvent;

                try
                {
                    if (!await enumerator.MoveNextAsync())
                    {
                        break;
                    }

                    toolEvent = enumerator.Current;
                }
                catch (RuntimeException exception)
                {
                    WriteDiagnostic("runtime.tool", $"Activity stream unavailable: {exception.Message}");
                    await WriteActivityUpdateAsync(turnMonitor, $"Runtime activity stream unavailable: {exception.Message}", cancellationToken);
                    break;
                }

                if (IsWorkIqTool(toolEvent.ToolName))
                {
                    turnMonitor.MarkWorkIqToolInvoked();
                }

                var activity = FormatActivity(toolEvent);
                WriteDiagnostic("runtime.tool", $"{toolEvent.EventType}: {FormatToolDisplayName(toolEvent.ToolName)}{(string.IsNullOrEmpty(activity) ? "" : $" — {activity}")}");
                await WriteActivityUpdateAsync(turnMonitor, activity, cancellationToken);
            }
        }
        finally
        {
            turnMonitor.CompleteActivity();
        }
    }

    private async Task WriteActivityUpdateAsync(WorkIqTurnMonitor turnMonitor, string activity, CancellationToken cancellationToken)
    {
        try
        {
            await turnMonitor.ActivityWriter.WriteAsync(activity, cancellationToken);
        }
        catch (ChannelClosedException)
        {
            WriteDiagnostic("runtime.tool", "Activity channel closed before the latest update could be published.");
        }
    }

    private async IAsyncEnumerable<string> StreamRuntimeActivityAsync(
        WorkIqTurnMonitor turnMonitor,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var activity in turnMonitor.ReadActivityAsync(cancellationToken))
        {
            yield return activity;
        }
    }

    private async IAsyncEnumerable<string> StreamBlockingResponseAsync(
        string conversationId,
        string prompt,
        string blockingReason,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var resolvedReason = string.IsNullOrWhiteSpace(blockingReason)
            ? "The live WorkICQ path is not available yet."
            : blockingReason;
        var response = BuildBlockingResponse(prompt, resolvedReason);
        foreach (var chunk in StreamChunks(response))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(32, cancellationToken);
            yield return chunk;
        }

        await PersistAssistantMessageAsync(conversationId, response, cancellationToken);
    }

    private async Task PersistAssistantMessageAsync(string conversationId, string content, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var conversationService = scope.ServiceProvider.GetRequiredService<IConversationService>();
        await conversationService.AddMessageAsync(conversationId, "assistant", content);
        WriteDiagnostic("runtime.persist", $"Stored assistant transcript for conversation '{conversationId}' ({content.Length} chars).");
    }

    private static ShellConversationSnapshot MapConversation(Conversation conversation, IReadOnlyList<Message> messages, string? sessionId)
    {
        var orderedMessages = messages
            .OrderBy(message => message.Timestamp)
            .Select(MapMessage)
            .ToList();

        var preview = orderedMessages
            .OrderByDescending(message => message.Timestamp)
            .Select(message => message.Content.Replace(Environment.NewLine, " "))
            .FirstOrDefault();

        return new ShellConversationSnapshot(
            conversation.Id,
            string.IsNullOrWhiteSpace(conversation.Title) ? "New chat" : conversation.Title,
            string.IsNullOrWhiteSpace(preview) ? "Waiting for your first question" : preview,
            conversation.UpdatedAt.ToLocalTime(),
            IsPersisted: true,
            IsDraft: orderedMessages.Count == 0,
            sessionId,
            orderedMessages);
    }

    private static ShellMessageSnapshot MapMessage(Message message)
    {
        var isUser = string.Equals(message.Role, "user", StringComparison.OrdinalIgnoreCase);
        return new ShellMessageSnapshot(
            isUser ? ChatRole.User : ChatRole.Assistant,
            isUser ? "You" : "WorkICQ",
            message.Content,
            message.Timestamp.ToLocalTime());
    }

    private static string BuildBlockingResponse(string prompt, string blockingReason)
    {
        var topic = WorkIQTextUtilities.BuildConversationTitle(prompt);
        const string heading = "## WorkICQ request blocked";
        const string route = "This WorkICQ-only shell only accepts replies that come through the live Copilot SDK + WorkICQ MCP path.";
        return
            $"{heading}\n\n" +
            $"I can't answer \"{topic}\" from local history, sample conversations, setup metadata, or placeholder content.\n" +
            $"{route}\n\n" +
            $"**Blocking issue:** {blockingReason}\n\n" +
            "Retry once the runtime is ready so the app can fetch a live response.";
    }

    private static string BuildMissingWorkIqToolResponse(string prompt)
        => BuildBlockingResponse(
            prompt,
            "The runtime returned assistant text without invoking a WorkICQ tool. This shell only accepts prompts that execute through WorkICQ on every turn.");

    private static string BuildRuntimePrompt(string prompt)
        => $$"""
        WorkICQ-only shell requirement:
        - You must invoke a WorkICQ MCP tool on this turn before producing any final answer.
        - If WorkICQ cannot be invoked, do not answer from general knowledge or local history.
        - Treat first-person workplace references such as me, my, and I as the currently authenticated WorkICQ user.
        - Calendar and schedule questions must also go through WorkICQ.

        User request:
        {{prompt.Trim()}}
        """;

    private static IEnumerable<string> StreamChunks(string response)
    {
        foreach (var word in response.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            yield return $"{word} ";
        }
    }

    private static IEnumerable<string> ReleaseBufferedChunks(List<string> bufferedChunks, StringBuilder builder)
    {
        if (bufferedChunks.Count == 0)
        {
            yield break;
        }

        foreach (var chunk in bufferedChunks)
        {
            builder.Append(chunk);
            yield return chunk;
        }

        bufferedChunks.Clear();
    }

    private static string ResolveConnectionBadge(string dataConnectionBadge, BootstrapSummary bootstrapSummary)
        => bootstrapSummary.ConnectionBadgeText == BootstrapReadyConnectionBadge
            ? BootstrapReadyConnectionBadge
            : dataConnectionBadge;

    private static string ComposeStatusText(params string?[] parts)
        => string.Join(" ", parts.Where(static part => !string.IsNullOrWhiteSpace(part)).Select(static part => part!.Trim()));

    private static string FormatActivity(ToolEvent toolEvent)
        => toolEvent.EventType switch
        {
            ToolEventType.Started => FormatToolDisplayName(toolEvent.ToolName) == "WorkICQ"
                ? "Querying WorkICQ…"
                : $"{FormatToolDisplayName(toolEvent.ToolName)} started",
            ToolEventType.Progress when toolEvent.ToolName == "thinking"
                => TruncateThinking(toolEvent.StatusMessage ?? "Reasoning…"),
            ToolEventType.Progress => toolEvent.StatusMessage ?? $"{FormatToolDisplayName(toolEvent.ToolName)} is still working",
            ToolEventType.Completed => string.Empty,
            ToolEventType.Failed => string.Empty,
            _ => toolEvent.StatusMessage ?? FormatToolDisplayName(toolEvent.ToolName)
        };

    private static bool IsWorkIqTool(string toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            return false;
        }

        return toolName.Contains("ask_work_iq", StringComparison.OrdinalIgnoreCase)
            || toolName.Contains("accept_eula", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, WorkIQRuntimeDefaults.ServerName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, "workicq", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatToolDisplayName(string toolName)
        => IsWorkIqTool(toolName)
            ? "WorkICQ"
            : toolName;

    private static string TruncateThinking(string text)
    {
        const int maxLength = 80;
        var singleLine = text.ReplaceLineEndings(" ").Trim();
        return singleLine.Length <= maxLength
            ? $"💭 {singleLine}"
            : $"💭 {singleLine[..maxLength].TrimEnd()}…";
    }

    private static void WriteDiagnostic(string stage, string message)
        => Trace.WriteLine($"[{DateTimeOffset.Now:O}] [WorkIQC.ChatShell] [{stage}] {message}");

    private static WorkspaceInitializationResult CreateWorkspacePlaceholder()
        => new()
        {
            WorkspacePath = StorageHelper.GetWorkspacePath(),
            CopilotDirectoryPath = Path.Combine(StorageHelper.GetWorkspacePath(), ".copilot"),
            McpConfigPath = StorageHelper.GetCopilotConfigPath(),
            WorkIQPackageReference = WorkIQRuntimeDefaults.PackageReference,
            UsesLatestWorkIQPackage = true,
            ConfigWasWritten = false
        };

    private static ShellSetupState CreateSetupPlaceholder(string summaryText)
        => new(
            RequiresUserAction: true,
            CanAttemptRuntime: false,
            IsEulaAccepted: false,
            IsAuthenticationHandoffStarted: false,
            SummaryText: summaryText,
            WorkIQPackageReference: WorkIQRuntimeDefaults.PackageReference,
            WorkspacePath: StorageHelper.GetWorkspacePath(),
            McpConfigPath: StorageHelper.GetCopilotConfigPath(),
            EulaUrl: WorkIQRuntimeDefaults.EulaUrl,
            EulaMarkerPath: WorkIQRuntimeDefaults.GetEulaMarkerPath(StorageHelper.GetWorkspacePath()),
            AuthenticationMarkerPath: WorkIQRuntimeDefaults.GetAuthenticationMarkerPath(StorageHelper.GetWorkspacePath()),
            AuthenticationCommandLine: WorkIQRuntimeDefaults.CopilotLoginCommand,
            Blockers: [new SetupCheckItem(summaryText, false)],
            Prerequisites: []);

    private static IAsyncEnumerable<string> EmptyActivityStream() => AsyncEnumerable.Empty<string>();

    private sealed class WorkIqTurnMonitor
    {
        private readonly Channel<string> _activityChannel = Channel.CreateUnbounded<string>();
        private int _hasObservedWorkIqTool;
        private Task? _observationTask;

        public bool HasObservedWorkIqTool => Volatile.Read(ref _hasObservedWorkIqTool) == 1;

        public ChannelWriter<string> ActivityWriter => _activityChannel.Writer;

        public Task ObservationTask => _observationTask ?? Task.CompletedTask;

        public void AttachObservationTask(Task observationTask)
            => _observationTask = observationTask;

        public void MarkWorkIqToolInvoked()
            => Interlocked.Exchange(ref _hasObservedWorkIqTool, 1);

        public void CompleteActivity()
            => _activityChannel.Writer.TryComplete();

        public async IAsyncEnumerable<string> ReadActivityAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            while (await _activityChannel.Reader.WaitToReadAsync(cancellationToken))
            {
                while (_activityChannel.Reader.TryRead(out var activity))
                {
                    yield return activity;
                }
            }
        }
    }

    private sealed record RuntimePlan(
        string? SessionId,
        string ConnectionBadgeText,
        string SidebarFooterText,
        IAsyncEnumerable<string> ResponseStream,
        IAsyncEnumerable<string> ActivityStream);

    private sealed record BootstrapSummary(
        string ConnectionBadgeText,
        string SidebarStatusText,
        bool CanAttemptRuntime,
        WorkspaceInitializationResult Workspace,
        ShellSetupState SetupState);
}
