using System.Diagnostics;
using System.Reflection;
using WorkIQC.Persistence;

namespace WorkIQC.App.Services;

internal static class AppTraceBootstrapper
{
    private const string ListenerName = "WorkIQC.FileTrace";
    private const long MaxLogFileBytes = 5 * 1024 * 1024;
    private const int MaxArchiveFiles = 5;
    private static readonly object Gate = new();
    private static bool _initialized;

    public static string LogPath => StorageHelper.GetDiagnosticsLogPath();

    public static void Initialize()
    {
        lock (Gate)
        {
            if (_initialized)
            {
                return;
            }

            var existing = Trace.Listeners[ListenerName];
            if (existing is null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                Trace.Listeners.Add(new RollingFileTraceListener(LogPath, MaxLogFileBytes, MaxArchiveFiles, ListenerName));
            }

            Trace.AutoFlush = true;
            _initialized = true;
            WriteStartupBanner();
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        }
    }

    private static void WriteStartupBanner()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
        Trace.WriteLine(
            $"[{DateTimeOffset.Now:O}] [WorkIQC.App] [diagnostics.init] Logging to '{LogPath}'. Rotation={MaxLogFileBytes / (1024 * 1024)}MB; Archives={MaxArchiveFiles}. Version={version}; PID={Environment.ProcessId}; Database='{StorageHelper.GetDatabasePath()}'; Workspace='{StorageHelper.GetWorkspacePath()}'; MCP='{StorageHelper.GetCopilotConfigPath()}'.");
    }

    private static void OnUnhandledException(object sender, System.UnhandledExceptionEventArgs args)
    {
        Trace.WriteLine(
            $"[{DateTimeOffset.Now:O}] [WorkIQC.App] [fatal.unhandled] IsTerminating={args.IsTerminating}; Exception={args.ExceptionObject}");
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs args)
    {
        Trace.WriteLine(
            $"[{DateTimeOffset.Now:O}] [WorkIQC.App] [fatal.task] Unobserved task exception: {args.Exception}");
    }
}
