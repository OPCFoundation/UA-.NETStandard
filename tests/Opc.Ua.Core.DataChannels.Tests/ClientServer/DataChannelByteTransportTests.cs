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
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Core.DataChannels.Tests.Runtime;
using Opc.Ua.DataChannels;

namespace Opc.Ua.Core.DataChannels.Tests.ClientServer
{
    /// <summary>
    /// Exercises the lane adapter through real common-flow state with an in-process carrier.
    /// These cases do not claim a real native transport or SecureChannel handshake.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class DataChannelByteTransportTests
    {
        private static TestCaseData[] ResetOutcomes =>
        [
            new(DataChannelResult.Unsupported, StatusCodes.BadNotSupported),
            new(DataChannelResult.InvalidArgument, StatusCodes.BadInvalidArgument),
            new(DataChannelResult.InvalidState, StatusCodes.BadInvalidState),
            new(DataChannelResult.NotAuthorized, StatusCodes.BadUserAccessDenied),
            new(DataChannelResult.SecurityFailure, StatusCodes.BadSecurityChecksFailed),
            new(DataChannelResult.TooLarge, StatusCodes.BadTcpMessageTooLarge),
            new(DataChannelResult.Timeout, StatusCodes.BadTimeout),
            new(DataChannelResult.Closed, StatusCodes.BadConnectionClosed),
            new(DataChannelResult.UnknownChannel, StatusCodes.BadSecureChannelIdInvalid),
            new(DataChannelResult.WouldBlock, StatusCodes.BadResourceUnavailable)
        ];

        /// <summary>
        /// Installs a buffer pool whose outstanding ownership is checked after every case.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            m_telemetry = DefaultTelemetry.Create(builder => builder.SetMinimumLevel(LogLevel.None));
            m_pool = new TrackingPool();
            m_buffers = new BufferManager("NativeLaneTests", 4096, m_telemetry, m_pool);
        }

