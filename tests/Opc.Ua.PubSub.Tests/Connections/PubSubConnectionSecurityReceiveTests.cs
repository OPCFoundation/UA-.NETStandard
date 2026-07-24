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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.PubSub.Connections;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Encoding.Uadp;
using Opc.Ua.PubSub.Groups;
using Opc.Ua.PubSub.MetaData;
using Opc.Ua.PubSub.Security;
using Opc.Ua.PubSub.Security.Policies;
using Opc.Ua.PubSub.Transports;
using Opc.Ua.Tests;
using UadpDataSetMessage = Opc.Ua.PubSub.Encoding.Uadp.UadpDataSetMessage;
using UadpNetworkMessage = Opc.Ua.PubSub.Encoding.Uadp.UadpNetworkMessage;

namespace Opc.Ua.PubSub.Tests.Connections
{
    /// <summary>
    /// Verifies the fail-closed receive enforcement and fail-soft
    /// chunk handling wired into <see cref="PubSubConnection"/> per
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/8.3">
    /// Part 14 §8.3</see>. A reader configured for
    /// <see cref="MessageSecurityMode.SignAndEncrypt"/> must reject a
    /// forged plaintext frame and accept a correctly secured frame, and
    /// a malformed chunk frame must never terminate the receive loop.
    /// </summary>
    [TestFixture]
    [TestSpec("8.3")]
    [CancelAfter(15000)]
    public sealed class PubSubConnectionSecurityReceiveTests
    {
        private const string UdpProfile =
            "http://opcfoundation.org/UA-Profile/Transport/pubsub-udp-uadp";

        [Test]
        public async Task SecuredReaderRejectsForgedPlaintextFrameAsync()
        {
            (UadpSecurityWrapper _, UadpSecurityWrapper subscriber) =
                CreateMatchingWrapperPair(tokenId: 1U);

            byte[] forged = await BuildPlaintextFrameAsync().ConfigureAwait(false);
            var transport = new ProgrammableTransport([forged]);
            var decoder = new RecordingDecoder();

            await using PubSubConnection conn = NewConnection(
                transport, decoder, subscriber,
                MessageSecurityMode.SignAndEncrypt);

            await conn.EnableAsync().ConfigureAwait(false);
            await transport.WaitUntilDrainedAsync().ConfigureAwait(false);
            await conn.DisableAsync().ConfigureAwait(false);

            Assert.That(decoder.CallCount, Is.Zero,
                "Forged plaintext frame must be dropped before decode.");
        }

        [Test]
        public async Task SecuredReaderRejectsNonUadpJsonFrameAsync()
        {
            // A reader configured for message security must drop a non-UADP
            // (JSON) frame: there is no JSON message-security wrapper, so the
            // frame cannot be authenticated and must never reach the decoder.
            // Before the fix the inbound security gate lived inside the UADP
            // branch, so a JSON frame bypassed it entirely and was delivered
            // to application sinks unauthenticated.
            (UadpSecurityWrapper _, UadpSecurityWrapper subscriber) =
                CreateMatchingWrapperPair(tokenId: 1U);

            byte[] json = System.Text.Encoding.UTF8.GetBytes(
                "{\"MessageId\":\"forged\",\"MessageType\":\"ua-data\"}");
            var transport = new ProgrammableTransport([json]);
            var decoder = new RecordingDecoder();

            await using PubSubConnection conn = NewConnection(
                transport, decoder, subscriber,
                MessageSecurityMode.SignAndEncrypt);

            await conn.EnableAsync().ConfigureAwait(false);
            await transport.WaitUntilDrainedAsync().ConfigureAwait(false);
            await conn.DisableAsync().ConfigureAwait(false);

            Assert.That(decoder.CallCount, Is.Zero,
                "A non-UADP (JSON) frame must be dropped by the inbound security " +
                "gate on a secured reader before decode.");
        }

