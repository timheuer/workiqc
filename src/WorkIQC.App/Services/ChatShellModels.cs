using WorkIQC.App.Models;

namespace WorkIQC.App.Services;

public sealed record ShellBootstrapState(
    IReadOnlyList<ShellConversationSnapshot> Conversations,
    string ConnectionBadgeText,
    string SidebarFooterText,
    ShellSetupState SetupState);

public sealed record ShellSetupState(
    bool RequiresUserAction,
    bool CanAttemptRuntime,
    bool IsEulaAccepted,
    bool IsAuthenticationHandoffStarted,
    string SummaryText,
    string WorkIQPackageReference,
    string WorkspacePath,
    string McpConfigPath,
    string EulaUrl,
    string EulaMarkerPath,
    string AuthenticationMarkerPath,
    string AuthenticationCommandLine,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> Prerequisites);

public sealed record ShellConversationSnapshot(
    string Id,
    string Title,
    string Preview,
    DateTime UpdatedAt,
    bool IsPersisted,
    bool IsDraft,
    string? SessionId,
    IReadOnlyList<ShellMessageSnapshot> Messages);

public sealed record ShellMessageSnapshot(
    ChatRole Role,
    string Author,
    string Content,
    DateTime Timestamp);

public sealed record ShellSendRequest(
    string ConversationId,
    string ConversationTitle,
    string Prompt,
    string? SessionId);

public sealed record ShellSendResponse(
    string ConversationId,
    string ConversationTitle,
    bool IsPersisted,
    bool IsDraft,
    string? SessionId,
    string ConnectionBadgeText,
    string SidebarFooterText,
    IAsyncEnumerable<string> ResponseStream,
    IAsyncEnumerable<string> ActivityStream);
