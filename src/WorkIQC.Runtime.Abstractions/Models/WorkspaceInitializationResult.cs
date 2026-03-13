namespace WorkIQC.Runtime.Abstractions.Models;

public sealed record WorkspaceInitializationResult
{
    public required string WorkspacePath { get; init; }
    public required string CopilotDirectoryPath { get; init; }
    public required string McpConfigPath { get; init; }
    public required string WorkIQPackageReference { get; init; }
    public bool UsesLatestWorkIQPackage { get; init; }
    public bool ConfigWasWritten { get; init; }
}
