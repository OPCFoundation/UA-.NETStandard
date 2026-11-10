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
using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Transport
{
    /// <summary>
    /// A reverse connect channel starts receiving before ConnectAsync assigns
    /// its endpoint. A peer close in that window must not schedule a
    /// reconnect, and the scheduled handshake handler must never throw.
    /// </summary>
    [TestFixture]
    [Category("TransportChannelDeterministic")]
    [Parallelizable]
    public sealed class ReverseChannelReceiveBeforeConnectTests
    {
        private ITelemetryContext m_telemetry = null!;
        private BufferManager m_buffers = null!;
        private ChannelQuotas m_quotas = null!;

        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_buffers = new BufferManager("reverse-early", TcpMessageLimits.MaxBufferSize, m_telemetry);
            m_quotas = new ChannelQuotas(ServiceMessageContext.Create(m_telemetry));
        }

        /// <summary>
        /// A peer close before ConnectAsync shuts the channel down instead of
        /// scheduling a reconnect handshake.
        /// </summary>
        [Test]
        public async Task PeerCloseBeforeConnectSchedulesNoReconnectAsync()
        {
            var timeProvider = new RecordingTimeProvider();
            var transport = new ClosedTransport();
            using var channel = new EarlyReceiveChannel(
                m_buffers, m_quotas, CreateEndpoint(), m_telemetry, timeProvider);

            channel.AttachTransport(transport);
            channel.ReverseSocket = true;
            channel.StartReceiveLoopPublic();

            // the receive loop reports the close on a pool thread.
            Assert.That(
                await transport.Received.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false),
                Is.True);
            await Task.Delay(200).ConfigureAwait(false);

            Assert.That(
                timeProvider.HandshakeTimers,
                Is.Empty,
                "no reconnect handshake may be scheduled for a channel without an endpoint");
        }

        /// <summary>
        /// The scheduled handshake handler is async void. Invoked for a channel
        /// without an endpoint, it must complete without raising an exception.
        /// </summary>
        [Test]
        public void ScheduledHandshakeWithoutEndpointDoesNotThrow()
        {
            using var channel = new EarlyReceiveChannel(
                m_buffers, m_quotas, CreateEndpoint(), m_telemetry, new RecordingTimeProvider());

            var callback = (TimerCallback)typeof(UaSCUaBinaryClientChannel)
                .GetField("m_startHandshake", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(channel)!;

            // An async void method posts its exception to the current
            // synchronization context: capture it there instead of letting it
            // escape on the thread pool.
            using var context = new CapturingSynchronizationContext();
            SynchronizationContext? previous = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(context);
            try
            {
                callback(null);
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previous);
            }

            Assert.That(context.WaitForIdle(TimeSpan.FromSeconds(10)), Is.True);
            Assert.That(context.Exceptions, Is.Empty);
        }

        private static EndpointDescription CreateEndpoint()
        {
            return new EndpointDescription
            {
                EndpointUrl = "opc.tcp://localhost:4840/ReverseEarly",
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None,
                TransportProfileUri = Profiles.UaTcpTransport
            };
        }

        private sealed class EarlyReceiveChannel : UaSCUaBinaryClientChannel
        {
            public EarlyReceiveChannel(
                BufferManager bufferManager,
                ChannelQuotas quotas,
                EndpointDescription endpoint,
                ITelemetryContext telemetry,
                TimeProvider timeProvider)
                : base(
                    "reverse-early",
                    bufferManager,
                    new ClosedTransportFactory(),
                    quotas,
                    null,
                    null,
                    null,
                    endpoint,
                    telemetry,
                    timeProvider)
            {
            }

            public void AttachTransport(IUaSCByteTransport transport)
            {
                Transport = transport;
            }

            public void StartReceiveLoopPublic()
            {
                StartReceiveLoop();
            }
        }

        /// <summary>
        /// Records the timers created for the scheduled handshake and never
        /// fires any timer.
        /// </summary>
        private sealed class RecordingTimeProvider : TimeProvider
        {
            public ConcurrentQueue<TimeSpan> HandshakeTimers { get; } = new();

            public override ITimer CreateTimer(
                TimerCallback callback,
                object? state,
                TimeSpan dueTime,
                TimeSpan period)
            {
                if (callback.Method.Name.Contains("ScheduledHandshake", StringComparison.Ordinal))
                {
                    HandshakeTimers.Enqueue(dueTime);
                }
                return new InertTimer();
            }

            private sealed class InertTimer : ITimer
            {
                public bool Change(TimeSpan dueTime, TimeSpan period)
                {
                    return true;
                }

                public void Dispose()
                {
                }

                public ValueTask DisposeAsync()
                {
                    return default;
                }
            }
        }

        /// <summary>
        /// Runs posted work on the pool and records the exceptions it raises.
        /// </summary>
        private sealed class CapturingSynchronizationContext : SynchronizationContext, IDisposable
        {
            public void Dispose()
            {
                m_idle.Dispose();
            }

            private int m_outstanding;
            private readonly ManualResetEventSlim m_idle = new(true);

            public ConcurrentQueue<Exception> Exceptions { get; } = new();

            public override void Post(SendOrPostCallback d, object? state)
            {
                OperationStarted();
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    SynchronizationContext? previous = Current;
                    SetSynchronizationContext(this);
                    try
                    {
                        d(state);
                    }
                    catch (Exception e)
                    {
                        Exceptions.Enqueue(e);
                    }
                    finally
                    {
                        SetSynchronizationContext(previous);
                        OperationCompleted();
                    }
                });
            }

            public override void OperationStarted()
            {
                if (Interlocked.Increment(ref m_outstanding) == 1)
                {
                    m_idle.Reset();
                }
            }

            public override void OperationCompleted()
            {
                if (Interlocked.Decrement(ref m_outstanding) == 0)
                {
                    m_idle.Set();
                }
            }

            public bool WaitForIdle(TimeSpan timeout)
            {
                return m_idle.Wait(timeout);
            }
        }

        private sealed class ClosedTransport : IUaSCByteTransport
        {
            public TaskCompletionSource<bool> Received { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public EndPoint? LocalEndpoint => null;

            public EndPoint? RemoteEndpoint => null;

            public TransportChannelFeatures Features => TransportChannelFeatures.None;

            public string Implementation => "ReverseEarly";

            public ValueTask ConnectAsync(Uri url, CancellationToken ct)
            {
                throw new NotSupportedException();
            }

            public ValueTask SendChunkAsync(ReadOnlyMemory<byte> chunk, CancellationToken ct)
            {
                return default;
            }

            public ValueTask SendChunkAsync(BufferCollection buffers, CancellationToken ct)
            {
                return default;
            }

            public ValueTask<ArraySegment<byte>> ReceiveChunkAsync(CancellationToken ct)
            {
                Received.TrySetResult(true);
                throw new ServiceResultException(StatusCodes.BadConnectionClosed, "Peer closed.");
            }

            public void Close()
            {
            }
        }

        private sealed class ClosedTransportFactory : IUaSCByteTransportFactory
        {
            public string Implementation => "ReverseEarly";

            public IUaSCByteTransport Create(
                BufferManager bufferManager,
                int receiveBufferSize,
                ITelemetryContext telemetry)
            {
                return new ClosedTransport();
            }
        }
    }
}
