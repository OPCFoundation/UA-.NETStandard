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
using System.Buffers.Binary;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Bindings.Pcap.Capture;
using Opc.Ua.Bindings.Pcap.Dissection;
using Opc.Ua.Bindings.Pcap.KeyLog;

namespace Opc.Ua.Bindings.Pcap.Tests.Dissection
{
    /// <summary>
    /// Negative / failure-mode tests for <see cref="OfflineSecureChannel"/>.
    /// The happy-path round-trip is already covered by
    /// <see cref="OfflineSecureChannelSmokeTests"/>; these tests pin down
    /// the documented exceptions raised for invalid inputs.
    /// </summary>
    [TestFixture]
    public sealed class OfflineSecureChannelNegativeTests
    {
        [Test]
        public void ConstructorRejectsNullFirstToken()
        {
            Assert.That(
                () => new OfflineSecureChannel(firstToken: null!),
                Throws.TypeOf<ArgumentNullException>()
                    .With.Property("ParamName").EqualTo("firstToken"));
        }

        [Test]
        public void ConstructorRejectsNullLoggerFactory()
        {
            ChannelKeyMaterial material = PcapTestHelpers.CreateMaterial(
                SecurityPolicies.None,
                MessageSecurityMode.None);

            Assert.That(
                () => new OfflineSecureChannel(material, loggerFactory: null!),
                Throws.TypeOf<ArgumentNullException>()
                    .With.Property("ParamName").EqualTo("loggerFactory"));
        }

        [Test]
        public void LoadKeyMaterialRejectsNull()
        {
            ChannelKeyMaterial material = PcapTestHelpers.CreateMaterial(
                SecurityPolicies.None,
                MessageSecurityMode.None);

            using var channel = new OfflineSecureChannel(material);

            Assert.That(
                () => channel.LoadKeyMaterial(material: null!),
                Throws.TypeOf<ArgumentNullException>()
                    .With.Property("ParamName").EqualTo("material"));
        }

        [Test]
        public void LoadKeyMaterialRejectsMismatchedChannelId()
        {
            ChannelKeyMaterial original = PcapTestHelpers.CreateMaterial(
                SecurityPolicies.None,
                MessageSecurityMode.None,
                channelId: 0x11111111,
                tokenId: 1);
            ChannelKeyMaterial different = PcapTestHelpers.CreateMaterial(
                SecurityPolicies.None,
                MessageSecurityMode.None,
                channelId: 0x22222222,
                tokenId: 2);

            using var channel = new OfflineSecureChannel(original);

            Assert.That(
                () => channel.LoadKeyMaterial(different),
                Throws.TypeOf<PcapDiagnosticsException>()
                    .With.Message.Contains("does not match this"));
        }

        [Test]
        public void ChannelIdReflectsFirstToken()
        {
            ChannelKeyMaterial material = PcapTestHelpers.CreateMaterial(
                SecurityPolicies.None,
                MessageSecurityMode.None,
                channelId: 0xABCDEF01);

            using var channel = new OfflineSecureChannel(material);

            Assert.That(channel.ChannelId, Is.EqualTo(0xABCDEF01U));
        }

        [Test]
        public void LoadKeyMaterialIsIdempotentForSameToken()
        {
            ChannelKeyMaterial material = PcapTestHelpers.CreateMaterial(
                SecurityPolicies.None,
                MessageSecurityMode.None,
                channelId: 0x10101010,
                tokenId: 0x42);

            using var channel = new OfflineSecureChannel(material);

            // Loading the same token twice must not throw.
            Assert.That(() => channel.LoadKeyMaterial(material), Throws.Nothing);
        }

        [Test]
        public void ReadChunkThrowsOnTooShortBuffer()
        {
            ChannelKeyMaterial material = PcapTestHelpers.CreateMaterial(
                SecurityPolicies.None,
                MessageSecurityMode.None);

            using var channel = new OfflineSecureChannel(material);

            // Symmetric header (16) + sequence header (8) is the minimum; supply 8.
            byte[] tooShort = new byte[8];
            BinaryPrimitives.WriteUInt32LittleEndian(tooShort, TcpMessageType.MessageFinal);

            Assert.That(
                () => channel.ReadChunk(tooShort, fromClient: true),
                Throws.TypeOf<PcapDiagnosticsException>()
                    .With.Message.Contains("too short"));
        }

        [Test]
        public void ReadChunkThrowsOnMismatchedChannelId()
        {
            ChannelKeyMaterial material = PcapTestHelpers.CreateMaterial(
                SecurityPolicies.None,
                MessageSecurityMode.None,
                channelId: 0x000000AA,
                tokenId: 1);

            using var channel = new OfflineSecureChannel(material);

            byte[] chunk = BuildMsgChunk(
                channelId: 0x000000BB,
                tokenId: 1,
                paddingSize: 32);

            Assert.That(
                () => channel.ReadChunk(chunk, fromClient: true),
                Throws.TypeOf<PcapDiagnosticsException>()
                    .With.Message.Contains("does not match"));
        }

        [Test]
        public void ReadChunkThrowsForUnknownTokenId()
        {
            ChannelKeyMaterial material = PcapTestHelpers.CreateMaterial(
                SecurityPolicies.None,
                MessageSecurityMode.None,
                channelId: 0x00C0FFEE,
                tokenId: 0x42);

            using var channel = new OfflineSecureChannel(material);

            byte[] chunk = BuildMsgChunk(
                channelId: 0x00C0FFEE,
                tokenId: 0x99,
                paddingSize: 32);

            Assert.That(
                () => channel.ReadChunk(chunk, fromClient: true),
                Throws.TypeOf<PcapDiagnosticsException>()
                    .With.Message.Contains("No key material loaded for token"));
        }

        [Test]
        public void ReadChunkAcceptsExplicitLoggerFactory()
        {
            // Exercise the two-arg constructor and follow up with a documented
            // failure-mode read to prove the logger pathway is wired without
            // crashing during failure paths.
            ChannelKeyMaterial material = PcapTestHelpers.CreateMaterial(
                SecurityPolicies.None,
                MessageSecurityMode.None,
                channelId: 0x00B16B00,
                tokenId: 1);

            using var channel = new OfflineSecureChannel(material, NullLoggerFactory.Instance);

            byte[] chunk = BuildMsgChunk(
                channelId: 0x00B16B00,
                tokenId: 0x123,
                paddingSize: 32);

            Assert.That(
                () => channel.ReadChunk(chunk, fromClient: false),
                Throws.TypeOf<PcapDiagnosticsException>());
        }

        private static byte[] BuildMsgChunk(uint channelId, uint tokenId, int paddingSize)
        {
            // 8-byte message header + 8-byte security header + N bytes of (encrypted)
            // sequence/body. We don't care about the cipher payload — the test cases
            // throw before any crypto runs.
            int size = 16 + paddingSize;
            byte[] chunk = new byte[size];
            BinaryPrimitives.WriteUInt32LittleEndian(chunk, TcpMessageType.MessageFinal);
            BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(4), (uint)size);
            BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(8), channelId);
            BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(12), tokenId);
            return chunk;
        }
    }
}
