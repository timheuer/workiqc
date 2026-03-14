namespace WorkIQC.App.Services;

public interface IChatShellService
{
    Task<ShellBootstrapState> LoadShellAsync(int recentLimit = 12, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ShellModelOption>> GetAvailableModelsAsync(CancellationToken cancellationToken = default);
    Task<ShellConversationSnapshot> CreateConversationAsync(string? title = null, CancellationToken cancellationToken = default);
    Task DeleteConversationAsync(string conversationId, CancellationToken cancellationToken = default);
    Task<ShellSendResponse> SendAsync(ShellSendRequest request, CancellationToken cancellationToken = default);
    Task<ShellSetupState> RefreshSetupAsync(CancellationToken cancellationToken = default);
    Task<ShellSetupState> AcceptWorkIqTermsAsync(CancellationToken cancellationToken = default);
    Task<ShellSetupState> RecordAuthenticationHandoffAsync(string? loginCommand = null, CancellationToken cancellationToken = default);
}
