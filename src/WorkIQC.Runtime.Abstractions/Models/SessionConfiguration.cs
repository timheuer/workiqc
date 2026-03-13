using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace WorkIQC.Runtime.Abstractions.Models;

public sealed record SessionConfiguration
{
    public required string WorkspacePath { get; init; }
    public required string McpConfigPath { get; init; }
    public string? WorkIQVersion { get; init; }
    public IDictionary<string, string> SystemGuidance { get; init; } = new Dictionary<string, string>();
    public bool EnableStreaming { get; init; } = true;
    public IReadOnlyList<string> AllowedTools { get; init; } = WorkIQRuntimeDefaults.SessionAllowedToolNames;

    public void Validate()
    {
        ValidateAbsolutePath(WorkspacePath, nameof(WorkspacePath));
        ValidateAbsolutePath(McpConfigPath, nameof(McpConfigPath));

        if (AllowedTools.Count == 0)
        {
            throw new ArgumentException("At least one allowed tool must be configured.", nameof(AllowedTools));
        }

        if (AllowedTools.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Allowed tool names cannot be blank.", nameof(AllowedTools));
        }
    }

    private static void ValidateAbsolutePath(string pathValue, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            throw new ArgumentException($"{parameterName} is required.", parameterName);
        }

        var expandedPath = Environment.ExpandEnvironmentVariables(pathValue);
        if (!Path.IsPathRooted(expandedPath))
        {
            throw new ArgumentException($"{parameterName} must be an absolute path.", parameterName);
        }
    }
}
