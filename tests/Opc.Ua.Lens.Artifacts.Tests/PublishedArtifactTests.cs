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
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;

[assembly: CLSCompliant(false)]

namespace Opc.Ua.Lens.Artifacts.Tests
{
    /// <summary>
    /// Exercises the published executable or installed shim in its own desktop process.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    [Explicit("Requires UALENS_ARTIFACT_EXECUTABLE and a native desktop; selected by artifact CI.")]
    public sealed class PublishedArtifactTests
    {
        /// <summary>
        /// Requires ordinary desktop startup and complete shutdown of the shipped artifact.
        /// </summary>
        [Test]
        public Task StartupDrainsAsync()
        {
            return RunAsync(nativeViewport: false);
        }

        /// <summary>
        /// Requires two changed native camera frames followed by complete shutdown.
        /// </summary>
        [Test]
        public Task NativeViewportRendersAndDrainsAsync()
        {
            return RunAsync(nativeViewport: true);
        }

        private static async Task RunAsync(bool nativeViewport)
        {
            string executable = Environment.GetEnvironmentVariable("UALENS_ARTIFACT_EXECUTABLE") ??
                throw new InvalidOperationException("Set UALENS_ARTIFACT_EXECUTABLE to the shipped executable.");
            string root = Environment.GetEnvironmentVariable("UALENS_ARTIFACT_LOG_DIRECTORY") ??
                Path.Combine(TestContext.CurrentContext.WorkDirectory, "artifact-logs");
            string logs = Path.Combine(root, nativeViewport ? "native-viewport" : "smoke");
            ArtifactProbeResult result = await ArtifactProbe.RunAsync(
                executable, logs, nativeViewport,
                ReadOption("UALENS_ARTIFACT_ALLOW_SOFTWARE_GL"),
                nativeViewport && ReadOption("UALENS_ARTIFACT_X11_VULKAN"),
                cancellationToken: TestContext.CurrentContext.CancellationToken).ConfigureAwait(false);

            foreach (string name in new[] { "stdout.txt", "stderr.txt", "result.json" })
            {
                TestContext.AddTestAttachment(Path.GetFullPath(Path.Combine(logs, name)));
            }
            foreach (NativeImageFrame frame in result.Frames)
            {
                TestContext.AddTestAttachment(Path.GetFullPath(Path.Combine(logs, frame.File)));
            }
            Assert.That(result.Passed, Is.True, result.Error);
        }

        private static bool ReadOption(string name)
        {
            string? value = Environment.GetEnvironmentVariable(name);
            return value is not null && bool.Parse(value);
        }
    }
}
