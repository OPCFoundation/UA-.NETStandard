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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;

namespace UaLens.Samples
{
    /// <summary>
    /// A handle to exactly one successfully started repository sample. Implementations
    /// must keep the handle until disposal and must never reacquire a process by PID.
    /// </summary>
    internal interface IRepositorySampleProcess : IAsyncDisposable
    {
        int Id { get; }

        Task<int> Exit { get; }

        ValueTask TerminateOwnedTreeAsync(CancellationToken cancellationToken);
    }

    internal interface IRepositorySampleRuntime
    {
        /// <summary>
        /// Before creation, cancellation may throw. After creation, return the owned
        /// handle even if canceled; the service must drain and stop that late child.
        /// A failed call must not leave an undisclosed process behind.
        /// </summary>
        Task<IRepositorySampleProcess> StartAsync(
            RepositorySampleLaunch launch,
            RepositorySampleOutput output,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// Minimal managed-sample apphost adapter, not a command runner. No shell,
    /// workspace command, inherited sample configuration or host diagnostic dump.
    /// </summary>
    internal sealed class RepositorySampleRuntime : IRepositorySampleRuntime
    {
        public async Task<IRepositorySampleProcess> StartAsync(
            RepositorySampleLaunch launch,
            RepositorySampleOutput output,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(launch);
            ArgumentNullException.ThrowIfNull(output);
            cancellationToken.ThrowIfCancellationRequested();
            await launch.Files.VerifyOwnershipAsync(cancellationToken).ConfigureAwait(false);
            ProcessStartInfo startInfo = CreateStartInfo(launch);
            Process? process = null;
            try
            {
                process = new Process
                {
                    StartInfo = startInfo
                };
                cancellationToken.ThrowIfCancellationRequested();
                if (!process.Start())
                {
                    throw new RepositorySampleException(
                        RepositorySampleFailure.Startup, "The repository sample apphost did not start.");
                }
                var owned = new OwnedProcess(process, output);
                process = null;
                return owned;
            }
            finally
            {
                process?.Dispose();
            }
        }

        public static ProcessStartInfo CreateStartInfo(RepositorySampleLaunch launch)
        {
            ArgumentNullException.ThrowIfNull(launch);
            launch.Files.EnsureInitialized();
            RepositorySampleDescriptor expected = RepositorySampleCatalog.Get(launch.Descriptor.Id);
            string expectedExecutable = Path.Combine(
                RepositorySampleCatalog.GetBuildDirectory(launch.Source, expected.Id),
                expected.AssemblyName + (OperatingSystem.IsWindows() ? ".exe" : string.Empty));
            if (launch.Descriptor != expected ||
                !string.Equals(launch.Executable, expectedExecutable, StringComparison.Ordinal))
            {
                throw new ArgumentException("The launch does not match the repository allowlist.", nameof(launch));
            }
            RepositorySamplePaths.EnsureNoLinks(launch.SourceRoot, launch.Executable);
            RepositorySamplePaths.EnsureNoLinks(launch.Files.Root, launch.Files.Root);
            var start = new ProcessStartInfo
            {
                FileName = launch.Executable,
                WorkingDirectory = launch.Files.Root,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true
            };
            foreach (string argument in launch.Arguments)
            {
                start.ArgumentList.Add(argument);
            }
            start.Environment.Clear();
            foreach (string name in (ArrayOf<string>)
                ["SystemRoot", "WINDIR", "DOTNET_ROOT", "DOTNET_ROOT_X64", "DOTNET_ROOT_ARM64"])
            {
                string? value = Environment.GetEnvironmentVariable(name);
                if (!string.IsNullOrEmpty(value))
                {
                    start.Environment[name] = value;
                }
            }
            string profile = Path.Combine(launch.Files.Root, "profile");
            string temporary = Path.Combine(launch.Files.Root, "temp");
            foreach (string name in (ArrayOf<string>)
                ["HOME", "USERPROFILE", "LOCALAPPDATA", "APPDATA", "XDG_CONFIG_HOME", "XDG_DATA_HOME"])
            {
                start.Environment[name] = profile;
            }
            foreach (string name in (ArrayOf<string>)["TEMP", "TMP", "TMPDIR"])
            {
                start.Environment[name] = temporary;
            }
            start.Environment["DOTNET_ENVIRONMENT"] = "Production";
            start.Environment["DOTNET_CONTENTROOT"] = launch.Files.Root;
            start.Environment["DOTNET_EnableDiagnostics"] = "0";
            start.Environment["DOTNET_DbgEnableMiniDump"] = "0";
            start.Environment["COMPlus_DbgEnableMiniDump"] = "0";
            return start;
        }

        private sealed class OwnedProcess : IRepositorySampleProcess
        {
            public OwnedProcess(Process process, RepositorySampleOutput output)
            {
                m_process = process;
                Id = process.Id;
                m_stdout = CaptureAsync(process.StandardOutput, RepositorySampleOutputStream.StandardOutput, output);
                m_stderr = CaptureAsync(process.StandardError, RepositorySampleOutputStream.StandardError, output);
                Exit = ObserveExitAsync();
            }

            public int Id { get; }

            public Task<int> Exit { get; }

            public async ValueTask TerminateOwnedTreeAsync(CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!m_process.HasExited)
                {
                    try
                    {
                        m_process.Kill(entireProcessTree: true);
                    }
                    catch (InvalidOperationException) when (m_process.HasExited)
                    {
                        // Natural exit won the exact-handle termination race.
                    }
                }
                await Exit.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            public ValueTask DisposeAsync()
            {
                lock (m_gate)
                {
                    m_disposal ??= DisposeCoreAsync();
                    return new ValueTask(m_disposal);
                }
            }

            private async Task<int> ObserveExitAsync()
            {
                await m_process.WaitForExitAsync().ConfigureAwait(false);
                return m_process.ExitCode;
            }

            private async Task CaptureAsync(
                StreamReader reader,
                RepositorySampleOutputStream stream,
                RepositorySampleOutput output)
            {
                char[] buffer = new char[512];
                try
                {
                    while (true)
                    {
                        int count = await reader.ReadAsync(buffer, m_readCancellation.Token).ConfigureAwait(false);
                        if (count == 0)
                        {
                            return;
                        }
                        output.Append(stream, buffer.AsSpan(0, count));
                    }
                }
                catch (OperationCanceledException) when (m_readCancellation.IsCancellationRequested)
                {
                    // An exited sample may have descendants holding its output pipes.
                }
                catch (IOException) when (m_readCancellation.IsCancellationRequested)
                {
                    // Closing this owned pipe interrupts an outstanding asynchronous read.
                }
                catch (ObjectDisposedException) when (m_readCancellation.IsCancellationRequested)
                {
                    // Closing this owned reader raced its final read.
                }
                catch (IOException)
                {
                    output.ReportCaptureFailure(stream);
                }
                catch (ObjectDisposedException)
                {
                    output.ReportCaptureFailure(stream);
                }
                finally
                {
                    output.Complete(stream);
                }
            }

            private async Task DisposeCoreAsync()
            {
                if (!m_process.HasExited)
                {
                    throw new InvalidOperationException("A running repository sample handle cannot be disposed.");
                }
                await Exit.ConfigureAwait(false);
                var readers = Task.WhenAll(m_stdout, m_stderr);
                try
                {
                    try
                    {
                        await readers.WaitAsync(TimeSpan.FromMilliseconds(250)).ConfigureAwait(false);
                    }
                    catch (TimeoutException)
                    {
                        // An inherited child pipe must not hold an exited sample open.
                    }
                }
                finally
                {
                    await m_readCancellation.CancelAsync().ConfigureAwait(false);
                    m_process.StandardOutput.Dispose();
                    m_process.StandardError.Dispose();
                    try
                    {
                        await readers.ConfigureAwait(false);
                    }
                    finally
                    {
                        m_process.Dispose();
                        m_readCancellation.Dispose();
                    }
                }
            }

            private readonly Process m_process;
            private readonly Task m_stdout;
            private readonly Task m_stderr;
            private readonly CancellationTokenSource m_readCancellation = new();
            private readonly Lock m_gate = new();
            private Task? m_disposal;
        }
    }
}
