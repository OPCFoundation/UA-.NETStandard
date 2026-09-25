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

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Transport
{
    /// <summary>
    /// Exercises quota-scoped message deadlines without a transport-listener sweep.
    /// </summary>
    [TestFixture]
    public sealed class TcpMessageAssemblyDeadlineTests
    {
        [Test]
        public void ChannelsShareOneTimerPerQuotaAndClock()
        {
            using var scope = new DeadlineScope();
            Assert.That(scope.Clock.CreatedTimers, Is.Zero);
            for (int ii = 0; ii < 4; ii++)
            {
                scope.CreateChannel();
            }
            Assert.That(scope.Clock.CreatedTimers, Is.EqualTo(1));
            Assert.That(scope.Clock.ActiveTimers, Is.EqualTo(1));

            using var otherScope = new DeadlineScope();
            otherScope.CreateChannel(scope.Clock);
            Assert.That(scope.Clock.CreatedTimers, Is.EqualTo(2));
            Assert.That(scope.Clock.ActiveTimers, Is.EqualTo(2));
            otherScope.Dispose();
            Assert.That(scope.Clock.ActiveTimers, Is.EqualTo(1));
            scope.Dispose();
            Assert.That(scope.Clock.ActiveTimers, Is.Zero);
        }

        [Test]
        public async Task DifferentClocksExpireIndependentlyAsync()
        {
            using var scope = new DeadlineScope();
            var otherClock = new CountingTimeProvider();
            DeadlineChannel first = scope.CreateChannel();
            DeadlineChannel second = scope.CreateChannel(otherClock);
            first.SavePart(1);
            second.SavePart(1);
            Assert.That(scope.Clock.CreatedTimers, Is.EqualTo(1));
            Assert.That(otherClock.CreatedTimers, Is.EqualTo(1));

            scope.Clock.Advance(1000);
            await first.DrainAsync().ConfigureAwait(false);
            Assert.That(first.CurrentState, Is.EqualTo(TcpChannelState.Closed));
            Assert.That(second.CurrentState, Is.EqualTo(TcpChannelState.Open));
            Assert.That(scope.Budget.ReservedBytes, Is.EqualTo(32));
            Assert.That(scope.Clock.ActiveTimers, Is.Zero);
            Assert.That(otherClock.ActiveTimers, Is.EqualTo(1));

            otherClock.Advance(1000);
            await second.DrainAsync().ConfigureAwait(false);
            Assert.That(second.CurrentState, Is.EqualTo(TcpChannelState.Closed));
            Assert.That(otherClock.ActiveTimers, Is.Zero);
            scope.AssertAllBuffersReturned();
        }

        [Test]
        public async Task CompletedAndIdleChannelsStayOpenAsync()
        {
            using var scope = new DeadlineScope();
            DeadlineChannel completed = scope.CreateChannel();
            DeadlineChannel idle = scope.CreateChannel();
            completed.SavePart(1);
            scope.Clock.Advance(600);
            completed.CompleteMessage(1);
            scope.Clock.Advance(400);
            await completed.DrainAsync().ConfigureAwait(false);
            Assert.That(completed.HasExpiredPartialMessage, Is.False);
            Assert.That(completed.CurrentState, Is.EqualTo(TcpChannelState.Open));
            Assert.That(idle.CurrentState, Is.EqualTo(TcpChannelState.Open));
            scope.AssertAllBuffersReturned();

            completed.SavePart(2);
            scope.Clock.Advance(500);
            Assert.That(completed.HasExpiredPartialMessage, Is.False);
            Assert.That(scope.Budget.ReservedBytes, Is.EqualTo(32));
            completed.CompleteMessage(2);
            scope.Clock.Advance(1500);
            await completed.DrainAsync().ConfigureAwait(false);
            Assert.That(completed.CurrentState, Is.EqualTo(TcpChannelState.Open));
            Assert.That(idle.CurrentState, Is.EqualTo(TcpChannelState.Open));
            Assert.That(completed.SentChunks, Is.Empty);
            Assert.That(idle.SentChunks, Is.Empty);
            scope.Listener.Verify(value => value.ChannelClosed(It.IsAny<uint>()), Times.Never);
            scope.AssertAllBuffersReturned();
        }

        [TestCase(TcpChannelState.Open)]
        [TestCase(TcpChannelState.Connecting)]
        [TestCase(TcpChannelState.Opening)]
        public async Task ContinuationDoesNotExtendDeadlineAndSendsTimeoutAsync(TcpChannelState state)
        {
            using var scope = new DeadlineScope();
            DeadlineChannel channel = scope.CreateChannel(state: state);
            channel.SavePart(1);
            scope.Clock.Advance(600);
            channel.SavePart(1);
            scope.Clock.Advance(399);
            Assert.That(channel.HasExpiredPartialMessage, Is.False);
            Assert.That(channel.CurrentState, Is.EqualTo(state));
            Assert.That(scope.Budget.ReservedBytes, Is.EqualTo(64));
            Assert.That(scope.OutstandingBuffers, Is.EqualTo(2));
            Assert.That(channel.SentChunks, Is.Empty);

            scope.Clock.Advance(1);
            await channel.DrainAsync().ConfigureAwait(false);

            Assert.That(channel.CurrentState, Is.EqualTo(TcpChannelState.Closed));
            AssertTimeoutError(channel);
            scope.Listener.Verify(value => value.ChannelClosed(channel.Id), Times.Once);
            Assert.That(scope.Clock.ActiveTimers, Is.Zero);
            scope.AssertAllBuffersReturned();
            scope.Clock.Advance(5000);
            channel.Dispose();
            channel.Dispose();
            Assert.That(channel.SentChunks, Has.Count.EqualTo(1));
            scope.AssertAllBuffersReturned();
        }

        [Test]
        public async Task ReplacementMessageStartsANewDeadlineAsync()
        {
            using var scope = new DeadlineScope();
            DeadlineChannel channel = scope.CreateChannel();
            channel.SavePart(1);
            scope.Clock.Advance(500);
            channel.SavePart(2);
            Assert.That(scope.OutstandingBuffers, Is.EqualTo(1));
            Assert.That(scope.Budget.ReservedBytes, Is.EqualTo(32));
            scope.Clock.Advance(500);
            await channel.DrainAsync().ConfigureAwait(false);
            Assert.That(channel.CurrentState, Is.EqualTo(TcpChannelState.Open));
            scope.Clock.Advance(499);
            Assert.That(channel.HasExpiredPartialMessage, Is.False);
            scope.Clock.Advance(1);
            await channel.DrainAsync().ConfigureAwait(false);

            Assert.That(channel.CurrentState, Is.EqualTo(TcpChannelState.Closed));
            AssertTimeoutError(channel);
            scope.AssertAllBuffersReturned();
        }

        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(int.MinValue)]
        public async Task NonPositiveLifetimeUsesDefaultWithoutChangingQuotasAsync(int lifetime)
        {
            using var scope = new DeadlineScope(lifetime);
            DeadlineChannel channel = scope.CreateChannel();
            channel.SavePart(1);
            Assert.That(scope.Quotas.MessageAssemblyLifetime, Is.EqualTo(TcpMessageLimits.DefaultChannelLifetime));
            scope.Clock.Advance(1);
            Assert.That(channel.HasExpiredPartialMessage, Is.False);
            scope.Clock.Advance(TcpMessageLimits.DefaultChannelLifetime - 2);
            await channel.DrainAsync().ConfigureAwait(false);
            Assert.That(channel.CurrentState, Is.EqualTo(TcpChannelState.Open));
            Assert.That(channel.HasExpiredPartialMessage, Is.False);
            Assert.That(scope.Budget.ReservedBytes, Is.EqualTo(32));

            scope.Clock.Advance(1);
            await channel.DrainAsync().ConfigureAwait(false);

            Assert.That(channel.CurrentState, Is.EqualTo(TcpChannelState.Closed));
            Assert.That(scope.Quotas.ChannelLifetime, Is.EqualTo(lifetime));
            Assert.That(scope.Quotas.SecurityTokenLifetime, Is.EqualTo(12345));
            AssertTimeoutError(channel);
            scope.AssertAllBuffersReturned();
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task LastUnregistrationDisposesTimerAndQuotaScopeCanBeReusedAsync(bool dispose)
        {
            using var scope = new DeadlineScope();
            DeadlineChannel first = scope.CreateChannel();
            DeadlineChannel second = scope.CreateChannel();
            first.SavePart(1);
            second.SavePart(1);
            first.Terminate(dispose);
            Assert.That(scope.Clock.ActiveTimers, Is.EqualTo(1));
            Assert.That(scope.Budget.ReservedBytes, Is.EqualTo(32));
            second.Terminate(dispose);
            Assert.That(scope.Clock.ActiveTimers, Is.Zero);
            scope.AssertAllBuffersReturned();

            DeadlineChannel replacement = scope.CreateChannel();
            Assert.That(scope.Clock.CreatedTimers, Is.EqualTo(2));
            Assert.That(scope.Clock.ActiveTimers, Is.EqualTo(1));
            first.Dispose();
            second.Dispose();
            Assert.That(scope.Clock.ActiveTimers, Is.EqualTo(1));
            replacement.SavePart(1);
            scope.Clock.Advance(1000);
            await replacement.DrainAsync().ConfigureAwait(false);

            Assert.That(replacement.CurrentState, Is.EqualTo(TcpChannelState.Closed));
            Assert.That(scope.Clock.ActiveTimers, Is.Zero);
            scope.AssertAllBuffersReturned();
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task BusyChannelGateDoesNotBlockSharedTimerAndCleanupRechecksDeadlineAsync(bool complete)
        {
            using var scope = new DeadlineScope();
            DeadlineChannel busy = scope.CreateChannel();
            DeadlineChannel peer = scope.CreateChannel();
            busy.SavePart(1);
            peer.SavePart(1);
            ChannelGate.Releaser gate = busy.Gate.Enter();
            Task advance = Task.Run(() => scope.Clock.Advance(3000));
            try
            {
                await advance.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                Assert.That(busy.PendingWork, Is.EqualTo(1), "Repeated ticks must not queue duplicate cleanup.");
                await peer.DrainAsync().ConfigureAwait(false);
                Assert.That(peer.CurrentState, Is.EqualTo(TcpChannelState.Closed));
                Assert.That(busy.CurrentState, Is.EqualTo(TcpChannelState.Open));
                Assert.That(scope.Clock.ActiveTimers, Is.EqualTo(1));
                if (complete)
                {
                    busy.CompleteMessage(1);
                }
            }
            finally
            {
                gate.Dispose();
                await advance.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            await busy.DrainAsync().ConfigureAwait(false);

            Assert.That(busy.CurrentState, Is.EqualTo(complete ? TcpChannelState.Open : TcpChannelState.Closed));
            Assert.That(busy.SentChunks, Has.Count.EqualTo(complete ? 0 : 1));
            scope.AssertAllBuffersReturned();
        }

        [Test]
        public void TerminalFaultUnregistersTheChannelAndReleasesRetainedBuffers()
        {
            using var scope = new DeadlineScope();
            DeadlineChannel channel = scope.CreateChannel();
            channel.SavePart(1);
            channel.Fault();

            Assert.That(channel.CurrentState, Is.EqualTo(TcpChannelState.Faulted));
            Assert.That(scope.Clock.ActiveTimers, Is.Zero);
            scope.Listener.Verify(value => value.ChannelClosed(channel.Id), Times.Once);
            scope.Clock.Advance(1000);
            Assert.That(channel.PendingWork, Is.Zero);
            Assert.That(channel.SentChunks, Is.Empty);
            scope.AssertAllBuffersReturned();
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task TimeoutCleanupSurvivesErrorSendOrAllocationFailureAsync(bool allocationFails)
        {
            using var scope = new DeadlineScope();
            DeadlineChannel channel = scope.CreateChannel();
            channel.SavePart(1);
            scope.FailErrorAllocation = allocationFails;
            channel.FailSend = !allocationFails;
            scope.Clock.Advance(1000);
            await channel.DrainAsync().ConfigureAwait(false);

            Assert.That(channel.CurrentState, Is.EqualTo(TcpChannelState.Closed));
            scope.Listener.Verify(value => value.ChannelClosed(channel.Id), Times.Once);
            scope.BufferMock.Verify(value => value.TakeBuffer(It.IsAny<int>(), "SendErrorMessage"), Times.Once);
            if (allocationFails)
            {
                Assert.That(channel.SentChunks, Is.Empty);
            }
            else
            {
                AssertTimeoutError(channel);
            }
            Assert.That(scope.Clock.ActiveTimers, Is.Zero);
            scope.AssertAllBuffersReturned();
        }

        private static void AssertTimeoutError(DeadlineChannel channel)
        {
            Assert.That(channel.SentChunks, Has.Count.EqualTo(1));
            byte[] sent = channel.SentChunks[0];
            Assert.That(BitConverter.ToUInt32(sent, 0), Is.EqualTo(TcpMessageType.Error));
            Assert.That(BitConverter.ToUInt32(sent, 4), Is.EqualTo(sent.Length));
            ErrorMessage error = TcpMessageParsers.ReadErrorMessage(new ArraySegment<byte>(sent, 8, sent.Length - 8));
            Assert.That(error.StatusCode, Is.EqualTo((uint)StatusCodes.BadTimeout));
            Assert.That(error.Reason, Is.EqualTo("Incomplete message exceeded its assembly deadline."));
            Assert.That(channel.SendPrecededClose, Is.True);
        }

        private sealed class DeadlineScope : IDisposable
        {
            public DeadlineScope(int lifetime = 1000)
            {
                Quotas = new ChannelQuotas(ServiceMessageContext.Create(m_telemetry))
                {
                    MaxBufferSize = 8192,
                    MaxMessageSize = 32768,
                    ChannelLifetime = lifetime,
                    SecurityTokenLifetime = 12345,
                    ChunkReassemblyBudget = Budget
                };
                BufferMock.SetupGet(value => value.MaxSuggestedBufferSize).Returns(8192);
                BufferMock.Setup(value => value.GetSuggestedBufferSize(It.IsAny<int>()))
                    .Returns((int size) => size);
                BufferMock.Setup(value => value.TakeBuffer(It.IsAny<int>(), It.IsAny<string>()))
                    .Returns((int size, string owner) =>
                    {
                        if (FailErrorAllocation && owner == "SendErrorMessage")
                        {
                            throw new ServiceResultException(StatusCodes.BadTcpNotEnoughResources);
                        }
                        byte[] buffer = new byte[size];
                        m_outstanding.TryAdd(buffer, 0);
                        return buffer;
                    });
                BufferMock.Setup(value => value.ReturnBuffer(It.IsAny<byte[]>(), It.IsAny<string>()))
                    .Callback((byte[] buffer, string owner) =>
                    {
                        if (!m_outstanding.TryRemove(buffer, out _))
                        {
                            Interlocked.Increment(ref m_duplicateReturns);
                        }
                    });
            }

            public ChannelQuotas Quotas { get; }

            public ChunkReassemblyBudget Budget { get; } = new(1024 * 1024);

            public CountingTimeProvider Clock { get; } = new();

            public Mock<ITcpChannelListener> Listener { get; } = new();

            public Mock<IBufferManager> BufferMock { get; } = new();

            public int OutstandingBuffers => m_outstanding.Count;

            public bool FailErrorAllocation { get; set; }

            public DeadlineChannel CreateChannel(
                CountingTimeProvider clock = null,
                TcpChannelState state = TcpChannelState.Open)
            {
                var channel = new DeadlineChannel(
                    Listener.Object,
                    new BufferManager(BufferMock.Object),
                    Quotas,
                    m_telemetry,
                    clock ?? Clock,
                    (uint)m_channels.Count + 1,
                    state);
                m_channels.Add(channel);
                return channel;
            }

            public void AssertAllBuffersReturned()
            {
                Assert.That(Budget.ReservedBytes, Is.Zero);
                Assert.That(OutstandingBuffers, Is.Zero);
                Assert.That(Volatile.Read(ref m_duplicateReturns), Is.Zero);
            }

            public void Dispose()
            {
                foreach (DeadlineChannel channel in m_channels)
                {
                    channel.Dispose();
                }
            }

            private readonly ITelemetryContext m_telemetry = NUnitTelemetryContext.Create();
            private readonly ConcurrentDictionary<byte[], byte> m_outstanding = new();
            private readonly List<DeadlineChannel> m_channels = [];
            private int m_duplicateReturns;
        }

        private sealed class DeadlineChannel : TcpListenerChannel
        {
            public DeadlineChannel(
                ITcpChannelListener listener,
                BufferManager buffers,
                ChannelQuotas quotas,
                ITelemetryContext telemetry,
                TimeProvider clock,
                uint id,
                TcpChannelState state)
                : base(nameof(TcpMessageAssemblyDeadlineTests), listener, buffers, quotas, null!, [], telemetry, clock)
            {
                ChannelId = id;
                State = state;
                var transport = new Mock<IUaSCByteTransport>();
                transport.Setup(value => value.SendChunkAsync(
                        It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
                    .Returns((ReadOnlyMemory<byte> chunk, CancellationToken _) =>
                    {
                        SendPrecededClose = !m_closed;
                        SentChunks.Add(chunk.ToArray());
                        return FailSend
                            ? new ValueTask(Task.FromException(new IOException("Controlled terminal send failure.")))
                            : default;
                    });
                transport.Setup(value => value.Close()).Callback(() => m_closed = true);
                Transport = transport.Object;
            }

            public TcpChannelState CurrentState => State;

            public int PendingWork => BackgroundWork.PendingCount;

            public List<byte[]> SentChunks { get; } = [];

            public bool FailSend { get; set; }

            public bool SendPrecededClose { get; private set; }

            public void SavePart(uint requestId)
            {
                byte[] buffer = BufferManager.TakeBuffer(32, nameof(SavePart));
                SaveIntermediateChunk(requestId, new ArraySegment<byte>(buffer), true, gateHeld: false);
            }

            public void CompleteMessage(uint requestId)
            {
                GetSavedChunks(requestId, default, true, gateHeld: false)
                    .Release(BufferManager, nameof(CompleteMessage));
            }

            public void Terminate(bool dispose)
            {
                if (dispose)
                {
                    Dispose();
                }
                else
                {
                    ChannelClosed();
                }
            }

            public void Fault()
            {
                ChannelFaulted();
            }

            public async Task DrainAsync()
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                while (BackgroundWork.PendingCount != 0)
                {
                    await Task.Delay(10, timeout.Token).ConfigureAwait(false);
                }
            }

            private bool m_closed;
        }

        private sealed class CountingTimeProvider : TimeProvider
        {
            public int CreatedTimers => Volatile.Read(ref m_createdTimers);

            public int ActiveTimers => Volatile.Read(ref m_activeTimers);

            public override long TimestampFrequency => m_clock.TimestampFrequency;

            public override long GetTimestamp()
            {
                return m_clock.GetTimestamp();
            }

            public override DateTimeOffset GetUtcNow()
            {
                return m_clock.GetUtcNow();
            }

            public override ITimer CreateTimer(TimerCallback callback, object state, TimeSpan dueTime, TimeSpan period)
            {
                Interlocked.Increment(ref m_createdTimers);
                Interlocked.Increment(ref m_activeTimers);
                return new CountingTimer(m_clock.CreateTimer(callback, state, dueTime, period), this);
            }

            public void Advance(int milliseconds)
            {
                m_clock.Advance(TimeSpan.FromMilliseconds(milliseconds));
            }

            private readonly FakeTimeProvider m_clock = new();
            private int m_createdTimers;
            private int m_activeTimers;

            private sealed class CountingTimer(ITimer timer, CountingTimeProvider owner) : ITimer
            {
                public bool Change(TimeSpan dueTime, TimeSpan period)
                {
                    return timer.Change(dueTime, period);
                }

                public void Dispose()
                {
                    if (Interlocked.Exchange(ref m_disposed, 1) == 0)
                    {
                        timer.Dispose();
                        Interlocked.Decrement(ref owner.m_activeTimers);
                    }
                }

                public ValueTask DisposeAsync()
                {
                    Dispose();
                    return default;
                }

                private int m_disposed;
            }
        }
    }
}
