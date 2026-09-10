/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

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