        /// <summary>
        /// Detects leaked or stolen UASC buffers independently of flow queue accounting.
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            try
            {
                Assert.That(m_pool.Outstanding, Is.Zero);
            }
            finally
            {
                if (m_telemetry is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
        }

        /// <summary>
        /// Preserves one whole chunk without a second payload wrapper for either opener role.
        /// </summary>
        [TestCase(DataChannelCarrierKind.Quic, true)]
        [TestCase(DataChannelCarrierKind.Quic, false)]
        [TestCase(DataChannelCarrierKind.WebTransport, true)]
        [TestCase(DataChannelCarrierKind.WebTransport, false)]
        public async Task CompleteChunkBytesSurviveEveryNativeRole(
            DataChannelCarrierKind carrier,
            bool fromInitiator)
        {
            await using var pair = new FlowLoopback(carrier);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync(LaneParameters(), fromInitiator).ConfigureAwait(false);
            await using DataChannelByteTransport transport = Create(pair, flow, fromInitiator);
            byte[] chunk = Chunk(64);
            byte[] expected = [.. chunk];
            await transport.SendChunkAsync(chunk, CancellationToken.None).ConfigureAwait(false);
            chunk.AsSpan().Fill(0xEE);

            using DataChannelMessage? message = await flow.Remote.ReadAsync().AsTask()
                .WaitAsync(s_timeout).ConfigureAwait(false);
            Assert.That(message, Is.Not.Null);
            Assert.That(message!.Payload.ToArray(), Is.EqualTo(expected));
            Assert.That(message.MessageId, Is.EqualTo(1));
            Assert.That(transport.ChannelId, Is.EqualTo(flow.Local.ChannelId));
            Assert.That(transport.Features, Is.EqualTo(TransportChannelFeatures.None));
            Assert.That(transport, Is.Not.InstanceOf<IUaSCChunkCoalescing>());
        }

        /// <summary>
        /// Gathers segments across the header boundary while retaining exactly one chunk.
        /// </summary>
        [Test]
        public async Task GatheredChunkHasNoExtraFramingAndReturnsItsTemporaryBuffer()
        {
            await using var pair = new FlowLoopback(DataChannelCarrierKind.Quic);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync(LaneParameters()).ConfigureAwait(false);
            await using DataChannelByteTransport transport = Create(pair, flow);
            byte[] chunk = Chunk(31);
            var segments = new BufferCollection
            {
                new(chunk, 0, 3),
                default,
                new(chunk, 3, 2),
                new(chunk, 5, chunk.Length - 5)
            };
            await transport.SendChunkAsync(segments, CancellationToken.None).ConfigureAwait(false);
            Assert.That(m_pool.Outstanding, Is.Zero);
            using DataChannelMessage? message = await flow.Remote.ReadAsync().ConfigureAwait(false);
            Assert.That(message!.Payload.ToArray(), Is.EqualTo(chunk));
            Assert.That(pair.InitiatorCarrier.Writes.Count(write => write.IsData), Is.EqualTo(1));
        }

        /// <summary>
        /// Rejects local framing errors before they consume a flow DATA identifier or buffer.
        /// </summary>
        [TestCase(0)]
        [TestCase(7)]
        [TestCase(8)]
        [TestCase(257)]
        public async Task InvalidLocalChunkLengthDoesNotConsumeMessageIdentity(int length)
        {
            await using var pair = new FlowLoopback(DataChannelCarrierKind.Quic);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync(LaneParameters()).ConfigureAwait(false);
            await using DataChannelByteTransport transport = Create(pair, flow);
            byte[] invalid = length > 8 ? Chunk(length) : new byte[length];
            ServiceResultException? error = Assert.ThrowsAsync<ServiceResultException>(
                async () => await transport.SendChunkAsync(invalid, CancellationToken.None).ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadTcpMessageTooLarge));
            Assert.That(pair.InitiatorCarrier.Writes.Any(write => write.IsData), Is.False);

            byte[] valid = Chunk(9);
            await transport.SendChunkAsync(valid, CancellationToken.None).ConfigureAwait(false);
            using DataChannelMessage? message = await flow.Remote.ReadAsync().ConfigureAwait(false);
            Assert.That(message!.MessageId, Is.EqualTo(1));
            Assert.That(message.Payload.ToArray(), Is.EqualTo(valid));
        }

