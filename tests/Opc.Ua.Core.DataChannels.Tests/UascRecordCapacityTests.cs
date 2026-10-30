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

using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.DataChannels;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.DataChannels.Tests
{
    /// <summary>
    /// Keeps the cipher-block boundary regression using complete UASC flow bodies rather than old frame deadlines.
    /// </summary>
    [TestFixture]
    [Category("DataChannels")]
    public sealed class UascRecordCapacityTests
    {
        [TestCase(SecurityPolicies.Basic256Sha256, 1, 0, 65463)]
        [TestCase(SecurityPolicies.Aes128_Sha256_RsaOaep, 1, 0, 65463)]
        [TestCase(SecurityPolicies.Aes256_Sha256_RsaPss, 1, 0, 65463)]
        [TestCase(SecurityPolicies.Basic256Sha256, 2, 0, 130926)]
        [TestCase(SecurityPolicies.Aes128_Sha256_RsaOaep, 2, 0, 130926)]
        [TestCase(SecurityPolicies.Aes256_Sha256_RsaPss, 2, 0, 130926)]
        [TestCase(SecurityPolicies.Basic256Sha256, 2, 100000, 100000)]
        [TestCase(SecurityPolicies.Aes128_Sha256_RsaOaep, 2, 100000, 100000)]
        [TestCase(SecurityPolicies.Aes256_Sha256_RsaPss, 2, 100000, 100000)]
        [TestCase(SecurityPolicies.Basic256Sha256, 0, 100000, 100000)]
        [TestCase(SecurityPolicies.Aes128_Sha256_RsaOaep, 0, 100000, 100000)]
        [TestCase(SecurityPolicies.Aes256_Sha256_RsaPss, 0, 100000, 100000)]
        public void AdvertisedBodyCapacityIncludesExactCipherBlockAndCarrierOverhead(
            string policy, int chunks, int messageBytes, int expected)
        {
            using var channel = new CapacityChannel(policy, chunks, messageBytes);

            Assert.Multiple(() =>
            {
                Assert.That(channel.GetMaxDataChannelRecordSize(DataChannelCarrierKind.DedicatedUasc),
                    Is.EqualTo(expected),
                    "Each 65535-byte chunk carries at most 65463 plaintext bytes, including the MSG TypeId.");
                Assert.That(channel.GetMaxDataChannelRecordSize(DataChannelCarrierKind.SharedUasc),
                    Is.EqualTo(expected), "The complete MSG is bounded by both message size and chunk count.");
                Assert.That(channel.GetMaxDataChannelRecordSize(DataChannelCarrierKind.WebTransport),
                    Is.EqualTo(65463), "Legacy management remains single-chunk.");
            });
        }

        /// <summary>
        /// Configures the real security policy arithmetic without pretending to authenticate a context.
        /// </summary>
        private sealed class CapacityChannel : UaSCUaBinaryChannel
        {
            /// <summary>
            /// Creates a deliberately non-block-aligned send capacity.
            /// </summary>
            public CapacityChannel(string policy, int chunks, int messageBytes)
                : base(
                    "record-capacity",
                    new BufferManager("record-capacity", 65536, NUnitTelemetryContext.Create()),
                    new ChannelQuotas(ServiceMessageContext.Create(NUnitTelemetryContext.Create())),
                    serverCertificates: null,
                    endpoints: null,
                    MessageSecurityMode.SignAndEncrypt,
                    policy,
                    NUnitTelemetryContext.Create())
            {
                SendBufferSize = 65535;
                MaxResponseChunkCount = chunks;
                MaxResponseMessageSize = messageBytes;
                CalculateSymmetricKeySizes();
            }

            /// <inheritdoc/>
            protected override ValueTask<bool> HandleIncomingMessageAsync(
                uint messageType,
                System.ArraySegment<byte> messageChunk,
                CancellationToken ct)
            {
                return new ValueTask<bool>(false);
            }
        }
    }
}