        [Test]
        public async Task SecuredReaderAcceptsSecuredFrameAsync()
        {
            (UadpSecurityWrapper publisher, UadpSecurityWrapper subscriber) =
                CreateMatchingWrapperPair(tokenId: 1U);

            byte[] secured = await BuildSecuredFrameAsync(publisher).ConfigureAwait(false);
            var transport = new ProgrammableTransport([secured]);
            var decoder = new RecordingDecoder();

            await using PubSubConnection conn = NewConnection(
                transport, decoder, subscriber,
                MessageSecurityMode.SignAndEncrypt);

            await conn.EnableAsync().ConfigureAwait(false);
            await transport.WaitUntilDrainedAsync().ConfigureAwait(false);
            await conn.DisableAsync().ConfigureAwait(false);

            Assert.That(decoder.CallCount, Is.GreaterThanOrEqualTo(1),
                "A correctly secured frame must be unwrapped and decoded.");
        }

        [Test]
        public async Task ReceiveLoopSurvivesMalformedChunkFrameAsync()
        {
            // A malformed chunk frame followed by a valid plaintext
            // frame on an unsecured reader: the loop must drop the bad
            // chunk and continue, decoding the subsequent frame.
            byte[] malformedChunk = UadpEncoder.WriteChunkEnvelope(
                new byte[] { 0x01, 0x02, 0x03 },
                PublisherId.FromByte(1),
                writerGroupId: 1).ToArray();
            byte[] plaintext = await BuildPlaintextFrameAsync().ConfigureAwait(false);

            var transport = new ProgrammableTransport([malformedChunk, plaintext]);
            var decoder = new RecordingDecoder();

            await using PubSubConnection conn = NewConnection(
                transport, decoder, securityWrapper: null,
                MessageSecurityMode.None);

            await conn.EnableAsync().ConfigureAwait(false);
            await transport.WaitUntilDrainedAsync().ConfigureAwait(false);
            await conn.DisableAsync().ConfigureAwait(false);

            Assert.That(decoder.CallCount, Is.EqualTo(1),
                "Receive loop must continue past a malformed chunk frame.");
        }

        [Test]
        public async Task SecuredReaderRejectsForgedChunkedPlaintextFrameAsync()
        {
            // SA-REGR-01: a forged plaintext NetworkMessage delivered as UADP
            // chunks must be rejected by the inbound security gate after
            // reassembly, exactly like a non-chunked forged frame. Before the
            // fix the chunk branch bypassed the gate and the forged payload
            // reached the decoder.
            (UadpSecurityWrapper _, UadpSecurityWrapper subscriber) =
                CreateMatchingWrapperPair(tokenId: 1U);

            byte[] forged = await BuildPlaintextFrameAsync().ConfigureAwait(false);
            byte[][] chunks = ChunkFrames(forged);
            var transport = new ProgrammableTransport(chunks);
            var decoder = new RecordingDecoder();

            await using PubSubConnection conn = NewConnection(
                transport, decoder, subscriber,
                MessageSecurityMode.SignAndEncrypt);

            await conn.EnableAsync().ConfigureAwait(false);
            await transport.WaitUntilDrainedAsync().ConfigureAwait(false);
            await conn.DisableAsync().ConfigureAwait(false);

            Assert.That(decoder.CallCount, Is.Zero,
                "Forged plaintext delivered as UADP chunks must be dropped by the " +
                "security gate after reassembly (SA-REGR-01).");
        }

