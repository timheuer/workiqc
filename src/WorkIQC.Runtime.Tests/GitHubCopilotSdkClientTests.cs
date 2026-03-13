using GitHub.Copilot.SDK;
using WorkIQC.Runtime.Abstractions;
using WorkIQC.Runtime.Abstractions.Models;
using WorkIQC.Runtime.Sdk;

namespace WorkIQC.Runtime.Tests;

[TestClass]
public sealed class GitHubCopilotSdkClientTests
{
    [TestMethod]
    public async Task BuildSessionConfig_BindsConsentSessionMcpServerWithAcceptTool()
    {
        using var workspace = new TempWorkspace();
        var copilotDirectory = Path.Combine(workspace.RootPath, ".copilot");
        Directory.CreateDirectory(copilotDirectory);
        var mcpConfigPath = Path.Combine(copilotDirectory, "mcp-config.json");
        await File.WriteAllTextAsync(
            mcpConfigPath,
            """
            {
              "mcpServers": {
                "workiq": {
                  "command": "cmd",
                  "args": ["/d", "/s", "/c", "npx", "-y", "@microsoft/workiq", "mcp"]
                }
              }
            }
            """);

        var sessionConfig = GitHubCopilotSdkClient.BuildSessionConfig(new SessionConfiguration
        {
            WorkspacePath = workspace.RootPath,
            McpConfigPath = mcpConfigPath,
            AllowedTools = WorkIQRuntimeDefaults.EulaAcceptanceToolNames
        });

        Assert.IsNull(sessionConfig.AvailableTools);
        Assert.AreEqual(workspace.RootPath, sessionConfig.WorkingDirectory);
        Assert.IsNull(sessionConfig.ConfigDir);
        Assert.IsNotNull(sessionConfig.McpServers);
        Assert.IsTrue(sessionConfig.McpServers.TryGetValue(WorkIQRuntimeDefaults.ServerName, out var server));
        Assert.IsInstanceOfType<McpLocalServerConfig>(server);
        var localServer = (McpLocalServerConfig)server;
        Assert.AreEqual("local", localServer.Type);
        Assert.AreEqual("cmd", localServer.Command);
        Assert.IsNotNull(localServer.Tools);
        CollectionAssert.AreEqual(new[] { "*" }, localServer.Tools.ToArray());
    }

    [TestMethod]
    public async Task BuildSessionConfig_RejectsUnreadableMcpServerDefinitions()
    {
        using var workspace = new TempWorkspace();
        var copilotDirectory = Path.Combine(workspace.RootPath, ".copilot");
        Directory.CreateDirectory(copilotDirectory);
        var mcpConfigPath = Path.Combine(copilotDirectory, "mcp-config.json");
        await File.WriteAllTextAsync(mcpConfigPath, """{ "mcpServers": { "workiq": { "args": ["mcp"] } } }""");

        var exception = await TestHelpers.ThrowsAsync<RuntimeException>(() => Task.FromResult(GitHubCopilotSdkClient.BuildSessionConfig(new SessionConfiguration
        {
            WorkspacePath = workspace.RootPath,
            McpConfigPath = mcpConfigPath,
            AllowedTools = WorkIQRuntimeDefaults.EulaAcceptanceToolNames
        })));

        StringAssert.Contains(exception.Message, "missing a required 'command'");
    }

    private sealed class TempWorkspace : IDisposable
    {
        public TempWorkspace()
        {
            RootPath = Path.Combine(Path.GetTempPath(), "workiqc-sdk-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(RootPath);
        }

        public string RootPath { get; }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}
