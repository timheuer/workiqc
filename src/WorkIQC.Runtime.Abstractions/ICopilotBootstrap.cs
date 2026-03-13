using System.Threading;
using System.Threading.Tasks;
using WorkIQC.Runtime.Abstractions.Models;

namespace WorkIQC.Runtime.Abstractions;

public interface ICopilotBootstrap
{
    Task<RuntimeReadinessReport> EnsureRuntimeDependenciesAsync(CancellationToken cancellationToken = default);
    Task<RuntimeReadinessReport> EnsureWorkIQAvailableAsync(string? version = null, CancellationToken cancellationToken = default);
    Task<WorkspaceInitializationResult> InitializeWorkspaceAsync(string? workspacePath = null, string? version = null, CancellationToken cancellationToken = default);
    Task<EulaAcceptanceReport> VerifyEulaAcceptanceAsync(CancellationToken cancellationToken = default);
    Task<EulaAcceptanceReport> AcceptEulaAsync(CancellationToken cancellationToken = default);
    Task<AuthenticationHandoffReport> VerifyAuthenticationHandoffAsync(CancellationToken cancellationToken = default);
    Task<AuthenticationHandoffReport> RecordAuthenticationHandoffAsync(string? loginCommand = null, CancellationToken cancellationToken = default);
}