        [Test]
        public async Task SecuredReaderAcceptsSecuredChunkedFrameAsync()
        {
            // SA-REGR-01 (legit path): a correctly secured NetworkMessage that is
            // split into chunks must reassemble, unwrap and decode. Before the fix
            // the reassembled ciphertext was fed straight to the plaintext decoder.
            (UadpSecurityWrapper publisher, UadpSecurityWrapper subscriber) =
                CreateMatchingWrapperPair(tokenId: 1U);

            byte[] secured = await BuildSecuredFrameAsync(publisher).ConfigureAwait(false);
            byte[][] chunks = ChunkFrames(secured);
            var transport = new ProgrammableTransport(chunks);
            var decoder = new RecordingDecoder();

            await using PubSubConnection conn = NewConnection(
                transport, decoder, subscriber,
                MessageSecurityMode.SignAndEncrypt);

            await conn.EnableAsync().ConfigureAwait(false);
            await transport.WaitUntilDrainedAsync().ConfigureAwait(false);
            await conn.DisableAsync().ConfigureAwait(false);

            Assert.That(decoder.CallCount, Is.GreaterThanOrEqualTo(1),
                "A correctly secured message split into chunks must reassemble, " +
                "unwrap and decode (SA-REGR-01 legit secured+chunked path).");
        }

        [Test]
        public async Task OpportunisticReaderUnwrapsSecuredFrameWhenSecurityNotRequiredAsync()
        {
            // A reader configured with SecurityMode.None but supplied with a
            // security wrapper unwraps a secured inbound frame opportunistically
            // (requiredMode None) instead of dropping it.
            (UadpSecurityWrapper publisher, UadpSecurityWrapper subscriber) =
                CreateMatchingWrapperPair(tokenId: 1U);

            byte[] secured = await BuildSecuredFrameAsync(publisher).ConfigureAwait(false);
            var transport = new ProgrammableTransport([secured]);
            var decoder = new RecordingDecoder();

            await using PubSubConnection conn = NewConnection(
                transport, decoder, subscriber,
                MessageSecurityMode.None);

            await conn.EnableAsync().ConfigureAwait(false);
            await transport.WaitUntilDrainedAsync().ConfigureAwait(false);
            await conn.DisableAsync().ConfigureAwait(false);

            Assert.That(decoder.CallCount, Is.GreaterThanOrEqualTo(1),
                "A reader that does not require security must still unwrap and decode " +
                "a secured frame when a security wrapper is available.");
        }

        private static byte[][] ChunkFrames(byte[] message)
        {
            int maxFrameSize = UadpChunker.ChunkHeaderSize +
                Math.Max(8, (message.Length + 1) / 2);
            IReadOnlyList<byte[]> chunks = new UadpChunker().Split(
                message, messageSequenceNumber: 1, maxFrameSize);
            byte[][] frames = new byte[chunks.Count][];
            for (int i = 0; i < chunks.Count; i++)
            {
                frames[i] = UadpEncoder.WriteChunkEnvelope(
                    chunks[i], PublisherId.FromByte(1), writerGroupId: 1).ToArray();
            }
            return frames;
        }

        private static PubSubConnection NewConnection(
            ProgrammableTransport transport,
            INetworkMessageDecoder decoder,
            UadpSecurityWrapper? securityWrapper,
            MessageSecurityMode requiredSecurityMode)
        {
            var cfg = new PubSubConnectionDataType
            {
                Name = "receive-conn",
                TransportProfileUri = UdpProfile
            };
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var readerGroup = new ReaderGroup(
                new ReaderGroupDataType { Name = "rg" },
                Array.Empty<DataSetReader>(),
                telemetry);

            return new PubSubConnection(
                cfg,
                new ProgrammableTransportFactory(transport),
                new Dictionary<string, INetworkMessageEncoder>(),
                new Dictionary<string, INetworkMessageDecoder>
                {
                    [UdpProfile] = decoder
                },
                Array.Empty<WriterGroup>(),
                new[] { readerGroup },
                new DataSetMetaDataRegistry(),
                new PubSubDiagnostics(PubSubDiagnosticsLevel.Low),
                telemetry,
                TimeProvider.System,
                securityWrapper,
                UadpSecurityWrapOptions.SignAndEncrypt,
                maxNetworkMessageSize: 0,
                requiredSecurityMode);
        }

