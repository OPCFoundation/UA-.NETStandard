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
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Redundancy.Samples.Tests
{
    /// <summary>
    /// Launches one of the redundant sample applications as an external process and
    /// captures its console output so tests can assert on the high-availability log
    /// lines the sample emits (for example <c>FAILOVER:</c>, <c>DATA LOSS:</c> and
    /// <c>HA OK:</c>).
    /// </summary>
    internal sealed class SampleAppProcess : IAsyncDisposable
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SampleAppProcess"/> class and
        /// starts the sample application.
        /// </summary>
        /// <param name="name">A short, human-readable name used to prefix captured output.</param>
        /// <param name="applicationDirectory">The sample application directory name under <c>Applications/</c>.</param>
        /// <param name="assemblyName">The sample application assembly (dll) name without extension.</param>
        /// <param name="arguments">The command-line arguments passed to the sample application.</param>
        /// <param name="environment">Additional environment variables set for the process.</param>
        public SampleAppProcess(
            string name,
            string applicationDirectory,
            string assemblyName,
            IReadOnlyList<string> arguments,
            IReadOnlyDictionary<string, string>? environment = null)
        {
            Name = name;
            string dll = LocateApplicationAssembly(applicationDirectory, assemblyName);
            var startInfo = new ProcessStartInfo
            {
                FileName = DotNetHostPath,
                WorkingDirectory = Path.GetDirectoryName(dll)!,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add(dll);
            foreach (string argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            if (environment != null)
            {
                foreach (KeyValuePair<string, string> variable in environment)
                {
                    startInfo.Environment[variable.Key] = variable.Value;
                }
            }

            m_process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            m_process.OutputDataReceived += OnOutput;
            m_process.ErrorDataReceived += OnOutput;
            if (!m_process.Start())
            {
                throw new InvalidOperationException($"Failed to start sample process '{name}'.");
            }

            m_process.BeginOutputReadLine();
            m_process.BeginErrorReadLine();
        }

        /// <summary>
        /// Gets the short, human-readable name of this sample process.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets a value indicating whether the underlying process has exited.
        /// </summary>
        public bool HasExited => m_process.HasExited;

        /// <summary>
        /// Waits until a captured output line contains the given substring, or the timeout elapses.
        /// </summary>
        /// <param name="substring">The substring to search for (ordinal, case-sensitive).</param>
        /// <param name="timeout">The maximum time to wait.</param>
        /// <param name="cancellationToken">A token used to cancel the wait.</param>
        /// <returns>The first matching line.</returns>
        /// <exception cref="TimeoutException">Thrown when no matching line is seen before the timeout.</exception>
        public async Task<string> WaitForLineAsync(
            string substring,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            return (string?)(await WaitForLineOrDefaultAsync(substring, timeout, cancellationToken)
                .ConfigureAwait(false) ??
                throw new TimeoutException(
                    $"Sample process '{Name}' did not emit a line containing '{substring}' within {timeout}. " +
                    $"Process {(HasExited ? "has exited" : "is still running")}."));
        }

        /// <summary>
        /// Waits until a captured output line contains the given substring, returning <c>null</c> on timeout.
        /// </summary>
        /// <param name="substring">The substring to search for (ordinal, case-sensitive).</param>
        /// <param name="timeout">The maximum time to wait.</param>
        /// <param name="cancellationToken">A token used to cancel the wait.</param>
        /// <returns>The first matching line, or <c>null</c> if the timeout elapsed.</returns>
        public async Task<string?> WaitForLineOrDefaultAsync(
            string substring,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            int index = 0;
            while (DateTime.UtcNow < deadline)
            {
                lock (m_lock)
                {
                    for (; index < m_lines.Count; index++)
                    {
                        if (m_lines[index].Contains(substring, StringComparison.Ordinal))
                        {
                            return m_lines[index];
                        }
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }

            return null;
        }

        /// <summary>
        /// Returns whether any captured output line contains the given substring.
        /// </summary>
        /// <param name="substring">The substring to search for (ordinal, case-sensitive).</param>
        /// <returns><c>true</c> when a matching line has been captured.</returns>
        public bool ContainsLine(string substring)
        {
            lock (m_lock)
            {
                foreach (string line in m_lines)
                {
                    if (line.Contains(substring, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Returns the last captured output line containing the given substring, or <c>null</c> when none matches.
        /// </summary>
        /// <param name="substring">The substring to search for (ordinal, case-sensitive).</param>
        /// <returns>The last matching line, or <c>null</c>.</returns>
        public string? LastLineContaining(string substring)
        {
            lock (m_lock)
            {
                for (int index = m_lines.Count - 1; index >= 0; index--)
                {
                    if (m_lines[index].Contains(substring, StringComparison.Ordinal))
                    {
                        return m_lines[index];
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Returns the number of captured output lines that contain the given substring.
        /// </summary>
        /// <param name="substring">The substring to search for (ordinal, case-sensitive).</param>
        /// <returns>The number of matching lines.</returns>
        public int CountLinesContaining(string substring)
        {
            int count = 0;
            lock (m_lock)
            {
                foreach (string line in m_lines)
                {
                    if (line.Contains(substring, StringComparison.Ordinal))
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        /// <summary>
        /// Waits until at least <paramref name="minimumCount"/> captured lines contain the given substring.
        /// </summary>
        /// <param name="substring">The substring to search for (ordinal, case-sensitive).</param>
        /// <param name="minimumCount">The minimum number of matching lines to wait for.</param>
        /// <param name="timeout">The maximum time to wait.</param>
        /// <param name="cancellationToken">A token used to cancel the wait.</param>
        /// <returns><c>true</c> when the count was reached before the timeout.</returns>
        public async Task<bool> WaitForCountAsync(
            string substring,
            int minimumCount,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (CountLinesContaining(substring) >= minimumCount)
                {
                    return true;
                }

                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }

            return false;
        }

        /// <summary>
        /// Waits for the process to exit, or the timeout to elapse.
        /// </summary>
        /// <param name="timeout">The maximum time to wait.</param>
        /// <returns><c>true</c> when the process exited before the timeout.</returns>
        public async Task<bool> WaitForExitAsync(TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource(timeout);
            try
            {
                await m_process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        /// <summary>
        /// Terminates the process and its child process tree if it is still running.
        /// </summary>
        public void Kill()
        {
            try
            {
                if (!m_process.HasExited)
                {
                    m_process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
                // The process already exited between the check and the kill.
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            Kill();
            await WaitForExitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
            m_process.OutputDataReceived -= OnOutput;
            m_process.ErrorDataReceived -= OnOutput;
            m_process.Dispose();
        }

        private void OnOutput(object sender, DataReceivedEventArgs e)
        {
            if (e.Data == null)
            {
                return;
            }

            lock (m_lock)
            {
                m_lines.Add(e.Data);
            }

            TestContext.Progress.WriteLine($"[{Name}] {e.Data}");
        }

        private static string LocateApplicationAssembly(string applicationDirectory, string assemblyName)
        {
            string repoRoot = FindRepositoryRoot();
            string configuration = CurrentConfiguration();
            var probePaths = new List<string>();
            foreach (string config in new[] { configuration, "Release", "Debug" })
            {
                probePaths.Add(Path.Combine(
                    repoRoot, "Applications", applicationDirectory, "bin", config, "net10.0", assemblyName + ".dll"));
            }

            foreach (string path in probePaths)
            {
                if (File.Exists(path))
                {
                    return path;
                }
            }

            throw new FileNotFoundException(
                $"Could not locate the '{assemblyName}' sample assembly. Probed: {string.Join("; ", probePaths)}. " +
                "Ensure the sample applications are built (they are referenced by this test project).");
        }

        private static string CurrentConfiguration()
        {
            // The test assembly runs from .../bin/<Configuration>/<tfm>/; reuse that
            // configuration when locating the sibling sample application output.
            string baseDirectory = AppContext.BaseDirectory.Replace('\\', '/').TrimEnd('/');
            string[] segments = baseDirectory.Split('/');
            for (int index = segments.Length - 1; index > 0; index--)
            {
                if (string.Equals(segments[index - 1], "bin", StringComparison.OrdinalIgnoreCase))
                {
                    return segments[index];
                }
            }

            return "Release";
        }

        private static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "UA.slnx")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException(
                "Could not locate the repository root (no UA.slnx found above the test output directory).");
        }

        private static string DotNetHostPath
        {
            get
            {
                string? root = Environment.GetEnvironmentVariable("DOTNET_ROOT");
                string executable = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
                if (!string.IsNullOrEmpty(root))
                {
                    string candidate = Path.Combine(root, executable);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }

                return "dotnet";
            }
        }

        private readonly Process m_process;
        private readonly List<string> m_lines = [];
        private readonly Lock m_lock = new();
    }
}
