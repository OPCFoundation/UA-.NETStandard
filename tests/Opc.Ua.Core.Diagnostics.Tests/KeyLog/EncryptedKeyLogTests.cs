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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Pcap.KeyLog;

namespace Opc.Ua.Pcap.Tests.KeyLog
{
    /// <summary>
    /// Verifies encrypted key-log file handling.
    /// </summary>
    [TestFixture]
    public sealed class EncryptedKeyLogTests : TempDirectoryFixture
    {
        /// <summary>
        /// Verifies encrypted JSON key logs preserve all serialized fields.
        /// </summary>
        [Test]
        public async Task EncryptedKeyLogJsonRoundtripPreservesAllFields()
        {
            byte[] sessionKey = RandomNumberGenerator.GetBytes(SessionKeyManager.KeySizeInBytes);
            ChannelKeyMaterial expected = PcapTestHelpers.CreateMaterial(
                SecurityPolicies.Basic256Sha256,
                MessageSecurityMode.SignAndEncrypt);
            string path = CreateTempPath("encrypted.uakeys.json");

            await using (var writer = new UaKeyLogJsonWriter(path, sessionKey))
            {
                await writer.AppendAsync(expected, CancellationToken.None).ConfigureAwait(false);
            }

            List<ChannelKeyMaterial> records = await PcapTestHelpers.ToListAsync(
                new UaKeyLogJsonReader(path, sessionKey).ReadAllAsync(CancellationToken.None)).ConfigureAwait(false);

            Assert.That(records, Has.Count.EqualTo(1));
            PcapTestHelpers.AssertMaterialEqual(records[0], expected, includeJsonOnlyFields: true);
        }

        /// <summary>
        /// Verifies encrypted JSON key logs do not store cleartext key material.
        /// </summary>
        [Test]
        public async Task EncryptedKeyLogJsonRawBytesDoNotContainPlaintextKeyMaterial()
        {
            byte[] sessionKey = RandomNumberGenerator.GetBytes(SessionKeyManager.KeySizeInBytes);
            byte[] knownSigningKey = Encoding.ASCII.GetBytes("known-client-signing-key");
            ChannelKeyMaterial material = CreateKnownMaterial(knownSigningKey);
            string path = CreateTempPath("encrypted-raw.uakeys.json");

            await using (var writer = new UaKeyLogJsonWriter(path, sessionKey))
            {
                await writer.AppendAsync(material, CancellationToken.None).ConfigureAwait(false);
            }

            byte[] rawBytes = await File.ReadAllBytesAsync(path, CancellationToken.None).ConfigureAwait(false);
            byte[] cleartextBase64 = Encoding.UTF8.GetBytes(Convert.ToBase64String(knownSigningKey));

            Assert.That(ContainsSequence(rawBytes, cleartextBase64), Is.False);
        }

        /// <summary>
        /// Verifies encrypted JSON key logs reject an incorrect AEAD key.
        /// </summary>
        [Test]
        public async Task EncryptedKeyLogJsonReaderRejectsWrongKey()
        {
            byte[] sessionKey = RandomNumberGenerator.GetBytes(SessionKeyManager.KeySizeInBytes);
            byte[] wrongKey = RandomNumberGenerator.GetBytes(SessionKeyManager.KeySizeInBytes);
            ChannelKeyMaterial material = PcapTestHelpers.CreateMaterial(
                SecurityPolicies.Basic256Sha256,
                MessageSecurityMode.SignAndEncrypt);
            string path = CreateTempPath("wrong-key.uakeys.json");

            await using (var writer = new UaKeyLogJsonWriter(path, sessionKey))
            {
                await writer.AppendAsync(material, CancellationToken.None).ConfigureAwait(false);
            }

            Exception? exception = Assert.ThrowsAsync<AuthenticationTagMismatchException>(async () =>
                await PcapTestHelpers.ToListAsync(
                    new UaKeyLogJsonReader(path, wrongKey).ReadAllAsync(CancellationToken.None)).ConfigureAwait(false));

            Assert.That(exception, Is.Not.Null);
        }

