using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using WorkIQC.Persistence;
using WorkIQC.Runtime.Abstractions;
using WorkIQC.Runtime.Abstractions.Models;
using WorkIQC.Runtime.Sdk;

namespace WorkIQC.App.Services;

public sealed class LocalCopilotBootstrap : ICopilotBootstrap
{
    private static readonly IReadOnlyList<string> CopilotCommandCandidates = ["github-copilot-cli", "github-copilot", "copilot"];
    private static readonly IReadOnlyList<string> NodeCommandCandidates = ["node"];
    private static readonly IReadOnlyList<string> NpmCommandCandidates = ["npm"];
    private static readonly IReadOnlyList<string> NpxCommandCandidates = ["npx"];
    private readonly ICopilotRuntimeBridge _runtimeBridge;

    public LocalCopilotBootstrap()
        : this(CopilotRuntimeBridge.Shared)
    {
    }

    internal LocalCopilotBootstrap(ICopilotRuntimeBridge runtimeBridge)
    {
        _runtimeBridge = runtimeBridge;
    }

    public Task<RuntimeReadinessReport> EnsureRuntimeDependenciesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var copilotCli = ProbeDependency(
            "GitHub Copilot CLI",
            CopilotCommandCandidates,
            "Install or expose a GitHub Copilot CLI command before attempting live session orchestration.");
        var node = ProbeDependency(
            "Node.js",
            NodeCommandCandidates,
            "Install Node.js so the WorkICQ MCP server can run through npx.");
        var npm = ProbeDependency(
            "npm",
            NpmCommandCandidates,
            "Install npm alongside Node.js so package execution remains diagnosable.");
        var npx = ProbeDependency(
            "npx",
            NpxCommandCandidates,
            "Expose npx on PATH so the runtime can launch the WorkICQ MCP package.");

