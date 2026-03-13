using System.Collections.Generic;
using WorkIQC.Runtime.Abstractions.Models;

namespace WorkIQC.Runtime;

public sealed record CopilotBootstrapOptions
{
    public string? WorkspaceRootPath { get; init; }
    public string? DefaultWorkIQVersion { get; init; }
    public string WorkIQPackageName { get; init; } = WorkIQRuntimeDefaults.PackageName;
    public string WorkIQServerName { get; init; } = WorkIQRuntimeDefaults.ServerName;
    public string McpConfigFileName { get; init; } = "mcp-config.json";
    public string McpRunnerCommand { get; init; } = "npx";
    public string? EulaMarkerPath { get; init; }
    public string? AuthenticationMarkerPath { get; init; }
    public IReadOnlyList<string> DependencySearchPaths { get; init; } = [];
    public IReadOnlyList<string> CopilotCommandCandidates { get; init; } = ["github-copilot-cli", "github-copilot", "copilot"];
    public IReadOnlyList<string> NodeCommandCandidates { get; init; } = ["node"];
    public IReadOnlyList<string> NpmCommandCandidates { get; init; } = ["npm"];
    public IReadOnlyList<string> NpxCommandCandidates { get; init; } = ["npx"];
}
