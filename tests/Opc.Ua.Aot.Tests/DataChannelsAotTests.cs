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

using System.Buffers.Binary;
using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Opc.Ua.DataChannels;

namespace Opc.Ua.Aot.Tests
{
    /// <summary>
    /// Exercises the public Part 28 runtime under NativeAOT without loading QUIC or HTTP/3 backends.
    /// The in-process carrier supplies verified-plaintext input, not evidence of transport security or native retention.
    /// </summary>
    public sealed class DataChannelsAotTests
    {
        /// <summary>
        /// Direct and injectable contexts preserve binary controls, opaque bytes and both revision reservations.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        [Test]
        [Arguments(DataChannelCarrierKind.DedicatedUasc)]
        [Arguments(DataChannelCarrierKind.SharedUasc)]
        public async Task BinaryControlsAndRevisionOwnershipSurviveAotAsync(DataChannelCarrierKind carrier)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await using var pair = new ContextPair(carrier);
            await pair.StartAsync(deadline.Token).ConfigureAwait(false);
            var parameters = new DataChannelParametersDataType
            {
                ResourceUri = "urn:opcfoundation:tests:aot:messages",
                Protocol = DataChannelProfiles.OpaquePayload,
                ContentType = "application/octet-stream",
                Direction = DataChannelDirection.Duplex,
                Delivery = DataChannelDeliveryMode.Ordered,
                Protection = DataChannelProtectionMode.Confidential,
                MaxMessageSize = 64,
                MaxReceiveBytes = 256,
                MaxRate = BitConverter.Int64BitsToDouble(long.MinValue)
            };
            await using DataChannel sender = await pair.Left.OpenAsync(
                parameters, cancellationToken: deadline.Token).ConfigureAwait(false);
            await using DataChannel receiver = await pair.Right.AcceptAsync(deadline.Token).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The AOT flow was not accepted.");
            await Assert.That(BitConverter.DoubleToInt64Bits(sender.Parameters.MaxRate)).IsEqualTo(0L);
            await Assert.That(BitConverter.DoubleToInt64Bits(parameters.MaxRate)).IsEqualTo(long.MinValue);

