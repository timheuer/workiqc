using System.Threading;
using System.Threading.Tasks;
using WorkIQC.Runtime.Abstractions.Models;

namespace WorkIQC.Runtime.Abstractions;

public interface ISessionCoordinator
{
    Task<string> CreateSessionAsync(SessionConfiguration config, CancellationToken cancellationToken = default);
    Task<bool> ResumeSessionAsync(string sessionId, string? modelId = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CopilotModelDescriptor>> ListAvailableModelsAsync(string workspacePath, CancellationToken cancellationToken = default);
    Task<SessionState> GetSessionStateAsync(string sessionId, CancellationToken cancellationToken = default);
    Task DisposeSessionAsync(string sessionId, CancellationToken cancellationToken = default);
}
