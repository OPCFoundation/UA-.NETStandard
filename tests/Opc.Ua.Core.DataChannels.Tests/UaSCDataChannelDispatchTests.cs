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
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.DataChannels.Tests
{
    /// <summary>
    /// Exercises the UA TCP STR dispatch that multiplexes data-channel
    /// frames onto the existing UASC secure channel.
    /// </summary>
    [TestFixture]
    [Category("DataChannels")]
    public sealed class UaSCDataChannelDispatchTests
    {
        private const uint SecureChannelId = 0x0000A17C;
        private const uint TokenId = 7;
        private const uint DataChannelId = 1;

        [Test]
        public async Task InboundFinalStreamChunkIsDispatchedToDataChannelManager()
        {
            using var channel = TestChannel.Create("str-dispatch-inbound");
            channel.AttachTransport(new CapturingByteTransport());
            channel.Activate(SecureChannelId, TokenId);
            DataChannel sink = OpenSink(channel);

            byte[] chunk = SpecVectors.Load("inline_data_first");

            Assert.That(channel.DispatchStream(chunk), Is.False);

            using DataChannelMessage? message = await ReadWithTimeoutAsync(sink)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(channel.ProtocolFaults, Is.Empty);
                Assert.That(channel.ProcessingErrors, Is.Empty);
                Assert.That(message, Is.Not.Null);
                Assert.That(message!.Payload.Span.ToArray(), Is.EqualTo(ExpectedPayload()));
                Assert.That(message.IsMessageStart, Is.True);
                Assert.That(message.IsMarker, Is.True);
                Assert.That(message.FrameSequenceNumber, Is.EqualTo(1u));
            });

            await DisposeDataChannelsAsync(channel).ConfigureAwait(false);
        }

        [TestCase('A')]
        [TestCase('C')]
        public async Task InboundStreamChunkRejectsNonFinalIsFinalByte(char isFinal)
        {
            using var channel = TestChannel.Create("str-dispatch-nonfinal");
            channel.AttachTransport(new CapturingByteTransport());
            channel.Activate(SecureChannelId, TokenId);
            DataChannel sink = OpenSink(channel);

            byte[] chunk = SpecVectors.Load("inline_data_first");
            chunk[3] = (byte)isFinal;

            Assert.That(channel.DispatchStream(chunk), Is.False);
            DataChannelMessage? message = await ReadWithTimeoutAsync(sink)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(
                    channel.ProtocolFaults,
                    Has.Count.EqualTo(1).And.Contain(DataChannelFrameError.InvalidIsFinal));
                Assert.That(channel.ProcessingErrors, Is.Empty);
                Assert.That(message, Is.Null);
            });

            await DisposeDataChannelsAsync(channel).ConfigureAwait(false);
        }

        [Test]
        public async Task MalformedShortStreamChunkIsRejectedWithoutEscapingTheReceiveLoop()
        {
            using var channel = TestChannel.Create("str-dispatch-short");
            channel.AttachTransport(new CapturingByteTransport());
            channel.Activate(SecureChannelId, TokenId);
            OpenSink(channel);

            byte[] chunk = [.. SpecVectors.Load("inline_data_first").Take(11)];

            Assert.DoesNotThrow(() => channel.ReceiveChunk(chunk));

            Assert.Multiple(() =>
            {
                Assert.That(
                    channel.ProtocolFaults,
                    Has.Count.EqualTo(1).And.Contain(DataChannelFrameError.MalformedHeader));
                Assert.That(channel.ProcessingErrors, Is.Empty);
            });

            await DisposeDataChannelsAsync(channel).ConfigureAwait(false);
        }

        [Test]
        public void StreamChunkBeforeDataChannelsAreEnabledFaultsTheSecureChannel()
        {
            using var channel = TestChannel.Create("str-dispatch-disabled");
            channel.Activate(SecureChannelId, TokenId);

            Assert.That(channel.DispatchStream(SpecVectors.Load("inline_data_first")), Is.False);

            Assert.Multiple(() =>
            {
                Assert.That(
                    channel.ProtocolFaults,
                    Has.Count.EqualTo(1).And.Contain(DataChannelFrameError.InvalidControlChannelFrame));
                Assert.That(channel.ProcessingErrors, Is.Empty);
            });
        }

        [Test]
        public async Task StreamChunkAfterTokenCloseIsRejectedWithoutDispatch()
        {
            using var channel = TestChannel.Create("str-dispatch-closed");
            channel.AttachTransport(new CapturingByteTransport());
            channel.Activate(SecureChannelId, TokenId);
            DataChannel sink = OpenSink(channel);
            channel.CloseTokens();

            Assert.That(channel.DispatchStream(SpecVectors.Load("inline_data_first")), Is.False);
            DataChannelMessage? message = await ReadWithTimeoutAsync(sink)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(
                    channel.ProtocolFaults,
                    Has.Count.EqualTo(1).And.Contain(DataChannelFrameError.MalformedHeader));
                Assert.That(message, Is.Null);
            });

            await DisposeDataChannelsAsync(channel).ConfigureAwait(false);
        }

        [Test]
        public async Task OutboundDataChannelFrameWritesSpecConformantStreamChunk()
        {
            using var channel = TestChannel.Create("str-dispatch-outbound");
            var transport = new CapturingByteTransport();
            channel.AttachTransport(transport);
            channel.Activate(SecureChannelId, TokenId);
            channel.SetNextSendSequenceNumber(51);

            await SendFrameAsync(
                    channel,
                    DataChannelFrame.Data(
                        DataChannelId,
                        1,
                        DataChannelFrameFlags.MessageStart | DataChannelFrameFlags.Marker,
                        ExpectedPayload()))
                .ConfigureAwait(false);

            Assert.That(transport.Chunks, Has.Count.EqualTo(1));
            byte[] chunk = transport.Chunks[0];

            Assert.Multiple(() =>
            {
                Assert.That(chunk, Is.EqualTo(SpecVectors.Load("inline_data_first")));
                Assert.That(SpecVectors.MessageType(chunk), Is.EqualTo("STR"));
                Assert.That(SpecVectors.IsFinal(chunk), Is.EqualTo('F'));
                Assert.That(SpecVectors.MessageSize(chunk), Is.EqualTo((uint)chunk.Length));
                Assert.That(BitConverter.ToUInt32(chunk, 8), Is.EqualTo(SecureChannelId));
                Assert.That(BitConverter.ToUInt32(chunk, 12), Is.EqualTo(TokenId));
                Assert.That(BitConverter.ToUInt32(chunk, 20), Is.Zero);
            });
        }

        [Test]
        public async Task SequenceBudgetCountsServiceChunksAndStreamChunks()
        {
            using var channel = TestChannel.Create("str-dispatch-budget");
            var transport = new CapturingByteTransport();
            channel.AttachTransport(transport);
            channel.Activate(SecureChannelId, TokenId);

            channel.EmitServiceChunk();
            channel.EmitServiceChunk();

            await SendFrameAsync(
                    channel,
                    DataChannelFrame.Data(
                        DataChannelId,
                        1,
                        DataChannelFrameFlags.MessageStart,
                        ExpectedPayload()))
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(transport.Chunks, Has.Count.EqualTo(3));
                Assert.That(channel.SequenceBudget.Consumed, Is.EqualTo(3));
            });
        }

        [Test]
        public async Task SequenceBudgetIsScopedToTheTokenRatherThanTheChannelLifetime()
        {
            using var channel = TestChannel.Create("str-dispatch-budget-reset");
            var transport = new CapturingByteTransport();
            channel.AttachTransport(transport);
            channel.Activate(SecureChannelId, TokenId);

            channel.EmitServiceChunk();

            await SendFrameAsync(
                    channel,
                    DataChannelFrame.Data(
                        DataChannelId,
                        1,
                        DataChannelFrameFlags.MessageStart,
                        ExpectedPayload()))
                .ConfigureAwait(false);

            Assert.That(
                channel.SequenceBudget.Consumed,
                Is.EqualTo(2),
                "Both chunks draw from the space under the first token.");

            // The SequenceNumber space is per SecurityToken, so activating a
            // new one restores the whole budget. m_sequenceNumber keeps
            // counting for the lifetime of the channel, so a budget that
            // observed it directly would undo its own reset and leave a long
            // lived channel permanently stalled.
            channel.Activate(SecureChannelId, TokenId + 1);

            Assert.That(
                channel.SequenceBudget.Consumed,
                Is.Zero,
                "The new token brings a fresh SequenceNumber space.");

            channel.EmitServiceChunk();

            Assert.That(
                channel.SequenceBudget.Consumed,
                Is.EqualTo(1),
                "Consumption under the new token counts from the new origin.");
        }

        [Test]
        public async Task EverySymmetricChunkNotifiesTheSequenceNumberHook()
        {
            using var channel = TestChannel.Create("str-dispatch-budget-hook");
            var transport = new CapturingByteTransport();
            channel.AttachTransport(transport);
            channel.Activate(SecureChannelId, TokenId);

            int issuedAfterActivation = channel.SequenceNumbersIssued;

            channel.EmitServiceChunk();

            await SendFrameAsync(
                    channel,
                    DataChannelFrame.Data(
                        DataChannelId,
                        1,
                        DataChannelFrameFlags.MessageStart,
                        ExpectedPayload()))
                .ConfigureAwait(false);

            Assert.That(
                channel.SequenceNumbersIssued - issuedAfterActivation,
                Is.EqualTo(2),
                "MSG and STR chunks alike draw from the one sequence space, " +
                    "so both have to reach the hook that drives early renewal.");
        }

        [Test]
        public void OutboundOversizedStreamFrameIsRefusedBeforeWrite()
        {
            using var channel = TestChannel.Create("str-dispatch-limits");
            var transport = new CapturingByteTransport();
            channel.AttachTransport(transport);
            channel.Activate(SecureChannelId, TokenId);
            channel.SetMaxResponseMessageSize(16);

            ServiceResultException? exception = Assert.ThrowsAsync<ServiceResultException>(
                async () => await SendFrameAsync(
                        channel,
                        DataChannelFrame.Data(
                            DataChannelId,
                            1,
                            DataChannelFrameFlags.MessageStart,
                            new byte[64]))
                    .ConfigureAwait(false));

            Assert.Multiple(() =>
            {
                Assert.That(exception, Is.Not.Null);
                Assert.That(
                    exception!.StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadDataChannelLimitsExceeded));
                Assert.That(transport.Chunks, Is.Empty);
            });
        }

        private static DataChannel OpenSink(TestChannel channel)
        {
            DataChannelManager manager = channel.EnableDataChannels(
                isServer: false,
                NUnitTelemetryContext.Create());

            var settings = new DataChannelSettings
            {
                Direction = DataChannelDirection.SourceToSink,
                DeliveryMode = DataChannelDeliveryMode.ReliableOrdered,
                MaxFrameSize = 4096,
                InitialCredit = 65536
            };

            DataChannel dataChannel = manager.Register(
                DataChannelId,
                new NodeId(1u),
                settings,
                isSource: false);

            manager.MarkOpen(DataChannelId);
            return dataChannel;
        }

        private static async Task<DataChannelMessage?> ReadWithTimeoutAsync(DataChannel channel)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

            try
            {
                return await channel.ReadAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }

        private static async Task DisposeDataChannelsAsync(TestChannel channel)
        {
            if (channel.DataChannels != null)
            {
                await channel.DataChannels.DisposeAsync().ConfigureAwait(false);
            }
        }

        private static async Task SendFrameAsync(TestChannel channel, DataChannelFrame frame)
        {
            MethodInfo method = typeof(UaSCUaBinaryChannel).GetMethod(
                    "SendDataChannelFrameAsync",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new AssertionException("SendDataChannelFrameAsync was not found.");

            var task = (ValueTask)(method.Invoke(channel, [frame, CancellationToken.None])
                ?? throw new AssertionException("SendDataChannelFrameAsync returned null."));

            await task.ConfigureAwait(false);
        }

        private static byte[] ExpectedPayload()
        {
            byte[] payload = new byte[16];
            for (int ii = 0; ii < payload.Length; ii++)
            {
                payload[ii] = (byte)ii;
            }

            return payload;
        }

        private sealed class TestChannel : UaSCUaBinaryChannel
        {
            private TestChannel(
                string contextId,
                BufferManager bufferManager,
                ChannelQuotas quotas,
                ITelemetryContext telemetry)
                : base(
                    contextId,
                    bufferManager,
                    quotas,
                    serverCertificates: null,
                    endpoints: null,
                    securityMode: MessageSecurityMode.None,
                    securityPolicyUri: SecurityPolicies.None,
                    telemetry: telemetry)
            {
            }

            public List<DataChannelFrameError> ProtocolFaults => m_protocolFaults;

            public List<ServiceResult> ProcessingErrors => m_processingErrors;

            public int SequenceNumbersIssued => m_sequenceNumbersIssued;

            public static TestChannel Create(string contextId)
            {
                ITelemetryContext telemetry = NUnitTelemetryContext.Create();
                return new TestChannel(
                    contextId,
                    new BufferManager(contextId, TcpMessageLimits.DefaultMaxBufferSize, telemetry),
                    new ChannelQuotas(ServiceMessageContext.CreateEmpty(telemetry)),
                    telemetry);
            }

            public void Activate(uint channelId, uint tokenId)
            {
                ChannelId = channelId;
                ChannelToken token = CreateToken();
                token.TokenId = tokenId;
                ActivateToken(token);
            }

            public bool DispatchStream(byte[] chunk)
            {
                return ProcessStreamMessage(
                    BitConverter.ToUInt32(chunk, 0),
                    new ArraySegment<byte>(chunk),
                    isRequest: true);
            }

            public void ReceiveChunk(byte[] chunk)
            {
                byte[] buffer = BufferManager.TakeBuffer(chunk.Length, nameof(ReceiveChunk));
                chunk.CopyTo(buffer, 0);
                OnChunkReceived(new ArraySegment<byte>(buffer, 0, chunk.Length));
            }

            public void AttachTransport(IUaSCByteTransport transport)
            {
                Transport = transport;
            }

            public void CloseTokens()
            {
                DiscardTokens();
            }

            public void SetMaxResponseMessageSize(int maxResponseMessageSize)
            {
                MaxRequestMessageSize = maxResponseMessageSize;
                MaxResponseMessageSize = maxResponseMessageSize;
                MaxRequestChunkCount = 1;
                MaxResponseChunkCount = 1;
            }

            public void SetNextSendSequenceNumber(uint sequenceNumber)
            {
                FieldInfo field = typeof(UaSCUaBinaryChannel).GetField(
                        "m_sequenceNumber",
                        BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new AssertionException("m_sequenceNumber was not found.");

                field.SetValue(this, (long)sequenceNumber - 1);
            }

            public void EmitServiceChunk()
            {
                ChannelToken token = CurrentToken
                    ?? throw new AssertionException("No active token.");

                BufferCollection chunks = WriteSymmetricMessage(
                    TcpMessageType.Message,
                    requestId: 1,
                    token,
                    new ArraySegment<byte>(Array.Empty<byte>()),
                    isRequest: true,
                    out bool limitsExceeded);

                Assert.That(limitsExceeded, Is.False);

                try
                {
                    Transport!.SendChunkAsync(chunks, CancellationToken.None).AsTask().GetAwaiter().GetResult();
                }
                finally
                {
                    chunks.Release(BufferManager, nameof(EmitServiceChunk));
                }
            }

            protected override void OnSequenceNumberIssued()
            {
                Interlocked.Increment(ref m_sequenceNumbersIssued);
            }

            protected override void OnDataChannelProtocolFault(DataChannelFrameError error)
            {
                m_protocolFaults.Add(error);
            }

            protected override bool HandleIncomingMessage(
                uint messageType,
                ArraySegment<byte> messageChunk)
            {
                if (TcpMessageType.IsType(messageType, TcpMessageType.Stream))
                {
                    return ProcessStreamMessage(messageType, messageChunk, isRequest: true);
                }

                return false;
            }

            protected override void HandleMessageProcessingError(ServiceResult result)
            {
                m_processingErrors.Add(result);
            }

            private readonly List<DataChannelFrameError> m_protocolFaults = [];
            private readonly List<ServiceResult> m_processingErrors = [];
            private int m_sequenceNumbersIssued;
        }

        private sealed class CapturingByteTransport : IUaSCByteTransport
        {
            public List<byte[]> Chunks => m_chunks;

            public EndPoint? LocalEndpoint => null;

            public EndPoint? RemoteEndpoint => null;

            public TransportChannelFeatures Features => TransportChannelFeatures.None;

            public string Implementation => "test";

            public ValueTask ConnectAsync(Uri url, CancellationToken ct)
            {
                throw new NotSupportedException();
            }

            public ValueTask SendChunkAsync(ReadOnlyMemory<byte> chunk, CancellationToken ct)
            {
                m_chunks.Add(chunk.ToArray());
                return default;
            }

            public ValueTask SendChunkAsync(BufferCollection buffers, CancellationToken ct)
            {
                byte[] chunk = new byte[buffers.Sum(segment => segment.Count)];
                int offset = 0;

                foreach (ArraySegment<byte> segment in buffers)
                {
                    segment.AsSpan().CopyTo(chunk.AsSpan(offset, segment.Count));
                    offset += segment.Count;
                }

                m_chunks.Add(chunk);
                return default;
            }

            public ValueTask<ArraySegment<byte>> ReceiveChunkAsync(CancellationToken ct)
            {
                throw new NotSupportedException();
            }

            public void Close()
            {
            }

            private readonly List<byte[]> m_chunks = [];
        }
    }
}
