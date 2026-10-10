/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * OPC Foundation MIT License 1.00. See LICENSE.txt in the repository root.
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.OneFuzz.Validator.Tests
{
    internal sealed record ProcessResult(int ExitCode, string Output, string Error, bool TimedOut, int ProcessId)
    {
        internal string Diagnostics => $"Exit: {ExitCode}; outer timeout: {TimedOut}\n{Output}\n{Error}";
    }

    internal static class ProcessRunner
    {
        internal static string DotNetHost =>
            Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";

        internal static async Task<ProcessResult> RunAsync(
            string executable,
            IEnumerable<string> arguments,
            string workingDirectory,
            TimeSpan limit)
        {
            var start = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = workingDirectory
            };
            foreach (string argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }

            start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
            start.Environment["DOTNET_NOLOGO"] = "1";
            start.Environment.Remove("DOTNET_ADDITIONAL_DEPS");
            start.Environment.Remove("DOTNET_SHARED_STORE");
            start.Environment.Remove("DOTNET_STARTUP_HOOKS");
            // Failure controls must not trigger machine-wide crash dump collection.
            start.Environment["DOTNET_DbgEnableMiniDump"] = "0";
            start.Environment["COMPlus_DbgEnableMiniDump"] = "0";

            using var process = new Process { StartInfo = start };
            if (!process.Start())
            {
                throw new InvalidOperationException("Failed to start " + executable);
            }

            Task<string> output = process.StandardOutput.ReadToEndAsync();
            Task<string> error = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(limit);
            bool timedOut = false;
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                timedOut = true;
            }
            finally
            {
                if (!process.HasExited)
                {
                    // Exact process object, never a name-based kill or a thread abort.
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().ConfigureAwait(false);
                }
            }

            return new ProcessResult(
                process.ExitCode,
                await output.ConfigureAwait(false),
                await error.ConfigureAwait(false),
                timedOut,
                process.Id);
        }
    }
}
