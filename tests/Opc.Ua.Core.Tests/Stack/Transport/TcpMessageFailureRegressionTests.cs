/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

#nullable enable

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Transport
{
    /// <summary>
    /// Verifies immediate sequence-failure reporting and well-formed secured abort messages with balanced buffers.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class TcpMessageFailureRegressionTests
    {
        /// <summary>
        /// Verifies that invalid response sequences fault a pending request promptly while the next valid sequence
        /// succeeds.
        /// </summary>
        [Test]
        public async Task ResponseSequenceFailureCompletesPendingRequestWithoutWaitingForTimeoutAsync(
            [Values(4u, 5u, 6u)] uint sequence)
        {
            var logger = new CaptureLogger();
            var factory = new Mock<ILoggerFactory>();
            factory.Setup(value => value.CreateLogger(It.IsAny<string>())).Returns(logger);
            var telemetry = new Mock<ITelemetryContext>();
            telemetry.SetupGet(value => value.LoggerFactory).Returns(factory.Object);
            var pool = new CountingPool();
            var context = ServiceMessageContext.Create(telemetry.Object);
            var buffers = new BufferManager("response-sequence", 65536, telemetry.Object, pool);
            using var channel = new ClientProbe(buffers, new ChannelQuotas(context), telemetry.Object);
            var sentRequest = new TaskCompletionSource<uint>(TaskCreationOptions.RunContinuationsAsynchronously);
            int closes = 0;
            var transport = new Mock<IUaSCByteTransport>();
            transport.Setup(value => value.Close()).Callback(() => Interlocked.Increment(ref closes));
            transport.Setup(value => value.SendChunkAsync(
                    It.IsAny<BufferCollection>(), It.IsAny<CancellationToken>()))
                .Returns((BufferCollection chunks, CancellationToken _) =>
                {
                    sentRequest.TrySetResult(BitConverter.ToUInt32(chunks[0].Array!, chunks[0].Offset + 20));
                    return default;
                });
            channel.OpenForTest(transport.Object);
            using var cancellation = new CancellationTokenSource();
            Task<IServiceResponse> pending = channel.SendRequestAsync(
                new ReadRequest { RequestHeader = new RequestHeader { RequestHandle = 19 } },
                60000, cancellation.Token).AsTask();
            try
            {
                uint requestId = await sentRequest.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                await channel.FeedAsync(BuildResponse(buffers, context, requestId, sequence)).ConfigureAwait(false);
                if (sequence == 6)
                {
                    IServiceResponse response = await pending.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    Assert.That(response, Is.TypeOf<ReadResponse>());
                    Assert.That(response.ResponseHeader.RequestHandle, Is.EqualTo(19));
                    Assert.That(closes, Is.Zero);
                    Assert.That(channel.CurrentState, Is.EqualTo(TcpChannelState.Open));
                }
                else
                {
                    ServiceResultException? failure = null;
                    try
                    {
                        await pending.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    }
                    catch (ServiceResultException error)
                    {
                        failure = error;
                    }
                    Assert.That(failure, Is.Not.Null);
                    Assert.That(failure!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
                    Assert.That(channel.CurrentState, Is.Not.EqualTo(TcpChannelState.Open));
                    Assert.That(closes, Is.GreaterThan(0));
                    Assert.That(logger.Messages, Has.Some.Contains("BadSequenceNumberInvalid"));
                }
            }
            finally
            {
                cancellation.Cancel();
                try
                {
                    await pending.ConfigureAwait(false);
                }
                catch (ServiceResultException)
                {
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                }
                channel.Dispose();
            }
            Assert.That(pool.Outstanding, Is.Zero);
            Assert.That(pool.Duplicates, Is.Zero);
        }

        /// <summary>
        /// Verifies that quota rejection encodes a valid secured abort even when the remaining payload is very short.
        /// </summary>
        [Test]
        public void ShortFinalPayloadStillEncodesAValidSecuredAbort(
            [Range(1, 7)] int tailLength,
            [Values(MessageSecurityMode.None, MessageSecurityMode.Sign, MessageSecurityMode.SignAndEncrypt)] MessageSecurityMode mode,
            [Values(false, true)] bool request)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var pool = new CountingPool();
            var buffers = new BufferManager("abort-tail", 8192, telemetry, pool);
            var context = ServiceMessageContext.Create(telemetry);
            using var channel = new SymmetricProbe(buffers, new ChannelQuotas(context), telemetry, mode);
            channel.LimitChunks = 1;
            byte[] payload = new byte[channel.PayloadCapacity + tailLength];
            BufferCollection? encoded = null;
            try
            {
                encoded = channel.Encode(new ArraySegment<byte>(payload), request, out bool exceeded);
                Assert.That(exceeded, Is.True);
                Assert.That(encoded, Has.Count.EqualTo(2));
                ArraySegment<byte> final = encoded[1];
                Assert.That(BitConverter.ToUInt32(final.Array!, final.Offset),
                    Is.EqualTo(TcpMessageType.Message | TcpMessageType.Abort));
                Assert.That(BitConverter.ToUInt32(final.Array!, final.Offset + 4), Is.EqualTo(final.Count));
                Assert.That(final, Has.Count.LessThanOrEqualTo(channel.BufferSize));
                ArraySegment<byte> body = channel.Decode(final, request, out uint requestId);
                Assert.That(requestId, Is.EqualTo(17));
                using var decoder = new BinaryDecoder(body, context);
                Assert.That(decoder.ReadUInt32(null),
                    Is.EqualTo((request ? StatusCodes.BadRequestTooLarge : StatusCodes.BadResponseTooLarge).Code));
                string? reason = decoder.ReadString(null);
                Assert.That(reason, Is.Null.Or.Empty);
                Assert.That(decoder.Position, Is.EqualTo(body.Count));
            }
            finally
            {
                encoded?.Release(buffers, "abort-tail");
            }
            Assert.That(pool.Outstanding, Is.Zero);
            Assert.That(pool.Duplicates, Is.Zero);
        }

        /// <summary>
        /// Verifies empty and exact-capacity payloads remain valid final messages rather than quota aborts.
        /// </summary>
        [Test]
        public void EmptyAndExactBoundaryPayloadsRemainValidWithoutExceedingLimits(
            [Values(false, true)] bool empty,
            [Values(MessageSecurityMode.None, MessageSecurityMode.Sign, MessageSecurityMode.SignAndEncrypt)] MessageSecurityMode mode)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var pool = new CountingPool();
            var buffers = new BufferManager("abort-control", 8192, telemetry, pool);
            using var channel = new SymmetricProbe(buffers, new ChannelQuotas(
                ServiceMessageContext.Create(telemetry)), telemetry, mode);
            channel.LimitChunks = 1;
            byte[] payload = new byte[empty ? 0 : channel.PayloadCapacity];
            BufferCollection encoded = channel.Encode(new ArraySegment<byte>(payload), false, out bool exceeded);
            try
            {
                Assert.That(exceeded, Is.False);
                Assert.That(encoded, Has.Count.EqualTo(1));
                Assert.That(channel.Decode(encoded[0], false, out uint requestId).AsSpan().ToArray(), Is.EqualTo(payload));
                Assert.That(requestId, Is.EqualTo(17));
            }
            finally
            {
                encoded.Release(buffers, "abort-control");
            }
            Assert.That(pool.Outstanding, Is.Zero);
            Assert.That(pool.Duplicates, Is.Zero);
        }

        /// <summary>
        /// Encodes a pooled read-response chunk with controlled request and sequence identifiers.
        /// </summary>
        private static ArraySegment<byte> BuildResponse(
            BufferManager buffers, IServiceMessageContext context, uint requestId, uint sequence)
        {
            using var body = new MemoryStream();
            BinaryEncoder.EncodeMessage(new ReadResponse
            {
                ResponseHeader = new ResponseHeader { RequestHandle = 19 },
                Results = [new DataValue(new Variant(123))]
            }, body, context, true);
            byte[] buffer = buffers.TakeBuffer(8192, "response-sequence");
            using var encoder = new BinaryEncoder(buffer, 0, 8192, context);
            encoder.WriteUInt32(null, TcpMessageType.Message | TcpMessageType.Final);
            encoder.WriteUInt32(null, (uint)(body.Length + 24));
            encoder.WriteUInt32(null, 1);
            encoder.WriteUInt32(null, 1);
            encoder.WriteUInt32(null, sequence);
            encoder.WriteUInt32(null, requestId);
            encoder.WriteRawBytes(body.GetBuffer(), 0, (int)body.Length);
            return new ArraySegment<byte>(buffer, 0, encoder.Close());
        }

        /// <summary>
        /// Exposes an initialized client receive path without reconnect scheduling or a real network.
        /// </summary>
        private sealed class ClientProbe : UaSCUaBinaryClientChannel
        {
            /// <summary>
            /// Creates an unsecured client channel with injectable buffers and a deterministic clock.
            /// </summary>
            public ClientProbe(BufferManager buffers, ChannelQuotas quotas, ITelemetryContext telemetry)
                : base("response-sequence", buffers, Mock.Of<IUaSCByteTransportFactory>(), quotas,
                    null, null, null, new EndpointDescription
                    {
                        EndpointUrl = "opc.tcp://localhost:4840",
                        SecurityMode = MessageSecurityMode.None,
                        SecurityPolicyUri = SecurityPolicies.None
                    }, telemetry, new FakeTimeProvider())
            {
            }

            /// <summary>
            /// Gets the transport state after response processing.
            /// </summary>
            public TcpChannelState CurrentState => State;

            /// <summary>
            /// Installs a token and fake transport and seeds the previously accepted sequence number.
            /// </summary>
            public void OpenForTest(IUaSCByteTransport transport)
            {
                // Successful OpenSecureChannel leaves reconnect scheduling to its transport owner.
                typeof(UaSCUaBinaryClientChannel).GetField(
                    "m_waitBetweenReconnects", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(this, Timeout.Infinite);
                ChannelId = 1;
                ChannelToken token = CreateToken();
                token.TokenId = 1;
                ActivateToken(token);
                Transport = transport;
                State = TcpChannelState.Open;
                Assert.That(VerifySequenceNumber(5, "seed"), Is.True);
            }

            /// <summary>
            /// Delivers a pooled response chunk to the real channel receive path.
            /// </summary>
            public ValueTask FeedAsync(ArraySegment<byte> message)
            {
                return OnChunkReceivedAsync(message, CancellationToken.None);
            }
        }

        /// <summary>
        /// Exposes symmetric message encoding and decoding under configurable security and chunk limits.
        /// </summary>
        private sealed class SymmetricProbe : UaSCUaBinaryChannel
        {
            /// <summary>
            /// Installs a token and deterministic key material for the selected message security mode.
            /// </summary>
            public SymmetricProbe(
                BufferManager buffers, ChannelQuotas quotas, ITelemetryContext telemetry, MessageSecurityMode mode)
                : base("abort", buffers, quotas, (Certificate?)null, null, mode,
                    mode == MessageSecurityMode.None ? SecurityPolicies.None : SecurityPolicies.Basic256Sha256,
                    telemetry, new FakeTimeProvider())
            {
                ChannelId = 1;
                SendBufferSize = BufferManager.GetSuggestedBufferSize(8192);
                ChannelToken token = CreateToken();
                token.TokenId = 1;
                token.ClientNonce = new byte[32];
                token.ServerNonce = new byte[32];
                token.ServerNonce[0] = 1;
                ActivateToken(token);
            }

            /// <summary>
            /// Gets the maximum size of one encoded chunk.
            /// </summary>
            public int BufferSize => SendBufferSize;

            /// <summary>
            /// Gets the payload capacity after accounting for the sequence header, signature, and padding.
            /// </summary>
            public int PayloadCapacity
            {
                get
                {
                    int block = SecurityMode == MessageSecurityMode.None ? 1 : 16;
                    int signature = SecurityMode == MessageSecurityMode.None ? 0 : 32;
                    int padding = SecurityMode == MessageSecurityMode.SignAndEncrypt ? 1 : 0;
                    return ((SendBufferSize - 16) / block * block) - signature - 8 - padding;
                }
            }

            /// <summary>
            /// Sets matching request and response chunk-count limits.
            /// </summary>
            public int LimitChunks
            {
                set
                {
                    MaxRequestChunkCount = value;
                    MaxResponseChunkCount = value;
                }
            }

            /// <summary>
            /// Encodes a request or response using the fixed request identifier and current security token.
            /// </summary>
            public BufferCollection Encode(ArraySegment<byte> payload, bool request, out bool exceeded)
            {
                BufferCollection chunks = WriteSymmetricMessage(
                    TcpMessageType.Message, 17, CurrentToken!, payload, request, out exceeded,
                    out SendGateTicket sendTicket);
                ReleaseSendTicket(sendTicket);
                return chunks;
            }

            /// <summary>
            /// Verifies and decodes the secured chunk to expose its payload and request identifier.
            /// </summary>
            public ArraySegment<byte> Decode(ArraySegment<byte> chunk, bool request, out uint requestId)
            {
                return ReadSymmetricMessage(chunk, request, out _, out requestId, out _);
            }
        }

        /// <summary>
        /// Captures formatted channel diagnostics for exact security-failure assertions.
        /// </summary>
        private sealed class CaptureLogger : ILogger
        {
            /// <summary>
            /// Gets messages in the order they were recorded.
            /// </summary>
            public ConcurrentQueue<string> Messages { get; } = new();

            /// <inheritdoc/>
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            {
                return null;
            }

            /// <inheritdoc/>
            public bool IsEnabled(LogLevel logLevel)
            {
                return true;
            }

            /// <inheritdoc/>
            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                Messages.Enqueue(formatter(state, exception));
            }
        }

        /// <summary>
        /// Tracks outstanding pooled arrays and duplicate returns across message failure paths.
        /// </summary>
        private sealed class CountingPool : ArrayPool<byte>
        {
            /// <summary>
            /// Gets the number of rentals not yet returned.
            /// </summary>
            public int Outstanding => m_owned.Count;

            /// <summary>
            /// Gets the number of arrays returned without a matching outstanding rental.
            /// </summary>
            public int Duplicates { get; private set; }

            /// <summary>
            /// Allocates and records an independently owned array for each rental.
            /// </summary>
            public override byte[] Rent(int minimumLength)
            {
                byte[] result = new byte[minimumLength];
                m_owned.TryAdd(result, 0);
                return result;
            }

            /// <summary>
            /// Completes a rental or records a duplicate return.
            /// </summary>
            public override void Return(byte[] array, bool clearArray = false)
            {
                if (!m_owned.TryRemove(array, out _))
                {
                    Duplicates++;
                }
            }

            /// <summary>
            /// Records arrays whose ownership has not yet been released.
            /// </summary>
            private readonly ConcurrentDictionary<byte[], byte> m_owned = new();
        }
    }
}
