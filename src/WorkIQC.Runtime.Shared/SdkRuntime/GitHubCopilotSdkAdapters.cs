using GitHub.Copilot.SDK;
using System.Diagnostics;
using System.Text.Json;
using WorkIQC.Runtime.Abstractions;
using WorkIQC.Runtime.Abstractions.Models;

namespace WorkIQC.Runtime.Sdk;

internal sealed class GitHubCopilotSdkClientFactory : ICopilotSdkClientFactory
{
    public ICopilotSdkClient Create(string workspacePath) => new GitHubCopilotSdkClient(workspacePath);
}

internal sealed class GitHubCopilotSdkClient : ICopilotSdkClient
{
    private readonly CopilotClient _client;
    private readonly SemaphoreSlim _startGate = new(1, 1);
    private bool _started;

    public GitHubCopilotSdkClient(string workspacePath)
    {
        _client = new CopilotClient(new CopilotClientOptions
        {
            AutoStart = false,
            Cwd = workspacePath
        });
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_started)
        {
            WriteDiagnostic("client.start", "Copilot client already started for this workspace.");
            return;
        }

        await _startGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_started)
            {
                return;
            }

            await _client.StartAsync().ConfigureAwait(false);
            _started = true;
            WriteDiagnostic("client.start", "Copilot SDK client started successfully.");
        }
        catch (Exception exception)
        {
            throw new RuntimeException(
                "GitHub Copilot SDK could not start its CLI bridge. Verify the Copilot CLI is installed, authenticated, and reachable from the app workspace.",
                exception,
                errorCode: "runtime.client.start.failed");
        }
        finally
        {
            _startGate.Release();
        }
    }

    public async Task<ICopilotSdkSession> CreateSessionAsync(SessionConfiguration config, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        await StartAsync(cancellationToken).ConfigureAwait(false);
        WriteDiagnostic(
            "session.create",
            $"Creating Copilot SDK session with model '{NormalizeModelId(config.ModelId) ?? "<default>"}', allowed tools [{string.Join(", ", config.AllowedTools)}], workspace '{config.WorkspacePath}', and MCP config '{config.McpConfigPath}'.");

        try
        {
            var sessionConfig = BuildSessionConfig(config);
            if (sessionConfig.McpServers is { Count: > 0 } configuredServers)
            {
                foreach (var (serverName, serverDef) in configuredServers)
                {
                    if (serverDef is McpLocalServerConfig local)
                    {
                        WriteDiagnostic("session.mcp", $"MCP server '{serverName}': command='{local.Command}', args=[{string.Join(", ", local.Args)}], tools=[{string.Join(", ", local.Tools)}], timeout={local.Timeout}ms.");
                    }
                }
            }

            var session = await _client.CreateSessionAsync(sessionConfig).ConfigureAwait(false);

            return new GitHubCopilotSdkSession(session);
        }
        catch (Exception exception)
        {
            throw new RuntimeException(
                "GitHub Copilot SDK rejected the WorkIQ session configuration before a session could be created.",
                exception,
                errorCode: "runtime.session.create.failed");
        }
    }

    public async Task<ICopilotSdkSession> ResumeSessionAsync(string sessionId, string? modelId = null, CancellationToken cancellationToken = default)
    {
        await StartAsync(cancellationToken).ConfigureAwait(false);
        var normalizedModelId = NormalizeModelId(modelId);
        WriteDiagnostic("session.resume", $"Attempting to resume Copilot SDK session '{sessionId}' with model '{normalizedModelId ?? "<default>"}'.");

        try
        {
            var session = await _client.ResumeSessionAsync(
                sessionId,
                new ResumeSessionConfig
                {
                    OnPermissionRequest = PermissionHandler.ApproveAll,
                    Model = normalizedModelId
                },
                cancellationToken).ConfigureAwait(false);
            return new GitHubCopilotSdkSession(session);
        }
        catch (Exception exception)
        {
            throw new RuntimeException(
                $"GitHub Copilot SDK could not resume session '{sessionId}'.",
                exception,
                errorCode: "runtime.session.resume.failed");
        }
    }

    public async Task<IReadOnlyList<CopilotModelDescriptor>> ListAvailableModelsAsync(CancellationToken cancellationToken = default)
    {
        await StartAsync(cancellationToken).ConfigureAwait(false);
        WriteDiagnostic("models.list", "Listing accessible Copilot SDK models.");

        try
        {
            var availableModels = await _client.ListModelsAsync(cancellationToken).ConfigureAwait(false);
            if (availableModels.Count == 0)
            {
                return Array.Empty<CopilotModelDescriptor>();
            }

            var models = new List<CopilotModelDescriptor>(availableModels.Count);
            var seenModelIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var model in availableModels)
            {
                var modelId = NormalizeModelId(model.Id);
                if (modelId is null || !seenModelIds.Add(modelId))
                {
                    continue;
                }

                var policyState = model.Policy?.State;
                if (!string.IsNullOrWhiteSpace(policyState)
                    && !string.Equals(policyState, "enabled", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var displayName = string.IsNullOrWhiteSpace(model.Name) ? modelId : model.Name;
                models.Add(new CopilotModelDescriptor(modelId, displayName));
            }

            return models;
        }
        catch (Exception exception)
        {
            throw new RuntimeException(
                "GitHub Copilot SDK could not list the models available to the current user.",
                exception,
                errorCode: "runtime.models.list.failed");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _client.DisposeAsync().ConfigureAwait(false);
        _startGate.Dispose();
    }

    internal static SessionConfig BuildSessionConfig(SessionConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.Validate();

        return new SessionConfig
        {
            OnPermissionRequest = PermissionHandler.ApproveAll,
            Model = NormalizeModelId(config.ModelId),
            Streaming = config.EnableStreaming,
            WorkingDirectory = config.WorkspacePath,
            McpServers = BuildMcpServers(config),
            SystemMessage = new SystemMessageConfig
            {
                Mode = SystemMessageMode.Append,
                Content = BuildSystemMessage(config)
            }
        };
    }

    internal static Dictionary<string, object> BuildMcpServers(SessionConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.Validate();

        try
        {
            using var stream = File.OpenRead(config.McpConfigPath);
            using var document = JsonDocument.Parse(stream);
            if (!document.RootElement.TryGetProperty("mcpServers", out var serversElement)
                || serversElement.ValueKind != JsonValueKind.Object)
            {
                throw new RuntimeException(
                    $"The MCP config at '{config.McpConfigPath}' does not define any MCP servers.",
                    errorCode: "runtime.session.mcp-config.invalid");
            }

            var servers = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var serverProperty in serversElement.EnumerateObject())
            {
                if (serverProperty.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var command = ReadRequiredString(serverProperty.Value, "command", config.McpConfigPath, serverProperty.Name);
                var args = ReadStringList(serverProperty.Value, "args");
                var serverTools = ResolveServerTools(serverProperty.Value, config.AllowedTools);
                var serverConfig = new McpLocalServerConfig
                {
                    Type = "local",
                    Command = command,
                    Args = args,
                    Tools = serverTools,
                    Timeout = 120000
                };

                var cwd = ReadOptionalString(serverProperty.Value, "cwd");
                if (!string.IsNullOrWhiteSpace(cwd))
                {
                    serverConfig.Cwd = cwd;
                }

                var env = ReadStringDictionary(serverProperty.Value, "env");
                if (env.Count > 0)
                {
                    serverConfig.Env = env;
                }

                var timeout = ReadOptionalInt32(serverProperty.Value, "timeout");
                if (timeout.HasValue)
                {
                    serverConfig.Timeout = timeout.Value;
                }

                servers[serverProperty.Name] = serverConfig;
            }

            if (servers.Count == 0)
            {
                throw new RuntimeException(
                    $"The MCP config at '{config.McpConfigPath}' did not produce any runnable MCP server definitions.",
                    errorCode: "runtime.session.mcp-config.empty");
            }

            return servers;
        }
        catch (RuntimeException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            throw new RuntimeException(
                $"WorkIQC could not load MCP server definitions from '{config.McpConfigPath}'.",
                exception,
                errorCode: "runtime.session.mcp-config.unreadable");
        }
    }

    private static string BuildSystemMessage(SessionConfiguration config)
    {
        var guidanceLines = config.SystemGuidance
            .Where(static pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
            .Select(pair => $"- {pair.Key}: {pair.Value}")
            .ToArray();

        var message = new List<string>
        {
            "You are operating inside the WorkIQC desktop client.",
            "WorkIQC is the app name, not a knowledge source.",
            "Use the WorkIQ MCP tools (ask_work_iq, accept_eula) whenever tool use is required.",
            "For first-person workplace requests, the signed-in Copilot/WorkIQ identity is the default current-user context.",
            "Do not ask for the user's own name or work email unless a WorkIQ tool result explicitly says the signed-in principal could not be resolved.",
            "Do not answer from local history, sample conversations, placeholder text, or setup metadata."
        };

        if (guidanceLines.Length > 0)
        {
            message.Add("Session guidance:");
            message.AddRange(guidanceLines);
        }

        return string.Join(Environment.NewLine, message);
    }

    private static void WriteDiagnostic(string stage, string message)
        => Trace.WriteLine($"[{DateTimeOffset.Now:O}] [WorkIQC.CopilotSdk] [{stage}] {message}");

    private static List<string> ResolveServerTools(JsonElement serverElement, IReadOnlyList<string> allowedTools)
    {
        var configuredTools = ReadStringList(serverElement, "tools");
        if (configuredTools.Count == 0)
        {
            return ["*"];
        }

        if (configuredTools.Any(static tool => string.Equals(tool, "*", StringComparison.Ordinal)))
        {
            return ["*"];
        }

        return configuredTools;
    }

    private static string ReadRequiredString(JsonElement element, string propertyName, string configPath, string serverName)
    {
        var value = ReadOptionalString(element, propertyName);
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        throw new RuntimeException(
            $"The MCP server '{serverName}' in '{configPath}' is missing a required '{propertyName}' value.",
            errorCode: "runtime.session.mcp-config.invalid");
    }

    private static string? ReadOptionalString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static List<string> ReadStringList(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return property.EnumerateArray()
            .Where(static value => value.ValueKind == JsonValueKind.String)
            .Select(static value => value.GetString())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .ToList();
    }

    private static Dictionary<string, string> ReadStringDictionary(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in property.EnumerateObject())
        {
            if (item.Value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.Value.GetString()))
            {
                values[item.Name] = item.Value.GetString()!;
            }
        }

        return values;
    }

    private static int? ReadOptionalInt32(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetInt32(out var timeout)
            ? timeout
            : null;

    private static string? NormalizeModelId(string? modelId)
        => string.IsNullOrWhiteSpace(modelId) ? null : modelId.Trim();
}

internal sealed class GitHubCopilotSdkSession : ICopilotSdkSession
{
    private readonly CopilotSession _session;
    private readonly Dictionary<string, string> _toolNamesByCallId = new(StringComparer.Ordinal);
    private readonly object _toolLock = new();

    public GitHubCopilotSdkSession(CopilotSession session)
    {
        _session = session;
    }

    public string SessionId => _session.SessionId;

    public IDisposable Subscribe(Action<CopilotSessionEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        return _session.On(sessionEvent =>
        {
            switch (sessionEvent)
            {
                case AssistantMessageDeltaEvent delta when !string.IsNullOrWhiteSpace(delta.Data.DeltaContent):
                    handler(new AssistantMessageDeltaRuntimeEvent(delta.Data.DeltaContent));
                    break;

                case AssistantMessageEvent message:
                    handler(new AssistantMessageCompletedRuntimeEvent(message.Data.Content ?? string.Empty));
                    break;

                case ToolExecutionStartEvent toolStart:
                    lock (_toolLock)
                    {
                        _toolNamesByCallId[toolStart.Data.ToolCallId] = toolStart.Data.ToolName;
                    }
                    handler(new ToolStartedRuntimeEvent(toolStart.Data.ToolName, toolStart.Data.ToolName));
                    break;

                case ToolExecutionCompleteEvent toolComplete:
                    string toolName;
                    lock (_toolLock)
                    {
                        toolName = _toolNamesByCallId.TryGetValue(toolComplete.Data.ToolCallId, out var resolvedName)
                            ? resolvedName
                            : toolComplete.Data.ToolCallId;
                    }

                    handler(toolComplete.Data.Success
                        ? new ToolCompletedRuntimeEvent(toolName, toolName)
                        : new ToolFailedRuntimeEvent(toolName, toolComplete.Data.Error?.Message ?? "Tool execution failed."));
                    break;

                case SessionIdleEvent:
                    handler(new SessionIdleRuntimeEvent());
                    break;

                case SessionErrorEvent error:
                    handler(new SessionErrorRuntimeEvent(error.Data.Message ?? "Copilot session reported an unknown error."));
                    break;
            }
        });
    }

    public async Task<string> SendAsync(string prompt, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        WriteDiagnostic("prompt.send", $"Dispatching prompt {SummarizePrompt(prompt)}");
        return await _session.SendAsync(new MessageOptions
        {
            Prompt = prompt
        }).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync() => _session.DisposeAsync();

    private static string SummarizePrompt(string prompt)
    {
        var normalized = prompt.ReplaceLineEndings(" ").Trim();
        return normalized.Length <= 120 ? $"\"{normalized}\"" : $"\"{normalized[..120].TrimEnd()}…\"";
    }

    private static void WriteDiagnostic(string stage, string message)
        => Trace.WriteLine($"[{DateTimeOffset.Now:O}] [WorkIQC.CopilotSdk] [{stage}] {message}");
}
