/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Server.TestFramework;

namespace Opc.Ua.Interop.Tests
{
    /// <summary>
    /// The 1.5.378 peer (tests/Opc.Ua.Interop.LegacyPeer) running as a child
    /// process. The 1.5 and 2.0 stacks share assembly names and namespaces,
    /// so the peer cannot be loaded into the test process.
    /// </summary>
    public sealed class LegacyPeerProcess : IDisposable
    {
        /// <summary>
        /// Overrides the location of Opc.Ua.Interop.LegacyPeer.dll.
        /// </summary>
        public const string PeerPathVariable = "OPCUA_INTEROP_LEGACY_PEER";

        /// <summary>
        /// Overrides the host that runs the peer, so a peer of another stack
        /// (e.g. node with a node-opcua script in <see cref="PeerPathVariable"/>)
        /// can stand in for the 1.5.378 peer. Defaults to the dotnet host.
        /// </summary>
        public const string PeerHostVariable = "OPCUA_INTEROP_PEER_HOST";

        /// <summary>
        /// Space separated arguments the host gets before the peer path,
        /// e.g. "-jar" for java and a Milo peer jar.
        /// </summary>
        public const string PeerHostArgumentsVariable = "OPCUA_INTEROP_PEER_HOST_ARGUMENTS";

        private const string kPeerAssembly = "Opc.Ua.Interop.LegacyPeer.dll";
        private const string kResultPrefix = "RESULT ";
        private const string kServerReadyPrefix = "LEGACY-SERVER-READY ";
        private const string kInfoPrefix = "PEER-INFO ";

        /// <summary>
        /// How much earlier than the harness timeout a client run ends its
        /// checks on its own, so it still reports a result for each check.
        /// </summary>
        private static readonly TimeSpan s_clientDeadlineMargin = TimeSpan.FromSeconds(30);

        private readonly Process m_process;
        private readonly StringBuilder m_output = new();
        private readonly List<string> m_lines = [];
        private readonly TaskCompletionSource<int> m_exited =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Lock m_lock = new();

        private LegacyPeerProcess(Process process)
        {
            m_process = process;
        }

        /// <summary>
        /// Everything the peer wrote to standard output and error so far.
        /// </summary>
        public string Output
        {
            get
            {
                lock (m_lock)
                {
                    return m_output.ToString();
                }
            }
        }

        /// <summary>
        /// The standard output lines the peer wrote so far.
        /// </summary>
        public IReadOnlyList<string> Lines
        {
            get
            {
                lock (m_lock)
                {
                    return [.. m_lines];
                }
            }
        }

        /// <summary>
        /// The check results a peer in client mode printed so far.
        /// </summary>
        public IReadOnlyList<PeerCheckResult> Results =>
        [
            .. Lines
                .Where(l => l.StartsWith(kResultPrefix, StringComparison.Ordinal))
                .Select(l => PeerCheckResult.Parse(l.Substring(kResultPrefix.Length)))
        ];

        /// <summary>
        /// What the peer reported about itself in its "PEER-INFO {json}"
        /// line, or null when it printed none (yet).
        /// </summary>
        public PeerInfo Info
        {
            get
            {
                string line = FindLine(kInfoPrefix);
                return line == null ? null : PeerInfo.Parse(line.Substring(kInfoPrefix.Length));
            }
        }