        /// <summary>
        /// Verifies encrypted text key logs preserve all text-format fields.
        /// </summary>
        [Test]
        public async Task EncryptedKeyLogTextRoundtripPreservesAllFields()
        {
            byte[] sessionKey = RandomNumberGenerator.GetBytes(SessionKeyManager.KeySizeInBytes);
            ChannelKeyMaterial expected = PcapTestHelpers.CreateMaterial(
                SecurityPolicies.Basic256Sha256,
                MessageSecurityMode.SignAndEncrypt);
            string path = CreateTempPath("encrypted.uakeys.txt");

            await using (var writer = new UaKeyLogTextWriter(path, sessionKey))
            {
                await writer.AppendAsync(expected, CancellationToken.None).ConfigureAwait(false);
            }

            List<ChannelKeyMaterial> records = await PcapTestHelpers.ToListAsync(
                new UaKeyLogTextReader(path, sessionKey).ReadAllAsync(CancellationToken.None)).ConfigureAwait(false);

            Assert.That(records, Has.Count.EqualTo(1));
            PcapTestHelpers.AssertMaterialEqual(records[0], expected, includeJsonOnlyFields: false);
        }

        /// <summary>
        /// Verifies session key files are created with user-only Unix permissions.
        /// </summary>
        [Test]
        [Platform("Linux,MacOSX")]
        public void SessionKeyManagerCreatesKeyFileWithUserOnlyMode()
        {
            if (OperatingSystem.IsWindows())
            {
                Assert.Ignore("Unix file modes are not available on Windows.");
                return;
            }

            string path = CreateTempPath("keys.uakeys.json");

            byte[] key = SessionKeyManager.CreateAndPersistKey(path);

            Assert.That(key, Has.Length.EqualTo(SessionKeyManager.KeySizeInBytes));
            Assert.That(File.ReadAllBytes(path + ".key"), Has.Length.EqualTo(SessionKeyManager.KeySizeInBytes));
            Assert.That(
                File.GetUnixFileMode(path + ".key"),
                Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
        }

        /// <summary>
        /// Verifies loading a session key rejects a missing key file.
        /// </summary>
        [Test]
        public void SessionKeyManagerLoadKeyRejectsMissingFile()
        {
            string path = CreateTempPath("missing.uakeys.json");

            Assert.That(() => SessionKeyManager.LoadKey(path), Throws.TypeOf<FileNotFoundException>());
        }

        /// <summary>
        /// Verifies loading a session key rejects files with the wrong length.
        /// </summary>
        [Test]
        public void SessionKeyManagerLoadKeyRejectsWrongLength()
        {
            string path = CreateTempPath("wrong-length.uakeys.json");
            File.WriteAllBytes(path + ".key", new byte[SessionKeyManager.KeySizeInBytes - 1]);

            Assert.That(() => SessionKeyManager.LoadKey(path), Throws.TypeOf<InvalidDataException>());
        }

        /// <summary>
        /// Verifies existing unencrypted writer constructors continue to produce readable files.
        /// </summary>
        [Test]
        public async Task UnencryptedWriterStillWorks()
        {
            ChannelKeyMaterial expected = PcapTestHelpers.CreateMaterial(
                SecurityPolicies.Basic256Sha256,
                MessageSecurityMode.SignAndEncrypt);
            string path = CreateTempPath("unencrypted.uakeys.json");

            await using (var writer = new UaKeyLogJsonWriter(path))
            {
                await writer.AppendAsync(expected, CancellationToken.None).ConfigureAwait(false);
            }

            byte[] rawBytes = await File.ReadAllBytesAsync(path, CancellationToken.None).ConfigureAwait(false);
            List<ChannelKeyMaterial> records = await PcapTestHelpers.ToListAsync(
                new UaKeyLogJsonReader().ReadAllAsync(path, CancellationToken.None)).ConfigureAwait(false);

            Assert.That(ContainsSequence(rawBytes, Encoding.UTF8.GetBytes("securityPolicyUri")), Is.True);
            Assert.That(records, Has.Count.EqualTo(1));
            PcapTestHelpers.AssertMaterialEqual(records[0], expected, includeJsonOnlyFields: true);
        }

        private static ChannelKeyMaterial CreateKnownMaterial(byte[] knownSigningKey)
        {
            return new ChannelKeyMaterial(
                0x12345678,
                0x11223344,
                SecurityPolicies.Basic256Sha256,
                MessageSecurityMode.SignAndEncrypt,
                DateTime.SpecifyKind(new DateTime(2026, 1, 2, 3, 4, 5), DateTimeKind.Utc),
                60000,
                [1, 2, 3, 4],
                [5, 6, 7, 8],
                knownSigningKey,
                [9, 10, 11, 12],
                [13, 14, 15, 16],
                [17, 18, 19, 20],
                [21, 22, 23, 24],
                [25, 26, 27, 28]);
        }

        private static bool ContainsSequence(byte[] source, byte[] value)
        {
            if (value.Length == 0 || source.Length < value.Length)
            {
                return false;
            }

            for (int index = 0; index <= source.Length - value.Length; index++)
            {
                if (source.AsSpan(index, value.Length).SequenceEqual(value))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
