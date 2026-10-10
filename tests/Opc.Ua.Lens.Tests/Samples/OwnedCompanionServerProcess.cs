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
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Samples;

namespace UaLens.Tests.Samples
{
    internal sealed class OwnedCompanionServerProcess : IAsyncDisposable
    {
        private OwnedCompanionServerProcess(
            Process process, NamedPipeServerStream pipe, Task exit, Task<string> output, Task<string> error)
        {
            m_process = process;
            m_pipe = pipe;
            m_reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
            m_writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
            m_output = output;
            m_error = error;
            m_exit = exit;
        }

        public bool ExitedNormally { get; private set; }

        public static async Task<OwnedCompanionServerProcess> StartAsync(
            string assembly, bool generator, string root, string endpoint, CancellationToken token)
        {
            if (!Path.IsPathFullyQualified(assembly) ||
                Path.GetFileName(assembly) != "Opc.Ua.Lens.Tests.dll" ||
                !File.Exists(assembly))
            {
                throw new ArgumentException("Select the existing server test assembly by its absolute path.");
            }
            string directory = Path.GetDirectoryName(assembly)!;
            RepositorySamplePaths.EnsureNoLinks(directory, assembly);
            RepositorySamplePaths.EnsureNoLinks(root, root);
            string pipeName = "ualens-companion-" + Guid.NewGuid().ToString("N");
            var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            var start = new ProcessStartInfo
            {
                FileName = "dotnet",
                WorkingDirectory = root,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            start.ArgumentList.Add("vstest");
            start.ArgumentList.Add(assembly);
            start.ArgumentList.Add("--TestAdapterPath:" + directory);
            start.ArgumentList.Add(
                "--TestCaseFilter:FullyQualifiedName=" +
                "UaLens.Tests.Samples.RepositorySampleLiveTests.RunOwnedCompanionServerProcess");
            start.ArgumentList.Add("--logger:console;verbosity=quiet");
            start.ArgumentList.Add("--ResultsDirectory:" + Path.Combine(root, "server-results"));
            start.Environment[kPipe] = pipeName;
            start.Environment[kRoot] = root;
            start.Environment[kEndpoint] = endpoint;
            start.Environment[kKind] = generator ? "generator" : "registry";
            var process = new Process { StartInfo = start };
            OwnedCompanionServerProcess? owned = null;
            Task? exited = null;
            try
            {
                token.ThrowIfCancellationRequested();
                if (!process.Start())
                {
                    throw new InvalidOperationException("The owned companion test host did not start.");
                }
                exited = process.WaitForExitAsync(CancellationToken.None);
                (Task<string> output, Task<string> error) = StartOutputReaders(process);
                Task connection = pipe.WaitForConnectionAsync(token);
                if (await Task.WhenAny(connection, exited).ConfigureAwait(false) != connection)
                {
                    throw new AssertionException(
                        "The owned server exited before connecting: " +
                        await output.ConfigureAwait(false) +
                        "\n" +
                        await error.ConfigureAwait(false));
                }
                await connection.ConfigureAwait(false);
                owned = new OwnedCompanionServerProcess(process, pipe, exited, output, error);
                await owned.RequireReplyAsync("ready", token).ConfigureAwait(false);
                return owned;
            }
            catch
            {
                if (owned is not null)
                {
                    try
                    {
                        await owned.TerminateAsync().ConfigureAwait(false);
                    }
                    finally
                    {
                        owned.DisposeHandles();
                    }
                }
                else
                {
                    try
                    {
                        if (exited is not null)
                        {
                            await TerminateAsync(process, exited).ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        process.Dispose();
                        await pipe.DisposeAsync().ConfigureAwait(false);
                    }
                }
                throw;
            }
        }

        public async Task SelectNumericProfileAsync(NodeId representation, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            await m_writer.WriteLineAsync("numeric-profile " + representation).ConfigureAwait(false);
            await RequireReplyAsync("profile-selected", token).ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            if (m_disposed)
            {
                return;
            }
            m_disposed = true;
            try
            {
                using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                if (!m_process.HasExited)
                {
                    await m_writer.WriteLineAsync("stop").ConfigureAwait(false);
                    await RequireReplyAsync("stopped", stop.Token).ConfigureAwait(false);
                }
                await m_exit.WaitAsync(stop.Token).ConfigureAwait(false);
                string output = await OutputAsync().ConfigureAwait(false);
                Assert.That(m_process.ExitCode, Is.Zero, output);
                ExitedNormally = true;
            }
            finally
            {
                try
                {
                    await TerminateAsync().ConfigureAwait(false);
                }
                finally
                {
                    DisposeHandles();
                }
            }
        }

        public static async Task RunServerAsync(
            Func<bool, string, string, Func<Task>, Func<Task<string>>, Func<string, Task>, CancellationToken, Task> run)
        {
            string pipeName = Environment.GetEnvironmentVariable(kPipe) ??
                throw new InvalidOperationException("The owned-server control pipe is missing.");
            string root = Environment.GetEnvironmentVariable(kRoot) ??
                throw new InvalidOperationException("The owned-server root is missing.");
            string endpoint = Environment.GetEnvironmentVariable(kEndpoint) ??
                throw new InvalidOperationException("The owned-server endpoint is missing.");
            string kind = Environment.GetEnvironmentVariable(kKind) ??
                throw new InvalidOperationException("The owned-server kind is missing.");
            if (!pipeName.StartsWith("ualens-companion-", StringComparison.Ordinal) ||
                !Guid.TryParseExact(pipeName["ualens-companion-".Length..], "N", out _) ||
                kind is not ("generator" or "registry") ||
                !Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri) ||
                uri.Scheme != Utils.UriSchemeOpcTcp ||
                !uri.IsLoopback ||
                uri.UserInfo.Length != 0 ||
                uri.Query.Length != 0 ||
                uri.Fragment.Length != 0)
            {
                throw new InvalidOperationException("The owned-server launch contract is invalid.");
            }
            RepositorySamplePaths.EnsureNoLinks(root, root);
            using var lifetime = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            using var pipe = new NamedPipeClientStream(
                ".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(lifetime.Token).ConfigureAwait(false);
            using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
            var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true)
            {
                AutoFlush = true
            };
            await using (writer.ConfigureAwait(false))
            {
                await run(kind == "generator", root, endpoint,
                    () => writer.WriteLineAsync("ready"),
                    async () => await reader.ReadLineAsync(lifetime.Token).ConfigureAwait(false) ??
                        throw new IOException("The fixture owner disconnected."),
                    writer.WriteLineAsync, lifetime.Token).ConfigureAwait(false);
            }
        }

        private async Task RequireReplyAsync(string expected, CancellationToken token)
        {
            string? reply = await m_reader.ReadLineAsync(token).ConfigureAwait(false);
            if (reply != expected)
            {
                throw new AssertionException(
                    $"The owned server did not report {expected}: {reply ?? "pipe closed"}.");
            }
        }

        private Task TerminateAsync()
        {
            return TerminateAsync(m_process, m_exit);
        }

        private static async Task TerminateAsync(Process process, Task exited)
        {
            if (!process.HasExited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException) when (process.HasExited)
                {
                    // Natural exit won the exact-handle termination race.
                }
            }
            await exited.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        }

