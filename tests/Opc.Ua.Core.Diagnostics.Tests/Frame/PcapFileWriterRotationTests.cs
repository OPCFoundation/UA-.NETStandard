/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Pcap.Frame;

namespace Opc.Ua.Pcap.Tests.Frame
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