        return Task.FromResult(new RuntimeReadinessReport
        {
            Subject = "runtime-prerequisites",
            Dependencies = [copilotCli, node, npm, npx],
            Capabilities =
            [
                new RuntimeCapability
                {
                    Name = "workspace.bootstrap",
                    Status = RuntimeCapabilityStatus.Available,
                    Details = $"Workspace root resolves to '{StorageHelper.GetWorkspacePath()}'."
                },
                new RuntimeCapability
                {
                    Name = "copilot.runtime.discovery",
                    Status = copilotCli.IsAvailable ? RuntimeCapabilityStatus.Available : RuntimeCapabilityStatus.ActionRequired,
                    Details = copilotCli.IsAvailable
                        ? $"Copilot command resolved to '{copilotCli.ResolvedPath}'."
                        : "No GitHub Copilot CLI executable was found on PATH.",
                    Resolution = copilotCli.IsAvailable
                        ? null
                        : "Install/configure GitHub Copilot CLI before enabling live session orchestration."
                },
                new RuntimeCapability
                {
                    Name = "WorkICQ.mcp-launch",
                    Status = node.IsAvailable && npx.IsAvailable ? RuntimeCapabilityStatus.Available : RuntimeCapabilityStatus.ActionRequired,
                    Details = node.IsAvailable && npx.IsAvailable
                        ? "Node.js and npx are available for launching the WorkICQ MCP server."
                        : "WorkICQ cannot be launched until both Node.js and npx are discoverable.",
                    Resolution = node.IsAvailable && npx.IsAvailable
                        ? null
                        : "Install Node.js and ensure both node and npx are on PATH."
                }
            ]
        });
    }

    public Task<RuntimeReadinessReport> EnsureWorkIQAvailableAsync(string? version = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var node = ProbeDependency(
            "Node.js",
            NodeCommandCandidates,
            "Install Node.js so WorkICQ can be launched via npx.");
        var npx = ProbeDependency(
            "npx",
            NpxCommandCandidates,
            "Expose npx on PATH so the WorkICQ package can be executed.");
        var packageReference = BuildPackageReference(version);

        return Task.FromResult(new RuntimeReadinessReport
        {
            Subject = "WorkICQ-prerequisites",
            RequestedVersion = version,
            Dependencies = [node, npx],
            Capabilities =
            [
                new RuntimeCapability
                {
                    Name = "WorkICQ.package.runner",
                    Status = node.IsAvailable && npx.IsAvailable ? RuntimeCapabilityStatus.Available : RuntimeCapabilityStatus.ActionRequired,
                    Details = node.IsAvailable && npx.IsAvailable
                        ? $"WorkICQ package '{packageReference}' can be launched through '{BuildMcpLaunchDescription(packageReference)}'."
                        : "The runtime can describe the WorkICQ package, but it cannot launch it yet because node and/or npx are missing.",
                    Resolution = node.IsAvailable && npx.IsAvailable
                        ? null
                        : "Install Node.js and ensure npx resolves before enabling WorkICQ-backed chats."
                },
                new RuntimeCapability
                {
                    Name = "WorkICQ.package-resolution",
                    Status = RuntimeCapabilityStatus.Available,
                    Details = $"mcp-config.json will launch the latest WorkICQ package reference '{packageReference}'.",
                    Resolution = null
                }
            ]
        });
    }

    public async Task<WorkspaceInitializationResult> InitializeWorkspaceAsync(string? workspacePath = null, string? version = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var resolvedWorkspacePath = ResolveWorkspacePath(workspacePath);
        var copilotDirectoryPath = Path.Combine(resolvedWorkspacePath, ".copilot");
        var mcpConfigPath = Path.Combine(copilotDirectoryPath, "mcp-config.json");
        var packageReference = BuildPackageReference(version);
        var (command, args) = BuildMcpLaunchCommand(packageReference);
        var configJson = JsonSerializer.Serialize(
            new
            {
                mcpServers = new Dictionary<string, object>
                {
                    ["WorkICQ"] = new
                    {
                        command,
                        args
                    }
                }
            },
            new JsonSerializerOptions
            {
                WriteIndented = true
            });

        Directory.CreateDirectory(copilotDirectoryPath);

        var configWasWritten = true;
        if (File.Exists(mcpConfigPath))
        {
            var existingContent = await File.ReadAllTextAsync(mcpConfigPath, cancellationToken).ConfigureAwait(false);
            configWasWritten = !string.Equals(existingContent, configJson, StringComparison.Ordinal);
        }

        if (configWasWritten)
        {
            await File.WriteAllTextAsync(mcpConfigPath, configJson, cancellationToken).ConfigureAwait(false);
        }

        WriteDiagnostic(
            "workspace.init",
            $"Workspace '{resolvedWorkspacePath}' ready; config {(configWasWritten ? "written" : "reused")} at '{mcpConfigPath}'.");

        return new WorkspaceInitializationResult
        {
            WorkspacePath = resolvedWorkspacePath,
            CopilotDirectoryPath = copilotDirectoryPath,
            McpConfigPath = mcpConfigPath,
            WorkIQPackageReference = packageReference,
            UsesLatestWorkIQPackage = true,
            ConfigWasWritten = configWasWritten
        };
    }

    public Task<EulaAcceptanceReport> VerifyEulaAcceptanceAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var markerPath = GetEulaMarkerPath();
        var verification = VerifyEulaMarker(markerPath);
        WriteDiagnostic("eula.verify", verification.DiagnosticsMessage);

        return Task.FromResult(new EulaAcceptanceReport
        {
            Status = verification.CanProceed ? EulaAcceptanceStatus.Accepted : EulaAcceptanceStatus.ActionRequired,
            MarkerPath = markerPath,
            Details = verification.Details,
            Resolution = verification.Resolution
        });
    }

    public async Task<EulaAcceptanceReport> AcceptEulaAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var workspace = await InitializeWorkspaceAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var workIqAvailability = await EnsureWorkIQAvailableAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!workIqAvailability.IsReady)
        {
            throw new BootstrapException(
                "WorkICQ EULA acceptance could not start because the native WorkICQ bootstrap command is not available yet.",
                errorCode: "runtime.bootstrap.eula-bootstrap-unavailable");
        }

        var outcome = await AcceptEulaThroughBootstrapAsync(workspace, cancellationToken).ConfigureAwait(false);
        await PersistVerifiedEulaMarkerAsync(workspace, outcome, cancellationToken).ConfigureAwait(false);
        WriteDiagnostic(
            "eula.accept",
            $"Native WorkICQ EULA bootstrap completed through '{outcome.EvidenceName}' for workspace '{workspace.WorkspacePath}'.");
        return await VerifyEulaAcceptanceAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<AuthenticationHandoffReport> VerifyAuthenticationHandoffAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var markerPath = GetAuthenticationMarkerPath();
        var started = File.Exists(markerPath);
        var loginCommand = BuildCopilotLoginCommand();
        var details = started
            ? BuildAuthenticationReadyDetails(markerPath, loginCommand)
            : $"Launch '{loginCommand}' from the bootstrap card before attempting the first live WorkICQ session.";
        WriteDiagnostic("auth.verify", started ? $"Authentication handoff marker found at '{markerPath}'." : $"Authentication handoff marker missing at '{markerPath}'.");

        return Task.FromResult(new AuthenticationHandoffReport
        {
            Status = started ? AuthenticationHandoffStatus.Completed : AuthenticationHandoffStatus.ActionRequired,
            MarkerPath = markerPath,
            LoginCommand = loginCommand,
            Details = details,
            Resolution = started
                ? "Use recheck if Copilot sign-in changes, expires, or WorkICQ cannot resolve the current user."
                : $"Use the bootstrap card to launch '{loginCommand}', then retry once sign-in completes."
        });
    }

    public async Task<AuthenticationHandoffReport> RecordAuthenticationHandoffAsync(string? loginCommand = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var markerPath = GetAuthenticationMarkerPath();
        Directory.CreateDirectory(Path.GetDirectoryName(markerPath)!);
        var payload = JsonSerializer.Serialize(new
        {
            launchedAt = DateTimeOffset.UtcNow,
            loginCommand = string.IsNullOrWhiteSpace(loginCommand) ? BuildCopilotLoginCommand() : loginCommand,
            workspacePath = StorageHelper.GetWorkspacePath()
        }, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        await File.WriteAllTextAsync(markerPath, payload, cancellationToken).ConfigureAwait(false);
        WriteDiagnostic("auth.record", $"Recorded Copilot sign-in handoff at '{markerPath}'.");
        return await VerifyAuthenticationHandoffAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<EulaAcceptanceOutcome> AcceptEulaThroughBootstrapAsync(
        WorkspaceInitializationResult workspace,
        CancellationToken cancellationToken)
    {
        try
        {
            var processStartInfo = CreateEulaBootstrapProcessStartInfo(workspace.WorkIQPackageReference, out var commandDisplay);
            WriteDiagnostic("eula.bootstrap.start", $"Launching native WorkICQ bootstrap command '{commandDisplay}'.");

            var execution = await ExternalProcessRunner.RunAsync(processStartInfo, "y" + Environment.NewLine, cancellationToken).ConfigureAwait(false);
            if (execution.ExitCode != 0)
            {
                throw new BootstrapException(
                    $"The native WorkICQ EULA bootstrap command failed with exit code {execution.ExitCode}. Output: {execution.CombinedOutput}",
                    errorCode: "runtime.bootstrap.eula-bootstrap-failed");
            }

            WriteDiagnostic(
                "eula.bootstrap.complete",
                $"Native WorkICQ bootstrap command '{commandDisplay}' completed successfully.");

            return new EulaAcceptanceOutcome(
                commandDisplay,
                DateTimeOffset.UtcNow,
                WorkIQRuntimeDefaults.NativeBootstrapVerificationMode,
                execution.CombinedOutput);
        }
        catch (Exception exception) when (exception is not BootstrapException and not OperationCanceledException)
        {
            throw new BootstrapException(
                "Native WorkICQ EULA bootstrap failed before a chat session could start.",
                exception,
                errorCode: "runtime.bootstrap.eula-bootstrap-failed");
        }
    }

    private static async Task PersistVerifiedEulaMarkerAsync(
        WorkspaceInitializationResult workspace,
        EulaAcceptanceOutcome outcome,
        CancellationToken cancellationToken)
    {
        var markerPath = GetEulaMarkerPath();
        Directory.CreateDirectory(Path.GetDirectoryName(markerPath)!);
        var payload = JsonSerializer.Serialize(new
        {
            acceptedAt = outcome.AcceptedAt,
            packageReference = workspace.WorkIQPackageReference,
            eulaUrl = WorkIQRuntimeDefaults.EulaUrl,
            verificationMode = outcome.VerificationMode,
            toolName = outcome.VerificationMode == WorkIQRuntimeDefaults.LiveMcpVerificationMode ? outcome.EvidenceName : null,
            command = outcome.VerificationMode == WorkIQRuntimeDefaults.NativeBootstrapVerificationMode ? outcome.EvidenceName : null,
            output = string.IsNullOrWhiteSpace(outcome.Output) ? null : outcome.Output,
            exitCode = 0,
            workspacePath = workspace.WorkspacePath,
            sessionId = (string?)null,
            messageId = (string?)null
        }, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        await File.WriteAllTextAsync(markerPath, payload, cancellationToken).ConfigureAwait(false);
        WriteDiagnostic("eula.persist", $"Recorded verified WorkICQ EULA bootstrap evidence at '{markerPath}'.");
    }

    private static DependencyCheckResult ProbeDependency(string name, IReadOnlyList<string> commandCandidates, string missingDetails)
    {
        var resolvedPath = FindFirstAvailableCommand(commandCandidates);
        return new DependencyCheckResult
        {
            Name = name,
            IsAvailable = resolvedPath is not null,
            ResolvedPath = resolvedPath,
            Details = resolvedPath is not null
                ? $"Resolved on disk at '{resolvedPath}'."
                : missingDetails
        };
    }

    private static string? FindFirstAvailableCommand(IReadOnlyList<string> commandCandidates)
    {
        foreach (var candidate in commandCandidates.Where(static value => !string.IsNullOrWhiteSpace(value)))
        {
            foreach (var searchPath in GetSearchPaths())
            {
                foreach (var fileName in ExpandExecutableNames(candidate))
                {
                    var candidatePath = Path.Combine(searchPath, fileName);
                    if (File.Exists(candidatePath))
                    {
                        return candidatePath;
                    }
                }
            }
        }

        return null;
    }

    private static IReadOnlyList<string> GetSearchPaths()
    {
        var environmentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        return environmentPath
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<string> ExpandExecutableNames(string candidate)
    {
        if (Path.HasExtension(candidate))
        {
            return [candidate];
        }

        var pathExt = Environment.GetEnvironmentVariable("PATHEXT");
        var extensions = string.IsNullOrWhiteSpace(pathExt)
            ? new[] { ".exe", ".cmd", ".bat" }
            : pathExt.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return [candidate, .. extensions.Select(extension => candidate + extension).Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    private static string ResolveWorkspacePath(string? workspacePath)
    {
        var candidatePath = string.IsNullOrWhiteSpace(workspacePath)
            ? StorageHelper.GetWorkspacePath()
            : Environment.ExpandEnvironmentVariables(workspacePath);

        return Path.GetFullPath(candidatePath);
    }

    private static string GetEulaMarkerPath()
        => Path.Combine(StorageHelper.GetWorkspacePath(), ".WorkICQ", "eula-accepted.json");

    private static string GetAuthenticationMarkerPath()
        => Path.Combine(StorageHelper.GetWorkspacePath(), ".WorkICQ", "auth-handoff.json");

    private static string BuildPackageReference(string? version)
        => WorkIQRuntimeDefaults.PackageReference;

    private static string BuildCopilotLoginCommand()
    {
        var resolvedPath = FindFirstAvailableCommand(CopilotCommandCandidates);
        return string.IsNullOrWhiteSpace(resolvedPath)
            ? WorkIQRuntimeDefaults.CopilotLoginCommand
            : $"\"{resolvedPath}\" login";
    }

    private static string BuildAuthenticationReadyDetails(string markerPath, string fallbackLoginCommand)
    {
        if (!TryReadAuthenticationMarker(markerPath, out var launchedAt, out var recordedLoginCommand))
        {
            return $"Copilot sign-in handoff was recorded locally. This does not verify that the live WorkICQ session can resolve the signed-in principal. Evidence is stored at '{markerPath}'.";
        }

        var command = string.IsNullOrWhiteSpace(recordedLoginCommand) ? fallbackLoginCommand : recordedLoginCommand;
        var timestamp = launchedAt?.ToLocalTime().ToString("g");
        return timestamp is null
            ? $"Copilot sign-in handoff was recorded via '{command}'. This does not verify that the live WorkICQ session can resolve the signed-in principal. Evidence is stored at '{markerPath}'."
            : $"Copilot sign-in handoff was recorded via '{command}' (recorded {timestamp}). This does not verify that the live WorkICQ session can resolve the signed-in principal. Evidence is stored at '{markerPath}'.";
    }

    private static bool TryReadAuthenticationMarker(string markerPath, out DateTimeOffset? launchedAt, out string? loginCommand)
    {
        launchedAt = null;
        loginCommand = null;

        try
        {
            using var stream = File.OpenRead(markerPath);
            using var document = JsonDocument.Parse(stream);
            if (document.RootElement.TryGetProperty("launchedAt", out var launchedAtElement)
                && launchedAtElement.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(launchedAtElement.GetString(), out var parsedLaunchedAt))
            {
                launchedAt = parsedLaunchedAt;
            }

            if (document.RootElement.TryGetProperty("loginCommand", out var loginCommandElement)
                && loginCommandElement.ValueKind == JsonValueKind.String)
            {
                loginCommand = loginCommandElement.GetString();
            }

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            WriteDiagnostic("auth.verify", $"Authentication marker at '{markerPath}' could not be parsed: {exception.Message}");
            return false;
        }
    }

    private static EulaMarkerVerification VerifyEulaMarker(string markerPath)
    {
        if (!File.Exists(markerPath))
        {
            return new EulaMarkerVerification(
                CanProceed: false,
                DiagnosticsMessage: $"Marker missing at '{markerPath}'.",
                Details: "No verified WorkICQ consent record was found, so the app cannot treat WorkICQ bootstrap as complete for first use.",
                Resolution: $"Complete the WorkICQ consent bootstrap from Settings so the app can run the native 'accept-eula' command and persist evidence at '{markerPath}'.");
        }

        if (!TryReadAcceptedEulaMarker(markerPath, out var acceptedAt, out var evidenceSummary))
        {
            return new EulaMarkerVerification(
                CanProceed: false,
                DiagnosticsMessage: $"Unverified or unreadable marker found at '{markerPath}'.",
                Details: "A local WorkICQ consent record exists, but it was not backed by verified bootstrap or live MCP evidence.",
                Resolution: $"Re-run the WorkICQ consent bootstrap from Settings so the app can refresh '{markerPath}' with verified consent evidence.");
        }

        var timestamp = acceptedAt?.ToLocalTime().ToString("g");
        var details = timestamp is null
            ? $"WorkICQ consent was verified during bootstrap through '{evidenceSummary}' and recorded at '{markerPath}'."
            : $"WorkICQ consent was verified during bootstrap through '{evidenceSummary}' (recorded {timestamp}) and stored at '{markerPath}'.";

        return new EulaMarkerVerification(
            CanProceed: true,
            DiagnosticsMessage: $"Verified WorkICQ consent marker at '{markerPath}'.",
            Details: details,
            Resolution: "Re-run the WorkICQ consent bootstrap if WorkICQ later reports that consent is missing.");
    }

    private static bool TryReadAcceptedEulaMarker(string markerPath, out DateTimeOffset? acceptedAt, out string? evidenceSummary)
    {
        acceptedAt = null;
        evidenceSummary = null;

        try
        {
            using var stream = File.OpenRead(markerPath);
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;
            if (!root.TryGetProperty("verificationMode", out var modeElement)
                || modeElement.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            if (root.TryGetProperty("acceptedAt", out var acceptedAtElement)
                && acceptedAtElement.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(acceptedAtElement.GetString(), out var parsedAcceptedAt))
            {
                acceptedAt = parsedAcceptedAt;
            }

            var verificationMode = modeElement.GetString();
            if (string.Equals(verificationMode, WorkIQRuntimeDefaults.LiveMcpVerificationMode, StringComparison.OrdinalIgnoreCase))
            {
                if (!root.TryGetProperty("toolName", out var toolNameElement)
                    || toolNameElement.ValueKind != JsonValueKind.String
                    || !IsEulaToolName(toolNameElement.GetString() ?? string.Empty))
                {
                    return false;
                }

                evidenceSummary = toolNameElement.GetString();
                return true;
            }

            if (!string.Equals(verificationMode, WorkIQRuntimeDefaults.NativeBootstrapVerificationMode, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!root.TryGetProperty("command", out var commandElement)
                || commandElement.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            var command = commandElement.GetString();
            if (string.IsNullOrWhiteSpace(command)
                || command.IndexOf("accept-eula", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }

            if (root.TryGetProperty("exitCode", out var exitCodeElement)
                && exitCodeElement.ValueKind == JsonValueKind.Number
                && exitCodeElement.TryGetInt32(out var exitCode)
                && exitCode != 0)
            {
                return false;
            }

            evidenceSummary = command;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            WriteDiagnostic("eula.verify", $"EULA marker at '{markerPath}' could not be parsed: {exception.Message}");
            return false;
        }
    }

    private static ProcessStartInfo CreateEulaBootstrapProcessStartInfo(string packageReference, out string commandDisplay)
    {
        var resolvedRunnerPath = FindFirstAvailableCommand(NpxCommandCandidates);
        var runnerPath = string.IsNullOrWhiteSpace(resolvedRunnerPath)
            ? OperatingSystem.IsWindows()
                ? BuildWindowsRunnerCommand("npx")
                : "npx"
            : QuoteCommandIfNeeded(resolvedRunnerPath);

        commandDisplay = $"{runnerPath} -y {packageReference} accept-eula";
        if (!OperatingSystem.IsWindows())
        {
            return new ProcessStartInfo
            {
                FileName = string.IsNullOrWhiteSpace(resolvedRunnerPath) ? "npx" : resolvedRunnerPath,
                Arguments = $"-y {packageReference} accept-eula",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = StorageHelper.GetWorkspacePath()
            };
        }

        return new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
            Arguments = $"/d /s /c \"{commandDisplay}\"",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = StorageHelper.GetWorkspacePath()
        };
    }

    private static bool IsEulaToolName(string toolName)
        => string.Equals(toolName, WorkIQRuntimeDefaults.AcceptEulaToolName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, "accept_eula", StringComparison.OrdinalIgnoreCase);

    private static (string Command, string[] Args) BuildMcpLaunchCommand(string packageReference)
    {
        return ("npx", ["-y", packageReference, "mcp"]);
    }

    private static string BuildMcpLaunchDescription(string packageReference)
    {
        var (command, args) = BuildMcpLaunchCommand(packageReference);
        return args.Length == 0
            ? command
            : $"{command} {string.Join(' ', args)}";
    }

    private static void WriteDiagnostic(string stage, string message)
        => Trace.WriteLine($"[{DateTimeOffset.Now:O}] [WorkIQC.LocalBootstrap] [{stage}] {message}");

    private static string BuildWindowsRunnerCommand(string runnerCommand)
    {
        var resolvedRunner = string.IsNullOrWhiteSpace(runnerCommand) ? "npx" : runnerCommand.Trim();
        if (Path.HasExtension(resolvedRunner))
        {
            return QuoteCommandIfNeeded(resolvedRunner);
        }

        return QuoteCommandIfNeeded(resolvedRunner + ".cmd");
    }

    private static string QuoteCommandIfNeeded(string command)
        => command.Contains(' ', StringComparison.Ordinal) ? $"\"{command}\"" : command;

    private sealed record EulaAcceptanceOutcome(
        string EvidenceName,
        DateTimeOffset AcceptedAt,
        string VerificationMode,
        string Output);

    private sealed record EulaMarkerVerification(
        bool CanProceed,
        string DiagnosticsMessage,
        string Details,
        string Resolution);
}

public sealed class LocalSessionCoordinator : ISessionCoordinator
{
    private readonly ICopilotRuntimeBridge _runtimeBridge;

    public LocalSessionCoordinator()
        : this(CopilotRuntimeBridge.Shared)
    {
    }

    internal LocalSessionCoordinator(ICopilotRuntimeBridge runtimeBridge)
    {
        _runtimeBridge = runtimeBridge;
    }

    public Task<string> CreateSessionAsync(SessionConfiguration config, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(config);
        config.Validate();
        return _runtimeBridge.CreateSessionAsync(config, cancellationToken);
    }

    public Task<bool> ResumeSessionAsync(string sessionId, string? modelId = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateSessionId(sessionId, nameof(sessionId));

        return _runtimeBridge.ResumeSessionAsync(sessionId, modelId, cancellationToken);
    }

    public Task<IReadOnlyList<CopilotModelDescriptor>> ListAvailableModelsAsync(string workspacePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(workspacePath))
        {
            throw new ArgumentException("Workspace path is required.", nameof(workspacePath));
        }

        return _runtimeBridge.ListAvailableModelsAsync(workspacePath, cancellationToken);
    }

    public Task<SessionState> GetSessionStateAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateSessionId(sessionId, nameof(sessionId));

        return _runtimeBridge.GetSessionStateAsync(sessionId, cancellationToken);
    }

    public Task DisposeSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateSessionId(sessionId, nameof(sessionId));

        return _runtimeBridge.DisposeSessionAsync(sessionId, cancellationToken);
    }

    private static void ValidateSessionId(string sessionId, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("Session identifier is required.", parameterName);
        }
    }
}

public sealed class LocalMessageOrchestrator : IMessageOrchestrator
{
    private readonly ICopilotRuntimeBridge _runtimeBridge;

    public LocalMessageOrchestrator()
        : this(CopilotRuntimeBridge.Shared)
    {
    }

    internal LocalMessageOrchestrator(ICopilotRuntimeBridge runtimeBridge)
    {
        _runtimeBridge = runtimeBridge;
    }

    public Task<SendMessageResponse> SendMessageAsync(SendMessageRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();
        return _runtimeBridge.SendMessageAsync(request, cancellationToken);
    }

    public IAsyncEnumerable<StreamingDelta> StreamResponseAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ValidateSessionId(sessionId);
        cancellationToken.ThrowIfCancellationRequested();
        return _runtimeBridge.StreamResponseAsync(sessionId, cancellationToken);
    }

    public IAsyncEnumerable<ToolEvent> ObserveToolEventsAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ValidateSessionId(sessionId);
        cancellationToken.ThrowIfCancellationRequested();
        return _runtimeBridge.ObserveToolEventsAsync(sessionId, cancellationToken);
    }

    private static void ValidateSessionId(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("Session identifier is required.", nameof(sessionId));
        }
    }
}
