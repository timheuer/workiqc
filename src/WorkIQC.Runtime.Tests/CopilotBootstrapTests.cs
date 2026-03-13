using System.Text.Json;
using WorkIQC.Runtime.Abstractions;
using WorkIQC.Runtime.Abstractions.Models;

namespace WorkIQC.Runtime.Tests;

[TestClass]
public sealed class CopilotBootstrapTests
{
    [TestMethod]
    public async Task EnsureRuntimeDependenciesAsync_ReportsResolvedDependenciesAndCapabilities()
    {
        using var workspace = new TempWorkspace();
        CreateTool(workspace.ToolsPath, "copilot.cmd");
        CreateTool(workspace.ToolsPath, "node.exe");
        CreateTool(workspace.ToolsPath, "npm.cmd");
        CreateTool(workspace.ToolsPath, "npx.cmd");

        var bootstrap = CreateBootstrap(workspace);

        var report = await bootstrap.EnsureRuntimeDependenciesAsync();

        Assert.IsTrue(report.IsReady);
        CollectionAssert.AreEquivalent(
            new[] { "GitHub Copilot CLI", "Node.js", "npm", "npx" },
            report.Dependencies.Select(dependency => dependency.Name).ToArray());
        Assert.AreEqual(RuntimeCapabilityStatus.Available, report.Capabilities.Single(capability => capability.Name == "copilot.runtime.discovery").Status);
    }

    [TestMethod]
    public async Task EnsureWorkIQAvailableAsync_UsesLatestPackageResolution()
    {
        using var workspace = new TempWorkspace();
        CreateTool(workspace.ToolsPath, "node.exe");
        CreateTool(workspace.ToolsPath, "npx.cmd");

        var bootstrap = CreateBootstrap(workspace);

        var report = await bootstrap.EnsureWorkIQAvailableAsync();

        Assert.IsTrue(report.IsReady);
        Assert.AreEqual(RuntimeCapabilityStatus.Available, report.Capabilities.Single(capability => capability.Name == "workiq.package-resolution").Status);
        StringAssert.Contains(
            report.Capabilities.Single(capability => capability.Name == "workiq.package-resolution").Details!,
            WorkIQRuntimeDefaults.PackageReference);
    }

    [TestMethod]
    public async Task InitializeWorkspaceAsync_IgnoresRequestedVersionAndWritesLatestWorkIQConfig()
    {
        using var workspace = new TempWorkspace();
        var bootstrap = CreateBootstrap(workspace);

        var result = await bootstrap.InitializeWorkspaceAsync(version: "1.2.3");

        Assert.IsTrue(result.ConfigWasWritten);
        Assert.AreEqual(Path.Combine(workspace.RootPath, ".copilot", "mcp-config.json"), result.McpConfigPath);
        Assert.AreEqual(WorkIQRuntimeDefaults.PackageReference, result.WorkIQPackageReference);

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(result.McpConfigPath));
        var workiqConfig = document.RootElement.GetProperty("mcpServers").GetProperty("workiq");
        var args = workiqConfig.GetProperty("args").EnumerateArray().Select(static value => value.GetString()).ToArray();

