namespace WorkIQC.Runtime.Abstractions.Models;

public static class WorkIQRuntimeDefaults
{
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
}
