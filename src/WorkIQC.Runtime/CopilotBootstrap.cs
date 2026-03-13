using System.Diagnostics;
using System.Text.Json;
using WorkIQC.Runtime.Abstractions;
using WorkIQC.Runtime.Abstractions.Models;
using WorkIQC.Runtime.Sdk;

namespace WorkIQC.Runtime;

public sealed class CopilotBootstrap : ICopilotBootstrap
{
    private readonly CopilotBootstrapOptions _options;
    private readonly ICopilotRuntimeBridge _runtimeBridge;

    public CopilotBootstrap(CopilotBootstrapOptions? options = null)
        : this(options, CopilotRuntimeBridge.Shared)
    {
    }

    internal CopilotBootstrap(CopilotBootstrapOptions? options, ICopilotRuntimeBridge runtimeBridge)
    {
        _options = options ?? new CopilotBootstrapOptions();
        _runtimeBridge = runtimeBridge;
    }

    public Task<RuntimeReadinessReport> EnsureRuntimeDependenciesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var copilotCli = ProbeDependency(
            "GitHub Copilot CLI",
            _options.CopilotCommandCandidates,
            "Install or expose a GitHub Copilot CLI command before attempting live session orchestration.");
        var node = ProbeDependency(
            "Node.js",
            _options.NodeCommandCandidates,
            "Install Node.js so the WorkIQ MCP server can run through npx.");
        var npm = ProbeDependency(
            "npm",
            _options.NpmCommandCandidates,
            "Install npm alongside Node.js so package execution remains diagnosable.");
        var npx = ProbeDependency(
            "npx",
            _options.NpxCommandCandidates,
            "Expose npx on PATH so the runtime can launch the pinned WorkIQ MCP package.");

        var workspacePath = ResolveWorkspacePath(null);
        var capabilities = new[]
        {
            new RuntimeCapability
            {
                Name = "workspace.bootstrap",
                Status = RuntimeCapabilityStatus.Available,
                Details = $"Workspace root resolves to '{workspacePath}'."
            },
            new RuntimeCapability
            {
                Name = "copilot.runtime.discovery",
                Status = copilotCli.IsAvailable ? RuntimeCapabilityStatus.Available : RuntimeCapabilityStatus.ActionRequired,
                Details = copilotCli.IsAvailable
                    ? $"Copilot command resolved to '{copilotCli.ResolvedPath}'."
                    : "No GitHub Copilot CLI executable was found on the configured search paths.",
                Resolution = copilotCli.IsAvailable
                    ? null
                    : "Install/configure GitHub Copilot CLI or finish the in-process SDK session spike before enabling chat orchestration."
            },
            new RuntimeCapability
            {
                Name = "workiq.mcp-launch",
                Status = node.IsAvailable && npx.IsAvailable
                    ? RuntimeCapabilityStatus.Available
                    : RuntimeCapabilityStatus.ActionRequired,
                Details = node.IsAvailable && npx.IsAvailable
                    ? "Node.js and npx are available for launching the WorkIQ MCP server."
                    : "WorkIQ cannot be launched until both Node.js and npx are discoverable.",
                Resolution = node.IsAvailable && npx.IsAvailable
                    ? null
                    : "Install Node.js and ensure both node and npx are on PATH."
            }
        };