        private static async Task<byte[]> BuildPlaintextFrameAsync()
        {
            var msg = new UadpNetworkMessage
            {
                ContentMask = UadpNetworkMessageContentMask.PublisherId |
                    UadpNetworkMessageContentMask.PayloadHeader,
                PublisherId = PublisherId.FromByte(1),
                DataSetMessages =
                [
                    new UadpDataSetMessage
                    {
                        DataSetWriterId = 1,
                        FieldEncoding = PubSubFieldEncoding.Variant,
                        Fields = [new DataSetField { Value = (Variant)42 }]
                    }
                ]
            };
            PubSubNetworkMessageContext context = NewContext();
            ReadOnlyMemory<byte> encoded = await new UadpEncoder()
                .EncodeAsync(msg, context).ConfigureAwait(false);
            return encoded.ToArray();
        }

        private static async Task<byte[]> BuildSecuredFrameAsync(UadpSecurityWrapper publisher)
        {
            var msg = new UadpNetworkMessage
            {
                ContentMask = UadpNetworkMessageContentMask.PublisherId |
                    UadpNetworkMessageContentMask.PayloadHeader,
                PublisherId = PublisherId.FromByte(1),
                DataSetMessages =
                [
                    new UadpDataSetMessage
                    {
                        DataSetWriterId = 1,
                        FieldEncoding = PubSubFieldEncoding.Variant,
                        Fields = [new DataSetField { Value = (Variant)42 }]
                    }
                ]
            };
            PubSubNetworkMessageContext context = NewContext();
            ReadOnlyMemory<byte> encoded = UadpEncoder.EncodeWithSecurityBoundary(
                msg, context, out int payloadOffset);
            ReadOnlyMemory<byte> prefix = encoded[..payloadOffset];
            ReadOnlyMemory<byte> inner = encoded[payloadOffset..];
            ReadOnlyMemory<byte> wrapped = await publisher
                .WrapAsync(prefix, inner, UadpSecurityWrapOptions.SignAndEncrypt)
                .ConfigureAwait(false);
            return wrapped.ToArray();
        }

        private static PubSubNetworkMessageContext NewContext()
        {
            return new PubSubNetworkMessageContext(
                ServiceMessageContext.CreateEmpty(null!),
                new DataSetMetaDataRegistry(),
                new PubSubDiagnostics(PubSubDiagnosticsLevel.Low),
                TimeProvider.System);
        }

        private static (UadpSecurityWrapper Publisher, UadpSecurityWrapper Subscriber)
            CreateMatchingWrapperPair(uint tokenId)
        {
            PubSubAes256CtrPolicy policy = PubSubAes256CtrPolicy.Instance;
            PubSubSecurityKey key = BuildKey(
                tokenId,
                policy.SigningKeyLength,
                policy.EncryptingKeyLength,
                policy.NonceLength);

            var publisherRing = new PubSubSecurityKeyRing("receive-group");
            publisherRing.SetCurrent(key);
            var subscriberRing = new PubSubSecurityKeyRing("receive-group");
            subscriberRing.SetCurrent(key);

            var publisherWindow = new SecurityTokenWindow();
            var subscriberWindow = new SecurityTokenWindow();
            subscriberWindow.RegisterToken(tokenId);

            var publisherNonce = new RandomNonceProvider(PublisherId.FromUInt32(0xCAFEBABEU));
            var subscriberNonce = new RandomNonceProvider(PublisherId.FromUInt32(0xCAFEBABEU));

            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var publisher = new UadpSecurityWrapper(
                policy,
                new StaticSecurityKeyProvider("receive-group", publisherRing),
                publisherNonce,
                publisherWindow,
                telemetry);
            var subscriber = new UadpSecurityWrapper(
                policy,
                new StaticSecurityKeyProvider("receive-group", subscriberRing),
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
            byte[] signing = new byte[signingKeyLength];
            byte[] encrypting = new byte[encryptingKeyLength];
            byte[] keyNonce = new byte[keyNonceLength];
            for (int i = 0; i < signing.Length; i++)
            {
                signing[i] = (byte)(((tokenId * 31u) + (uint)i) & 0xFF);
            }
            for (int i = 0; i < encrypting.Length; i++)
            {
                encrypting[i] = (byte)(((tokenId * 17u) + (uint)i + 1u) & 0xFF);
            }
            for (int i = 0; i < keyNonce.Length; i++)
            {
                keyNonce[i] = (byte)(((tokenId * 7u) + (uint)i + 2u) & 0xFF);
            }

            return new PubSubSecurityKey(
                tokenId,
                ByteString.Create(signing),
                ByteString.Create(encrypting),
                ByteString.Create(keyNonce),
                DateTimeUtc.From(DateTime.UtcNow),
                TimeSpan.FromMinutes(60));
        }

        private sealed class RecordingDecoder : INetworkMessageDecoder
        {
            private int m_callCount;

            public string TransportProfileUri => UdpProfile;

            public int CallCount => Volatile.Read(ref m_callCount);

            public ValueTask<PubSubNetworkMessage?> TryDecodeAsync(
                ReadOnlyMemory<byte> frame,
                PubSubNetworkMessageContext context,
                CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref m_callCount);
                return new ValueTask<PubSubNetworkMessage?>((PubSubNetworkMessage?)null);
            }
        }

