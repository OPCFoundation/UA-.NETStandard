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
using Opc.Ua.Pcap.KeyLog;
using Opc.Ua.Bindings;

namespace Opc.Ua.Pcap.Tests.KeyLog
{
    [TestFixture]
    public sealed class KeyLogWriterRotationTests : TempDirectoryFixture
    {
        [TestCase(typeof(UaKeyLogJsonWriter))]
        [TestCase(typeof(UaKeyLogTextWriter))]
        public async Task WriterRotatesWhenMaxBytesExceeded(Type writerType)
        {
            string path = CreatePath(writerType);
            using ChannelKeyMaterial material = CreateLargeMaterial();

            await UseWriterAsync(writerType, path, 1024, 16, async writer =>
            {
                await writer.AppendAsync(material, CancellationToken.None).ConfigureAwait(false);
                await writer.AppendAsync(material, CancellationToken.None).ConfigureAwait(false);
            }).ConfigureAwait(false);

            Assert.That(File.Exists(path), Is.True);
            Assert.That(File.Exists(GetRotatedPath(path, 1)), Is.True);
        }

        [TestCase(typeof(UaKeyLogJsonWriter))]
        [TestCase(typeof(UaKeyLogTextWriter))]
        public async Task WriterDoesNotRotateWhenUnderCap(Type writerType)
        {
            string path = CreatePath(writerType);
            using ChannelKeyMaterial material = PcapTestHelpers.CreateMaterial(
                SecurityPolicies.Basic256Sha256,
                MessageSecurityMode.SignAndEncrypt);

            await UseWriterAsync(writerType, path, 1024 * 1024, 16, async writer =>
            {
                await writer.AppendAsync(material, CancellationToken.None).ConfigureAwait(false);
            }).ConfigureAwait(false);

            Assert.That(File.Exists(GetRotatedPath(path, 1)), Is.False);
        }

        [TestCase(typeof(UaKeyLogJsonWriter))]
        [TestCase(typeof(UaKeyLogTextWriter))]
        public async Task WriterPrunesOldestWhenMaxArtifactsExceeded(Type writerType)
        {
            string path = CreatePath(writerType);
            using ChannelKeyMaterial material = CreateLargeMaterial();

            await UseWriterAsync(writerType, path, 1024, 3, async writer =>
            {
                for (int index = 0; index < 5; index++)
                {
                    await writer.AppendAsync(material, CancellationToken.None).ConfigureAwait(false);
                }
            }).ConfigureAwait(false);

            string fileName = Path.GetFileNameWithoutExtension(path);
            string extension = Path.GetExtension(path);
            string[] artifacts = GetArtifacts(path);
            Assert.That(artifacts, Has.Length.EqualTo(3));
            Assert.That(artifacts.Select(Path.GetFileName), Does.Contain(fileName + ".003" + extension));
            Assert.That(artifacts.Select(Path.GetFileName), Does.Contain(fileName + ".004" + extension));
            Assert.That(artifacts.Select(Path.GetFileName), Does.Contain(fileName + ".005" + extension));
        }

        [TestCase(typeof(UaKeyLogJsonWriter))]
        [TestCase(typeof(UaKeyLogTextWriter))]
        public async Task WriterMaxBytesZeroDisablesRotation(Type writerType)
        {
            string path = CreatePath(writerType);
            using ChannelKeyMaterial material = CreateLargeMaterial();

            await UseWriterAsync(writerType, path, 0, 16, async writer =>
            {
                for (int index = 0; index < 512; index++)
                {
                    await writer.AppendAsync(material, CancellationToken.None).ConfigureAwait(false);
                }
            }).ConfigureAwait(false);

            Assert.That(File.Exists(GetRotatedPath(path, 1)), Is.False);
        }

        [TestCase(typeof(UaKeyLogJsonWriter))]
        [TestCase(typeof(UaKeyLogTextWriter))]
        [Platform("Linux,MacOSX")]
        public async Task WriterRotationPreservesUnixFileMode(Type writerType)
        {
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                string path = CreatePath(writerType);
                using ChannelKeyMaterial material = CreateLargeMaterial();

                await UseWriterAsync(writerType, path, 1024, 16, async writer =>
                {
                    await writer.AppendAsync(material, CancellationToken.None).ConfigureAwait(false);
                }).ConfigureAwait(false);

                Assert.That(
                    File.GetUnixFileMode(GetRotatedPath(path, 1)),
                    Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
                return;
            }

            Assert.Ignore("Unix file modes are not available on this platform.");
        }

        private string CreatePath(Type writerType)
        {
            string fileName = writerType == typeof(UaKeyLogJsonWriter)
                ? "keys.uakeys.json"
                : "keys.uakeys.txt";
            return CreateTempPath(fileName);
        }

        private static async Task UseWriterAsync(
            Type writerType,
            string path,
            long maxBytes,
            int maxArtifacts,
            Func<IKeyLogWriter, Task> action)
        {
            IKeyLogWriter writer;
            if (writerType == typeof(UaKeyLogJsonWriter))
            {
                writer = new UaKeyLogJsonWriter(path, maxBytes, maxArtifacts);
            }
            else if (writerType == typeof(UaKeyLogTextWriter))
            {
                writer = new UaKeyLogTextWriter(path, maxBytes, maxArtifacts);
            }
            else
            {
                throw new AssertionException("Unsupported keylog writer type.");
            }

            try
            {
                await action(writer).ConfigureAwait(false);
            }
            finally
            {
                await writer.DisposeAsync().ConfigureAwait(false);
            }
        }

        private static ChannelKeyMaterial CreateLargeMaterial()
        {
            return new ChannelKeyMaterial(
                0x12345678,
                0x11223344,
                new string('u', 2048),
                MessageSecurityMode.SignAndEncrypt,
                DateTime.SpecifyKind(new DateTime(2026, 1, 2, 3, 4, 5), DateTimeKind.Utc),
                60000,
                [1, 2, 3, 4],
                [5, 6, 7, 8],
                [9, 10, 11, 12],
                [13, 14, 15, 16],
                [17, 18, 19, 20],
                [21, 22, 23, 24],
                [25, 26, 27, 28],
                [29, 30, 31, 32]);
        }

        private string[] GetArtifacts(string path)
        {
            string baseName = Path.GetFileNameWithoutExtension(path);
            string extension = Path.GetExtension(path);
            return Directory.GetFiles(TempDirectory, baseName + "*" + extension);
        }

        private static string GetRotatedPath(string path, int suffix)
        {
            string? directory = Path.GetDirectoryName(path);
            string fileName = Path.GetFileNameWithoutExtension(path);
            string extension = Path.GetExtension(path);
            string suffixText = suffix.ToString("000", System.Globalization.CultureInfo.InvariantCulture);
            return Path.Combine(directory ?? string.Empty, fileName + "." + suffixText + extension);
        }
    }
}
