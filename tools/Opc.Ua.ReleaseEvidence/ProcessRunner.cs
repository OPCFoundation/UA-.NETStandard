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
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.ReleaseEvidence
{
    /// <summary>
    /// Runs bounded external tool invocations with captured output, cancellation, and a fixed execution deadline.
    /// </summary>
    internal sealed class ProcessRunner
    {
        /// <summary>
        /// Returns bounded standard output from a successful tool invocation and rejects failures or expired execution.
        /// </summary>
        public async Task<string> RunAsync(
            string root,
            string command,
            string[] arguments,
            CancellationToken cancellationToken,
            bool offlineNuget = false)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromMinutes(3));
            var start = new ProcessStartInfo(command)
            {
                WorkingDirectory = root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            foreach (string argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }
            if (offlineNuget)
            {
                start.Environment["NUGET_CERT_REVOCATION_MODE"] = "offline";
                start.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en-US";
            }
            using Process process = Process.Start(start) ?? throw new IOException("Could not start input evaluation.");
            Task<string> output = ReadBoundedAsync(
                process.StandardOutput.ReadAsync, deadline.CancelAsync, deadline.Token);
            Task<string> error = ReadBoundedAsync(
                process.StandardError.ReadAsync, deadline.CancelAsync, deadline.Token);
            try
            {
                await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
                string stdout = await output.ConfigureAwait(false);
                await error.ConfigureAwait(false);
                return process.ExitCode == 0
                    ? stdout
                    : throw new IOException(
                        $"Input evaluation failed ({Path.GetFileName(command)}, exit {process.ExitCode}).");
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(true);
                    await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                }
                await Task.WhenAll(output, error).ConfigureAwait(false);
            }
        }

        private static async Task<string> ReadBoundedAsync(
            Func<Memory<char>, CancellationToken, ValueTask<int>> read,
            Func<Task> cancel,
            CancellationToken cancellationToken)
        {
            var text = new StringBuilder();
            var buffer = new char[8192];
            int count;
            while ((count = await read(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) != 0)
            {
                if (text.Length + count > 8 * 1024 * 1024)
                {
                    await cancel().ConfigureAwait(false);
                    throw new InvalidDataException("Verifier process output exceeds the 8 MiB limit.");
                }
                text.Append(buffer, 0, count);
            }
            return text.ToString();
        }
    }
}
