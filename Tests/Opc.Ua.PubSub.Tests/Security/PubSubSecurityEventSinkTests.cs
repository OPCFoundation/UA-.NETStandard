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
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Security;
using Opc.Ua.PubSub.Security.Policies;
using Opc.Ua.PubSub.Security.Sks;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Tests.Security
{
    /// <summary>
    /// Tests for structured PubSub security event notifications.
    /// </summary>
    [TestFixture]
    public sealed class PubSubSecurityEventSinkTests
    {
        private const uint TokenId = 1U;
        private const string CallerId = "client/cn=test";

        private static readonly byte[] s_outerPrefix = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0x00, 0x01 };
        private static readonly byte[] s_innerPayload = new byte[]
        {
            0xDE, 0xAD, 0xBE, 0xEF, 0xCA, 0xFE, 0xBA, 0xBE,
            0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08
        };

        [Test]
        public async Task UadpSinkReceivesSignatureFailureWithoutKeyBytes()
        {
            var events = new List<PubSubSecurityEvent>();
            Mock<IPubSubSecurityEventSink> sink = CreateSink(events);
            (UadpSecurityWrapper sender, UadpSecurityWrapper receiver) =
                CreateUadpPair(sink.Object);
            ReadOnlyMemory<byte> wrapped = await sender
                .WrapAsync(s_outerPrefix, s_innerPayload)
                .ConfigureAwait(false);
            byte[] tampered = wrapped.ToArray();
            tampered[^1] ^= 0x01;

            UadpSecurityWrapper.UnwrapResult result = await receiver
                .TryUnwrapAsync(
                    s_outerPrefix.AsMemory(),
                    new ReadOnlyMemory<byte>(tampered, s_outerPrefix.Length, tampered.Length - s_outerPrefix.Length))
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.IsSuccess, Is.False);
                Assert.That(events, Has.Count.EqualTo(1));
                Assert.That(events[0].Kind, Is.EqualTo(PubSubSecurityEventKind.SignatureVerificationFailed));
                Assert.That(events[0].Outcome, Is.EqualTo(PubSubSecurityEventOutcome.Failed));
                Assert.That(events[0].TokenId, Is.EqualTo(TokenId));
                Assert.That(EventTypeExposesKeyBytes(), Is.False);
            });
            sink.Verify(s => s.OnSecurityEvent(It.IsAny<PubSubSecurityEvent>()), Times.Once);
        }

        [Test]
        public async Task UadpSinkReceivesReplayRejectionWithoutKeyBytes()
        {
            var events = new List<PubSubSecurityEvent>();
            Mock<IPubSubSecurityEventSink> sink = CreateSink(events);
            (UadpSecurityWrapper sender, UadpSecurityWrapper receiver) =
                CreateUadpPair(sink.Object);
            ReadOnlyMemory<byte> wrapped = await sender
                .WrapAsync(s_outerPrefix, s_innerPayload)
                .ConfigureAwait(false);
            UadpSecurityWrapper.UnwrapResult first = await receiver
                .TryUnwrapAsync(s_outerPrefix.AsMemory(), wrapped.Slice(s_outerPrefix.Length))
                .ConfigureAwait(false);
            UadpSecurityWrapper.UnwrapResult replay = await receiver
                .TryUnwrapAsync(s_outerPrefix.AsMemory(), wrapped.Slice(s_outerPrefix.Length))
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(first.IsSuccess, Is.True, first.Reason);
                Assert.That(replay.IsSuccess, Is.False);
                Assert.That(events, Has.Count.EqualTo(1));
                Assert.That(events[0].Kind, Is.EqualTo(PubSubSecurityEventKind.ReplayRejected));
                Assert.That(events[0].Outcome, Is.EqualTo(PubSubSecurityEventOutcome.Rejected));
                Assert.That(events[0].TokenId, Is.EqualTo(TokenId));
                Assert.That(EventTypeExposesKeyBytes(), Is.False);
            });
            sink.Verify(s => s.OnSecurityEvent(It.IsAny<PubSubSecurityEvent>()), Times.Once);
        }

        [Test]
        public async Task SksSinkReceivesIssuanceAndDenialEvents()
        {
            var events = new List<PubSubSecurityEvent>();
            Mock<IPubSubSecurityEventSink> sink = CreateSink(events);
            var server = new InMemoryPubSubKeyServiceServer(
                new FakeTimeProvider(),
                NUnitTelemetryContext.Create(),
                sink.Object);
            await server.AddSecurityGroupAsync(BuildGroup()).ConfigureAwait(false);

            SksKeyResponse response = await server
                .GetSecurityKeysAsync(CallerId, new SksKeyRequest("group-1", 0U, 1U))
                .ConfigureAwait(false);
            OpcUaSksException ex = Assert.ThrowsAsync<OpcUaSksException>(
                async () => await server
                    .GetSecurityKeysAsync("client/cn=denied", new SksKeyRequest("group-1", 0U, 1U))
                    .ConfigureAwait(false))!;

            Assert.Multiple(() =>
            {
                Assert.That(((byte[][]?)response.Keys) ?? [], Has.Length.EqualTo(1));
                Assert.That((uint)ex.Status.Code, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                Assert.That(events, Has.Count.EqualTo(2));
                Assert.That(events[0].Kind, Is.EqualTo(PubSubSecurityEventKind.SksKeysIssued));
                Assert.That(events[0].Outcome, Is.EqualTo(PubSubSecurityEventOutcome.Success));
                Assert.That(events[0].SecurityGroupId, Is.EqualTo("group-1"));
                Assert.That(events[1].Kind, Is.EqualTo(PubSubSecurityEventKind.SksKeyRequestDenied));
                Assert.That(events[1].Outcome, Is.EqualTo(PubSubSecurityEventOutcome.Rejected));
                Assert.That(events[1].SecurityGroupId, Is.EqualTo("group-1"));
                Assert.That(EventTypeExposesKeyBytes(), Is.False);
            });
            sink.Verify(s => s.OnSecurityEvent(It.IsAny<PubSubSecurityEvent>()), Times.Exactly(2));
        }

        private static Mock<IPubSubSecurityEventSink> CreateSink(
            List<PubSubSecurityEvent> events)
        {
            var sink = new Mock<IPubSubSecurityEventSink>(MockBehavior.Strict);
            sink
                .Setup(s => s.OnSecurityEvent(It.IsAny<PubSubSecurityEvent>()))
                .Callback<PubSubSecurityEvent>(events.Add);
            return sink;
        }

        private static (UadpSecurityWrapper Sender, UadpSecurityWrapper Receiver) CreateUadpPair(
            IPubSubSecurityEventSink receiverSink)
        {
            PubSubAes128CtrPolicy policy = PubSubAes128CtrPolicy.Instance;
            PubSubSecurityKey key = TestSecurityKeyFactory.Create(
                TokenId,
                signingKeyLength: policy.SigningKeyLength,
                encryptingKeyLength: policy.EncryptingKeyLength,
                keyNonceLength: policy.NonceLength);
            var senderRing = new PubSubSecurityKeyRing("group-1");
            senderRing.SetCurrent(key);
            var receiverRing = new PubSubSecurityKeyRing("group-1");
            receiverRing.SetCurrent(key);
            var receiverWindow = new SecurityTokenWindow();
            receiverWindow.RegisterToken(TokenId);

            var sender = new UadpSecurityWrapper(
                policy,
                new StaticSecurityKeyProvider("group-1", senderRing),
                new RandomNonceProvider(PublisherId.FromUInt32(0x12345678U)),
                new SecurityTokenWindow(),
                NUnitTelemetryContext.Create());
            var receiver = new UadpSecurityWrapper(
                policy,
                new StaticSecurityKeyProvider("group-1", receiverRing),
                new RandomNonceProvider(PublisherId.FromUInt32(0x12345678U)),
                receiverWindow,
                NUnitTelemetryContext.Create(),
                receiverSink);

            return (sender, receiver);
        }

        private static SksSecurityGroup BuildGroup()
        {
            return new SksSecurityGroup(
                "group-1",
                PubSubSecurityPolicyUri.PubSubAes128Ctr,
                TimeSpan.FromMinutes(5),
                maxFutureKeyCount: 4,
                maxPastKeyCount: 2,
                keys: Array.Empty<PubSubSecurityKey>(),
                authorizedCallerIdentities: [CallerId]);
        }

        private static bool EventTypeExposesKeyBytes()
        {
            foreach (PropertyInfo property in typeof(PubSubSecurityEvent)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.PropertyType == typeof(byte[]) ||
                    property.PropertyType == typeof(ReadOnlyMemory<byte>) ||
                    property.PropertyType == typeof(Memory<byte>))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
