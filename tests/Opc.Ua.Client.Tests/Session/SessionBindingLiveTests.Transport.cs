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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Bindings;

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// Contains tests for session binding live.
    /// </summary>
    public sealed partial class SessionBindingLiveTests
    {
        /// <summary>
        /// Verifies that bound request cannot dispatch while native replacement is pending.
        /// </summary>
        [Test]
        public async Task BoundRequestCannotDispatchWhileNativeReplacementIsPendingAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var channels = new BarrierChannelBindings();
            await WithSessionAsync(async session =>
            {
                Assert.That(session.TransportChannel, Is.InstanceOf<IManagedTransportChannel>());
                ISessionClient binding = await ((ISessionBindingProvider)session).CreateBindingAsync(timeout.Token)
                    .ConfigureAwait(false);
                AsyncBarrier barrier = channels.PauseNextConnect();
                Task reconnect = session.TransportChannel.ReconnectAsync(ct: timeout.Token).AsTask();
                try
                {
                    await barrier.Entered.WaitAsync(timeout.Token).ConfigureAwait(false);
                    await Assert.ThatAsync(() => binding.ReadValueAsync(
                            VariableIds.Server_ServerStatus_State, timeout.Token),
                        Throws.TypeOf<ServiceResultException>()
                            .With.Property(nameof(ServiceResultException.StatusCode))
                            .EqualTo(StatusCodes.BadSecurityChecksFailed)).ConfigureAwait(false);
                    Assert.That(reconnect.IsCompleted, Is.False);
                }
                finally
                {
                    barrier.Release();
                    await reconnect.ConfigureAwait(false);
                    await binding.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                    binding.Dispose();
                }
            }, timeout.Token, channels).ConfigureAwait(false);
        }

        /// <summary>
        /// Verifies that bound response rejects invalidation after native dispatch.
        /// </summary>
        [TestCase("incarnation")]
        [TestCase("namespace")]
        [TestCase("dispose")]
        [TestCase("cancel")]
        public async Task BoundResponseRejectsInvalidationAfterNativeDispatchAsync(string change)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var channels = new BarrierChannelBindings();
            await WithSessionAsync(async session =>
            {
                ISessionClient binding = await ((ISessionBindingProvider)session).CreateBindingAsync(timeout.Token)
                    .ConfigureAwait(false);
                using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
                AsyncBarrier barrier = channels.PauseNextResponse();
                Task<DataValue> request = binding.ReadValueAsync(
                    VariableIds.Server_ServerStatus_State, requestCancellation.Token);
                try
                {
                    await barrier.Entered.WaitAsync(timeout.Token).ConfigureAwait(false);
                    Assert.That(request.IsCompleted, Is.False, "The actual response has not been released.");
                    switch (change)
                    {
                        case "incarnation":
                            Session native = session is Client.ManagedSession managed
                                ? managed.InnerSession
                                : (Session)session;
                            SessionConfiguration configuration = native.SaveSessionConfiguration();
                            native.SessionCreated(configuration.SessionId, configuration.AuthenticationToken);
                            break;
                        case "namespace":
                            session.NamespaceUris.Append("urn:session-binding:in-flight");
                            break;
                        case "dispose":
                            await binding.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                            break;
                        case "cancel":
                            await requestCancellation.CancelAsync().ConfigureAwait(false);
                            break;
                        default:
                            Assert.Fail("Unknown in-flight mutation.");
                            break;
                    }
                    barrier.Release();
                    if (change == "cancel")
                    {
                        await Assert.ThatAsync(() => request,
                            Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
                        DataValue owner = await session.ReadValueAsync(
                            VariableIds.Server_ServerStatus_State, timeout.Token).ConfigureAwait(false);
                        Assert.That(owner.StatusCode, Is.EqualTo(StatusCodes.Good));
                    }
                    else
                    {
                        await Assert.ThatAsync(() => request,
                            Throws.TypeOf<ServiceResultException>()
                                .With.Property(nameof(ServiceResultException.StatusCode))
                                .EqualTo(StatusCodes.BadSecurityChecksFailed)).ConfigureAwait(false);
                    }
                }
                finally
                {
                    barrier.Release();
                    await binding.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                    binding.Dispose();
                }
            }, timeout.Token, channels).ConfigureAwait(false);
        }

        /// <summary>
        /// Verifies that bound client cannot retry across faulted managed entry replacement.
        /// </summary>
        [Test]
        public async Task BoundClientCannotRetryAcrossFaultedManagedEntryReplacementAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var channels = new BarrierChannelBindings();
            await WithSessionAsync(async session =>
            {
                var lease = (IManagedTransportChannel)session.TransportChannel;
                ISessionClient binding = await ((ISessionBindingProvider)session).CreateBindingAsync(timeout.Token)
                    .ConfigureAwait(false);
                try
                {
                    channels.FailConnect = true;
                    var budget = new RetryBudget(TimeSpan.FromSeconds(10), TimeProvider.System);
                    var manager = (ClientChannelManager)lease.Manager;
                    await Assert.ThatAsync(async () => await manager.ReconnectAsync(lease, budget, timeout.Token)
                            .ConfigureAwait(false),
                        Throws.TypeOf<ServiceResultException>()).ConfigureAwait(false);
                    Assert.That(lease.State, Is.EqualTo(ChannelState.Faulted));
                    channels.FailConnect = false;
                    await lease.Manager.ReconnectAsync(lease, timeout.Token).ConfigureAwait(false);
                    Assert.That(lease.State, Is.EqualTo(ChannelState.Ready));
                    int connections = channels.Connections;
                    await Assert.ThatAsync(() => binding.ReadValueAsync(
                            VariableIds.Server_ServerStatus_State, timeout.Token),
                        Throws.TypeOf<ServiceResultException>()
                            .With.Property(nameof(ServiceResultException.StatusCode))
                            .EqualTo(StatusCodes.BadSecurityChecksFailed)).ConfigureAwait(false);
                    Assert.That(channels.Connections, Is.EqualTo(connections),
                        "A bound request must not start another managed reconnect/retry.");
                }
                finally
                {
                    channels.FailConnect = false;
                    await binding.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                    binding.Dispose();
                }
            }, timeout.Token, channels).ConfigureAwait(false);
        }

        private sealed class BarrierChannelBindings : ITransportChannelBindings, IUaSCByteTransportFactory
        {
            /// <summary>
            /// Gets the implementation for <c>BarrierChannelBindings</c>.
            /// </summary>
            public string Implementation => "UA-TCP-Binding-Test";

            /// <summary>
            /// Gets or sets the fail connect for <c>BarrierChannelBindings</c>.
            /// </summary>
            public bool FailConnect { get; set; }

            public int Connections => Volatile.Read(ref m_connections);

            /// <inheritdoc/>
            /// <param name="uriScheme">The URI scheme.</param>
            /// <param name="telemetry">The telemetry context.</param>
            public ITransportChannel? Create(string uriScheme, ITelemetryContext telemetry)
            {
                return uriScheme == Utils.UriSchemeOpcTcp
                    ? new UaSCUaBinaryTransportChannel(this, telemetry)
                    : null;
            }

            /// <inheritdoc/>
            /// <param name="bufferManager">The buffer manager.</param>
            /// <param name="receiveBufferSize">The receive buffer size.</param>
            /// <param name="telemetry">The telemetry context.</param>
            public IUaSCByteTransport Create(
                BufferManager bufferManager,
                int receiveBufferSize,
                ITelemetryContext telemetry)
            {
                return new BarrierByteTransport(
                    new TcpByteTransport(bufferManager, receiveBufferSize, telemetry),
                    bufferManager, this);
            }

            public AsyncBarrier PauseNextConnect()
            {
                var barrier = new AsyncBarrier();
                Interlocked.Exchange(ref m_connect, barrier);
                return barrier;
            }

            public AsyncBarrier PauseNextResponse()
            {
                var barrier = new AsyncBarrier();
                Interlocked.Exchange(ref m_response, barrier);
                return barrier;
            }

            public AsyncBarrier? TakeConnect()
            {
                Interlocked.Increment(ref m_connections);
                if (FailConnect)
                {
                    throw new ServiceResultException(StatusCodes.BadConnectionRejected, "Injected connect failure.");
                }
                return Interlocked.Exchange(ref m_connect, null);
            }

            public AsyncBarrier? TakeResponse()
            {
                return Interlocked.Exchange(ref m_response, null);
            }

            private AsyncBarrier? m_connect;
            private AsyncBarrier? m_response;
            private int m_connections;
        }

        private sealed class BarrierByteTransport : IUaSCByteTransport
        {
            /// <summary>
            /// Initializes a new <c>BarrierByteTransport</c> instance.
            /// </summary>
            /// <param name="inner">The UA sc byte transport used to initialize the instance.</param>
            /// <param name="buffers">The buffers used to initialize the instance.</param>
            /// <param name="owner">The barrier channel bindings used to initialize the instance.</param>
            public BarrierByteTransport(
                IUaSCByteTransport inner,
                BufferManager buffers,
                BarrierChannelBindings owner)
            {
                m_inner = inner;
                m_buffers = buffers;
                m_owner = owner;
            }

            /// <summary>
            /// Gets the local endpoint for <c>BarrierByteTransport</c>.
            /// </summary>
            public EndPoint? LocalEndpoint => m_inner.LocalEndpoint;

            /// <summary>
            /// Gets the remote endpoint for <c>BarrierByteTransport</c>.
            /// </summary>
            public EndPoint? RemoteEndpoint => m_inner.RemoteEndpoint;

            /// <summary>
            /// Gets the features for <c>BarrierByteTransport</c>.
            /// </summary>
            public TransportChannelFeatures Features => m_inner.Features;

            /// <summary>
            /// Gets the implementation for <c>BarrierByteTransport</c>.
            /// </summary>
            public string Implementation => m_inner.Implementation;

            /// <inheritdoc/>
            /// <param name="url">The connection endpoint.</param>
            /// <param name="ct">The cancellation token supplied by the caller.</param>
            public async ValueTask ConnectAsync(Uri url, CancellationToken ct)
            {
                AsyncBarrier? barrier = m_owner.TakeConnect();
                if (barrier is not null)
                {
                    await barrier.WaitAsync(ct).ConfigureAwait(false);
                }
                await m_inner.ConnectAsync(url, ct).ConfigureAwait(false);
            }

            /// <summary>
            /// Delegates <c>SendChunkAsync</c> to the <c>IUaSCByteTransport</c> test collaborator.
            /// </summary>
            /// <param name="chunk">The encoded chunk to send.</param>
            /// <param name="ct">The cancellation token supplied by the caller.</param>
            public ValueTask SendChunkAsync(ReadOnlyMemory<byte> chunk, CancellationToken ct)
            {
                return m_inner.SendChunkAsync(chunk, ct);
            }

            /// <summary>
            /// Delegates <c>SendChunkAsync</c> to the <c>IUaSCByteTransport</c> test collaborator.
            /// </summary>
            /// <param name="buffers">The buffer collection.</param>
            /// <param name="ct">The cancellation token supplied by the caller.</param>
            public ValueTask SendChunkAsync(BufferCollection buffers, CancellationToken ct)
            {
                return m_inner.SendChunkAsync(buffers, ct);
            }

            /// <inheritdoc/>
            /// <param name="ct">The cancellation token supplied by the caller.</param>
            public async ValueTask<ArraySegment<byte>> ReceiveChunkAsync(CancellationToken ct)
            {
                ArraySegment<byte> chunk = await m_inner.ReceiveChunkAsync(ct).ConfigureAwait(false);
                if (chunk.Count >= 3 &&
                    chunk.Array![chunk.Offset] == (byte)'M' &&
                    chunk.Array[chunk.Offset + 1] == (byte)'S' &&
                    chunk.Array[chunk.Offset + 2] == (byte)'G')
                {
                    AsyncBarrier? barrier = m_owner.TakeResponse();
                    if (barrier is not null)
                    {
                        try
                        {
                            await barrier.WaitAsync(ct).ConfigureAwait(false);
                        }
                        catch
                        {
                            m_buffers.ReturnBuffer(chunk.Array, "BindingBarrier");
                            throw;
                        }
                    }
                }
                return chunk;
            }

            public void Close()
            {
                m_inner.Close();
            }

            private readonly IUaSCByteTransport m_inner;
            private readonly BufferManager m_buffers;
            private readonly BarrierChannelBindings m_owner;
        }

        private sealed class AsyncBarrier
        {
            /// <summary>
            /// Gets the entered for <c>AsyncBarrier</c>.
            /// </summary>
            public Task Entered => m_entered.Task;

            /// <summary>
            /// Signals arrival and awaits the bounded test-controlled release.
            /// </summary>
            /// <param name="ct">The cancellation token supplied by the caller.</param>
            public async Task WaitAsync(CancellationToken ct)
            {
                m_entered.TrySetResult(true);
                await m_release.Task.WaitAsync(ct).ConfigureAwait(false);
            }

            public void Release()
            {
                m_release.TrySetResult(true);
            }

            private readonly TaskCompletionSource<bool> m_entered =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            private readonly TaskCompletionSource<bool> m_release =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }
}
