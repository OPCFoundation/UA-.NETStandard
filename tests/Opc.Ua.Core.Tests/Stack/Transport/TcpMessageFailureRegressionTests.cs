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
    [TestFixture]
    [NonParallelizable]
    public sealed class TcpMessageFailureRegressionTests
    {
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

        private sealed class ClientProbe : UaSCUaBinaryClientChannel
        {
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

            public TcpChannelState CurrentState => State;

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

            public ValueTask FeedAsync(ArraySegment<byte> message)
            {
                return OnChunkReceivedAsync(message, CancellationToken.None);
            }
        }

        private sealed class SymmetricProbe : UaSCUaBinaryChannel
        {
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

            public int BufferSize => SendBufferSize;

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

            public int LimitChunks
            {
                set
                {
                    MaxRequestChunkCount = value;
                    MaxResponseChunkCount = value;
                }
            }

            public BufferCollection Encode(ArraySegment<byte> payload, bool request, out bool exceeded)
            {
                return WriteSymmetricMessage(
                    TcpMessageType.Message, 17, CurrentToken!, payload, request, out exceeded);
            }

            public ArraySegment<byte> Decode(ArraySegment<byte> chunk, bool request, out uint requestId)
            {
                return ReadSymmetricMessage(chunk, request, out _, out requestId, out _);
            }
        }

        private sealed class CaptureLogger : ILogger
        {
            public ConcurrentQueue<string> Messages { get; } = new();

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            {
                return null;
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return true;
            }

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                Messages.Enqueue(formatter(state, exception));
            }
        }

        private sealed class CountingPool : ArrayPool<byte>
        {
            public int Outstanding => m_owned.Count;
            public int Duplicates { get; private set; }

            public override byte[] Rent(int minimumLength)
            {
                byte[] result = new byte[minimumLength];
                m_owned.TryAdd(result, 0);
                return result;
            }

            public override void Return(byte[] array, bool clearArray = false)
            {
                if (!m_owned.TryRemove(array, out _))
                {
                    Duplicates++;
                }
            }

            private readonly ConcurrentDictionary<byte[], byte> m_owned = new();
        }
    }
}
