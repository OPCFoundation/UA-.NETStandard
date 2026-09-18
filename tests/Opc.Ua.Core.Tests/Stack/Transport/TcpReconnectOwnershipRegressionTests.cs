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
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Transport
{
    /// <summary>
    /// Verifies transport ownership transfer and receive-loop generation isolation during TCP reconnect.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class TcpReconnectOwnershipRegressionTests
    {
        /// <summary>
        /// Verifies that disposing a temporary channel cannot close a transport adopted by the renewed channel.
        /// </summary>
        [Test]
        public async Task RenewHandoffDoesNotCloseTheAdoptedTransportAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var context = ServiceMessageContext.Create(telemetry);
            var buffers = new BufferManager("reconnect-handoff", 65536, telemetry);
            var quotas = new ChannelQuotas(context);
            var listener = new Mock<ITcpChannelListener>();
            listener.SetupGet(value => value.EndpointUrl).Returns(new Uri("opc.tcp://localhost:4840"));
            int closes = 0;
            var sent = new ConcurrentQueue<byte[]>();
            var sentSignal = new SemaphoreSlim(0);
            var transport = new Mock<IUaSCByteTransport>();
            transport.Setup(value => value.Close()).Callback(() => Interlocked.Increment(ref closes));
            transport.Setup(value => value.SendChunkAsync(It.IsAny<BufferCollection>(), It.IsAny<CancellationToken>()))
                .Returns((BufferCollection chunks, CancellationToken _) =>
                {
                    sent.Enqueue(Flatten(chunks));
                    sentSignal.Release();
                    return default;
                });
            using var target = new HandoffChannel(listener.Object, buffers, quotas, telemetry);
            using var temporary = new HandoffChannel(listener.Object, buffers, quotas, telemetry);
            target.Attach(1, Mock.Of<IUaSCByteTransport>());
            target.CurrentState = TcpChannelState.Faulted;
            temporary.Attach(2, transport.Object);
            temporary.CurrentState = TcpChannelState.Opening;
            listener.Setup(value => value.ReconnectToExistingChannel(
                    temporary, transport.Object, 77, 5, 1, It.IsAny<Certificate>(),
                    It.IsAny<ChannelToken>(), It.IsAny<OpenSecureChannelRequest>()))
                .Callback<TcpListenerChannel, IUaSCByteTransport, uint, uint, uint, Certificate, ChannelToken, OpenSecureChannelRequest>(
                    (_, adopted, requestId, sequence, _, certificate, token, request) =>
                        target.Reconnect(adopted, requestId, sequence, certificate, token, request))
                .Returns(true);
            listener.Setup(value => value.ChannelClosed(2)).Callback(temporary.Dispose);
            try
            {
                await temporary.FeedAsync(CreateRenewChunk(buffers, context)).ConfigureAwait(false);
                listener.Verify(value => value.ChannelClosed(2), Times.Once);
                Assert.That(closes, Is.Zero, "The temporary channel must relinquish the adopted transport.");
                Assert.That(target.CurrentState, Is.EqualTo(TcpChannelState.Open));
                Assert.That(await sentSignal.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false), Is.True);
                Assert.That(sent.TryDequeue(out byte[]? response), Is.True);
                using (var decoder = new BinaryDecoder(response!, context))
                {
                    Assert.That(decoder.ReadUInt32(null), Is.EqualTo(TcpMessageType.Open | TcpMessageType.Final));
                    Assert.That(decoder.ReadUInt32(null), Is.EqualTo(response!.Length));
                    Assert.That(decoder.ReadUInt32(null), Is.EqualTo(1));
                    Assert.That(decoder.ReadString(null), Is.EqualTo(SecurityPolicies.None));
                    _ = decoder.ReadByteString(null);
                    _ = decoder.ReadByteString(null);
                    _ = decoder.ReadUInt32(null);
                    Assert.That(decoder.ReadUInt32(null), Is.EqualTo(77));
                    OpenSecureChannelResponse opened = decoder.DecodeMessage<OpenSecureChannelResponse>();
                    Assert.That(opened.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
                    Assert.That(opened.SecurityToken.ChannelId, Is.EqualTo(1));
                }
                Assert.That(
                    target.SendResponse(78, new ReadResponse { Results = [] }),
                    Is.False,
                    "A response sent immediately must not be retained for reconnect.");
                Assert.That(await sentSignal.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false), Is.True);
                Assert.That(sent.TryDequeue(out byte[]? service), Is.True);
                Assert.That(BitConverter.ToUInt32(service!, 0), Is.EqualTo(TcpMessageType.Message | TcpMessageType.Final));
                Assert.That(closes, Is.Zero);
            }
            finally
            {
                target.Dispose();
                sentSignal.Dispose();
            }
        }

        /// <summary>
        /// Verifies a replacement receive loop starts immediately and ignores the previous transport's late failure.
        /// </summary>
        [Test]
        public async Task NewReceiveLoopRunsBeforeOldLoopExitsAndIgnoresItsLateFailureAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var pool = new CountingPool();
            var buffers = new BufferManager("reconnect-loops", 65536, telemetry, pool);
            using var channel = new ReceiveChannel(buffers, new ChannelQuotas(
                ServiceMessageContext.Create(telemetry)), telemetry);
            var oldEntered = Signal();
            var oldRelease = new TaskCompletionSource<ArraySegment<byte>>(TaskCreationOptions.RunContinuationsAsynchronously);
            var newEntered = Signal();
            var newChunk = new TaskCompletionSource<ArraySegment<byte>>(TaskCreationOptions.RunContinuationsAsynchronously);
            int newReceives = 0;
            var oldTransport = new Mock<IUaSCByteTransport>();
            oldTransport.Setup(value => value.ReceiveChunkAsync(It.IsAny<CancellationToken>()))
                .Returns((CancellationToken _) =>
                {
                    oldEntered.TrySetResult(true);
                    return new ValueTask<ArraySegment<byte>>(oldRelease.Task);
                });
            var current = new Mock<IUaSCByteTransport>();
            current.Setup(value => value.ReceiveChunkAsync(It.IsAny<CancellationToken>()))
                .Returns((CancellationToken ct) =>
                {
                    Interlocked.Increment(ref newReceives);
                    newEntered.TrySetResult(true);
                    return new ValueTask<ArraySegment<byte>>(newChunk.Task.WaitAsync(ct));
                });
            try
            {
                channel.Install(oldTransport.Object);
                await oldEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                channel.Install(current.Object);
                await newEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                oldRelease.TrySetException(new ServiceResultException(StatusCodes.BadConnectionClosed));
                byte[] bytes = buffers.TakeBuffer(8, "new-generation");
                BitConverter.GetBytes(TcpMessageType.Message | TcpMessageType.Final).CopyTo(bytes, 0);
                BitConverter.GetBytes(8).CopyTo(bytes, 4);
                newChunk.TrySetResult(new ArraySegment<byte>(bytes, 0, 8));
                await channel.Received.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                await channel.DrainOldLoopAsync().ConfigureAwait(false);
                Assert.That(channel.Errors, Is.Zero);
                Assert.That(pool.Returns, Is.EqualTo(1));
                Assert.That(pool.Duplicates, Is.Zero);
                Assert.That(newReceives, Is.EqualTo(1));
            }
            finally
            {
                oldRelease.TrySetCanceled();
                newChunk.TrySetCanceled();
            }
        }

        /// <summary>
        /// Creates an asynchronous barrier that never runs test continuations inline.
        /// </summary>
        private static TaskCompletionSource<bool> Signal()
        {
            return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        /// <summary>
        /// Copies outgoing chunks into a single stable message for decoding after transport dispatch.
        /// </summary>
        private static byte[] Flatten(BufferCollection chunks)
        {
            using var stream = new MemoryStream();
            foreach (ArraySegment<byte> chunk in chunks)
            {
                stream.Write(chunk.Array!, chunk.Offset, chunk.Count);
            }
            return stream.ToArray();
        }

        /// <summary>
        /// Creates a pooled unsecured OpenSecureChannel renewal targeting the existing channel.
        /// </summary>
        private static ArraySegment<byte> CreateRenewChunk(BufferManager buffers, IServiceMessageContext context)
        {
            using var body = new MemoryStream();
            BinaryEncoder.EncodeMessage(new OpenSecureChannelRequest
            {
                RequestHeader = new RequestHeader { RequestHandle = 77 },
                RequestType = SecurityTokenRequestType.Renew,
                SecurityMode = MessageSecurityMode.None,
                RequestedLifetime = 60000
            }, body, context, true);
            byte[] buffer = buffers.TakeBuffer(8192, "renew-handoff");
            using var encoder = new BinaryEncoder(buffer, 0, 8192, context);
            encoder.WriteUInt32(null, TcpMessageType.Open | TcpMessageType.Final);
            encoder.WriteUInt32(null, 0);
            encoder.WriteUInt32(null, 1);
            encoder.WriteString(null, SecurityPolicies.None);
            encoder.WriteByteString(null, ByteString.Empty);
            encoder.WriteByteString(null, ByteString.Empty);
            encoder.WriteUInt32(null, 5);
            encoder.WriteUInt32(null, 77);
            encoder.WriteRawBytes(body.GetBuffer(), 0, (int)body.Length);
            int size = encoder.Close();
            BitConverter.GetBytes(size).CopyTo(buffer, 4);
            return new ArraySegment<byte>(buffer, 0, size);
        }

        /// <summary>
        /// Exposes server-channel renewal without starting a real receive loop.
        /// </summary>
        private sealed class HandoffChannel : TcpServerChannel
        {
            /// <summary>
            /// Creates a server channel for controlled ownership transfer between listener entries.
            /// </summary>
            public HandoffChannel(
                ITcpChannelListener listener, BufferManager buffers, ChannelQuotas quotas, ITelemetryContext telemetry)
                : base("handoff", listener, buffers, quotas, null!, [
                    new EndpointDescription
                    {
                        EndpointUrl = "opc.tcp://localhost:4840",
                        SecurityMode = MessageSecurityMode.None,
                        SecurityPolicyUri = SecurityPolicies.None,
                        TransportProfileUri = Profiles.UaTcpTransport
                    }
                ], telemetry, new FakeTimeProvider())
            {
            }

            /// <summary>
            /// Gets or sets the channel state used to reproduce the reconnect handoff.
            /// </summary>
            public TcpChannelState CurrentState
            {
                get => State;
                set => State = value;
            }

            /// <summary>
            /// Delivers the renewal chunk through the real server receive path.
            /// </summary>
            public ValueTask FeedAsync(ArraySegment<byte> chunk)
            {
                return OnChunkReceivedAsync(chunk, CancellationToken.None);
            }

            /// <summary>
            /// Suppresses network reads so the test controls every incoming chunk.
            /// </summary>
            protected internal override void StartReceiveLoop()
            {
            }
        }

        /// <summary>
        /// Observes receive-loop replacement while an earlier transport remains blocked.
        /// </summary>
        private sealed class ReceiveChannel : UaSCUaBinaryChannel
        {
            /// <summary>
            /// Creates a channel whose transports and pooled input are supplied by the test.
            /// </summary>
            public ReceiveChannel(BufferManager buffers, ChannelQuotas quotas, ITelemetryContext telemetry)
                : base("loops", buffers, quotas, (Certificate?)null, null,
                    MessageSecurityMode.None, SecurityPolicies.None, telemetry)
            {
            }

            /// <summary>
            /// Signals receipt of a chunk by the replacement loop.
            /// </summary>
            public TaskCompletionSource<bool> Received { get; } = Signal();

            /// <summary>
            /// Gets transport errors delivered to the current channel generation.
            /// </summary>
            public int Errors { get; private set; }

            /// <summary>
            /// Installs a transport and immediately starts its receive generation.
            /// </summary>
            public void Install(IUaSCByteTransport transport)
            {
                Transport = transport;
                StartReceiveLoop();
            }

            /// <summary>
            /// Joins tracked receive work so late errors and buffer returns are observable before assertions.
            /// </summary>
            public async Task DrainOldLoopAsync()
            {
                await BackgroundWork.DisposeAsync().ConfigureAwait(false);
            }

            /// <summary>
            /// Processes one chunk and holds the current loop until it is canceled.
            /// </summary>
            protected override async ValueTask OnChunkReceivedAsync(ArraySegment<byte> message, CancellationToken ct)
            {
                await base.OnChunkReceivedAsync(message, ct).ConfigureAwait(false);
                Received.TrySetResult(true);
                await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            }

            /// <summary>
            /// Records current-generation transport failures and closes the installed transport.
            /// </summary>
            protected override void OnTransportError(ServiceResult result)
            {
                Errors++;
                Transport?.Close();
            }
        }

        /// <summary>
        /// Counts receive-buffer returns and detects duplicate ownership release.
        /// </summary>
        private sealed class CountingPool : ArrayPool<byte>
        {
            /// <summary>
            /// Gets the total number of return attempts.
            /// </summary>
            public int Returns => Volatile.Read(ref m_returns);

            /// <summary>
            /// Gets return attempts for arrays that were no longer owned.
            /// </summary>
            public int Duplicates => Volatile.Read(ref m_duplicates);

            /// <summary>
            /// Allocates a fresh array and records its outstanding ownership.
            /// </summary>
            public override byte[] Rent(int minimumLength)
            {
                byte[] buffer = new byte[minimumLength];
                m_owned.TryAdd(buffer, 0);
                return buffer;
            }

            /// <summary>
            /// Releases the rental and counts duplicate release attempts.
            /// </summary>
            public override void Return(byte[] array, bool clearArray = false)
            {
                if (!m_owned.TryRemove(array, out _))
                {
                    Interlocked.Increment(ref m_duplicates);
                }
                Interlocked.Increment(ref m_returns);
            }

            /// <summary>
            /// Tracks outstanding receive-buffer rentals by array identity.
            /// </summary>
            private readonly ConcurrentDictionary<byte[], byte> m_owned = new();

            /// <summary>
            /// Counts all buffer-return calls.
            /// </summary>
            private int m_returns;

            /// <summary>
            /// Counts buffer-return calls without a matching rental.
            /// </summary>
            private int m_duplicates;
        }
    }
}
