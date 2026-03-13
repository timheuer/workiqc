using System.Diagnostics;
using System.Text;

namespace WorkIQC.Runtime.Sdk;

internal static class ExternalProcessRunner
{
    public static async Task<ProcessExecutionResult> RunAsync(
        ProcessStartInfo startInfo,
        string? standardInput,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(startInfo);

        using var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException($"Failed to start '{startInfo.FileName}'.");
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            throw new InvalidOperationException(
                $"Failed to start '{startInfo.FileName} {startInfo.Arguments}'.",
                exception);
        }

        using var cancellationRegistration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception)
            {
            }
        });

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        if (startInfo.RedirectStandardInput)
        {
            if (!string.IsNullOrWhiteSpace(standardInput))
            {
                await process.StandardInput.WriteAsync(standardInput.AsMemory(), cancellationToken).ConfigureAwait(false);
                await process.StandardInput.FlushAsync().ConfigureAwait(false);
            }

            process.StandardInput.Close();
        }

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        return new ProcessExecutionResult(process.ExitCode, stdout, stderr);
    }

    internal sealed record ProcessExecutionResult(int ExitCode, string StandardOutput, string StandardError)
    {
        public string CombinedOutput
        {
            get
            {
                if (string.IsNullOrWhiteSpace(StandardError))
                {
                    return StandardOutput.Trim();
                }

                if (string.IsNullOrWhiteSpace(StandardOutput))
                {
                    return StandardError.Trim();
                }

                var builder = new StringBuilder(StandardOutput.Trim());
                builder.AppendLine();
                builder.Append(StandardError.Trim());
                return builder.ToString();
            }
        }
    }
}
