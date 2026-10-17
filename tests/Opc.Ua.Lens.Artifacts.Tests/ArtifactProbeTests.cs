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
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Lens.Artifacts.Tests
{
    /// <summary>
    /// Exercises artifact acceptance without starting UaLens or a renderer.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class ArtifactProbeTests
    {
        /// <summary>
        /// Gives each test its own files.
        /// </summary>
        [SetUp]
        public void CreateDirectory()
        {
            m_directory = Path.Combine(Path.GetTempPath(), "ualens-probe-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_directory);
        }

        /// <summary>
        /// Removes only this test's owned directory.
        /// </summary>
        [TearDown]
        public void RemoveDirectory()
        {
            Directory.Delete(m_directory, recursive: true);
        }

        /// <summary>
        /// A successful marker cannot hide a timeout, failed exit or missing native completion.
        /// </summary>
        [TestCase(false, 0, false, "UALENS_DESKTOP_SMOKE_PASS\n", true)]
        [TestCase(false, 0, false, "UALENS_DESKTOP_SMOKE_PASS\r\n", true)]
        [TestCase(false, 1, false, "UALENS_DESKTOP_SMOKE_PASS\n", false)]
        [TestCase(false, 0, true, "UALENS_DESKTOP_SMOKE_PASS\n", false)]
        [TestCase(false, null, false, "UALENS_DESKTOP_SMOKE_PASS\n", false)]
        [TestCase(false, 0, false, "prefix UALENS_DESKTOP_SMOKE_PASS\n", false)]
        [TestCase(false, 0, false, "", false)]
        [TestCase(true, 0, false, "UALENS_DESKTOP_SMOKE_PASS\n", false)]
        [TestCase(true, 0, false, "UALENS_NATIVE_VIEWPORT_SMOKE_PASS\n", false)]
        [TestCase(true, 0, false, Markers, true)]
        public void MarkersAndOrdinaryExitAreRequired(
            bool nativeViewport, int? exitCode, bool timedOut, string stdout, bool passed)
        {
            var result = new ArtifactProcessResult(exitCode, timedOut, stdout, string.Empty);

            Assert.That(ArtifactProbe.GetFailure(result, nativeViewport) is null, Is.EqualTo(passed));
        }

        /// <summary>
        /// Graphics options are explicit and paths remain single literal arguments.
        /// </summary>
        [Test]
        public void ArgumentsAndUserStateAreIsolated(
            [Values] bool nativeViewport, [Values] bool allowSoftwareGl, [Values] bool x11Vulkan)
        {
            string executable = Path.Combine(m_directory, "artifact with spaces");
            string logs = Path.Combine(m_directory, "logs with spaces");
            ProcessStartInfo startInfo = ArtifactProbe.CreateStartInfo(
                executable, m_directory, logs, nativeViewport, allowSoftwareGl, x11Vulkan, linux: false);
            var expected = new List<string> { "--smoke-test" };
            if (allowSoftwareGl)
            {
                expected.Add("--allow-software-gl");
            }
            if (x11Vulkan)
            {
                expected.Add("--x11-vulkan");
            }
            if (nativeViewport)
            {
                expected.AddRange(["--smoke-native-viewport", "--smoke-artifact-directory", logs]);
            }

            Assert.That(startInfo.FileName, Is.EqualTo(executable));
            Assert.That(startInfo.ArgumentList, Is.EqualTo(expected));
            Assert.That(startInfo.UseShellExecute, Is.False);
            Assert.That(startInfo.WorkingDirectory, Is.EqualTo(m_directory));
            Assert.That(startInfo.Environment["HOME"], Is.EqualTo(Path.Combine(m_directory, "home")));
            Assert.That(startInfo.Environment["APPDATA"], Is.EqualTo(Path.Combine(m_directory, "roaming")));
            Assert.That(startInfo.Environment["XDG_CONFIG_HOME"], Is.EqualTo(Path.Combine(m_directory, "config")));
        }

        /// <summary>
        /// Missing Linux display support fails instead of choosing a headless backend.
        /// </summary>
        [Test]
        public void MissingDisplayIsRejected()
        {
            string? display = Environment.GetEnvironmentVariable("DISPLAY");
            try
            {
                Environment.SetEnvironmentVariable("DISPLAY", null);
                Assert.That(() => ArtifactProbe.CreateStartInfo(
                    "fixture", m_directory, m_directory, false, false, false, linux: true),
                    Throws.InstanceOf<IOException>().With.Message.Contains("X11 display"));
            }
            finally
            {
                Environment.SetEnvironmentVariable("DISPLAY", display);
            }
        }

        /// <summary>
        /// Isolating user data does not discard the selected display authorization or .NET installation.
        /// </summary>
        [Test]
        public void DisplayAuthorizationAndDotnetRootArePreserved()
        {
            string[] names = ["DISPLAY", "XAUTHORITY", "DOTNET_ROOT"];
            string?[] original = Array.ConvertAll(names, Environment.GetEnvironmentVariable);
            string[] expected = [":fixture-display", Path.Combine(m_directory, "authority"), m_directory];
            try
            {
                for (int index = 0; index < names.Length; index++)
                {
                    Environment.SetEnvironmentVariable(names[index], expected[index]);
                }
                ProcessStartInfo startInfo = ArtifactProbe.CreateStartInfo(
                    "fixture", m_directory, m_directory, false, false, false, linux: true);

                for (int index = 0; index < names.Length; index++)
                {
                    Assert.That(startInfo.Environment[names[index]], Is.EqualTo(expected[index]), names[index]);
                }
            }
            finally
            {
                for (int index = 0; index < names.Length; index++)
                {
                    Environment.SetEnvironmentVariable(names[index], original[index]);
                }
            }
        }

        /// <summary>
        /// Successful offline startup retains its actual output and removes isolated user state.
        /// </summary>
        [Test]
        public async Task StartupLogsAndCleanupAreRetainedAsync()
        {
            string executable = await CreateExecutableAsync().ConfigureAwait(false);
            string logs = Path.Combine(m_directory, "logs");
            string? home = null;
            ArtifactProbeResult result = await RunWithDisplayAsync(() => ArtifactProbe.RunAsync(
                executable, logs, nativeViewport: false,
                execute: (startInfo, timeout, _) =>
                {
                    home = startInfo.WorkingDirectory;
                    Assert.That(timeout, Is.EqualTo(TimeSpan.FromSeconds(90)));
                    Assert.That(Directory.Exists(home), Is.True);
                    return Task.FromResult(new ArtifactProcessResult(0, false, "UALENS_DESKTOP_SMOKE_PASS\n", "stderr"));
                })).ConfigureAwait(false);

            Assert.That(result.Passed, Is.True, result.Error);
            Assert.That(result.Frames, Is.Empty);
            Assert.That(Directory.Exists(home), Is.False);
            Assert.That(await File.ReadAllTextAsync(Path.Combine(logs, "stdout.txt")).ConfigureAwait(false),
                Is.EqualTo("UALENS_DESKTOP_SMOKE_PASS\n"));
            Assert.That(await File.ReadAllTextAsync(Path.Combine(logs, "stderr.txt")).ConfigureAwait(false),
                Is.EqualTo("stderr"));
            using var document = JsonDocument.Parse(
                await File.ReadAllTextAsync(Path.Combine(logs, "result.json")).ConfigureAwait(false));
            Assert.That(document.RootElement.GetProperty("passed").GetBoolean(), Is.True);
            Assert.That(document.RootElement.GetProperty("exitCode").GetInt32(), Is.Zero);
        }

        /// <summary>
        /// Both markers without newly produced image files are insufficient.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task StaleImagesAreRemovedBeforeTheProcessStartsAsync(bool allowSoftwareGl)
        {
            string executable = await CreateExecutableAsync().ConfigureAwait(false);
            string logs = Path.Combine(m_directory, "logs");
            await WriteFramesAsync(logs).ConfigureAwait(false);
            ArtifactProbeResult result = await RunWithDisplayAsync(() => ArtifactProbe.RunAsync(
                executable, logs, nativeViewport: true, allowSoftwareGl: allowSoftwareGl,
                execute: (_, _, _) =>
                {
                    Assert.That(File.Exists(Path.Combine(logs, "native-before.png")), Is.False);
                    Assert.That(File.Exists(Path.Combine(logs, "native-after.png")), Is.False);
                    return Task.FromResult(new ArtifactProcessResult(0, false, Markers, string.Empty));
                })).ConfigureAwait(false);

            Assert.That(result.Passed, Is.False);
            Assert.That(result.Error, Does.Contain("Missing regular"));
        }

        /// <summary>
        /// Image validation cannot overwrite a process or marker failure.
        /// </summary>
        [TestCase(0, false, Markers, true)]
        [TestCase(1, false, Markers, false)]
        [TestCase(null, true, Markers, false)]
        [TestCase(0, false, "UALENS_DESKTOP_SMOKE_PASS\n", false)]
        public async Task FreshImagesDoNotBypassExitOrMarkerRequirementsAsync(
            int? exitCode, bool timedOut, string stdout, bool passed)
        {
            string executable = await CreateExecutableAsync().ConfigureAwait(false);
            string logs = Path.Combine(m_directory, "logs");
            ArtifactProbeResult result = await RunWithDisplayAsync(() => ArtifactProbe.RunAsync(
                executable, logs, nativeViewport: true,
                execute: async (_, _, _) =>
                {
                    await WriteFramesAsync(logs).ConfigureAwait(false);
                    return new ArtifactProcessResult(exitCode, timedOut, stdout, string.Empty);
                })).ConfigureAwait(false);

            Assert.That(result.Passed, Is.EqualTo(passed), result.Error);
            Assert.That(result.Frames, Has.Length.EqualTo(2));
            Assert.That(result.Frames[0].PixelSha256, Is.Not.EqualTo(result.Frames[1].PixelSha256));
            Assert.That(result.TimedOut, Is.EqualTo(timedOut));
        }

        /// <summary>
        /// A missing executable and an operating-system start failure stay explicit failures.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task StartupFailureCannotPassAsync(bool executableExists)
        {
            string executable = executableExists
                ? await CreateExecutableAsync().ConfigureAwait(false)
                : Path.Combine(m_directory, "missing");
            ArtifactProbeResult result = await RunWithDisplayAsync(() => ArtifactProbe.RunAsync(
                executable, Path.Combine(m_directory, "logs"), nativeViewport: false,
                execute: (_, _, _) => throw new Win32Exception("Fixture start failed."))).ConfigureAwait(false);

            Assert.That(result.Passed, Is.False);
            Assert.That(result.ExitCode, Is.Null);
            Assert.That(result.Error, Does.Contain(executableExists ? "Fixture start failed." : "not found"));
        }

        /// <summary>
        /// The real process runner drains both pipes and preserves a nonzero exit code.
        /// </summary>
        [Test]
        public async Task ProcessRunnerDrainsBothStreamsAsync()
        {
            ProcessStartInfo startInfo = Child(
                "[Console]::Out.Write('out'); [Console]::Error.Write('err'); exit 7");

            ArtifactProcessResult result = await ArtifactProbe.RunProcessAsync(
                startInfo, TimeSpan.FromSeconds(30)).ConfigureAwait(false);

            Assert.That(result.TimedOut, Is.False);
            Assert.That(result.ExitCode, Is.EqualTo(7));
            Assert.That(result.StandardOutput, Is.EqualTo("out"));
            Assert.That(result.StandardError, Is.EqualTo("err"));
        }

        /// <summary>
        /// Expiry terminates the owned process rather than leaving it running after the test.
        /// </summary>
        [Test]
        public async Task ProcessRunnerTerminatesOnDeadlineAsync()
        {
            ArtifactProcessResult result = await ArtifactProbe.RunProcessAsync(
                Child("Start-Sleep -Seconds 60"), TimeSpan.FromMilliseconds(100)).ConfigureAwait(false);

            Assert.That(result.TimedOut, Is.True);
            Assert.That(result.ExitCode, Is.Null);
            Assert.That(result.ProcessId, Is.Not.Null);
            Assert.That(IsRunning(result.ProcessId!.Value), Is.False);
        }

        private async Task<string> CreateExecutableAsync()
        {
            string path = Path.Combine(m_directory, "not-an-executable");
            await File.WriteAllTextAsync(path, "Process execution is replaced by the test.").ConfigureAwait(false);
            return path;
        }

        private static async Task WriteFramesAsync(string logs)
        {
            Directory.CreateDirectory(logs);
            await File.WriteAllBytesAsync(Path.Combine(logs, "native-before.png"), PngFixture.Create())
                .ConfigureAwait(false);
            await File.WriteAllBytesAsync(Path.Combine(logs, "native-after.png"), PngFixture.Create(red: 0, green: 255))
                .ConfigureAwait(false);
        }

        private static async Task<ArtifactProbeResult> RunWithDisplayAsync(Func<Task<ArtifactProbeResult>> action)
        {
            string? display = Environment.GetEnvironmentVariable("DISPLAY");
            try
            {
                Environment.SetEnvironmentVariable("DISPLAY", ":unit-test-no-display-started");
                return await action().ConfigureAwait(false);
            }
            finally
            {
                Environment.SetEnvironmentVariable("DISPLAY", display);
            }
        }

        private static ProcessStartInfo Child(string script)
        {
            var startInfo = new ProcessStartInfo("pwsh")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add("-NoLogo");
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NonInteractive");
            startInfo.ArgumentList.Add("-Command");
            startInfo.ArgumentList.Add(script);
            return startInfo;
        }

        private static bool IsRunning(int processId)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                return !process.HasExited;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        private const string Markers = "UALENS_DESKTOP_SMOKE_PASS\nUALENS_NATIVE_VIEWPORT_SMOKE_PASS\n";
        private string m_directory = string.Empty;
    }
}