        /// <summary>
        /// Rejects concatenation even when both individual chunks and the total fit the flow.
        /// </summary>
        [Test]
        public async Task GatheredChunksCannotBeCoalescedIntoOneFlowPayload()
        {
            await using var pair = new FlowLoopback(DataChannelCarrierKind.Quic);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync(LaneParameters()).ConfigureAwait(false);
            await using DataChannelByteTransport transport = Create(pair, flow);
            var segments = new BufferCollection { new(Chunk(16)), new(Chunk(16)) };
            ServiceResultException? error = Assert.ThrowsAsync<ServiceResultException>(
                async () => await transport.SendChunkAsync(segments, CancellationToken.None).ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadTcpMessageTooLarge));
            Assert.That(m_pool.Outstanding, Is.Zero);
            Assert.That(pair.InitiatorCarrier.Writes.Any(write => write.IsData), Is.False);
        }

        /// <summary>
        /// FLO is forbidden only at the reserved UACP lane boundary, never by inspecting unrelated opaque DATA.
        /// </summary>
        [TestCase(DataChannelCarrierKind.Quic, false)]
        [TestCase(DataChannelCarrierKind.Quic, true)]
        [TestCase(DataChannelCarrierKind.WebTransport, false)]
        [TestCase(DataChannelCarrierKind.WebTransport, true)]
        public async Task NestedFlowChunksCannotEnterTheClientServerLane(
            DataChannelCarrierKind carrier, bool gathered)
        {
            await using var pair = new FlowLoopback(carrier);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync(LaneParameters()).ConfigureAwait(false);
            await using DataChannelByteTransport transport = Create(pair, flow);
            byte[] nested = Chunk(32);
            BinaryPrimitives.WriteUInt32LittleEndian(nested, 0x464F4C46);
            ServiceResultException? outgoing = Assert.ThrowsAsync<ServiceResultException>(async () =>
            {
                if (gathered)
                {
                    var segments = new BufferCollection { new(nested, 0, 3), new(nested, 3, nested.Length - 3) };
                    await transport.SendChunkAsync(segments, CancellationToken.None).ConfigureAwait(false);
                }
                else
                {
                    await transport.SendChunkAsync(nested, CancellationToken.None).ConfigureAwait(false);
                }
            });
            Assert.That(outgoing!.StatusCode, Is.EqualTo(StatusCodes.BadTcpMessageTypeInvalid));
            Assert.That(pair.InitiatorCarrier.Writes.Any(write => write.IsData), Is.False);

            int rented = m_pool.TotalRented;
            Assert.That(await flow.Remote.SendAsync(new ByteString(nested)).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            ServiceResultException? incoming = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await transport.ReceiveChunkAsync(CancellationToken.None).ConfigureAwait(false));
            Assert.That(incoming!.StatusCode, Is.EqualTo(StatusCodes.BadTcpMessageTypeInvalid));
            Assert.That(m_pool.TotalRented, Is.EqualTo(rented));
            Assert.That(pair.InitiatorBudget.Usage.Messages, Is.Zero);

            FlowPair opaque = await pair.OpenAsync(FlowLoopback.Parameters()).ConfigureAwait(false);
            Assert.That(await opaque.Local.SendAsync(new ByteString(nested)).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage? received = await opaque.Remote.ReadAsync().ConfigureAwait(false);
            Assert.That(received!.Payload.ToArray(), Is.EqualTo(nested));
            Assert.That(pair.Initiator.IsReady, Is.True);
            Assert.That(pair.Acceptor.IsReady, Is.True);
        }

        /// <summary>
        /// Checks the payload contract separately from a ready authenticated native context.
        /// </summary>
        [TestCase("resource")]
        [TestCase("size")]
        public async Task IncompatibleReadyFlowIsNotAnImplicitClientServerLane(string field)
        {
            await using var pair = new FlowLoopback(DataChannelCarrierKind.Quic);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync(LaneParameters(field)).ConfigureAwait(false);
            Assert.Throws<ArgumentException>(() =>
            {
                using DataChannelByteTransport transport = Create(pair, flow);
            });
            Assert.That(flow.Local.State, Is.EqualTo(DataChannelState.Open));
        }

        /// <summary>
        /// The common runtime rejects forbidden reserved-lane descriptors before an adapter can acquire them.
        /// </summary>
        [TestCase("protocol", DataChannelResult.InvalidArgument)]
        [TestCase("direction", DataChannelResult.InvalidArgument)]
        [TestCase("delivery", DataChannelResult.InvalidArgument)]
        [TestCase("expiry", DataChannelResult.InvalidArgument)]
        [TestCase("protection", DataChannelResult.Unsupported)]
        public async Task ReservedLaneDescriptorIsRefusedBeforeReadiness(string field, DataChannelResult result)
        {
            await using var pair = new FlowLoopback(DataChannelCarrierKind.Quic);
            await pair.StartAsync().ConfigureAwait(false);
            DataChannelException? error = Assert.ThrowsAsync<DataChannelException>(
                async () => await pair.OpenAsync(LaneParameters(field)).ConfigureAwait(false));
            Assert.That(error!.Result, Is.EqualTo(result));
            Assert.That(pair.Initiator.Channels, Is.Empty);
            Assert.That(pair.InitiatorBudget.Usage.Flows, Is.Zero);
            Assert.That(pair.InitiatorCarrier.Writes.Any(write => write.ChannelId != 0), Is.False);
        }

        /// <summary>
        /// Does not accept an otherwise valid flow under another authenticated association.
        /// </summary>
        [Test]
        public async Task ForeignContextCannotBorrowAReadyFlow()
        {
            await using var first = new FlowLoopback(DataChannelCarrierKind.Quic);
            await using var second = new FlowLoopback(DataChannelCarrierKind.Quic);
            await Task.WhenAll(first.StartAsync(), second.StartAsync()).ConfigureAwait(false);
            FlowPair flow = await first.OpenAsync(LaneParameters()).ConfigureAwait(false);
            Assert.Throws<ArgumentException>(() =>
            {
                using DataChannelByteTransport transport = Create(second, flow);
            });
            Assert.That(flow.Local.State, Is.EqualTo(DataChannelState.Open));
        }

        /// <summary>
        /// Keeps the reserved native lane from turning a UASC association into a recursive carrier.
        /// </summary>
        [TestCase(DataChannelCarrierKind.SharedUasc)]
        [TestCase(DataChannelCarrierKind.DedicatedUasc)]
        public async Task UascContextCannotHostTheNativeLane(DataChannelCarrierKind kind)
        {
            await using var native = new FlowLoopback(DataChannelCarrierKind.Quic);
            await using var uasc = new FlowLoopback(kind);
            await Task.WhenAll(native.StartAsync(), uasc.StartAsync()).ConfigureAwait(false);
            FlowPair flow = await native.OpenAsync(LaneParameters()).ConfigureAwait(false);
            Assert.Throws<ArgumentException>(() =>
            {
                using DataChannelByteTransport transport = Create(uasc, flow);
            });
        }

        /// <summary>
        /// Hands receive storage to UASC and never reclaims it when the flow closes.
        /// </summary>
        [Test]
        public async Task ReceivedChunkOwnershipSurvivesTransportClosure()
        {
            await using var pair = new FlowLoopback(DataChannelCarrierKind.Quic);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync(LaneParameters()).ConfigureAwait(false);
            await using DataChannelByteTransport transport = Create(pair, flow);
            byte[] expected = Chunk(256);
            Assert.That(await flow.Remote.SendAsync(new ByteString(expected)).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            ArraySegment<byte> received = await transport.ReceiveChunkAsync(CancellationToken.None)
                .ConfigureAwait(false);
            try
            {
                Assert.That(m_pool.Outstanding, Is.EqualTo(1));
                Assert.That(pair.InitiatorBudget.Usage.Messages, Is.Zero);
                await transport.DisposeAsync().ConfigureAwait(false);
                Assert.That(m_pool.Outstanding, Is.EqualTo(1));
                Assert.That(received.ToArray(), Is.EqualTo(expected));
            }
            finally
            {
                m_buffers.ReturnBuffer(received.Array, nameof(ReceivedChunkOwnershipSurvivesTransportClosure));
            }
        }

        /// <summary>
        /// Rejects an invalid inner header before renting a UASC receive buffer.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task MalformedInboundChunkReleasesTheFlowMessageWithoutRenting(bool wrongType)
        {
            await using var pair = new FlowLoopback(DataChannelCarrierKind.Quic);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync(LaneParameters()).ConfigureAwait(false);
            await using DataChannelByteTransport transport = Create(pair, flow);
            byte[] invalid = Chunk(32);
            BinaryPrimitives.WriteUInt32LittleEndian(invalid.AsSpan(wrongType ? 0 : 4), 0xFFFFFFFFu);
            Assert.That(await flow.Remote.SendAsync(new ByteString(invalid)).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            ServiceResultException? error = Assert.ThrowsAsync<ServiceResultException>(
                async () => await transport.ReceiveChunkAsync(CancellationToken.None).ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(wrongType
                ? StatusCodes.BadTcpMessageTypeInvalid : StatusCodes.BadTcpMessageTooLarge));
            Assert.That(m_pool.TotalRented, Is.Zero);
            Assert.That(pair.InitiatorBudget.Usage.Messages, Is.Zero);
        }

        /// <summary>
        /// Applies updated UASC buffer limits without stealing or enlarging the flow's admission.
        /// </summary>
        [Test]
        public async Task NegotiatedReceiveCapacityIsInclusiveAndMutable()
        {
            await using var pair = new FlowLoopback(DataChannelCarrierKind.Quic);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync(LaneParameters()).ConfigureAwait(false);
            await using DataChannelByteTransport transport = Create(pair, flow);
            var limits = (IUaSCByteTransportLimits)transport;
            limits.SetReceiveBufferSize(32);
            Assert.Throws<ArgumentOutOfRangeException>(() => limits.SetReceiveBufferSize(8));
            Assert.That(await flow.Remote.SendAsync(new ByteString(Chunk(33))).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            ServiceResultException? error = Assert.ThrowsAsync<ServiceResultException>(
                async () => await transport.ReceiveChunkAsync(CancellationToken.None).ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadTcpMessageTooLarge));
            Assert.That(m_pool.TotalRented, Is.Zero);

            byte[] expected = Chunk(32);
            await flow.Remote.SendAsync(new ByteString(expected)).ConfigureAwait(false);
            ArraySegment<byte> received = await transport.ReceiveChunkAsync(CancellationToken.None)
                .ConfigureAwait(false);
            try
            {
                Assert.That(received.ToArray(), Is.EqualTo(expected));
            }
            finally
            {
                m_buffers.ReturnBuffer(received.Array, nameof(NegotiatedReceiveCapacityIsInclusiveAndMutable));
            }
        }

        /// <summary>
        /// Cancelling a reader does not close a flow or permit two simultaneous UASC readers.
        /// </summary>
        [Test]
        public async Task CancelledReceiveCanBeRetriedWithoutLosingTheNextChunk()
        {
            await using var pair = new FlowLoopback(DataChannelCarrierKind.Quic);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync(LaneParameters()).ConfigureAwait(false);
            await using DataChannelByteTransport transport = Create(pair, flow);
            using var cancellation = new CancellationTokenSource();
            Task<ArraySegment<byte>> pending = transport.ReceiveChunkAsync(cancellation.Token).AsTask();
            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await transport.ReceiveChunkAsync(CancellationToken.None).ConfigureAwait(false));
            await cancellation.CancelAsync().ConfigureAwait(false);
            Assert.CatchAsync<OperationCanceledException>(async () => await pending.ConfigureAwait(false));
            Assert.That(flow.Local.State, Is.EqualTo(DataChannelState.Open));

            byte[] expected = Chunk(24);
            await flow.Remote.SendAsync(new ByteString(expected)).ConfigureAwait(false);
            ArraySegment<byte> received = await transport.ReceiveChunkAsync(CancellationToken.None)
                .ConfigureAwait(false);
            try
            {
                Assert.That(received.ToArray(), Is.EqualTo(expected));
            }
            finally
            {
                m_buffers.ReturnBuffer(received.Array, nameof(CancelledReceiveCanBeRetriedWithoutLosingTheNextChunk));
            }
        }

        /// <summary>
        /// Synchronous close wakes pending reads while asynchronous disposal joins only this flow.
        /// </summary>
        [Test]
        public async Task CloseIsIdempotentAndDoesNotTerminateAnotherFlowOrContext()
        {
            await using var pair = new FlowLoopback(DataChannelCarrierKind.Quic);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair lane = await pair.OpenAsync(LaneParameters()).ConfigureAwait(false);
            FlowPair other = await pair.OpenAsync().ConfigureAwait(false);
            await using DataChannelByteTransport transport = Create(pair, lane);
            Task<ArraySegment<byte>> pending = transport.ReceiveChunkAsync(CancellationToken.None).AsTask();
            transport.Close();
            transport.Close();
            await transport.DisposeAsync().AsTask().WaitAsync(s_timeout).ConfigureAwait(false);
            ServiceResultException? error = Assert.ThrowsAsync<ServiceResultException>(
                async () => await pending.WaitAsync(s_timeout).ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadConnectionClosed));
            Assert.That(pair.Initiator.IsReady, Is.True);
            Assert.That(pair.Acceptor.IsReady, Is.True);
            Assert.That(other.Local.State, Is.EqualTo(DataChannelState.Open));
            Assert.That(pair.InitiatorCarrier.ClosedFlows, Is.EqualTo(1));
            Assert.That(await other.Local.SendAsync(new ByteString("*"u8.ToArray())).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage? message = await other.Remote.ReadAsync().ConfigureAwait(false);
            Assert.That(message!.Payload.ToArray(), Is.EqualTo("*"u8.ToArray()));
        }

        /// <summary>
        /// Preserves the observed flow refusal rather than treating failed I/O as a successful chunk.
        /// </summary>
        [TestCaseSource(nameof(ResetOutcomes))]
        public async Task PeerResetIsReportedWithItsTransportStatus(DataChannelResult result, StatusCode expected)
        {
            await using var pair = new FlowLoopback(DataChannelCarrierKind.Quic);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync(LaneParameters()).ConfigureAwait(false);
            await using DataChannelByteTransport transport = Create(pair, flow);
            await flow.Remote.ResetAsync(result).ConfigureAwait(false);
            ServiceResultException? error = Assert.ThrowsAsync<ServiceResultException>(
                async () => await transport.ReceiveChunkAsync(CancellationToken.None).ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(expected));
            Assert.That(m_pool.TotalRented, Is.Zero);
        }

        /// <summary>
        /// Reports the remote half-close as EOF while preserving the other ordered direction.
        /// </summary>
        [Test]
        public async Task RemoteEndBecomesConnectionClosedWithoutClosingTheOppositeDirection()
        {
            await using var pair = new FlowLoopback(DataChannelCarrierKind.Quic);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync(LaneParameters()).ConfigureAwait(false);
            await using DataChannelByteTransport transport = Create(pair, flow);
            Assert.That(await flow.Remote.EndAsync().ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
            ServiceResultException? error = Assert.ThrowsAsync<ServiceResultException>(
                async () => await transport.ReceiveChunkAsync(CancellationToken.None).ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadConnectionClosed));
            byte[] expected = Chunk(32);
            await transport.SendChunkAsync(expected, CancellationToken.None).ConfigureAwait(false);
            using DataChannelMessage? received = await flow.Remote.ReadAsync().ConfigureAwait(false);
            Assert.That(received!.Payload.ToArray(), Is.EqualTo(expected));
        }

        /// <summary>
        /// Translates local half-close refusal and prevents writes after synchronous disposal.
        /// </summary>
        [Test]
        public async Task EndedAndDisposedLanesRejectNewWrites()
        {
            await using var pair = new FlowLoopback(DataChannelCarrierKind.Quic);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync(LaneParameters()).ConfigureAwait(false);
            await using DataChannelByteTransport transport = Create(pair, flow);
            Assert.That(await flow.Local.EndAsync().ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
            ServiceResultException? ended = Assert.ThrowsAsync<ServiceResultException>(
                async () => await transport.SendChunkAsync(Chunk(32), CancellationToken.None).ConfigureAwait(false));
            Assert.That(ended!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            using (transport)
            {
                Assert.That(flow.Local.GetStatus().OpenerEnded, Is.True);
            }
            ServiceResultException? disposed = Assert.ThrowsAsync<ServiceResultException>(
                async () => await transport.SendChunkAsync(Chunk(32), CancellationToken.None).ConfigureAwait(false));
            Assert.That(disposed!.StatusCode, Is.EqualTo(StatusCodes.BadConnectionClosed));
            Assert.That(pair.InitiatorCarrier.Writes.Any(write => write.IsData), Is.False);
        }

        /// <summary>
        /// Does not mistake an established-flow wrapper for a connector or silently ignore cancellation.
        /// </summary>
        [Test]
        public async Task EstablishedTransportRetainsEndpointsAndRejectsReconnect()
        {
            await using var pair = new FlowLoopback(DataChannelCarrierKind.Quic);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync(LaneParameters()).ConfigureAwait(false);
            var local = new IPEndPoint(IPAddress.Loopback, 12345);
            var remote = new IPEndPoint(IPAddress.Loopback, 4840);
            await using var transport = new DataChannelByteTransport(
                pair.Initiator, flow.Local, m_buffers, 256, m_telemetry, local, remote);
            Assert.That(transport.LocalEndpoint, Is.SameAs(local));
            Assert.That(transport.RemoteEndpoint, Is.SameAs(remote));
            Assert.That(transport.Implementation, Is.EqualTo("UA-FLOW"));
            Assert.ThrowsAsync<ArgumentNullException>(
                async () => await transport.ConnectAsync(null!, CancellationToken.None).ConfigureAwait(false));
            Assert.ThrowsAsync<NotSupportedException>(async () =>
                await transport.ConnectAsync(new Uri("opc.quic://localhost:4840"), CancellationToken.None)
                    .ConfigureAwait(false));
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync().ConfigureAwait(false);
            Assert.CatchAsync<OperationCanceledException>(async () =>
                await transport.ConnectAsync(new Uri("opc.quic://localhost:4840"), cancellation.Token)
                    .ConfigureAwait(false));
            Assert.That(flow.Local.State, Is.EqualTo(DataChannelState.Open));
        }

        /// <summary>
        /// Validates construction before taking ownership of a usable flow.
        /// </summary>
        [TestCase("context")]
        [TestCase("channel")]
        [TestCase("bufferManager")]
        [TestCase("telemetry")]
        [TestCase("receiveBufferSize")]
        public async Task InvalidConstructionDoesNotCloseOrBorrowTheFlow(string argument)
        {
            await using var pair = new FlowLoopback(DataChannelCarrierKind.Quic);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync(LaneParameters()).ConfigureAwait(false);
            ArgumentException? error = Assert.Catch<ArgumentException>(() =>
            {
                using var transport = new DataChannelByteTransport(
                    argument == "context" ? null! : pair.Initiator,
                    argument == "channel" ? null! : flow.Local,
                    argument == "bufferManager" ? null! : m_buffers,
                    argument == "receiveBufferSize" ? 8 : 256,
                    argument == "telemetry" ? null! : m_telemetry);
            });
            Assert.That(error!.ParamName, Is.EqualTo(argument));
            Assert.That(flow.Local.State, Is.EqualTo(DataChannelState.Open));
            Assert.That(m_pool.TotalRented, Is.Zero);
        }

        /// <summary>
        /// Refuses empty and oversized gather lists without renting storage or transmitting bytes.
        /// </summary>
        [TestCase(0)]
        [TestCase(8)]
        [TestCase(257)]
        public async Task InvalidGatherSizeIsRejectedBeforeRenting(int length)
        {
            await using var pair = new FlowLoopback(DataChannelCarrierKind.Quic);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync(LaneParameters()).ConfigureAwait(false);
            await using DataChannelByteTransport transport = Create(pair, flow);
            var buffers = new BufferCollection(new byte[length], 0, length);
            ServiceResultException? error = Assert.ThrowsAsync<ServiceResultException>(
                async () => await transport.SendChunkAsync(buffers, CancellationToken.None).ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadTcpMessageTooLarge));
            Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await transport.SendChunkAsync((BufferCollection)null!, CancellationToken.None).ConfigureAwait(false));
            Assert.That(m_pool.TotalRented, Is.Zero);
            Assert.That(pair.InitiatorCarrier.Writes.Any(write => write.IsData), Is.False);
        }

        /// <summary>
        /// A cancelled caller may return scratch storage, but a committed carrier still owns unchanged bytes.
        /// </summary>
        [Test]
        public async Task CancelledCommittedGatherRetainsBytesUntilTheCarrierFinishes()
        {
            await using var pair = new FlowLoopback(DataChannelCarrierKind.Quic);
            await pair.StartAsync().ConfigureAwait(false);
            FlowPair flow = await pair.OpenAsync(LaneParameters()).ConfigureAwait(false);
            await using DataChannelByteTransport transport = Create(pair, flow);
            pair.InitiatorCarrier.GateAfter = write => write.IsData;
            byte[] expected = Chunk(32);
            var buffers = new BufferCollection
            {
                new(expected, 0, 7),
                new(expected, 7, expected.Length - 7)
            };
            using var cancellation = new CancellationTokenSource();
            Task send = transport.SendChunkAsync(buffers, cancellation.Token).AsTask();
            await pair.InitiatorCarrier.AfterEntered.Task.WaitAsync(s_timeout).ConfigureAwait(false);
            try
            {
                await cancellation.CancelAsync().ConfigureAwait(false);
                Assert.CatchAsync<OperationCanceledException>(
                    async () => await send.WaitAsync(s_timeout).ConfigureAwait(false));
                Assert.That(m_pool.Outstanding, Is.Zero);
                Assert.That(pair.InitiatorCarrier.ActiveSends, Is.EqualTo(1));
            }
            finally
            {
                pair.InitiatorCarrier.AfterRelease.TrySetResult(true);
            }
            using DataChannelMessage? received = await flow.Remote.ReadAsync().AsTask()
                .WaitAsync(s_timeout).ConfigureAwait(false);
            Assert.That(received!.Payload.ToArray(), Is.EqualTo(expected));
            Assert.That(received.MessageId, Is.EqualTo(1));
        }

        private DataChannelByteTransport Create(FlowLoopback pair, FlowPair flow, bool fromInitiator = true)
        {
            return new DataChannelByteTransport(
                fromInitiator ? pair.Initiator : pair.Acceptor,
                flow.Local,
                m_buffers,
                256,
                m_telemetry);
        }

        private static DataChannelParametersDataType LaneParameters(string? modifiedField = null)
        {
            DataChannelParametersDataType parameters = FlowLoopback.Parameters();
            parameters.ResourceUri = DataChannelProfiles.ClientServerResource;
            switch (modifiedField)
            {
                case null:
                    break;
                case "resource":
                    parameters.ResourceUri = "urn:lane:test:other";
                    break;
                case "protocol":
                    parameters.Protocol = DataChannelProfiles.JsonPayload;
                    break;
                case "direction":
                    parameters.Direction = DataChannelDirection.OpenerToAcceptor;
                    break;
                case "delivery":
                    parameters.Delivery = DataChannelDeliveryMode.Datagram;
                    parameters.MaxRate = 4096;
                    break;
                case "expiry":
                    parameters.AllowQueueExpiry = true;
                    break;
                case "size":
                    parameters.MaxMessageSize = 8;
                    break;
                case "protection":
                    parameters.Protection = DataChannelProtectionMode.Integrity;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(modifiedField));
            }
            return parameters;
        }

        private static byte[] Chunk(int length)
        {
            byte[] bytes = new byte[length];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, TcpMessageType.MessageFinal);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)length);
            for (int index = 8; index < bytes.Length; index++)
            {
                bytes[index] = (byte)index;
            }
            return bytes;
        }

        private ITelemetryContext m_telemetry = null!;
        private TrackingPool m_pool = null!;
        private BufferManager m_buffers = null!;
        private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(5);

        private sealed class TrackingPool : ArrayPool<byte>
        {
            public int Outstanding => m_outstanding.Count;
            public int TotalRented => Volatile.Read(ref m_totalRented);

            public override byte[] Rent(int minimumLength)
            {
                byte[] bytes = new byte[minimumLength];
                if (!m_outstanding.TryAdd(bytes, 0))
                {
                    throw new InvalidOperationException("The pool returned the same live array twice.");
                }
                Interlocked.Increment(ref m_totalRented);
                return bytes;
            }

            public override void Return(byte[] array, bool clearArray = false)
            {
                if (!m_outstanding.TryRemove(array, out _))
                {
                    throw new InvalidOperationException("A buffer was returned without caller ownership.");
                }
                array.AsSpan().Fill(0xCC);
            }

            private readonly ConcurrentDictionary<byte[], byte> m_outstanding = new();
            private int m_totalRented;
        }
    }
}
