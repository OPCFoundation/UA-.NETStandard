// Copyright (c) OPC Foundation, Inc. All rights reserved.
// Licensed under the MIT License. See LICENSE.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.ReleaseEvidence
{
    internal sealed class ProcessRunner
    {
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