        private sealed class ProgrammableTransportFactory : IPubSubTransportFactory
        {
            private readonly ProgrammableTransport m_transport;

            public ProgrammableTransportFactory(ProgrammableTransport transport)
            {
                m_transport = transport;
            }

            public string TransportProfileUri => Profiles.PubSubUdpUadpTransport;

            public IPubSubTransport Create(
                PubSubConnectionDataType connection,
                ITelemetryContext telemetry,
                TimeProvider timeProvider)
            {
                return m_transport;
            }
        }

        private sealed class ProgrammableTransport : IPubSubTransport
        {
            private readonly IReadOnlyList<byte[]> m_frames;

            private readonly TaskCompletionSource<bool> m_drained =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            private bool m_isConnected;

            public ProgrammableTransport(IReadOnlyList<byte[]> frames)
            {
                m_frames = frames;
            }

            public string TransportProfileUri => Profiles.PubSubUdpUadpTransport;

            public PubSubTransportDirection Direction => PubSubTransportDirection.Receive;

            public bool IsConnected => m_isConnected;

            public event EventHandler<PubSubTransportStateChangedEventArgs>? StateChanged
            {
                add { }
                remove { }
            }

            public ValueTask OpenAsync(CancellationToken cancellationToken = default)
            {
                m_isConnected = true;
                return default;
            }

            public ValueTask CloseAsync(CancellationToken cancellationToken = default)
            {
                m_isConnected = false;
                return default;
            }

            public ValueTask SendAsync(
                ReadOnlyMemory<byte> payload,
                string? topic = null,
                CancellationToken cancellationToken = default)
            {
                return default;
            }

            public async IAsyncEnumerable<PubSubTransportFrame> ReceiveAsync(
                [EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                foreach (byte[] frame in m_frames)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    yield return new PubSubTransportFrame(
                        frame, null, DateTimeUtc.From(DateTime.UtcNow));
                }
                // The receive loop only requests the next element after
                // fully processing the previous frame, so signalling here
                // guarantees every frame has been handled.
                m_drained.TrySetResult(true);
                try
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
            }

            public ValueTask DisposeAsync()
            {
                m_isConnected = false;
                m_drained.TrySetResult(true);
                return default;
            }

            public async Task WaitUntilDrainedAsync()
            {
                Task completed = await Task.WhenAny(
                    m_drained.Task,
                    Task.Delay(TimeSpan.FromSeconds(10))).ConfigureAwait(false);
                Assert.That(completed, Is.SameAs(m_drained.Task),
                    "Timed out waiting for the transport to drain its frames.");
                // Allow the final processed frame's continuation to settle.
                await Task.Delay(50).ConfigureAwait(false);
            }
        }
    }
}
