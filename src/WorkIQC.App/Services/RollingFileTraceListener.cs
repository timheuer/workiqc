using System.Diagnostics;
using System.Text;

namespace WorkIQC.App.Services;

internal sealed class RollingFileTraceListener : TraceListener
{
    private readonly object _gate = new();
    private readonly string _logPath;
    private readonly string _logDirectory;
    private readonly string _archivePattern;
    private readonly long _maxFileBytes;
    private readonly int _maxArchiveFiles;
    private FileStream? _stream;
    private StreamWriter? _writer;
    private bool _disposed;

    public RollingFileTraceListener(string logPath, long maxFileBytes, int maxArchiveFiles, string? name = null)
    {
        if (string.IsNullOrWhiteSpace(logPath))
        {
            throw new ArgumentException("Log path is required.", nameof(logPath));
        }

        if (maxFileBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxFileBytes), "Max file size must be positive.");
        }

        if (maxArchiveFiles < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxArchiveFiles), "At least one archive must be retained.");
        }

        _logPath = logPath;
        _maxFileBytes = maxFileBytes;
        _maxArchiveFiles = maxArchiveFiles;
        _logDirectory = Path.GetDirectoryName(_logPath)
            ?? throw new ArgumentException("Log path must include a directory.", nameof(logPath));
        _archivePattern = $"{Path.GetFileNameWithoutExtension(_logPath)}.*{Path.GetExtension(_logPath)}";
        Name = name ?? nameof(RollingFileTraceListener);

        Directory.CreateDirectory(_logDirectory);
        PruneArchives();
        OpenWriter(append: true);
        RotateIfNeeded(incomingBytes: 0);
    }

    public override void Write(string? message)
    {
        if (message is null)
        {
            return;
        }

        WriteCore(message, appendNewLine: false);
    }

    public override void WriteLine(string? message)
        => WriteCore(message ?? string.Empty, appendNewLine: true);

    public override void Flush()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            _writer?.Flush();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                DisposeWriter();
                _disposed = true;
            }
        }

        base.Dispose(disposing);
    }

    private void WriteCore(string message, bool appendNewLine)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            RotateIfNeeded(CalculateIncomingBytes(message, appendNewLine));
            EnsureWriter();

            if (appendNewLine)
            {
                _writer!.WriteLine(message);
            }
            else
            {
                _writer!.Write(message);
            }

            _writer!.Flush();
        }
    }

    private void RotateIfNeeded(int incomingBytes)
    {
        EnsureWriter();
        var projectedLength = _stream!.Length + incomingBytes;
        if (projectedLength <= _maxFileBytes)
        {
            return;
        }

        RotateCore();
    }

    private void RotateCore()
    {
        DisposeWriter();

        if (File.Exists(_logPath))
        {
            File.Move(_logPath, BuildArchivePath());
        }

        OpenWriter(append: false);
        PruneArchives();
    }

    private void EnsureWriter()
    {
        if (_writer is not null && _stream is not null)
        {
            return;
        }

        OpenWriter(append: true);
    }

    private void OpenWriter(bool append)
    {
        var mode = append ? FileMode.Append : FileMode.Create;
        _stream = new FileStream(_logPath, mode, FileAccess.Write, FileShare.ReadWrite);
        _writer = new StreamWriter(_stream)
        {
            AutoFlush = true
        };
    }

    private void DisposeWriter()
    {
        _writer?.Dispose();
        _writer = null;
        _stream?.Dispose();
        _stream = null;
    }

    private string BuildArchivePath()
    {
        var baseName = Path.GetFileNameWithoutExtension(_logPath);
        var extension = Path.GetExtension(_logPath);
        return Path.Combine(_logDirectory, $"{baseName}.{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}{extension}");
    }

    private void PruneArchives()
    {
        var archives = Directory
            .EnumerateFiles(_logDirectory, _archivePattern, SearchOption.TopDirectoryOnly)
            .Where(path => !string.Equals(path, _logPath, StringComparison.OrdinalIgnoreCase))
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ThenByDescending(file => file.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var staleArchive in archives.Skip(_maxArchiveFiles))
        {
            staleArchive.Delete();
        }
    }

    private static int CalculateIncomingBytes(string message, bool appendNewLine)
    {
        var bytes = Encoding.UTF8.GetByteCount(message);
        if (appendNewLine)
        {
            bytes += Encoding.UTF8.GetByteCount(Environment.NewLine);
        }

        return bytes;
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(_disposed, this);
}
