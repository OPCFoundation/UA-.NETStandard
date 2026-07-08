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
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Security;
using Opc.Ua.PubSub.Security.Policies;
using Opc.Ua.PubSub.Tests;
using Opc.Ua.PubSub.Transports;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Udp.Tests
{
    /// <summary>
    /// End-to-end loopback test that publishes a
    /// <c>PubSub-Aes128-CTR</c>-protected UADP NetworkMessage over a
    /// real UDP multicast group and verifies the subscriber recovers
    /// the cleartext payload via the matching
    /// <see cref="UadpSecurityWrapper"/> seeded from the same
    /// <see cref="PubSubSecurityKeyRing"/>.
    /// </summary>
    /// <remarks>
    /// Exercises the wire-up of the security primitives
    /// into the UDP transport pipeline. Covers
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/8.3">
    /// Part 14 §8.3 Security</see> and
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/A.2.2.5">
    /// Annex A.2.2.5 PubSub-Aes128-CTR</see>.
    /// </remarks>
    [TestFixture]
    [Category("Integration")]
    [TestSpec("8.3")]
    [TestSpec("A.2.2.5")]
    [CancelAfter(15000)]
    public sealed class UdpSecuredLoopbackTests
    {
        private static readonly byte[] s_outerPrefix =
        [
            0xB1, 0x10, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00
        ];

        private static readonly byte[] s_innerPayload =
        [
            0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17,
            0x18, 0x19, 0x1A, 0x1B, 0x1C, 0x1D, 0x1E, 0x1F,
            0x20, 0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27,
            0x28, 0x29, 0x2A, 0x2B, 0x2C, 0x2D, 0x2E, 0x2F
        ];

        [Test]
        public async Task SecuredUadpRoundTrip_DecodesCleartextOnSubscriberAsync()
        {
            int port;
            try
            {
                port = UdpIntegrationTestHelpers.ReserveEphemeralPort(IPAddress.Loopback);
            }
            catch (SocketException ex)
            {
                Assert.Ignore($"Loopback UDP socket bind failed: {ex.Message}");
                return;
            }

            int groupLow = (port % 250) + 1;
            string url = $"opc.udp://239.255.43.{groupLow}:{port}";
            UdpEndpoint endpoint = UdpEndpointParser.Parse(url);
            UdpTransportOptions options = UdpIntegrationTestHelpers.LoopbackOptions();
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            (UadpSecurityWrapper publisherWrapper, UadpSecurityWrapper subscriberWrapper)
                = CreateMatchingWrapperPair(tokenId: 5U);

            await using var subscriber = new UdpDatagramTransport(
                UdpIntegrationTestHelpers.NewConnection(url, "Sub"),
                endpoint,
                PubSubTransportDirection.Receive,
                networkInterface: null,
                telemetry,
                TimeProvider.System,
                options);
            await using var publisher = new UdpDatagramTransport(
                UdpIntegrationTestHelpers.NewConnection(url, "Pub"),
                endpoint,
                PubSubTransportDirection.Send,
                networkInterface: null,
                telemetry,
                TimeProvider.System,
                options);

            try
            {
                await subscriber.OpenAsync().ConfigureAwait(false);
                await publisher.OpenAsync().ConfigureAwait(false);
            }
            catch (SocketException ex)
            {
                Assert.Ignore($"Multicast loopback open failed: {ex.Message}");
                return;
            }

            ReadOnlyMemory<byte> wrapped = await publisherWrapper.WrapAsync(
                s_outerPrefix,
                s_innerPayload).ConfigureAwait(false);
            byte[] datagram = wrapped.ToArray();

            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    await publisher.SendAsync(datagram).ConfigureAwait(false);
                }
                catch (SocketException ex)
                {
                    Assert.Ignore(
                        $"Multicast send failed: {ex.Message}; environment likely blocks multicast routing.");
                    return;
                }
                PubSubTransportFrame? frame = await UdpIntegrationTestHelpers.ReceiveOneAsync(
                    subscriber,
                    TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);
                if (frame is null)
                {
                    continue;
                }

                ReadOnlyMemory<byte> received = frame.Value.Payload;
                Assert.That(received.Length, Is.EqualTo(datagram.Length));

                int prefixLength = s_outerPrefix.Length;
                ReadOnlyMemory<byte> prefix = received.Slice(0, prefixLength);
                ReadOnlyMemory<byte> securityAndPayload = received.Slice(prefixLength);

                UadpSecurityWrapper.UnwrapResult result = await subscriberWrapper
                    .TryUnwrapAsync(prefix, securityAndPayload)
                    .ConfigureAwait(false);

                Assert.Multiple(() =>
                {
                    Assert.That(result.IsSuccess, Is.True,
                        $"Unwrap failed: {result.Reason}");
                    Assert.That(result.InnerPayload, Is.Not.Null);
                    Assert.That(result.InnerPayload!.Value.ToArray(),
                        Is.EqualTo(s_innerPayload));
                });
                return;
            }

            Assert.Ignore("No secured multicast frame received; environment likely blocks multicast loopback.");
        }

        private static (UadpSecurityWrapper Publisher, UadpSecurityWrapper Subscriber)
            CreateMatchingWrapperPair(uint tokenId)
        {
            PubSubAes128CtrPolicy policy = PubSubAes128CtrPolicy.Instance;
            PubSubSecurityKey key = BuildKey(
                tokenId,
                policy.SigningKeyLength,
                policy.EncryptingKeyLength,
                policy.NonceLength);

            var publisherRing = new PubSubSecurityKeyRing("integration-group");
            publisherRing.SetCurrent(key);
            var subscriberRing = new PubSubSecurityKeyRing("integration-group");
            subscriberRing.SetCurrent(key);

            var publisherWindow = new SecurityTokenWindow();
            var subscriberWindow = new SecurityTokenWindow();
            subscriberWindow.RegisterToken(tokenId);

            var publisherNonce = new RandomNonceProvider(PublisherId.FromUInt32(0xCAFEBABEU));
            var subscriberNonce = new RandomNonceProvider(PublisherId.FromUInt32(0xCAFEBABEU));

            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var publisher = new UadpSecurityWrapper(
                policy,
                new StaticSecurityKeyProvider("integration-group", publisherRing),
                publisherNonce,
                publisherWindow,
                telemetry);
            var subscriber = new UadpSecurityWrapper(
                policy,
                new StaticSecurityKeyProvider("integration-group", subscriberRing),
                subscriberNonce,
                subscriberWindow,
                telemetry);

            return (publisher, subscriber);
        }

        private static PubSubSecurityKey BuildKey(
            uint tokenId,
            int signingKeyLength,
            int encryptingKeyLength,
            int keyNonceLength)
        {
            int signingLen = signingKeyLength == 0 ? 1 : signingKeyLength;
            int encryptingLen = encryptingKeyLength == 0 ? 1 : encryptingKeyLength;
            int nonceLen = keyNonceLength == 0 ? 1 : keyNonceLength;

            byte[] signing = new byte[signingLen];
            byte[] encrypting = new byte[encryptingLen];
            byte[] keyNonce = new byte[nonceLen];
            for (int i = 0; i < signing.Length; i++)
            {
                signing[i] = (byte)((tokenId * 31u + (uint)i) & 0xFF);
            }
            for (int i = 0; i < encrypting.Length; i++)
            {
                encrypting[i] = (byte)((tokenId * 17u + (uint)i + 1u) & 0xFF);
            }
            for (int i = 0; i < keyNonce.Length; i++)
            {
                keyNonce[i] = (byte)((tokenId * 7u + (uint)i + 2u) & 0xFF);
            }

            return new PubSubSecurityKey(
                tokenId,
                ByteString.Create(signing),
                ByteString.Create(encrypting),
                ByteString.Create(keyNonce),
                DateTimeUtc.From(DateTime.UtcNow),
                TimeSpan.FromMinutes(5));
        }
    }
}
