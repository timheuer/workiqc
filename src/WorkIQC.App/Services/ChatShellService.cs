using System.Diagnostics;
using System.Text;
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
    private const string RuntimeBlockedConnectionBadge = "Runtime blocked";
    private const string WorkIqBlockedConnectionBadge = "WorkICQ blocked";
    private const string LocalHistorySidebarFooter = "Saved conversations reopen from local history, but new prompts still go through the runtime path when it is ready.";
    private const string RuntimeSidebarFooter = "The active reply is coming from the WorkICQ runtime. Local history only keeps the thread and session resume hints.";
    private const string RuntimeBlockedSidebarFooter = "This turn was not answered locally. The app only returns live Copilot SDK output routed through the configured WorkICQ MCP path.";
    private const string WorkIqBlockingSidebarFooter = "This workplace request requires a live WorkICQ call. The app did not answer from local history, saved org data, or placeholder story content.";
    private static readonly string[] LiveWorkIqPromptMarkers =
    [
        "direct report",
        "org chart",
        "organization chart",
        "organisation chart",
        "organization",
        "organisation",
        "reporting chain",
        "team member",
        "my team",
        "manager",
        "employee",
        "coworker",
        "colleague",
        "people on my team",
        "who do i manage",
        "who reports to me"
    ];

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
        var requiresLiveWorkIq = RequiresLiveWorkIqPrompt(request.Prompt);
        WriteDiagnostic(
            "send.start",
            $"Conversation '{request.ConversationId}' sending {(requiresLiveWorkIq ? "workplace" : "general")} prompt with stored session '{request.SessionId ?? "<none>"}'. Prompt={SummarizePrompt(request.Prompt)}");
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

        var runtimePlan = await CreateRuntimePlanAsync(conversationService, conversation.Id, request.Prompt, request.SessionId, cancellationToken);
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
        CancellationToken cancellationToken)
    {
        BootstrapSummary? bootstrapSummary = null;
        var requiresLiveWorkIq = RequiresLiveWorkIqPrompt(prompt);

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            sessionId = await conversationService.GetCopilotSessionIdAsync(conversationId);
        }

        try
        {
            bootstrapSummary = await TryGetBootstrapSummaryAsync(cancellationToken);
            WriteDiagnostic(
                "runtime.availability",
                $"Runtime ready={bootstrapSummary.CanAttemptRuntime}; workplacePrompt={requiresLiveWorkIq}; workspace='{bootstrapSummary.Workspace.WorkspacePath}'; mcp='{bootstrapSummary.Workspace.McpConfigPath}'.");
            if (!bootstrapSummary.CanAttemptRuntime)
            {
                WriteDiagnostic(
                    "runtime.plan",
                    requiresLiveWorkIq
                        ? "Bootstrap blockers remain, so this workplace/org turn is blocked until the live WorkICQ path is available."
                        : "Bootstrap blockers remain, so this turn is blocked instead of answering from local content.");
                return CreateBlockedPlan(
                    conversationId,
                    prompt,
                    sessionId,
                    ComposeStatusText(bootstrapSummary.SidebarStatusText, bootstrapSummary.SetupState.Blockers.FirstOrDefault()?.Text),
                    requiresLiveWorkIq);
            }

            var sessionConfiguration = new SessionConfiguration
            {
                WorkspacePath = bootstrapSummary.Workspace.WorkspacePath,
                McpConfigPath = bootstrapSummary.Workspace.McpConfigPath,
                AllowedTools = WorkIQRuntimeDefaults.SessionAllowedToolNames,
                SystemGuidance = new Dictionary<string, string>
                {
                    ["app-identity"] = "The app name WorkIQC identifies the desktop shell only. It is not a knowledge source.",
                    ["answer-sources"] = "Use live Copilot SDK reasoning and the allowed WorkICQ MCP tool path only. Never answer from local history, sample conversations, placeholder content, or setup metadata.",
                    ["tool-posture"] = "WorkICQ-first",
                    ["current-user"] = "For first-person workplace requests such as 'my direct reports', 'my manager', 'my meetings', or 'who reports to me', treat 'me', 'my', and 'I' as the currently authenticated Copilot/WorkICQ user.",
                    ["principal-resolution"] = "Invoke WorkICQ first for first-person workplace questions. Only ask the user for their own name or work email if the WorkICQ tool explicitly reports that the signed-in principal could not be resolved or multiple people matched.",
                    ["eula-recovery"] = "If WorkICQ reports that EULA acceptance is required, stop and tell the user to complete the WorkICQ consent bootstrap in Settings before retrying the request.",
                    ["allowed-tools"] = "All WorkICQ MCP tools (ask_work_iq, accept_eula, and any future additions)",
                    ["ui-contract"] = "Stream markdown-rich assistant text and keep WorkICQ usage explicit."
                }
            };

            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                WriteDiagnostic("runtime.resume", $"Attempting to resume persisted session '{sessionId}'.");
                var resumed = await _sessionCoordinator.ResumeSessionAsync(sessionId, cancellationToken);
                if (!resumed)
                {
                    WriteDiagnostic("runtime.resume", $"Stored session '{sessionId}' could not be resumed. Creating a fresh session.");
                    sessionId = null;
                }
            }

            sessionId ??= await _sessionCoordinator.CreateSessionAsync(sessionConfiguration, cancellationToken);
            WriteDiagnostic("runtime.session", $"Using live runtime session '{sessionId}'.");

            await conversationService.SetCopilotSessionIdAsync(conversationId, sessionId);
            WriteDiagnostic("runtime.persist", $"Stored session resume metadata for conversation '{conversationId}' as '{sessionId}'.");
            WriteDiagnostic(
                "runtime.dispatch",
                $"Dispatching prompt to Copilot SDK session '{sessionId}' with allowed tools [{string.Join(", ", sessionConfiguration.AllowedTools)}] and MCP config '{sessionConfiguration.McpConfigPath}'.");

            var sendResponse = await _messageOrchestrator.SendMessageAsync(
                new SendMessageRequest
                {
                    ConversationId = conversationId,
                    UserMessage = prompt,
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
                        requiresLiveWorkIq
                            ? "The live WorkICQ handoff failed before the request could run."
                            : "The Copilot SDK handoff failed before the request could run.",
                        sendResponse.ErrorMessage ?? sendResponse.ErrorCode),
                    requiresLiveWorkIq);
            }

            var resolvedSessionId = sendResponse.SessionId;
            await conversationService.SetCopilotSessionIdAsync(conversationId, resolvedSessionId);
            WriteDiagnostic(
                "runtime.send",
                $"Runtime accepted turn for session '{resolvedSessionId}' and message '{sendResponse.MessageId}'. Resume metadata updated for conversation '{conversationId}'.");

            return new RuntimePlan(
                resolvedSessionId,
                RuntimeConnectionBadge,
                RuntimeSidebarFooter,
                StreamRuntimeResponseAsync(conversationId, prompt, resolvedSessionId, cancellationToken),
                StreamRuntimeActivityAsync(resolvedSessionId, cancellationToken));
        }
        catch (RuntimeException exception)
        {
            WriteDiagnostic("runtime.error", exception.Message);
            var resolution = exception is UnsupportedRuntimeActionException unsupported
                ? unsupported.Resolution
                : null;

            if (requiresLiveWorkIq)
            {
                return CreateBlockedPlan(
                    conversationId,
                    prompt,
                    sessionId,
                    ComposeStatusText(exception.Message, resolution),
                    requiresLiveWorkIq);
            }

            return CreateBlockedPlan(
                conversationId,
                prompt,
                sessionId,
                ComposeStatusText(
                    bootstrapSummary?.SidebarStatusText,
                    exception.Message,
                    resolution),
                requiresLiveWorkIq);
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
                    requiresLiveWorkIq
                        ? $"Unexpected live WorkICQ failure: {exception.Message}"
                        : $"Unexpected Copilot SDK handoff failure: {exception.Message}"),
                requiresLiveWorkIq);
        }
    }

    private RuntimePlan CreateBlockedPlan(
        string conversationId,
        string prompt,
        string? sessionId,
        string blockingReason,
        bool requiresLiveWorkIq)
    {
        return new RuntimePlan(
            sessionId,
            requiresLiveWorkIq ? WorkIqBlockedConnectionBadge : RuntimeBlockedConnectionBadge,
            ComposeStatusText(
                requiresLiveWorkIq ? WorkIqBlockingSidebarFooter : RuntimeBlockedSidebarFooter,
                blockingReason),
            StreamBlockingResponseAsync(conversationId, prompt, blockingReason, requiresLiveWorkIq),
            EmptyActivityStream());
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
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        var requiresLiveWorkIq = RequiresLiveWorkIqPrompt(prompt);

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

            builder.Append(delta.Content);
            WriteDiagnostic("runtime.stream", $"Assistant transcript chunk accepted ({delta.Content.Length} chars).");
            yield return delta.Content;
        }

        if (runtimeFailure is not null)
        {
            if (builder.Length == 0)
            {
                var fallbackResponse = requiresLiveWorkIq
                    ? BuildBlockingResponse(prompt, $"The live WorkICQ stream ended before any answer arrived. {runtimeFailure.Message}", requiresLiveWorkIq: true)
                    : BuildBlockingResponse(prompt, $"The Copilot SDK stream ended before any answer arrived. {runtimeFailure.Message}", requiresLiveWorkIq: false);

                WriteDiagnostic(
                    "runtime.stream",
                    requiresLiveWorkIq
                        ? "Runtime stream ended before producing an assistant answer for a workplace/org turn. Returning a blocking error instead of placeholder content."
                        : "Runtime stream ended before producing an assistant answer. Returning a blocking error instead of local fallback content.");

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

            var failureNote = requiresLiveWorkIq
                ? $"{Environment.NewLine}{Environment.NewLine}_Live WorkICQ execution stopped early: {runtimeFailure.Message}_"
                : $"{Environment.NewLine}{Environment.NewLine}_Runtime stream ended early: {runtimeFailure.Message}_";
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
                requiresLiveWorkIq
                    ? "Runtime finished without assistant content for a workplace/org turn. Returning a blocking error instead of placeholder content."
                    : "Runtime finished without assistant content. Returning a blocking error instead of local fallback content.");
            assistantContent = requiresLiveWorkIq
                ? BuildBlockingResponse(prompt, "The live WorkICQ runtime finished without returning answer content.", requiresLiveWorkIq: true)
                : BuildBlockingResponse(prompt, "The Copilot SDK runtime finished without returning answer content.", requiresLiveWorkIq: false);
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

    private async IAsyncEnumerable<string> StreamRuntimeActivityAsync(
        string sessionId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var enumerator = _messageOrchestrator
            .ObserveToolEventsAsync(sessionId, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);

        RuntimeException? runtimeFailure = null;

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
                runtimeFailure = exception;
                break;
            }

            var activity = toolEvent.EventType switch
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
            WriteDiagnostic("runtime.tool", $"{toolEvent.EventType}: {FormatToolDisplayName(toolEvent.ToolName)}{(string.IsNullOrEmpty(activity) ? "" : $" — {activity}")}");
            yield return activity;
        }

        if (runtimeFailure is not null)
        {
            WriteDiagnostic("runtime.tool", $"Activity stream unavailable: {runtimeFailure.Message}");
            yield return $"Runtime activity stream unavailable: {runtimeFailure.Message}";
        }
    }

    private async IAsyncEnumerable<string> StreamBlockingResponseAsync(
        string conversationId,
        string prompt,
        string blockingReason,
        bool requiresLiveWorkIq,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var resolvedReason = string.IsNullOrWhiteSpace(blockingReason)
            ? requiresLiveWorkIq
                ? "The live WorkICQ path is not available yet."
                : "The live Copilot SDK path is not available yet."
            : blockingReason;
        var response = BuildBlockingResponse(prompt, resolvedReason, requiresLiveWorkIq);
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

    private static string BuildBlockingResponse(string prompt, string blockingReason, bool requiresLiveWorkIq)
    {
        var topic = BuildTitle(prompt);
        var heading = requiresLiveWorkIq ? "## WorkICQ request blocked" : "## Runtime request blocked";
        var route = requiresLiveWorkIq
            ? "This workplace question must go through the live Copilot SDK + WorkICQ MCP path."
            : "This prompt only runs through the live Copilot SDK path configured with the WorkICQ MCP server.";
        return
            $"{heading}\n\n" +
            $"I can't answer \"{topic}\" from local history, sample conversations, setup metadata, or placeholder content.\n" +
            $"{route}\n\n" +
            $"**Blocking issue:** {blockingReason}\n\n" +
            "Retry once the runtime is ready so the app can fetch a live response.";
    }

    private static IEnumerable<string> StreamChunks(string response)
    {
        foreach (var word in response.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            yield return $"{word} ";
        }
    }

    private static string BuildTitle(string prompt)
    {
        const int maxLength = 42;
        var singleLine = prompt.Replace(Environment.NewLine, " ").Trim();
        return singleLine.Length <= maxLength ? singleLine : string.Concat(singleLine.Substring(0, maxLength).TrimEnd(), "…");
    }

    private static bool RequiresLiveWorkIqPrompt(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return false;
        }

        return LiveWorkIqPromptMarkers.Any(marker => prompt.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static string SummarizePrompt(string prompt)
    {
        var normalized = prompt.ReplaceLineEndings(" ").Trim();
        return normalized.Length <= 120 ? $"\"{normalized}\"" : $"\"{normalized[..120].TrimEnd()}…\"";
    }

    private static string ResolveConnectionBadge(string dataConnectionBadge, BootstrapSummary bootstrapSummary)
        => bootstrapSummary.ConnectionBadgeText == BootstrapReadyConnectionBadge
            ? BootstrapReadyConnectionBadge
            : dataConnectionBadge;

    private static string ComposeStatusText(params string?[] parts)
        => string.Join(" ", parts.Where(static part => !string.IsNullOrWhiteSpace(part)).Select(static part => part!.Trim()));

    private static string FormatToolDisplayName(string toolName)
        => string.Equals(toolName, WorkIQRuntimeDefaults.ServerName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, WorkIQRuntimeDefaults.AskWorkIqToolName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, WorkIQRuntimeDefaults.AcceptEulaToolName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, "ask_work_iq", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, "accept_eula", StringComparison.OrdinalIgnoreCase)
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
            EulaMarkerPath: Path.Combine(StorageHelper.GetWorkspacePath(), ".WorkICQ", "eula-accepted.json"),
            AuthenticationMarkerPath: Path.Combine(StorageHelper.GetWorkspacePath(), ".WorkICQ", "auth-handoff.json"),
            AuthenticationCommandLine: WorkIQRuntimeDefaults.CopilotLoginCommand,
            Blockers: [new SetupCheckItem(summaryText, false)],
            Prerequisites: []);

    private static IAsyncEnumerable<string> EmptyActivityStream() => AsyncEnumerable.Empty<string>();

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