        private async Task<string> OutputAsync()
        {
            return await m_output.ConfigureAwait(false) + "\n" + await m_error.ConfigureAwait(false);
        }

        private void DisposeHandles()
        {
            m_writer.Dispose();
            m_reader.Dispose();
            m_pipe.Dispose();
            m_process.Dispose();
        }

        private static (Task<string> Output, Task<string> Error) StartOutputReaders(Process process)
        {
            return (ReadTailAsync(process.StandardOutput), ReadTailAsync(process.StandardError));
        }

        private static async Task<string> ReadTailAsync(StreamReader reader)
        {
            var text = new StringBuilder();
            var buffer = new char[1024];
            while (true)
            {
                int read = await reader.ReadAsync(buffer.AsMemory()).ConfigureAwait(false);
                if (read == 0)
                {
                    return text.ToString();
                }
                text.Append(buffer, 0, read);
                if (text.Length > 8192)
                {
                    text.Remove(0, text.Length - 8192);
                }
            }
        }

        private const string kPipe = "UALENS_OWNED_COMPANION_PIPE";
        private const string kRoot = "UALENS_OWNED_COMPANION_ROOT";
        private const string kEndpoint = "UALENS_OWNED_COMPANION_ENDPOINT";
        private const string kKind = "UALENS_OWNED_COMPANION_KIND";
        private readonly Process m_process;
        private readonly NamedPipeServerStream m_pipe;
        private readonly StreamReader m_reader;
        private readonly StreamWriter m_writer;
        private readonly Task<string> m_output;
        private readonly Task<string> m_error;
        private readonly Task m_exit;
        private bool m_disposed;
    }
}
