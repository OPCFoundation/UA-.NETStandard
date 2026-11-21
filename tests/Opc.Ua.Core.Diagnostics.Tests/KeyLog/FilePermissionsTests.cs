// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Pcap.Frame;
using Opc.Ua.Pcap.KeyLog;

namespace Opc.Ua.Pcap.Tests.KeyLog
{
    /// <summary>
    /// Verifies that packet capture writers restrict file permissions on Unix platforms.
    /// </summary>
    [TestFixture]
    public sealed class FilePermissionsTests : TempDirectoryFixture
    {
        /// <summary>
        /// Verifies that the JSON key-log writer creates files readable and writable only by the user.
        /// </summary>
        [Test]
        [Platform("Linux,MacOSX")]
        public async Task UaKeyLogJsonWriterCreatesFileWithUserOnlyMode()
        {
            if (OperatingSystem.IsWindows())
            {
                Assert.Ignore("Unix file modes are not available on Windows.");
                return;
            }

            string path = CreateTempPath("keys.uakeys.json");
            await using var writer = new UaKeyLogJsonWriter(path);

            await writer.AppendAsync(
                PcapTestHelpers.CreateMaterial(SecurityPolicies.Basic256Sha256, MessageSecurityMode.SignAndEncrypt),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(File.GetUnixFileMode(path), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
        }

        /// <summary>
        /// Verifies that the text key-log writer creates files readable and writable only by the user.
        /// </summary>
        [Test]
        [Platform("Linux,MacOSX")]
        public async Task UaKeyLogTextWriterCreatesFileWithUserOnlyMode()
        {
            if (OperatingSystem.IsWindows())
            {
                Assert.Ignore("Unix file modes are not available on Windows.");
                return;
            }

            string path = CreateTempPath("keys.uakeys.txt");
            await using var writer = new UaKeyLogTextWriter(path);

            await writer.AppendAsync(
                PcapTestHelpers.CreateMaterial(SecurityPolicies.Basic256Sha256, MessageSecurityMode.SignAndEncrypt),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(File.GetUnixFileMode(path), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
        }

        /// <summary>
        /// Verifies that the pcap writer creates files readable and writable only by the user.
        /// </summary>
        [Test]
        [Platform("Linux,MacOSX")]
        public async Task PcapFileWriterCreatesFileWithUserOnlyMode()
        {
            if (OperatingSystem.IsWindows())
            {
                Assert.Ignore("Unix file modes are not available on Windows.");
                return;
            }

            string path = CreateTempPath("capture.pcap");
            await using var writer = new PcapFileWriter(path, PcapFileWriter.LinkTypeEthernet);

            await writer.WriteAsync(
                DateTimeOffset.UnixEpoch,
                new byte[] { 1, 2, 3, 4 },
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(File.GetUnixFileMode(path), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
        }

        /// <summary>
        /// Verifies that the JSON key-log writer skips Unix file mode changes on Windows.
        /// </summary>
        [Test]
        [Platform("Win")]
        public async Task UaKeyLogJsonWriterSkipsFileModeOnWindows()
        {
            string path = CreateTempPath("keys.uakeys.json");
            UaKeyLogJsonWriter? writer = null;

            try
            {
                Assert.That(() => writer = new UaKeyLogJsonWriter(path), Throws.Nothing);
            }
            finally
            {
                if (writer is not null)
                {
                    await writer.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
    }
}
