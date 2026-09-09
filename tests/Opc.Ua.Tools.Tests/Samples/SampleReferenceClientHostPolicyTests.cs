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

#if NET10_0
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Tools.Tests.Samples
{
    /// <summary>
    /// Checks reference-client help and parse failures in a process isolated from inherited client configuration.
    /// </summary>
    [TestFixture]
    public sealed class SampleReferenceClientHostPolicyTests
    {
        /// <summary>
        /// Verifies that each help alias explains test-mode trust consent without warnings or client initialization.
        /// </summary>
        [TestCase("--help")]
        [TestCase("-h")]
        [TestCase("-?")]
        public async Task HelpExplainsTestAllTrustConsentWithoutStartingClientAsync(string help)
        {
            (int exitCode, string output, string error) = await RunAsync(
                ["--testall", "--autoaccept=false", help]).ConfigureAwait(false);

            Assert.That(exitCode, Is.Zero, error);
            Assert.That(error, Is.Empty);
            Assert.That(output, Does.Contain("--testall").And.Contain("--ea")
                .And.Contain("BadCertificateUntrusted").And.Contain("--autoaccept=false")
                .And.Contain("isolated testing"));
            Assert.That(output, Does.Not.Contain("WARNING:"));
        }

        /// <summary>
        /// Verifies that unknown or malformed options fail without starting the client or emitting security warnings.
        /// </summary>
        [TestCase("--unknown")]
        [TestCase("--testall=perhaps")]
        [TestCase("--ea=perhaps")]
        [TestCase("--autoaccept=perhaps")]
        public async Task InvalidOptionsFailWithoutStartingClientOrWarningAsync(string argument)
        {
            (int exitCode, string output, string error) = await RunAsync([argument]).ConfigureAwait(false);

            Assert.That(exitCode, Is.Not.Zero);
            Assert.That(error, Is.Not.Empty);
            Assert.That(output + error, Does.Not.Contain("WARNING:"));
        }

        private static async Task<(int ExitCode, string Output, string Error)> RunAsync(string[] arguments)
        {
            DirectoryInfo? root = new(TestContext.CurrentContext.TestDirectory);
            while (root is not null && !File.Exists(Path.Combine(root.FullName, "UA.slnx")))
            {
                root = root.Parent;
            }
            Assert.That(root, Is.Not.Null, "Run these tests from a repository build.");
            string configuration = new DirectoryInfo(TestContext.CurrentContext.TestDirectory).Parent!.Name;
            string assembly = Path.Combine(
                root!.FullName, "samples", "Reference", "ConsoleReferenceClient", "bin",
                configuration, "net10.0", "ConsoleReferenceClient.dll");
            Assert.That(File.Exists(assembly), Is.True,
                "Build ConsoleReferenceClient for net10.0 before running its host policy tests.");
            string workingDirectory = Path.Combine(
                root.FullName, ".reference-client-policy-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workingDirectory);
            try
            {
                var startInfo = new ProcessStartInfo("dotnet")
                {
                    WorkingDirectory = workingDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                };
                startInfo.ArgumentList.Add(assembly);
                foreach (string argument in arguments)
                {
                    startInfo.ArgumentList.Add(argument);
                }
                foreach (string key in startInfo.Environment.Keys
                    .Where(key => key.StartsWith("REFCLIENT_", StringComparison.OrdinalIgnoreCase)).ToArray())
                {
                    startInfo.Environment.Remove(key);
                }
                startInfo.Environment["DOTNET_PROCESSOR_COUNT"] = "2";
                using var process = new Process { StartInfo = startInfo };
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                Assert.That(process.Start(), Is.True);
                Task<string> output = process.StandardOutput.ReadToEndAsync(timeout.Token);
                Task<string> error = process.StandardError.ReadToEndAsync(timeout.Token);
                try
                {
                    await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                    Assert.That(Directory.GetFileSystemEntries(workingDirectory), Is.Empty,
                        "Help and parse errors must not write configuration or PKI files.");
                    return (process.ExitCode, await output.ConfigureAwait(false), await error.ConfigureAwait(false));
                }
                finally
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                    await Task.WhenAll(output, error).ConfigureAwait(false);
                }
            }
            finally
            {
                Directory.Delete(workingDirectory, recursive: true);
            }
        }
    }
}
#endif
