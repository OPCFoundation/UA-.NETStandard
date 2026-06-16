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

using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Core.Diagnostics.Bindings;
using Opc.Ua.Core.Diagnostics.Capture.Sources;
using Opc.Ua.Core.Diagnostics.Models;

using Opc.Ua.Bindings;

namespace Opc.Ua.Core.Diagnostics.Tests.Capture
{
    /// <summary>
    /// Tests that <see cref="InProcessClientCaptureSource"/> honors the
    /// <see cref="StartCaptureRequest.PcapFilePath"/> and
    /// <see cref="StartCaptureRequest.KeyLogFilePath"/> overrides used
    /// by the env-var driven auto-start.
    /// </summary>
    [TestFixture]
    public sealed class InProcessCaptureSourceExplicitPathsTests : TempDirectoryFixture
    {
        [Test]
        public async Task ExplicitPcapFilePathIsHonored()
        {
            var registry = new ChannelCaptureRegistry();
            await using var source = new InProcessClientCaptureSource(registry);

            string explicitPcap = CreateTempPath("explicit.pcap");
            await source.StartAsync(
                new StartCaptureRequest
                {
                    SessionFolder = TempDirectory,
                    PcapFilePath = explicitPcap
                },
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(File.Exists(explicitPcap), Is.True);
            Assert.That(File.Exists(Path.Combine(TempDirectory, "capture.pcap")), Is.False);

            await source.StopAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.That(source.GetRawPcapFilePath(), Is.EqualTo(explicitPcap));
        }

        [Test]
        public async Task ExplicitKeyLogFilePathIsHonored()
        {
            var registry = new ChannelCaptureRegistry();
            await using var source = new InProcessClientCaptureSource(registry);

            string explicitKeyLog = CreateTempPath("explicit.uakeys.json");
            await source.StartAsync(
                new StartCaptureRequest
                {
                    SessionFolder = TempDirectory,
                    KeyLogFilePath = explicitKeyLog
                },
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(File.Exists(explicitKeyLog), Is.True);
            // The text-format sibling is derived by extension swap; for
            // *.uakeys.json that yields *.uakeys.txt next to it.
            Assert.That(File.Exists(CreateTempPath("explicit.uakeys.txt")), Is.True);
            Assert.That(File.Exists(Path.Combine(TempDirectory, "keys.uakeys.json")), Is.False);

            await source.StopAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.That(source.GetKeyLogFilePath(), Is.EqualTo(explicitKeyLog));
        }

        [Test]
        public async Task DefaultsArePreservedWhenExplicitPathsAreNull()
        {
            var registry = new ChannelCaptureRegistry();
            await using var source = new InProcessClientCaptureSource(registry);

            await source.StartAsync(
                new StartCaptureRequest
                {
                    SessionFolder = TempDirectory
                },
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(File.Exists(Path.Combine(TempDirectory, "capture.pcap")), Is.True);
            Assert.That(File.Exists(Path.Combine(TempDirectory, "keys.uakeys.json")), Is.True);
            Assert.That(File.Exists(Path.Combine(TempDirectory, "keys.uakeys.txt")), Is.True);

            await source.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }

        [Test]
        public async Task RelativeExplicitPathsResolveAgainstSessionFolder()
        {
            var registry = new ChannelCaptureRegistry();
            await using var source = new InProcessClientCaptureSource(registry);

            await source.StartAsync(
                new StartCaptureRequest
                {
                    SessionFolder = TempDirectory,
                    PcapFilePath = "relative.pcap",
                    KeyLogFilePath = "relative.uakeys.json"
                },
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(File.Exists(Path.Combine(TempDirectory, "relative.pcap")), Is.True);
            Assert.That(File.Exists(Path.Combine(TempDirectory, "relative.uakeys.json")), Is.True);
            Assert.That(File.Exists(Path.Combine(TempDirectory, "relative.uakeys.txt")), Is.True);

            await source.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }

        [Test]
        public async Task ExplicitPathsCreateMissingParentDirectories()
        {
            var registry = new ChannelCaptureRegistry();
            await using var source = new InProcessClientCaptureSource(registry);

            string nestedDir = Path.Combine(TempDirectory, "nested", "deeper");
            string nestedPcap = Path.Combine(nestedDir, "out.pcap");
            string nestedKeys = Path.Combine(nestedDir, "out.uakeys.json");

            Assert.That(Directory.Exists(nestedDir), Is.False);

            await source.StartAsync(
                new StartCaptureRequest
                {
                    SessionFolder = TempDirectory,
                    PcapFilePath = nestedPcap,
                    KeyLogFilePath = nestedKeys
                },
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(File.Exists(nestedPcap), Is.True);
            Assert.That(File.Exists(nestedKeys), Is.True);

            await source.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }
}