            ByteString payload = new(new byte[] { 0, 0x7F, 0xFF, 1 });
            await Assert.That(await sender.SendAsync(payload, cancellationToken: deadline.Token).ConfigureAwait(false))
                .IsEqualTo(DataChannelResult.Good);
            using (DataChannelMessage old = await receiver.ReadAsync(deadline.Token).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The old-revision payload was not delivered."))
            {
                await Assert.That(old.Payload).IsEqualTo(payload);
                await Assert.That(old.MessageId).IsEqualTo(1u);
                await Assert.That(old.Revision).IsEqualTo(1u);
                DataChannelParametersDataType proposed = sender.Parameters;
                proposed.MaxMessageSize = 32;
                proposed.MaxReceiveBytes = 128;
                proposed.Priority = 2;
                await Assert.That(await sender.UpdateAsync(proposed, 1, deadline.Token).ConfigureAwait(false))
                    .IsEqualTo(DataChannelResult.Good);
                while (receiver.GetOperationStatus(1).PendingRevision != 0)
                {
                    await Task.Delay(1, deadline.Token).ConfigureAwait(false);
                }
                await Assert.That(receiver.Revision).IsEqualTo(2u);
                await Assert.That(pair.RightBudget.Usage.ReservedBytes).IsEqualTo(512L);
                await Assert.That(old.Payload).IsEqualTo(payload);
                await Assert.That(old.Revision).IsEqualTo(1u);
            }
            await Assert.That(pair.RightBudget.Usage.ReservedBytes).IsEqualTo(256L);
            await Assert.That(await sender.SendAsync(ByteString.Empty, cancellationToken: deadline.Token)
                .ConfigureAwait(false)).IsEqualTo(DataChannelResult.Good);
            using (DataChannelMessage current = await receiver.ReadAsync(deadline.Token).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The new-revision payload was not delivered."))
            {
                await Assert.That(current.Payload.IsEmpty).IsTrue();
                await Assert.That(current.MessageId).IsEqualTo(2u);
                await Assert.That(current.Revision).IsEqualTo(2u);
            }

            await Assert.That(await sender.EndAsync(deadline.Token).ConfigureAwait(false))
                .IsEqualTo(DataChannelResult.Good);
            await Assert.That(await receiver.ReadAsync(deadline.Token).ConfigureAwait(false)).IsNull();
            await Assert.That(await receiver.EndAsync(deadline.Token).ConfigureAwait(false))
                .IsEqualTo(DataChannelResult.Good);
            await Assert.That(await sender.ReadAsync(deadline.Token).ConfigureAwait(false)).IsNull();

            var operations = new HashSet<byte>();
            ArrayOf<RecordedWrite> recorded = [.. pair.LeftCarrier.Writes, .. pair.RightCarrier.Writes];
            for (int index = 0; index < recorded.Count; index++)
            {
                RecordedWrite write = recorded[index];
                await Assert.That(write.RequestId).IsEqualTo(0u);
                await Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(write.Payload.Span))
                    .IsEqualTo(write.ChannelId);
                await Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(write.Payload.Span[4..]))
                    .IsEqualTo((uint)write.Payload.Length - 8);
                if (write.Payload[8] == 1)
                {
                    operations.Add(write.Payload[17]);
                    if (write.Payload[17] == 3)
                    {
                        await Assert.That(write.Payload.Span[17..].SequenceEqual(new byte[] { 3, 1, 0, 0, 0 }))
                            .IsTrue();
                    }
                }
            }
            foreach (byte opcode in new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 10 })
            {
                await Assert.That(operations.Contains(opcode)).IsTrue();
            }
            await Assert.That(operations.Contains(9)).IsEqualTo(carrier == DataChannelCarrierKind.SharedUasc);
            await pair.DisposeAsync().ConfigureAwait(false);
            await AssertReleasedAsync(pair.LeftBudget).ConfigureAwait(false);
            await AssertReleasedAsync(pair.RightBudget).ConfigureAwait(false);
        }

        /// <summary>
        /// AOT decoding has no fallback for legacy JSON or an unknown opcode.
        /// </summary>
        [Test]
        [Arguments("000000000A000000010000000063000000FF")]
        [Arguments("000000000B0000000100000000630000007B7D")]
        public async Task UnsupportedControlDoesNotCreateAnAotFlowAsync(string hex)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await using var pair = new ContextPair(DataChannelCarrierKind.SharedUasc);
            await pair.StartAsync(deadline.Token).ConfigureAwait(false);

            DataChannelResult result = await pair.Right.ReceiveUascAsync(
                (byte)'F', 0, ByteString.FromHexString(hex), deadline.Token).ConfigureAwait(false);

            await Assert.That(result).IsEqualTo(DataChannelResult.Unsupported);
            await Assert.That(pair.Right.IsReady).IsFalse();
            await Assert.That(pair.Right.Channels.IsEmpty).IsTrue();
            await AssertReleasedAsync(pair.RightBudget).ConfigureAwait(false);
        }

        /// <summary>
        /// Checks each logical resource dimension rather than comparing struct padding.
        /// </summary>
        private static async Task AssertReleasedAsync(DataChannelApplicationBudget budget)
        {
            DataChannelResourceUsage used = budget.Usage;
            await Assert.That(used.Contexts).IsEqualTo(0);
            await Assert.That(used.Flows).IsEqualTo(0);
            await Assert.That(used.ReservedBytes).IsEqualTo(0L);
            await Assert.That(used.BufferedBytes).IsEqualTo(0L);
            await Assert.That(used.Messages).IsEqualTo(0);
            await Assert.That(used.Operations).IsEqualTo(0);
            await Assert.That(used.Grants).IsEqualTo(0);
            await Assert.That(used.Probes).IsEqualTo(0);
            await Assert.That(used.Streams).IsEqualTo(0);
        }

        /// <summary>
        /// Connects a DI-created context to a directly constructed context through an in-process UASC seam.
        /// </summary>
        private sealed class ContextPair : IAsyncDisposable
        {
            /// <summary>
            /// Creates finite, independently budgeted peers without allocating a transport backend.
            /// </summary>
            internal ContextPair(DataChannelCarrierKind carrier)
            {
                var telemetry = new TestTelemetry();
                var authorizer = new AllowMessages();
                var options = new DataChannelOptions
                {
                    MaxMessageSize = 1024,
                    MaxReceiveBytes = 4096,
                    MaxContextBytes = 16384
                };
                var services = new ServiceCollection();
                services.AddSingleton<ITelemetryContext>(telemetry);
                services.AddSingleton<IDataChannelAuthorizer>(authorizer);
                services.AddSingleton(LeftBudget);
                services.AddOpcUa().AddDataChannels(options);
                m_services = services.BuildServiceProvider();
                LeftCarrier = new UascCarrier(carrier, true);
                RightCarrier = new UascCarrier(carrier, false);
                Left = m_services.GetRequiredService<IDataChannelContextFactory>().Create(LeftCarrier);
                Right = new DataChannelContext(
                    RightCarrier, authorizer, options, RightBudget, TimeProvider.System, telemetry);
                LeftCarrier.Peer = Right;
                RightCarrier.Peer = Left;
            }

            /// <summary>
            /// Gets the DI-owned application's logical admission budget.
            /// </summary>
            internal DataChannelApplicationBudget LeftBudget { get; } = new();

            /// <summary>
            /// Gets the directly constructed application's logical admission budget.
            /// </summary>
            internal DataChannelApplicationBudget RightBudget { get; } = new();

            /// <summary>
            /// Gets the transport initiator's context.
            /// </summary>
            internal DataChannelContext Left { get; }

            /// <summary>
            /// Gets the transport acceptor's context.
            /// </summary>
            internal DataChannelContext Right { get; }

            /// <summary>
            /// Gets the initiator's recorded common-wire writes.
            /// </summary>
            internal UascCarrier LeftCarrier { get; }

            /// <summary>
            /// Gets the acceptor's recorded common-wire writes.
            /// </summary>
            internal UascCarrier RightCarrier { get; }

            /// <summary>
            /// Confirms both contexts before a flow can be opened.
            /// </summary>
            internal async Task StartAsync(CancellationToken cancellationToken)
            {
                await Task.WhenAll(
                    Left.StartAsync(cancellationToken).AsTask(),
                    Right.StartAsync(cancellationToken).AsTask()).ConfigureAwait(false);
            }

            /// <inheritdoc/>
            public async ValueTask DisposeAsync()
            {
                if (Interlocked.Exchange(ref m_disposed, 1) == 0)
                {
                    await Task.WhenAll(Left.DisposeAsync().AsTask(), Right.DisposeAsync().AsTask())
                        .ConfigureAwait(false);
                    LeftCarrier.Dispose();
                    RightCarrier.Dispose();
                    await m_services.DisposeAsync().ConfigureAwait(false);
                }
            }

            private readonly ServiceProvider m_services;
            private int m_disposed;
        }

        /// <summary>
        /// Simulates only serialized verified-UASC plaintext delivery, never a native transport.
        /// </summary>
        private sealed class UascCarrier : IDataChannelCarrier, IDisposable
        {
            /// <summary>
            /// Supplies test-only identity evidence for one in-process peer.
            /// </summary>
            internal UascCarrier(DataChannelCarrierKind kind, bool initiator)
            {
                Kind = kind;
                ByteString first = new(Enumerable.Repeat((byte)1, 32).ToArray());
                ByteString second = new(Enumerable.Repeat((byte)2, 32).ToArray());
                Identity = new DataChannelPeerIdentity(
                    initiator ? "urn:aot:left" : "urn:aot:right",
                    initiator ? "urn:aot:right" : "urn:aot:left",
                    initiator ? first : second,
                    initiator ? second : first,
                    DataChannelProtectionMode.Confidential, initiator);
            }

            /// <inheritdoc/>
            public DataChannelCarrierKind Kind { get; }

            /// <inheritdoc/>
            public DataChannelPeerIdentity Identity { get; }

            /// <inheritdoc/>
            public int MaxRecordSize => 65536;

            /// <inheritdoc/>
            public int MaxDatagramSize => 0;

            /// <inheritdoc/>
            public ulong ManagementStreamId => 0;

            /// <summary>
            /// Gets or sets the other verified-plaintext endpoint before starting HELLO.
            /// </summary>
            internal DataChannelContext Peer { get; set; } = null!;

            /// <summary>
            /// Gets independent copies of submitted records for binary-layout assertions.
            /// </summary>
            internal ArrayOf<RecordedWrite> Writes => new(m_records.ToArray());

            /// <inheritdoc/>
            public ValueTask<ulong> OpenStreamAsync(CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException("A UASC flow must not allocate a native stream.");
            }

            /// <inheritdoc/>
            public ValueTask<bool> BindStreamAsync(
                ulong streamId, uint channelId, bool locallyOpened, CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException("A UASC flow must not bind a native stream.");
            }

            /// <inheritdoc/>
            public async ValueTask<DataChannelResult> SendAsync(
                DataChannelWrite write, CancellationToken cancellationToken = default)
            {
                await m_sending.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    if (!write.TryCommit())
                    {
                        return write.Result;
                    }
                    m_records.Enqueue(new RecordedWrite(write.ChannelId, write.RequestId, write.Payload.Copy()));
                    return await Peer.ReceiveUascAsync(
                        (byte)'F', write.RequestId, write.Payload, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                finally
                {
                    m_sending.Release();
                }
            }

            /// <inheritdoc/>
            public ValueTask CloseFlowAsync(
                ulong streamId, DataChannelResult result, CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException("A UASC flow must not close a native stream.");
            }

            /// <inheritdoc/>
            public ValueTask CloseAsync(DataChannelResult result, CancellationToken cancellationToken = default)
            {
                return default;
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                m_sending.Dispose();
            }

            private readonly ConcurrentQueue<RecordedWrite> m_records = new();
            private readonly SemaphoreSlim m_sending = new(1, 1);
        }

        /// <summary>
        /// Captures one submitted record without using the private codec for expected values.
        /// </summary>
        private sealed record RecordedWrite(uint ChannelId, uint RequestId, ByteString Payload);

        /// <summary>
        /// Admits the test resource through the same public authorization boundary as production providers.
        /// </summary>
        private sealed class AllowMessages : IDataChannelAuthorizer
        {
            /// <inheritdoc/>
            public ValueTask<DataChannelAuthorization> AuthorizeAsync(
                DataChannelAuthorizationRequest request, CancellationToken cancellationToken = default)
            {
                return new ValueTask<DataChannelAuthorization>(
                    new DataChannelAuthorization(DataChannelResult.Good, request.Parameters));
            }
        }

        /// <summary>
        /// Supplies allocation-free logging for the test's explicit runtime composition.
        /// </summary>
        private sealed class TestTelemetry() : TelemetryContextBase(NullLoggerFactory.Instance);
    }
}
