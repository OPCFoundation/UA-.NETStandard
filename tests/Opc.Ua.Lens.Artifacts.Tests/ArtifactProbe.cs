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
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Lens.Artifacts.Tests
{
    /// <summary>
    /// Runs the shipped executable with isolated user state and checks its exit evidence.
    /// </summary>
    internal static class ArtifactProbe
    {
        /// <summary>
        /// Retains logs and a result document for success, startup errors, timeouts and invalid captures.
        /// </summary>
        /// <exception cref="FileNotFoundException"></exception>
        internal static async Task<ArtifactProbeResult> RunAsync(
            string executable,
            string logs,
            bool nativeViewport,
            bool allowSoftwareGl = false,
            bool x11Vulkan = false,
            Func<ProcessStartInfo, TimeSpan, CancellationToken, Task<ArtifactProcessResult>>? execute = null,
            CancellationToken cancellationToken = default)
        {
            executable = Path.GetFullPath(executable);
            logs = Path.GetFullPath(logs);
            Directory.CreateDirectory(logs);
            string home = Path.Combine(Path.GetTempPath(), "ualens-artifact-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(home);
            var process = new ArtifactProcessResult(null, false, string.Empty, string.Empty);
            NativeImageFrame[] frames = [];
            string? error = null;
            try
            {
                if (!File.Exists(executable))
                {
                    throw new FileNotFoundException("The published or installed executable was not found.", executable);
                }
                if (nativeViewport)
                {
                    File.Delete(Path.Combine(logs, "native-before.png"));
                    File.Delete(Path.Combine(logs, "native-after.png"));
                }
                ProcessStartInfo startInfo = CreateStartInfo(
                    executable, home, logs, nativeViewport, allowSoftwareGl, x11Vulkan, OperatingSystem.IsLinux());
                process = await (execute ?? RunProcessAsync)(startInfo, Timeout, cancellationToken).ConfigureAwait(false);
                error = GetFailure(process, nativeViewport);
                if (nativeViewport)
                {
                    frames = await NativeImageEvidence.ReadAsync(logs, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception exception) when (
                exception is IOException or InvalidDataException or UnauthorizedAccessException or Win32Exception)
            {
                error ??= exception.Message;
            }
            finally
            {
                Directory.Delete(home, recursive: true);
            }

            await File.WriteAllTextAsync(
                Path.Combine(logs, "stdout.txt"), process.StandardOutput, cancellationToken).ConfigureAwait(false);
            await File.WriteAllTextAsync(
                Path.Combine(logs, "stderr.txt"), process.StandardError, cancellationToken).ConfigureAwait(false);
            var result = new ArtifactProbeResult(
                executable, Timeout.TotalSeconds, process.TimedOut, process.ExitCode,
                nativeViewport, allowSoftwareGl, x11Vulkan, frames, error, error is null);
            string json = JsonSerializer.Serialize(result, ArtifactProbeJsonContext.Default.ArtifactProbeResult);
            await File.WriteAllTextAsync(Path.Combine(logs, "result.json"), json, cancellationToken).ConfigureAwait(false);
            return result;
        }

        /// <summary>
        /// Builds literal arguments while retaining display authorization and replacing user-state locations.
        /// </summary>
        /// <exception cref="IOException"></exception>
        internal static ProcessStartInfo CreateStartInfo(
            string executable,
            string home,
            string logs,
            bool nativeViewport,
            bool allowSoftwareGl,
            bool x11Vulkan,
            bool linux)
        {
            var startInfo = new ProcessStartInfo(executable)
            {
                WorkingDirectory = home,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            if (linux &&
                (!startInfo.Environment.TryGetValue("DISPLAY", out string? display) ||
                    string.IsNullOrWhiteSpace(display)))
            {
                throw new IOException("A real X11 display is required; use xvfb-run for CI.");
            }
            if (linux)
            {
                SetNativeLibraryPath(startInfo, executable);
            }
            if (!startInfo.Environment.ContainsKey("XAUTHORITY"))
            {
                string authority = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".Xauthority");
                if (File.Exists(authority))
                {
                    startInfo.Environment["XAUTHORITY"] = authority;
                }
            }
            foreach ((string variable, string directory) in new[]
            {
                ("HOME", "home"), ("USERPROFILE", "home"), ("APPDATA", "roaming"),
                ("LOCALAPPDATA", "local"), ("XDG_CONFIG_HOME", "config"), ("XDG_DATA_HOME", "data"),
                ("XDG_CACHE_HOME", "cache"), ("DOTNET_CLI_HOME", "dotnet")
            })
            {
                string path = Path.Combine(home, directory);
                Directory.CreateDirectory(path);
                startInfo.Environment[variable] = path;
            }
            startInfo.Environment["DOTNET_NOLOGO"] = "true";
            startInfo.ArgumentList.Add("--smoke-test");
            if (allowSoftwareGl)
            {
                startInfo.ArgumentList.Add("--allow-software-gl");
            }
            if (x11Vulkan)
            {
                startInfo.ArgumentList.Add("--x11-vulkan");
            }
            if (nativeViewport)
            {
                startInfo.ArgumentList.Add("--smoke-native-viewport");
                startInfo.ArgumentList.Add("--smoke-artifact-directory");
                startInfo.ArgumentList.Add(logs);
            }
            return startInfo;
        }

        /// <summary>
        /// Requires an ordinary zero exit and complete, exact success-marker lines.
        /// </summary>
        internal static string? GetFailure(ArtifactProcessResult result, bool nativeViewport)
        {
            if (result.TimedOut)
            {
                return "The artifact exceeded its process deadline.";
            }
            if (result.ExitCode != 0)
            {
                return $"The artifact did not exit successfully (exit code {result.ExitCode}).";
            }
            string[] lines = result.StandardOutput.Split(["\r\n", "\n", "\r"], StringSplitOptions.None);
            if (!lines.Contains("UALENS_DESKTOP_SMOKE_PASS", StringComparer.Ordinal) ||
                (nativeViewport && !lines.Contains("UALENS_NATIVE_VIEWPORT_SMOKE_PASS", StringComparer.Ordinal)))
            {
                return "The artifact did not report every required drained-shutdown marker.";
            }
            return null;
        }

        /// <summary>
        /// Captures both streams and terminates the owned process tree on timeout or cancellation.
        /// </summary>
        /// <exception cref="IOException"></exception>
        internal static async Task<ArtifactProcessResult> RunProcessAsync(
            ProcessStartInfo startInfo,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            using var process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                throw new IOException("The artifact process could not be started.");
            }
            Task<string> stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> stderr = process.StandardError.ReadToEndAsync(cancellationToken);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(timeout);
            bool timedOut = false;
            try
            {
                await Task.WhenAll(process.WaitForExitAsync(deadline.Token), stdout, stderr)
                    .WaitAsync(deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                timedOut = true;
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await process.WaitForExitAsync(cleanup.Token).ConfigureAwait(false);
            }
            string output = await stdout.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
            string errors = await stderr.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
            return new ArtifactProcessResult(timedOut ? null : process.ExitCode, timedOut, output, errors, process.Id);
        }

        private static void SetNativeLibraryPath(ProcessStartInfo startInfo, string executable)
        {
            string directory = Path.GetDirectoryName(Path.GetFullPath(executable)) ??
                throw new InvalidDataException("The artifact has no application directory.");
            var paths = new List<string>
            {
                directory,
                Path.Combine(directory, "runtimes", "linux-x64", "native")
            };
            string store = Path.Combine(directory, ".store");
            if (Directory.Exists(store))
            {
                var options = new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    AttributesToSkip = FileAttributes.ReparsePoint,
                    IgnoreInaccessible = false
                };
                foreach (string library in Directory.EnumerateFiles(store, "libopenusd_dotnet.so", options)
                    .Order(StringComparer.Ordinal))
                {
                    paths.Add(Path.GetDirectoryName(library)!);
                }
            }
            string nativePath = string.Join(':', paths.Distinct(StringComparer.Ordinal));
            if (startInfo.Environment.TryGetValue("LD_LIBRARY_PATH", out string? inherited) &&
                !string.IsNullOrEmpty(inherited))
            {
                nativePath += ":" + inherited;
            }
            startInfo.Environment["LD_LIBRARY_PATH"] = nativePath;
        }

        /// <summary>The artifact's startup, rendering and shutdown deadline.</summary>
        internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(90);
    }

    /// <summary>
    /// Records process completion independently of marker or image validation.
    /// </summary>
    /// <param name="ExitCode">Observed exit code, or no code on timeout.</param>
    /// <param name="TimedOut">Whether the process deadline expired.</param>
    /// <param name="StandardOutput">Captured output.</param>
    /// <param name="StandardError">Captured diagnostics.</param>
    /// <param name="ProcessId">Owned process identifier, when a process was started.</param>
    internal sealed record ArtifactProcessResult(
        int? ExitCode, bool TimedOut, string StandardOutput, string StandardError, int? ProcessId = null);

    /// <summary>
    /// Retained evidence for one standalone artifact check.
    /// </summary>
    /// <param name="Executable">Exact executable selected.</param>
    /// <param name="TimeoutSeconds">Process deadline.</param>
    /// <param name="TimedOut">Whether that deadline expired.</param>
    /// <param name="ExitCode">Observed exit code.</param>
    /// <param name="NativeViewport">Whether native rendering was required.</param>
    /// <param name="AllowSoftwareGl">Explicit Mesa llvmpipe opt-in.</param>
    /// <param name="X11Vulkan">Explicit X11 Vulkan compositor selection.</param>
    /// <param name="Frames">Validated native image evidence.</param>
    /// <param name="Error">Failure reason.</param>
    /// <param name="Passed">Whether all required evidence passed.</param>
    internal sealed record ArtifactProbeResult(
        string Executable, double TimeoutSeconds, bool TimedOut, int? ExitCode,
        bool NativeViewport, bool AllowSoftwareGl, bool X11Vulkan,
        NativeImageFrame[] Frames, string? Error, bool Passed);

    /// <summary>
    /// Serializes retained evidence without reflection.
    /// </summary>
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
    [JsonSerializable(typeof(ArtifactProbeResult))]
    internal sealed partial class ArtifactProbeJsonContext : JsonSerializerContext;
}
