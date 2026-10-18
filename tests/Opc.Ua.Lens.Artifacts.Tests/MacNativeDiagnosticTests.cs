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
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Lens.Artifacts.Tests
{
    /// <summary>
    /// One approved native-crash capture, removed after its hosted result is retained.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    [Explicit("One authorized CI4822 macOS diagnosis; not an artifact acceptance test.")]
    public sealed class MacNativeDiagnosticTests
    {
        /// <summary>
        /// Captures native frames from one bounded LLDB execution of the unchanged artifact.
        /// </summary>
        [Test]
        public async Task CaptureCrashOnceAsync()
        {
            Assert.That(OperatingSystem.IsMacOS(), Is.True, "The diagnostic is restricted to the macOS job.");
            Assert.That(Environment.GetEnvironmentVariable("GITHUB_REPOSITORY"),
                Is.EqualTo("OPCFoundation/UA-.NETStandard"));
            Assert.That(Environment.GetEnvironmentVariable("GITHUB_RUN_ATTEMPT"), Is.EqualTo("1"));
            Assert.That(Environment.GetEnvironmentVariable("CI4822_DIAGNOSTIC"),
                Is.EqualTo("mac-native-once-20261018"));
            string head = Required("CI4822_DIAGNOSTIC_HEAD");
            Assert.That(head.Length == 40 && head.All(Uri.IsHexDigit), Is.True);

            var git = new ProcessStartInfo("git")
            {
                WorkingDirectory = Required("GITHUB_WORKSPACE"),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            git.ArgumentList.Add("rev-parse");
            git.ArgumentList.Add("--verify");
            git.ArgumentList.Add(head + "^");
            ArtifactProcessResult parent = await ArtifactProbe.RunProcessAsync(git, TimeSpan.FromSeconds(30))
                .ConfigureAwait(false);
            Assert.That(parent.ExitCode, Is.Zero, parent.StandardError);
            Assert.That(parent.StandardOutput.Trim(), Is.EqualTo("38d42498ad74a180f300a22f46a43d8af71bdeb8"),
                "Only the diagnostic commit directly following the approved repair merge may run.");

            string executable = Required("UALENS_ARTIFACT_EXECUTABLE");
            string logs = Path.Combine(Required("UALENS_ARTIFACT_LOG_DIRECTORY"), "mac-native-diagnostic");
            Directory.CreateDirectory(logs);
            string home = Path.Combine(Path.GetTempPath(), "ci4822-mac-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(home);
            try
            {
                ProcessStartInfo startInfo = ArtifactProbe.CreateStartInfo(
                    executable, home, logs, true, false, false, false);
                string[] applicationArguments = [.. startInfo.ArgumentList];
                startInfo.FileName = "/usr/bin/lldb";
                startInfo.ArgumentList.Clear();
                foreach (string argument in new[]
                {
                    "--batch", "-o", "settings set target.disable-aslr false",
                    "-k", "thread backtrace all", "-k", "image list", "-o", "run", "--", executable
                }.Concat(applicationArguments))
                {
                    startInfo.ArgumentList.Add(argument);
                }

                ArtifactProcessResult result = await ArtifactProbe.RunProcessAsync(
                    startInfo, TimeSpan.FromSeconds(120), TestContext.CurrentContext.CancellationToken)
                    .ConfigureAwait(false);
                string output = Path.Combine(logs, "lldb.txt");
                string errors = Path.Combine(logs, "lldb-stderr.txt");
                string provenance = Path.Combine(logs, "provenance.txt");
                await File.WriteAllTextAsync(output, result.StandardOutput).ConfigureAwait(false);
                await File.WriteAllTextAsync(errors, result.StandardError).ConfigureAwait(false);
                await File.WriteAllTextAsync(provenance,
                    $"diagnostic-only\nhead={head}\ncheckout={Required("GITHUB_SHA")}\n" +
                    $"run={Required("GITHUB_RUN_ID")}\nattempt=1\nexecutable={executable}\n" +
                    $"exit={result.ExitCode}\ntimedOut={result.TimedOut}\n").ConfigureAwait(false);
                TestContext.AddTestAttachment(output);
                TestContext.AddTestAttachment(errors);
                TestContext.AddTestAttachment(provenance);
                Assert.That(result.TimedOut, Is.False, "The one permitted diagnostic execution timed out.");
                Assert.That(result.StandardOutput, Does.Contain("frame #0:"),
                    "The diagnostic did not capture a native stack; do not retry without authorization.");
            }
            finally
            {
                Directory.Delete(home, recursive: true);
            }
        }

        private static string Required(string name)
        {
            return Environment.GetEnvironmentVariable(name) ??
                throw new InvalidOperationException($"Missing diagnostic input: {name}");
        }
    }
}
