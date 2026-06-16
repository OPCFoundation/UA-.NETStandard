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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Core.Diagnostics.Frame;

using Opc.Ua.Bindings;

namespace Opc.Ua.Core.Diagnostics.Tests.Frame
{
    [TestFixture]
    public sealed class PcapFileWriterRotationTests : TempDirectoryFixture
    {
        [Test]
        public async Task WriterRotatesWhenMaxBytesExceeded()
        {
            string path = CreateTempPath("capture.pcap");
            byte[] packet = new byte[600];

            var writer = new PcapFileWriter(path, PcapFileWriter.LinkTypeEthernet, 65535, 1024, 16);
            try
            {
                for (int index = 0; index < 4; index++)
                {
                    await writer.WriteAsync(
                        DateTimeOffset.UnixEpoch,
                        packet,
                        CancellationToken.None).ConfigureAwait(false);
                }
            }
            finally
            {
                await writer.DisposeAsync().ConfigureAwait(false);
            }

            Assert.That(File.Exists(path), Is.True);
            Assert.That(File.Exists(CreateTempPath("capture.001.pcap")), Is.True);
        }

        [Test]
        public async Task WriterDoesNotRotateWhenUnderCap()
        {
            string path = CreateTempPath("capture.pcap");
            byte[] packet = new byte[100];

            var writer = new PcapFileWriter(
                path,
                PcapFileWriter.LinkTypeEthernet,
                65535,
                1024 * 1024,
                16);
            try
            {
                await writer.WriteAsync(DateTimeOffset.UnixEpoch, packet, CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                await writer.DisposeAsync().ConfigureAwait(false);
            }

            Assert.That(File.Exists(CreateTempPath("capture.001.pcap")), Is.False);
        }

        [Test]
        public async Task WriterPrunesOldestWhenMaxArtifactsExceeded()
        {
            string path = CreateTempPath("capture.pcap");
            byte[] packet = new byte[1200];

            var writer = new PcapFileWriter(path, PcapFileWriter.LinkTypeEthernet, 65535, 1024, 3);
            try
            {
                for (int index = 0; index < 5; index++)
                {
                    await writer.WriteAsync(
                        DateTimeOffset.UnixEpoch,
                        packet,
                        CancellationToken.None).ConfigureAwait(false);
                }
            }
            finally
            {
                await writer.DisposeAsync().ConfigureAwait(false);
            }

            string[] artifacts = GetArtifacts("capture", ".pcap");
            Assert.That(artifacts, Has.Length.EqualTo(3));
            Assert.That(artifacts.Select(Path.GetFileName), Does.Contain("capture.003.pcap"));
            Assert.That(artifacts.Select(Path.GetFileName), Does.Contain("capture.004.pcap"));
            Assert.That(artifacts.Select(Path.GetFileName), Does.Contain("capture.005.pcap"));
        }

        [Test]
        public async Task WriterMaxBytesZeroDisablesRotation()
        {
            string path = CreateTempPath("capture.pcap");
            byte[] packet = new byte[1024 * 1024];

            var writer = new PcapFileWriter(
                path,
                PcapFileWriter.LinkTypeEthernet,
                2 * 1024 * 1024,
                0,
                16);
            try
            {
                await writer.WriteAsync(
                    DateTimeOffset.UnixEpoch,
                    packet,
                    CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                await writer.DisposeAsync().ConfigureAwait(false);
            }

            Assert.That(File.Exists(CreateTempPath("capture.001.pcap")), Is.False);
        }

        [Test]
        [Platform("Linux,MacOSX")]
        public async Task WriterRotationPreservesUnixFileMode()
        {
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                string path = CreateTempPath("capture.pcap");
                byte[] packet = new byte[1200];

                var writer = new PcapFileWriter(path, PcapFileWriter.LinkTypeEthernet, 65535, 1024, 16);
                try
                {
                    await writer.WriteAsync(
                        DateTimeOffset.UnixEpoch,
                        packet,
                        CancellationToken.None).ConfigureAwait(false);
                }
                finally
                {
                    await writer.DisposeAsync().ConfigureAwait(false);
                }

                Assert.That(
                    File.GetUnixFileMode(CreateTempPath("capture.001.pcap")),
                    Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
                return;
            }

            Assert.Ignore("Unix file modes are not available on this platform.");
        }

        private string[] GetArtifacts(string baseName, string extension)
        {
            return Directory.GetFiles(TempDirectory, baseName + "*" + extension);
        }
    }
}