        return Task.FromResult(new RuntimeReadinessReport
        {
            Subject = "runtime-prerequisites",
            Dependencies = new[] { copilotCli, node, npm, npx },
            Capabilities = capabilities
        });
    }

    public Task<RuntimeReadinessReport> EnsureWorkIQAvailableAsync(string? version = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var node = ProbeDependency(
            "Node.js",
            _options.NodeCommandCandidates,
            "Install Node.js so WorkIQ can be launched via npx.");
        var npx = ProbeDependency(
            "npx",
            _options.NpxCommandCandidates,
            "Expose npx on PATH so the latest WorkIQ package can be executed.");
        var packageReference = BuildPackageReference(version);

        var capabilities = new[]
        {
            new RuntimeCapability
            {
                Name = "workiq.package.runner",
                Status = node.IsAvailable && npx.IsAvailable
                    ? RuntimeCapabilityStatus.Available
                    : RuntimeCapabilityStatus.ActionRequired,
                Details = node.IsAvailable && npx.IsAvailable
                    ? $"WorkIQ package '{packageReference}' can be launched through '{BuildMcpLaunchDescription(packageReference)}'."
                    : "The runtime can describe the WorkIQ package, but it cannot launch it yet because node and/or npx are missing.",
                Resolution = node.IsAvailable && npx.IsAvailable
                    ? null
                    : "Install Node.js and ensure npx resolves before enabling WorkIQ-backed chats."
            },
            new RuntimeCapability
            {
                Name = "workiq.package-resolution",
                Status = RuntimeCapabilityStatus.Available,
                Details = $"mcp-config.json will launch the latest WorkIQ package reference '{packageReference}'.",
                Resolution = null
            }
        };

        return Task.FromResult(new RuntimeReadinessReport
        {
            Subject = "workiq-prerequisites",
            RequestedVersion = version,
            Dependencies = new[] { node, npx },
            Capabilities = capabilities
        });
    }

    public async Task<WorkspaceInitializationResult> InitializeWorkspaceAsync(string? workspacePath = null, string? version = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var resolvedWorkspacePath = ResolveWorkspacePath(workspacePath);
            var copilotDirectoryPath = Path.Combine(resolvedWorkspacePath, ".copilot");
            var mcpConfigPath = Path.Combine(copilotDirectoryPath, _options.McpConfigFileName);
            var packageReference = BuildPackageReference(version);

            Directory.CreateDirectory(copilotDirectoryPath);

            var configJson = BuildMcpConfigJson(packageReference);
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
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            throw new BootstrapException(
                $"Failed to initialize the Copilot workspace for '{workspacePath ?? "<default>"}'.",
                exception,
                errorCode: "runtime.bootstrap.workspace-initialization-failed");
        }
    }

    public Task<EulaAcceptanceReport> VerifyEulaAcceptanceAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var markerPath = ResolveEulaMarkerPath();
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
                "WorkIQ EULA acceptance could not start because the native WorkIQ bootstrap command is not available yet.",
                errorCode: "runtime.bootstrap.eula-bootstrap-unavailable");
        }

        var outcome = await AcceptEulaThroughBootstrapAsync(workspace, cancellationToken).ConfigureAwait(false);
        await PersistVerifiedEulaMarkerAsync(workspace, outcome, cancellationToken).ConfigureAwait(false);
        WriteDiagnostic(
            "eula.accept",
            $"Native WorkIQ EULA bootstrap completed through '{outcome.EvidenceName}' for workspace '{workspace.WorkspacePath}'.");
        return await VerifyEulaAcceptanceAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<AuthenticationHandoffReport> VerifyAuthenticationHandoffAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var markerPath = ResolveAuthenticationMarkerPath();
        var markerExists = File.Exists(markerPath);
        var loginCommand = BuildCopilotLoginCommand();
        var details = markerExists
            ? BuildAuthenticationReadyDetails(markerPath, loginCommand)
            : $"Launch '{loginCommand}' from the first-run setup flow before attempting the first live WorkIQ turn.";
        WriteDiagnostic("auth.verify", markerExists ? $"Authentication handoff marker found at '{markerPath}'." : $"Authentication handoff marker missing at '{markerPath}'.");

        return Task.FromResult(new AuthenticationHandoffReport
        {
            Status = markerExists ? AuthenticationHandoffStatus.Completed : AuthenticationHandoffStatus.ActionRequired,
            MarkerPath = markerPath,
            LoginCommand = loginCommand,
            Details = details,
            Resolution = markerExists
                ? "Use recheck if Copilot sign-in changes, expires, or WorkIQ cannot resolve the current user."
                : $"Use the app's first-run setup to launch '{loginCommand}', then retry once sign-in completes."
        });
    }

    public async Task<AuthenticationHandoffReport> RecordAuthenticationHandoffAsync(string? loginCommand = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var markerPath = ResolveAuthenticationMarkerPath();
        Directory.CreateDirectory(Path.GetDirectoryName(markerPath)!);

        var payload = JsonSerializer.Serialize(new
        {
            launchedAt = DateTimeOffset.UtcNow,
            loginCommand = string.IsNullOrWhiteSpace(loginCommand) ? BuildCopilotLoginCommand() : loginCommand,
            workspacePath = ResolveWorkspacePath(null)
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
            WriteDiagnostic("eula.bootstrap.start", $"Launching native WorkIQ bootstrap command '{commandDisplay}'.");

            var execution = await ExternalProcessRunner.RunAsync(processStartInfo, "y" + Environment.NewLine, cancellationToken).ConfigureAwait(false);
            if (execution.ExitCode != 0)
            {
                throw new BootstrapException(
                    $"The native WorkIQ EULA bootstrap command failed with exit code {execution.ExitCode}. Output: {execution.CombinedOutput}",
                    errorCode: "runtime.bootstrap.eula-bootstrap-failed");
            }

            WriteDiagnostic(
                "eula.bootstrap.complete",
                $"Native WorkIQ bootstrap command '{commandDisplay}' completed successfully.");

            return new EulaAcceptanceOutcome(
                commandDisplay,
                DateTimeOffset.UtcNow,
                WorkIQRuntimeDefaults.NativeBootstrapVerificationMode,
                execution.CombinedOutput);
        }
        catch (Exception exception) when (exception is not BootstrapException and not OperationCanceledException)
        {
            throw new BootstrapException(
                "Native WorkIQ EULA bootstrap failed before a chat session could start.",
                exception,
                errorCode: "runtime.bootstrap.eula-bootstrap-failed");
        }
    }

    private async Task PersistVerifiedEulaMarkerAsync(
        WorkspaceInitializationResult workspace,
        EulaAcceptanceOutcome outcome,
        CancellationToken cancellationToken)
    {
        var markerPath = ResolveEulaMarkerPath();
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
        WriteDiagnostic("eula.persist", $"Recorded verified WorkIQ EULA bootstrap evidence at '{markerPath}'.");
    }

    private ProcessStartInfo CreateEulaBootstrapProcessStartInfo(string packageReference, out string commandDisplay)
    {
        var resolvedRunnerPath = FindFirstAvailableCommand(_options.NpxCommandCandidates);
        var runnerPath = string.IsNullOrWhiteSpace(resolvedRunnerPath)
            ? OperatingSystem.IsWindows()
                ? BuildWindowsRunnerCommand(_options.McpRunnerCommand)
                : _options.McpRunnerCommand
            : QuoteCommandIfNeeded(resolvedRunnerPath);

        commandDisplay = $"{runnerPath} -y {packageReference} accept-eula";
        if (!OperatingSystem.IsWindows())
        {
            return new ProcessStartInfo
            {
                FileName = string.IsNullOrWhiteSpace(resolvedRunnerPath) ? _options.McpRunnerCommand : resolvedRunnerPath,
                Arguments = $"-y {packageReference} accept-eula",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = ResolveWorkspacePath(null)
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
            WorkingDirectory = ResolveWorkspacePath(null)
        };
    }

    private static EulaMarkerVerification VerifyEulaMarker(string markerPath)
    {
        if (!File.Exists(markerPath))
        {
            return new EulaMarkerVerification(
                CanProceed: false,
                DiagnosticsMessage: $"Marker missing at '{markerPath}'.",
                Details: "No verified WorkIQ EULA acceptance record was found, so the runtime cannot claim WorkIQ is ready for first use.",
                Resolution: $"Run the Settings consent bootstrap so WorkIQ can complete its native 'accept-eula' command and persist evidence at '{markerPath}'.");
        }

        if (!TryReadAcceptedEulaMarker(markerPath, out var acceptedAt, out var evidenceSummary))
        {
            return new EulaMarkerVerification(
                CanProceed: false,
                DiagnosticsMessage: $"Unverified or unreadable marker found at '{markerPath}'.",
                Details: "A local WorkIQ EULA marker exists, but it was not backed by verified WorkIQ bootstrap or live MCP evidence.",
                Resolution: $"Re-run the Settings consent bootstrap so WorkIQ can refresh '{markerPath}' with verified acceptance evidence.");
        }

        var timestamp = acceptedAt?.ToLocalTime().ToString("g");
        var details = timestamp is null
            ? $"Verified WorkIQ EULA acceptance was confirmed through '{evidenceSummary}' and recorded at '{markerPath}'."
            : $"Verified WorkIQ EULA acceptance was confirmed through '{evidenceSummary}' (recorded {timestamp}) and stored at '{markerPath}'.";

        return new EulaMarkerVerification(
            CanProceed: true,
            DiagnosticsMessage: $"Verified WorkIQ EULA marker at '{markerPath}'.",
            Details: details,
            Resolution: "Re-run the Settings consent bootstrap if WorkIQ later reports that EULA consent is missing.");
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

    private DependencyCheckResult ProbeDependency(string name, IReadOnlyList<string> commandCandidates, string missingDetails)
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

    private string? FindFirstAvailableCommand(IReadOnlyList<string> commandCandidates)
    {
        foreach (var candidate in commandCandidates.Where(static value => !string.IsNullOrWhiteSpace(value)))
        {
            if (Path.IsPathRooted(candidate) && File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }

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

    private IReadOnlyList<string> GetSearchPaths()
    {
        var configuredPaths = _options.DependencySearchPaths
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (configuredPaths.Length > 0)
        {
            return configuredPaths;
        }

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
            return new[] { candidate };
        }

        var pathExt = Environment.GetEnvironmentVariable("PATHEXT");
        var extensions = string.IsNullOrWhiteSpace(pathExt)
            ? new[] { ".exe", ".cmd", ".bat" }
            : pathExt.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return new[] { candidate }
            .Concat(extensions.Select(extension => candidate + extension))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private string ResolveWorkspacePath(string? workspacePath)
    {
        var candidatePath = string.IsNullOrWhiteSpace(workspacePath)
            ? _options.WorkspaceRootPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WorkIQC")
            : Environment.ExpandEnvironmentVariables(workspacePath);

        return Path.GetFullPath(candidatePath);
    }

    private string ResolveEulaMarkerPath()
    {
        var configuredMarkerPath = string.IsNullOrWhiteSpace(_options.EulaMarkerPath)
            ? Path.Combine(ResolveWorkspacePath(null), ".workiq", "eula-accepted.json")
            : Environment.ExpandEnvironmentVariables(_options.EulaMarkerPath);

        return Path.GetFullPath(configuredMarkerPath);
    }

    private string ResolveAuthenticationMarkerPath()
    {
        var configuredMarkerPath = string.IsNullOrWhiteSpace(_options.AuthenticationMarkerPath)
            ? Path.Combine(ResolveWorkspacePath(null), ".workiq", "auth-handoff.json")
            : Environment.ExpandEnvironmentVariables(_options.AuthenticationMarkerPath);

        return Path.GetFullPath(configuredMarkerPath);
    }

    private string BuildCopilotLoginCommand()
    {
        var resolvedPath = FindFirstAvailableCommand(_options.CopilotCommandCandidates);
        return string.IsNullOrWhiteSpace(resolvedPath)
            ? WorkIQRuntimeDefaults.CopilotLoginCommand
            : $"\"{resolvedPath}\" login";
    }

    private static string BuildAuthenticationReadyDetails(string markerPath, string fallbackLoginCommand)
    {
        if (!TryReadAuthenticationMarker(markerPath, out var launchedAt, out var recordedLoginCommand))
        {
            return $"Copilot sign-in handoff was recorded locally. This does not verify that the live WorkIQ session can resolve the signed-in principal. Evidence is stored at '{markerPath}'.";
        }

        var command = string.IsNullOrWhiteSpace(recordedLoginCommand) ? fallbackLoginCommand : recordedLoginCommand;
        var timestamp = launchedAt?.ToLocalTime().ToString("g");
        return timestamp is null
            ? $"Copilot sign-in handoff was recorded via '{command}'. This does not verify that the live WorkIQ session can resolve the signed-in principal. Evidence is stored at '{markerPath}'."
            : $"Copilot sign-in handoff was recorded via '{command}' (recorded {timestamp}). This does not verify that the live WorkIQ session can resolve the signed-in principal. Evidence is stored at '{markerPath}'.";
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

    private string BuildPackageReference(string? version)
        => _options.WorkIQPackageName;

    private string BuildMcpConfigJson(string packageReference)
    {
        var (command, args) = BuildMcpLaunchCommand(packageReference);
        var config = new
        {
            mcpServers = new Dictionary<string, object>
            {
                [_options.WorkIQServerName] = new
                {
                    command,
                    args
                }
            }
        };

        return JsonSerializer.Serialize(config, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    private (string Command, string[] Args) BuildMcpLaunchCommand(string packageReference)
    {
        var command = string.IsNullOrWhiteSpace(_options.McpRunnerCommand) ? "npx" : _options.McpRunnerCommand.Trim();
        return (command, ["-y", packageReference, "mcp"]);
    }

    private string BuildMcpLaunchDescription(string packageReference)
    {
        var (command, args) = BuildMcpLaunchCommand(packageReference);
        return args.Length == 0
            ? command
            : $"{command} {string.Join(' ', args)}";
    }

    private static bool IsEulaToolName(string toolName)
        => string.Equals(toolName, WorkIQRuntimeDefaults.AcceptEulaToolName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, "accept_eula", StringComparison.OrdinalIgnoreCase);

    private static void WriteDiagnostic(string stage, string message)
        => Trace.WriteLine($"[{DateTimeOffset.Now:O}] [WorkIQC.Bootstrap] [{stage}] {message}");

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
