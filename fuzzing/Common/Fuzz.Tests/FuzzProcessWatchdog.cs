// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Fuzzing
{
    /// <summary>
    /// Enforces a hard deadline for synchronous callbacks without blocking the test host.
    /// </summary>
    internal static class FuzzProcessWatchdog
    {
        public static async Task<(int ExitCode, bool TimedOut, string Output, string Error)> RunAsync(
            ProcessStartInfo startInfo,
            TimeSpan timeout)
        {
            _ = startInfo ?? throw new ArgumentNullException(nameof(startInfo));
            if (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout));
            }

            startInfo.UseShellExecute = false;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            var exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            process.Exited += (_, _) => exited.TrySetResult(true);
            if (!process.Start())
            {
                throw new InvalidOperationException("Could not start the fuzz replay worker.");
            }

            using var cancellation = new CancellationTokenSource();
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            Task<string> error = process.StandardError.ReadToEndAsync();
            try
            {
                Task deadline = Task.Delay(timeout, cancellation.Token);
                Task completed = await Task.WhenAny(exited.Task, deadline).ConfigureAwait(false);
                bool timedOut = completed != exited.Task;
                if (timedOut)
                {
                    KillIfRunning(process);
                }
                await exited.Task.ConfigureAwait(false);
                return (
                    process.ExitCode,
                    timedOut,
                    await output.ConfigureAwait(false),
                    await error.ConfigureAwait(false));
            }
            finally
            {
                cancellation.Cancel();
                KillIfRunning(process);
                await exited.Task.ConfigureAwait(false);
            }
        }

        private static void KillIfRunning(Process process)
        {
            if (!process.HasExited)
            {
                try
                {
#if NETFRAMEWORK
                    process.Kill();
#else
                    process.Kill(entireProcessTree: true);
#endif
                }
                catch (InvalidOperationException) when (process.HasExited)
                {
                    // The callback exited between the liveness check and termination.
                }
            }
        }
    }
}
