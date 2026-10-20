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
using NUnit.Framework;
using Pumps;

namespace Opc.Ua.Di.Tests
{
    /// <summary>
    /// Exercises the sample entry point without starting a server or creating certificate stores.
    /// </summary>
    [TestFixture]
    [Platform("Win")]
    public sealed class PumpDeviceIntegrationCommandLineTests
    {
        /// <summary>
        /// Only paths independent of the current drive and directory reach later option validation.
        /// </summary>
        [TestCase("pki", false)]
        [TestCase(@"C:pki", false)]
        [TestCase(@"\pki", false)]
        [TestCase(@"C:\private-pki", true)]
        [TestCase(@"\\server\share\private-pki", true)]
        public async Task SampleRequiresAFullyQualifiedPkiRootAsync(string path, bool fullyQualified)
        {
            using var process = new Process();
            var exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) => exited.TrySetResult(true);
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            string assembly = typeof(SoftwareUpdateDemo).Assembly.Location;
#if NETFRAMEWORK
            process.StartInfo.FileName = assembly;
            process.StartInfo.Arguments = $"--pki-root \"{path}\" --pumps 0";
#else
            process.StartInfo.FileName = "dotnet";
            process.StartInfo.ArgumentList.Add("exec");
            process.StartInfo.ArgumentList.Add("--runtimeconfig");
            process.StartInfo.ArgumentList.Add(
                Path.ChangeExtension(typeof(PumpDeviceIntegrationCommandLineTests).Assembly.Location, ".runtimeconfig.json"));
            process.StartInfo.ArgumentList.Add("--depsfile");
            process.StartInfo.ArgumentList.Add(
                Path.ChangeExtension(typeof(PumpDeviceIntegrationCommandLineTests).Assembly.Location, ".deps.json"));
            process.StartInfo.ArgumentList.Add(assembly);
            process.StartInfo.ArgumentList.Add("--pki-root");
            process.StartInfo.ArgumentList.Add(path);
            process.StartInfo.ArgumentList.Add("--pumps");
            process.StartInfo.ArgumentList.Add("0");
#endif
            Assert.That(process.Start(), Is.True);
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            Task<string> error = process.StandardError.ReadToEndAsync();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                await exited.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
            }
            finally
            {
                if (!process.HasExited)
                {
#if NETFRAMEWORK
                    process.Kill();
#else
                    process.Kill(entireProcessTree: true);
#endif
                }
            }
            string diagnostics = await output.ConfigureAwait(false) + await error.ConfigureAwait(false);
            Assert.That(process.ExitCode, Is.EqualTo(2), diagnostics);
            Assert.That(
                diagnostics,
                fullyQualified
                    ? Does.Contain("--pumps").And.Not.Contain("--pki-root")
                    : Does.Contain("--pki-root must be an absolute path.").And.Not.Contain("--pumps"));
        }
    }
}
