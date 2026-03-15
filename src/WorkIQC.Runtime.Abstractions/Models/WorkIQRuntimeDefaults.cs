namespace WorkIQC.Runtime.Abstractions.Models;

public static class WorkIQRuntimeDefaults
{
    public const string MarkerFolderName = ".workiq";
    public const string EulaMarkerFileName = "eula-accepted.json";
    public const string AuthenticationMarkerFileName = "auth-handoff.json";
    public const string PackageName = "@microsoft/workiq";
    public const string PackageReference = PackageName;
    public const string ServerName = "workiq";
    public const string AskWorkIqToolName = "workiq-ask_work_iq";
    public const string AcceptEulaToolName = "workiq-accept_eula";
    public const string NativeBootstrapVerificationMode = "native-cli-bootstrap";
    public const string LiveMcpVerificationMode = "live-mcp-tool";
    public const string EulaUrl = "https://github.com/microsoft/work-iq-mcp";
    public const string CopilotLoginCommand = "copilot login";

    public static IReadOnlyList<string> EulaAcceptanceToolNames { get; } =
    [
        AcceptEulaToolName
    ];

    public static IReadOnlyList<string> SessionAllowedToolNames { get; } =
    [
        "*"
    ];

    public static string GetEulaMarkerPath(string workspacePath)
        => Path.Combine(workspacePath, MarkerFolderName, EulaMarkerFileName);

    public static string GetAuthenticationMarkerPath(string workspacePath)
        => Path.Combine(workspacePath, MarkerFolderName, AuthenticationMarkerFileName);
}
