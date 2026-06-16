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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Pcap.Frame;
using Opc.Ua.Pcap.KeyLog;

using Opc.Ua.Bindings;

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