        if (OperatingSystem.IsWindows())
        {
            Assert.AreEqual("npx", workiqConfig.GetProperty("command").GetString());
            CollectionAssert.AreEqual(
                new[] { "-y", "@microsoft/workiq", "mcp" },
                args);
        }
        else
        {
            Assert.AreEqual("npx", workiqConfig.GetProperty("command").GetString());
            CollectionAssert.AreEqual(
                new[] { "-y", "@microsoft/workiq", "mcp" },
                args);
        }
    }

    [TestMethod]
    public async Task InitializeWorkspaceAsync_DoesNotRewriteMatchingConfig()
    {
        using var workspace = new TempWorkspace();
        var bootstrap = CreateBootstrap(workspace);

        var firstRun = await bootstrap.InitializeWorkspaceAsync(version: "1.2.3");
        var secondRun = await bootstrap.InitializeWorkspaceAsync(version: "1.2.3");

        Assert.IsTrue(firstRun.ConfigWasWritten);
        Assert.IsFalse(secondRun.ConfigWasWritten);
        Assert.AreEqual(firstRun.McpConfigPath, secondRun.McpConfigPath);
    }

    [TestMethod]
    public async Task VerifyEulaAcceptanceAsync_ReportsActionRequiredWhenMarkerMissing()
    {
        using var workspace = new TempWorkspace();
        var bootstrap = CreateBootstrap(workspace);

        var report = await bootstrap.VerifyEulaAcceptanceAsync();

        Assert.AreEqual(EulaAcceptanceStatus.ActionRequired, report.Status);
        Assert.IsFalse(report.CanProceed);
        StringAssert.Contains(report.MarkerPath, ".workiq");
    }

    [TestMethod]
    public async Task VerifyEulaAcceptanceAsync_ReportsAcceptedWhenMarkerExists()
    {
        using var workspace = new TempWorkspace();
        Directory.CreateDirectory(Path.GetDirectoryName(workspace.EulaMarkerPath)!);
        await File.WriteAllTextAsync(
            workspace.EulaMarkerPath,
            $$"""
            {
              "acceptedAt": "{{DateTimeOffset.UtcNow:O}}",
              "verificationMode": "live-mcp-tool",
              "toolName": "{{WorkIQRuntimeDefaults.AcceptEulaToolName}}"
            }
            """);
        var bootstrap = CreateBootstrap(workspace);

        var report = await bootstrap.VerifyEulaAcceptanceAsync();

        Assert.AreEqual(EulaAcceptanceStatus.Accepted, report.Status);
        Assert.IsTrue(report.CanProceed);
        StringAssert.Contains(report.Details!, WorkIQRuntimeDefaults.AcceptEulaToolName);
    }

    [TestMethod]
    public async Task VerifyEulaAcceptanceAsync_ReportsAcceptedWhenMarkerContainsNativeBootstrapEvidence()
    {
        using var workspace = new TempWorkspace();
        Directory.CreateDirectory(Path.GetDirectoryName(workspace.EulaMarkerPath)!);
        var payload = JsonSerializer.Serialize(new
        {
            acceptedAt = DateTimeOffset.UtcNow,
            verificationMode = WorkIQRuntimeDefaults.NativeBootstrapVerificationMode,
            command = $"\"{Path.Combine(workspace.ToolsPath, "npx.cmd")}\" -y {WorkIQRuntimeDefaults.PackageReference} accept-eula",
            exitCode = 0
        });
        await File.WriteAllTextAsync(workspace.EulaMarkerPath, payload);
        var bootstrap = CreateBootstrap(workspace);

        var report = await bootstrap.VerifyEulaAcceptanceAsync();

        Assert.AreEqual(EulaAcceptanceStatus.Accepted, report.Status);
        Assert.IsTrue(report.CanProceed);
        StringAssert.Contains(report.Details!, "accept-eula");
    }

    [TestMethod]
    public async Task VerifyEulaAcceptanceAsync_RejectsLegacyMarkerThatWasNotWrittenByLiveMcpFlow()
    {
        using var workspace = new TempWorkspace();
        Directory.CreateDirectory(Path.GetDirectoryName(workspace.EulaMarkerPath)!);
        await File.WriteAllTextAsync(workspace.EulaMarkerPath, @"{""accepted"":true}");
        var bootstrap = CreateBootstrap(workspace);

        var report = await bootstrap.VerifyEulaAcceptanceAsync();

        Assert.AreEqual(EulaAcceptanceStatus.ActionRequired, report.Status);
        Assert.IsFalse(report.CanProceed);
        StringAssert.Contains(report.Resolution!, "bootstrap");
    }

    [TestMethod]
    public async Task AcceptEulaAsync_UsesNativeBootstrapAndWritesVerifiedMarker()
    {
        using var workspace = new TempWorkspace();
        CreateTool(workspace.ToolsPath, "node.exe");
        CreateNativeBootstrapTool(workspace.ToolsPath, exitCode: 0, standardOutput: "EULA accepted.");
        var bridge = new TestRuntimeBridge();
        bridge.OnCreateSessionAsync = (_, _) => throw new AssertFailedException("Native bootstrap should not create a Copilot session.");
        var bootstrap = CreateBootstrap(workspace, bridge);

        var report = await bootstrap.AcceptEulaAsync();

        Assert.IsTrue(report.CanProceed);
        var content = await File.ReadAllTextAsync(workspace.EulaMarkerPath);
        StringAssert.Contains(content, WorkIQRuntimeDefaults.PackageReference);
        StringAssert.Contains(content, $"\"verificationMode\": \"{WorkIQRuntimeDefaults.NativeBootstrapVerificationMode}\"");
        StringAssert.Contains(content, "accept-eula");
    }

    [TestMethod]
    public async Task AcceptEulaAsync_FailsWhenNativeBootstrapCommandFails()
    {
        using var workspace = new TempWorkspace();
        CreateTool(workspace.ToolsPath, "node.exe");
        CreateNativeBootstrapTool(workspace.ToolsPath, exitCode: 7, standardError: "accept-eula failed");
        var bridge = new TestRuntimeBridge();
        var bootstrap = CreateBootstrap(workspace, bridge);

        var exception = await TestHelpers.ThrowsAsync<BootstrapException>(() => bootstrap.AcceptEulaAsync());

        StringAssert.Contains(exception.Message, "exit code 7");
        Assert.IsFalse(File.Exists(workspace.EulaMarkerPath));
    }

    [TestMethod]
    public async Task RecordAuthenticationHandoffAsync_WritesMarkerAndCompletesReport()
    {
        using var workspace = new TempWorkspace();
        CreateTool(workspace.ToolsPath, "copilot.cmd");
        var bootstrap = CreateBootstrap(workspace);

        var report = await bootstrap.RecordAuthenticationHandoffAsync();

        Assert.AreEqual(AuthenticationHandoffStatus.Completed, report.Status);
        Assert.IsTrue(File.Exists(workspace.AuthenticationMarkerPath));
        StringAssert.Contains(report.LoginCommand, "copilot");
        StringAssert.Contains(report.Details!, "Copilot sign-in handoff was recorded");
    }

    private static CopilotBootstrap CreateBootstrap(TempWorkspace workspace, TestRuntimeBridge? runtimeBridge = null) =>
        new(new CopilotBootstrapOptions
        {
            WorkspaceRootPath = workspace.RootPath,
            DependencySearchPaths = new[] { workspace.ToolsPath },
            EulaMarkerPath = workspace.EulaMarkerPath,
            AuthenticationMarkerPath = workspace.AuthenticationMarkerPath
        }, runtimeBridge ?? new TestRuntimeBridge());

    private static void CreateTool(string toolsPath, string fileName)
    {
        Directory.CreateDirectory(toolsPath);
        File.WriteAllText(Path.Combine(toolsPath, fileName), "echo tool");
    }

    private static void CreateNativeBootstrapTool(string toolsPath, int exitCode, string? standardOutput = null, string? standardError = null)
    {
        Directory.CreateDirectory(toolsPath);
        var path = Path.Combine(toolsPath, "npx.cmd");
        File.WriteAllText(
            path,
            $"""
            @echo off
            setlocal
            set /p ACCEPT=
            if /I not "%~3"=="accept-eula" (
              echo unexpected args %* 1>&2
              exit /b 9
            )
            if /I "%ACCEPT%"=="y" (
              {(string.IsNullOrWhiteSpace(standardOutput) ? string.Empty : $"echo {standardOutput}")}
              exit /b {exitCode}
            )
            {(string.IsNullOrWhiteSpace(standardError) ? "echo acceptance input missing 1>&2" : $"echo {standardError} 1>&2")}
            exit /b {exitCode}
            """);
    }

    private sealed class TempWorkspace : IDisposable
    {
        public TempWorkspace()
        {
            RootPath = Path.Combine(Path.GetTempPath(), "workiqc-runtime-tests", Guid.NewGuid().ToString("N"));
            ToolsPath = Path.Combine(RootPath, "tools");
            EulaMarkerPath = Path.Combine(RootPath, ".workiq", "eula-accepted.json");
            AuthenticationMarkerPath = Path.Combine(RootPath, ".workiq", "auth-handoff.json");
            Directory.CreateDirectory(RootPath);
        }

        public string RootPath { get; }

        public string ToolsPath { get; }

        public string EulaMarkerPath { get; }

        public string AuthenticationMarkerPath { get; }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}
