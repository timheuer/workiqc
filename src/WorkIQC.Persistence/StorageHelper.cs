namespace WorkIQC.Persistence;

public static class StorageHelper
{
    public static string GetDatabasePath()
    {
        // In MSIX context, use ApplicationData.Current.LocalFolder
        // For development, use %LocalAppData%\WorkIQC
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appFolder = Path.Combine(localAppData, "WorkIQC");
        
        Directory.CreateDirectory(appFolder);
        
        return Path.Combine(appFolder, "workiq.db");
    }

    public static string GetWorkspacePath()
    {
        // App-owned workspace for .copilot\mcp-config.json
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var workspaceFolder = Path.Combine(localAppData, "WorkIQC");
        
        Directory.CreateDirectory(workspaceFolder);
        
        return workspaceFolder;
    }

    public static string GetCopilotConfigPath()
    {
        var workspace = GetWorkspacePath();
        var copilotFolder = Path.Combine(workspace, ".copilot");
        
        Directory.CreateDirectory(copilotFolder);

        return Path.Combine(copilotFolder, "mcp-config.json");
    }

    public static string GetDiagnosticsDirectoryPath()
    {
        var diagnosticsFolder = Path.Combine(GetWorkspacePath(), "logs");
        Directory.CreateDirectory(diagnosticsFolder);
        return diagnosticsFolder;
    }

    public static string GetDiagnosticsLogPath()
        => Path.Combine(GetDiagnosticsDirectoryPath(), "workiqc-runtime.log");
}
