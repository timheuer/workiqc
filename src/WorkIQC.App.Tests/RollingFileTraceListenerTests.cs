using WorkIQC.App.Services;

namespace WorkIQC.App.Tests;

[TestClass]
public sealed class RollingFileTraceListenerTests
{
    [TestMethod]
    public void WriteLine_WhenSizeLimitIsExceeded_RotatesAndBoundsArchiveCount()
    {
        var logRoot = Path.Combine(Path.GetTempPath(), "workiqc-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(logRoot);
        var logPath = Path.Combine(logRoot, "workiqc-runtime.log");

        try
        {
            using var listener = new RollingFileTraceListener(logPath, maxFileBytes: 128, maxArchiveFiles: 2);

            for (var i = 0; i < 20; i++)
            {
                listener.WriteLine(new string('x', 40));
            }

            listener.Flush();

            var archives = Directory.GetFiles(logRoot, "workiqc-runtime.*.log");
            Assert.IsTrue(File.Exists(logPath), "Active runtime log should exist.");
            Assert.IsLessThanOrEqualTo(2, archives.Length, $"Expected at most 2 archives, found {archives.Length}.");
            Assert.IsLessThanOrEqualTo(128L, new FileInfo(logPath).Length, "Active log should remain within configured size limit.");
        }
        finally
        {
            if (Directory.Exists(logRoot))
            {
                Directory.Delete(logRoot, recursive: true);
            }
        }
    }

    [TestMethod]
    public void Constructor_WhenExistingLogExceedsLimit_RotatesOnStartup()
    {
        var logRoot = Path.Combine(Path.GetTempPath(), "workiqc-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(logRoot);
        var logPath = Path.Combine(logRoot, "workiqc-runtime.log");

        try
        {
            File.WriteAllText(logPath, new string('y', 512));

            using var listener = new RollingFileTraceListener(logPath, maxFileBytes: 128, maxArchiveFiles: 3);
            listener.Flush();

            var archives = Directory.GetFiles(logRoot, "workiqc-runtime.*.log");
            Assert.HasCount(1, archives, "Oversized startup log should be rotated to one archive.");
            Assert.IsLessThanOrEqualTo(128L, new FileInfo(logPath).Length, "Active log should be recreated within size bounds.");
        }
        finally
        {
            if (Directory.Exists(logRoot))
            {
                Directory.Delete(logRoot, recursive: true);
            }
        }
    }
}