        /// <summary>
        /// Runs the peer to completion and returns it for inspection of its
        /// exit code, output and results.
        /// </summary>
        public static async Task<(LegacyPeerProcess Peer, int ExitCode)> RunAsync(
            TimeSpan timeout,
            params string[] arguments)
        {
            if (arguments.Length > 0 && arguments[0] == "client")
            {
                // The peer cancels its checks before the harness kills it, so
                // a hanging check is reported as that check's failure.
                TimeSpan deadline = timeout - s_clientDeadlineMargin;
                if (deadline < s_clientDeadlineMargin)
                {
                    deadline = s_clientDeadlineMargin;
                }
                arguments =
                [
                    .. arguments,
                    "--timeout-seconds",
                    ((int)deadline.TotalSeconds).ToString(CultureInfo.InvariantCulture)
                ];
            }
            LegacyPeerProcess peer = Start(arguments);
            try
            {
                int exitCode = await peer.WaitForExitAsync(timeout).ConfigureAwait(false);
                return (peer, exitCode);
            }
            catch
            {
                peer.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Starts the peer in server mode on a free port and waits until it
        /// listens. The port is only reserved until the peer binds it, so a
        /// peer that exits before it is ready (another process took the
        /// port) is restarted on another port.
        /// </summary>
        public static async Task<(LegacyPeerProcess Server, string Url)> StartServerAsync(
            string pkiRoot,
            TimeSpan timeout,
            params string[] arguments)
        {
            const int maxAttempts = 3;
            for (int attempt = 1; ; attempt++)
            {
                int port = ServerFixtureUtils.GetNextFreeIPPort();
                LegacyPeerProcess server = Start(
                [
                    "server",
                    "--port", port.ToString(CultureInfo.InvariantCulture),
                    "--pki", pkiRoot,
                    .. arguments
                ]);
                try
                {
                    string ready = await server
                        .WaitForLineAsync(kServerReadyPrefix, timeout, failOnExit: attempt == maxAttempts)
                        .ConfigureAwait(false);
                    if (ready != null)
                    {
                        return (server, ready.Substring(kServerReadyPrefix.Length).Trim());
                    }
                    TestContext.Progress.WriteLine(
                        $"The 1.5 server exited before it listened on port {port}; retrying.");
                }
                catch
                {
                    server.Dispose();
                    throw;
                }
                server.Dispose();
            }
        }

        /// <summary>
        /// Starts the peer with the given arguments.
        /// </summary>
        public static LegacyPeerProcess Start(params string[] arguments)
        {
            string peer = FindPeer();
            var startInfo = new ProcessStartInfo
            {
                FileName = FindPeerHost(),
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(peer)
            };
            startInfo.Arguments = string.Join(" ", HostArguments().Append(peer).Concat(arguments).Select(Quote));

            var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            var peerProcess = new LegacyPeerProcess(process);
            process.OutputDataReceived += (_, e) => peerProcess.OnOutput(e.Data, true);
            process.ErrorDataReceived += (_, e) => peerProcess.OnOutput(e.Data, false);
            process.Exited += (_, _) => peerProcess.OnExited();

            if (!process.Start())
            {
                throw new InvalidOperationException("Could not start " + startInfo.FileName);
            }
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            return peerProcess;
        }

        /// <summary>
        /// Waits for a standard output line starting with the prefix and
        /// returns it. Fails when the timeout elapses first, and when the peer
        /// exits first unless <paramref name="failOnExit"/> is false, in
        /// which case it returns null.
        /// </summary>
        public async Task<string> WaitForLineAsync(string prefix, TimeSpan timeout, bool failOnExit = true)
        {
            var deadline = Stopwatch.StartNew();
            while (deadline.Elapsed < timeout)
            {
                string line = FindLine(prefix);
                if (line != null)
                {
                    return line;
                }
                if (m_exited.Task.IsCompleted)
                {
                    // The exit event can arrive before the last redirected
                    // lines; collect them before deciding.
                    m_process.WaitForExit();
                    line = FindLine(prefix);
                    if (line != null)
                    {
                        return line;
                    }
                    if (!failOnExit)
                    {
                        return null;
                    }
                    break;
                }
                await Task.Delay(100).ConfigureAwait(false);
            }
            Assert.Fail(
                $"The 1.5.378 peer did not print '{prefix}' within {timeout} " +
                $"(exited: {m_exited.Task.IsCompleted}).{Environment.NewLine}{Output}");
            return null;
        }

        /// <summary>
        /// Waits for the peer to exit and returns its exit code. Kills the
        /// peer and fails when it outlives the timeout.
        /// </summary>
        public async Task<int> WaitForExitAsync(TimeSpan timeout)
        {
            Task completed = await Task.WhenAny(m_exited.Task, Task.Delay(timeout)).ConfigureAwait(false);
            if (completed != m_exited.Task)
            {
                Kill();
                Assert.Fail($"The 1.5.378 peer did not exit within {timeout}.{Environment.NewLine}{Output}");
            }
            int exitCode = await m_exited.Task.ConfigureAwait(false);
            // The exit event can arrive before the last redirected line.
            m_process.WaitForExit();
            return exitCode;
        }

        /// <summary>
        /// Asks a peer running in server mode to stop and waits for it.
        /// </summary>
        public async Task StopAsync(TimeSpan timeout)
        {
            if (m_exited.Task.IsCompleted)
            {
                return;
            }
            try
            {
                await m_process.StandardInput.WriteLineAsync("stop").ConfigureAwait(false);
                await m_process.StandardInput.FlushAsync().ConfigureAwait(false);
                m_process.StandardInput.Close();
            }
            catch (IOException)
            {
                // The peer already exited.
            }
            Task completed = await Task.WhenAny(m_exited.Task, Task.Delay(timeout)).ConfigureAwait(false);
            if (completed != m_exited.Task)
            {
                Kill();
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Kill();
            m_process.Dispose();
        }

        private string FindLine(string prefix)
        {
            return Lines.FirstOrDefault(l => l.StartsWith(prefix, StringComparison.Ordinal));
        }

        private void Kill()
        {
            try
            {
                if (!m_process.HasExited)
                {
#if NETFRAMEWORK
                    m_process.Kill();
#else
                    m_process.Kill(entireProcessTree: true);
#endif
                    m_process.WaitForExit(10_000);
                }
            }
            catch (Exception e) when (e is InvalidOperationException or Win32Exception or AggregateException)
            {
                // Never started, already gone, or exiting while being killed
                // (.NET Framework reports that as access denied).
            }
        }

        private void OnOutput(string line, bool standardOutput)
        {
            if (line == null)
            {
                return;
            }
            lock (m_lock)
            {
                m_output.AppendLine(standardOutput ? line : "[stderr] " + line);
                if (standardOutput)
                {
                    m_lines.Add(line);
                }
            }
            TestContext.Progress.WriteLine("[1.5.378] " + line);
        }

        private void OnExited()
        {
            int exitCode;
            try
            {
                exitCode = m_process.ExitCode;
            }
            catch (InvalidOperationException)
            {
                exitCode = -1;
            }
            m_exited.TrySetResult(exitCode);
        }

        /// <summary>
        /// The folder of the peer, which may hold its expected differences.
        /// </summary>
        public static string PeerDirectory => Path.GetDirectoryName(FindPeer());

        private static string FindPeer()
        {
            string configured = Environment.GetEnvironmentVariable(PeerPathVariable);
            // Absolute, because the peer runs with its own folder as working
            // directory and gets the path as argument.
            string peer = Path.GetFullPath(!string.IsNullOrEmpty(configured)
                ? configured
                : Path.Combine(TestContext.CurrentContext.TestDirectory, "legacy-peer", kPeerAssembly));
            if (!File.Exists(peer))
            {
                Assert.Fail(
                    $"The 1.5.378 interop peer was not found at {peer}. It is built by the " +
                    $"BuildLegacyInteropPeer target of Opc.Ua.Interop.Tests.csproj, or set {PeerPathVariable}.");
            }
            return peer;
        }

        /// <summary>
        /// The host that runs the peer: <see cref="PeerHostVariable"/> when
        /// set, otherwise the dotnet host.
        /// </summary>
        private static string FindPeerHost()
        {
            string host = Environment.GetEnvironmentVariable(PeerHostVariable);
            return !string.IsNullOrEmpty(host) ? host : FindDotnetHost();
        }

        private static string[] HostArguments()
        {
            string arguments = Environment.GetEnvironmentVariable(PeerHostArgumentsVariable);
            return string.IsNullOrWhiteSpace(arguments)
                ? []
                : arguments.Split([' '], StringSplitOptions.RemoveEmptyEntries);
        }

        /// <summary>
        /// The dotnet host that runs the peer. 'dotnet test' publishes the
        /// host it runs under in DOTNET_HOST_PATH; otherwise the host on the
        /// PATH is used.
        /// </summary>
        private static string FindDotnetHost()
        {
            string host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
            if (!string.IsNullOrEmpty(host) && File.Exists(host))
            {
                return host;
            }
            return Path.DirectorySeparatorChar == '\\' ? "dotnet.exe" : "dotnet";
        }

        private static string Quote(string argument)
        {
            if (argument.Length > 0 && argument.IndexOfAny([' ', '\t', '"']) < 0)
            {
                return argument;
            }
            return "\"" + argument.Replace("\"", "\\\"") + "\"";
        }
    }
}
