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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Owns every data channel on one SecureChannel: the ChannelId space,
    /// the connection level flow control window, the scheduler that
    /// shares the connection between channels, and the demultiplexing of
    /// inbound frames.
    /// </summary>
    /// <remarks>
    /// The scheduler is the deficit round robin of Part 6 errata 5.7.
    /// Service traffic keeps its precedence structurally: the transport
    /// serializes writes in arrival order, so a MSG, OPN or CLO chunk
    /// that becomes ready while a frame is being written is admitted
    /// immediately after it and is never delayed by more than the
    /// transmission of one maximum size frame.
    /// </remarks>
    public sealed class DataChannelManager : IAsyncDisposable
    {
        /// <summary>
        /// Creates a manager.
        /// </summary>
        /// <param name="transport">The transport that carries the
        /// frames.</param>
        /// <param name="isServer">True on the server side, which is what
        /// allocates ChannelIds and is normally the data channel
        /// source.</param>
        /// <param name="telemetry">Telemetry context.</param>
        /// <param name="maxDataChannels">The most channels this manager
        /// keeps open at once.</param>
        /// <param name="maxCreditPerChannel">The largest window granted
        /// to one channel, which with the channel count bounds the
        /// connection level grant.</param>
        public DataChannelManager(
            IDataChannelTransport transport,
            bool isServer,
            ITelemetryContext telemetry,
            ushort maxDataChannels = 16,
            uint maxCreditPerChannel = 1024 * 1024)
        {
            m_transport = transport ?? throw new ArgumentNullException(nameof(transport));
            m_isServer = isServer;
            m_logger = telemetry.CreateLogger<DataChannelManager>();
            MaxDataChannels = maxDataChannels;
            MaxCreditPerChannel = maxCreditPerChannel;

            m_connectionSend = new DataChannelSendWindow();
            m_connectionReceive = new DataChannelReceiveCredit();
            m_wakeUp = new SemaphoreSlim(0, 1);
            m_stop = new CancellationTokenSource();
            m_scheduler = Task.Run(() => RunSchedulerAsync(m_stop.Token));
        }

        private static bool IsTerminal(DataChannelState state)
            => state is DataChannelState.Closed or DataChannelState.Faulted;

        /// <summary>
        /// The most channels this manager keeps open at once.
        /// </summary>
        public ushort MaxDataChannels { get; }

        /// <summary>
        /// The largest flow control window granted to one channel.
        /// </summary>
        public uint MaxCreditPerChannel { get; }

        /// <summary>
        /// The channels currently open, in ascending ChannelId order.
        /// </summary>
        public IReadOnlyList<DataChannel> Channels
            => [.. m_channels.Values
                .Where(static c => !IsTerminal(c.State))
                .OrderBy(c => c.ChannelId)];

        /// <summary>
        /// The number of channels currently open.
        /// </summary>
        public int ActiveChannelCount
            => m_channels.Values.Count(static c => !IsTerminal(c.State));

        /// <summary>
        /// Raised for every state transition of every channel this
        /// manager owns.
        /// </summary>
        public event EventHandler<DataChannelStateChangedEventArgs>? ChannelStateChanged;

        /// <summary>
        /// Allocates the next ChannelId. Identifiers are allocated
        /// monotonically from one and are never reassigned while the
        /// SecureChannel that owns them is open, so a late frame from the
        /// previous occupant can never be delivered to a successor.
        /// </summary>
        /// <param name="channelId">The identifier.</param>
        /// <returns>False when the space is exhausted, which the caller
        /// answers with Bad_TooManyDataChannels.</returns>
        public bool TryAllocateChannelId(out uint channelId)
        {
            lock (m_lock)
            {
                if (m_nextChannelId == 0 || ActiveChannelCount >= MaxDataChannels)
                {
                    channelId = 0;
                    return false;
                }

                channelId = m_nextChannelId;
                m_nextChannelId = m_nextChannelId == uint.MaxValue ? 0 : m_nextChannelId + 1;
                return true;
            }
        }

        /// <summary>
        /// Registers a channel that OpenDataChannel accepted. The channel
        /// stays in Opening until <see cref="MarkOpen"/> reports that the
        /// response has been handed to the transport, because no frame
        /// may name a ChannelId the peer has not yet been told about.
        /// </summary>
        /// <param name="channelId">The allocated identifier.</param>
        /// <param name="sourceNodeId">The data channel source.</param>
        /// <param name="settings">The revised parameters.</param>
        /// <param name="isSource">True when this peer is the source.</param>
        /// <param name="transportChannelId">The transport identifier.</param>
        public DataChannel Register(
            uint channelId,
            NodeId sourceNodeId,
            DataChannelSettings settings,
            bool isSource,
            ulong transportChannelId = 0)
        {
            var channel = new DataChannel(
                channelId,
                sourceNodeId,
                settings,
                m_transport,
                isSource,
                transportChannelId);

            channel.SendReady += OnSendReady;
            channel.StateChanged += OnChannelStateChanged;

            if (!m_channels.TryAdd(channelId, channel))
            {
                channel.SendReady -= OnSendReady;
                channel.StateChanged -= OnChannelStateChanged;
                channel.Dispose();

                throw ServiceResultException.Create(
                    StatusCodes.BadDataChannelIdInvalid,
                    "ChannelId {0} is already in use.",
                    channelId);
            }

            lock (m_lock)
            {
                m_pendingOpens.Add(channelId);
            }

            return channel;
        }

        /// <summary>
        /// Reports that the OpenDataChannel response carrying a ChannelId
        /// has been handed to the transport, which is what makes the
        /// channel eligible to carry frames.
        /// </summary>
        /// <param name="channelId">The identifier.</param>
        public void MarkOpen(uint channelId)
        {
            if (!m_channels.TryGetValue(channelId, out DataChannel? channel))
            {
                return;
            }

            List<PendingFrame>? replay = null;

            lock (m_lock)
            {
                m_pendingOpens.Remove(channelId);

                if (m_pendingFrames.Count > 0)
                {
                    replay = [.. m_pendingFrames.Where(f => f.ChannelId == channelId)];

                    foreach (PendingFrame pending in replay)
                    {
                        m_pendingFrames.Remove(pending);
                        m_pendingBytes -= pending.EncodedSize;
                    }
                }
            }

            channel.MarkOpen();
            EnsureConnectionCreditGranted();

            if (replay != null)
            {
                foreach (PendingFrame pending in replay)
                {
                    DispatchToChannel(channel, pending.ToFrame());
                    m_transport.BufferManager.ReturnBuffer(
                        pending.Buffer,
                        nameof(DataChannelManager));
                }
            }
        }

        /// <summary>
        /// Finds an open channel.
        /// </summary>
        /// <param name="channelId">The identifier.</param>
        /// <param name="channel">The channel.</param>
        public bool TryGetChannel(uint channelId, out DataChannel? channel)
        {
            return m_channels.TryGetValue(channelId, out channel);
        }

        /// <summary>
        /// Whether a ChannelId was ever issued on this SecureChannel, even
        /// if the channel has since ended and been released.
        /// </summary>
        /// <remarks>
        /// ChannelIds are allocated monotonically and never reused
        /// (Part 6 errata §5.11), so an identifier below the high water
        /// mark that is no longer present named a channel that has ended,
        /// while one at or above it was never issued at all. That is what
        /// lets a Server distinguish <c>Bad_DataChannelClosed</c> from
        /// <c>Bad_DataChannelIdInvalid</c> without retaining every channel
        /// it has ever opened.
        /// </remarks>
        /// <param name="channelId">The identifier.</param>
        public bool WasEverAllocated(uint channelId)
        {
            lock (m_lock)
            {
                return channelId >= DataChannelConstants.FirstChannelId &&
                    (m_nextChannelId == 0 || channelId < m_nextChannelId);
            }
        }

        /// <summary>
        /// Removes a channel and releases its resources. The identifier
        /// is not returned to the pool.
        /// </summary>
        /// <param name="channelId">The identifier.</param>
        public void Remove(uint channelId)
        {
            if (!m_channels.TryRemove(channelId, out DataChannel? channel))
            {
                return;
            }

            channel.SendReady -= OnSendReady;
            channel.StateChanged -= OnChannelStateChanged;
            channel.Dispose();
        }

        /// <summary>
        /// How many scheduler rounds have run, which lets a test wait for the
        /// scheduler to have considered the channels rather than sleep for a
        /// guess at how long that takes.
        /// </summary>
        internal long SchedulerRounds => Interlocked.Read(ref m_schedulerRounds);

        /// <summary>
        /// Aborts every channel, used when the SecureChannel is closed or
        /// the transport is lost. Frames have nowhere to flow, so each
        /// channel enters Faulted.
        /// </summary>
        /// <param name="reason">Why.</param>
        public void AbortAll(StatusCode reason)
        {
            foreach (DataChannel channel in m_channels.Values)
            {
                channel.Abort(reason);
            }
        }

        /// <summary>
        /// Handles one decoded frame arriving on this SecureChannel.
        /// </summary>
        /// <param name="frame">The frame.</param>
        public void HandleFrame(in DataChannelFrame frame)
        {
            if (frame.ChannelId == DataChannelConstants.ConnectionControlChannelId)
            {
                HandleControlChannelFrame(frame);
                return;
            }

            if (m_channels.TryGetValue(frame.ChannelId, out DataChannel? channel))
            {
                DispatchToChannel(channel, frame);
                return;
            }

            // A frame naming a ChannelId whose OpenDataChannel is still
            // outstanding is buffered rather than rejected, because over
            // a transport with no ordering between streams it can
            // legitimately overtake the response (7.4). The buffer is
            // bounded, since a not yet open ChannelId has no credit
            // window to charge against.
            if (TryBufferPendingFrame(frame))
            {
                return;
            }

            m_logger.DataChannelManagerUnknownChannel(frame.ChannelId);
            m_transport.OnProtocolFault(DataChannelFrameError.InvalidControlChannelFrame);
        }

        /// <summary>
        /// Sends the connection level CREDIT frame that unblocks the
        /// peer's send direction. Each peer's obligation is triggered by
        /// the peer's need to send, not by its own: a CREDIT frame flows
        /// opposite to the data it authorizes.
        /// </summary>
        public void EnsureConnectionCreditGranted()
        {
            if (m_transport.HasTransportFlowControl)
            {
                return;
            }

            uint grant;

            lock (m_lock)
            {
                if (m_connectionCreditGranted)
                {
                    return;
                }

                m_connectionCreditGranted = true;

                ulong bound = (ulong)MaxCreditPerChannel * MaxDataChannels;
                grant = bound > uint.MaxValue ? uint.MaxValue : (uint)bound;
                m_connectionReceive.Grant(grant);

                m_controlQueue.Enqueue(DataChannelFrame.Credit(
                    DataChannelConstants.ConnectionControlChannelId,
                    TakeControlSequenceNumber(),
                    0,
                    grant));
            }

            Wake();
        }

        /// <summary>
        /// Queues a PING on the connection control channel, which
        /// measures the connection and keeps an idle one alive.
        /// </summary>
        public bool TryPingConnection()
        {
            lock (m_lock)
            {
                if (m_connectionPingOutstanding)
                {
                    return false;
                }

                long now = m_transport.TimeProvider.GetTimestamp();

                if (m_lastConnectionPing != 0 &&
                    m_transport.TimeProvider.GetElapsedTime(m_lastConnectionPing, now)
                        .TotalMilliseconds < DataChannelConstants.MinPingInterval)
                {
                    return false;
                }

                m_lastConnectionPing = now;
                m_connectionPingOutstanding = true;

                m_controlQueue.Enqueue(DataChannelFrame.Ping(
                    DataChannelConstants.ConnectionControlChannelId,
                    TakeControlSequenceNumber(),
                    now));
            }

            Wake();
            return true;
        }

        /// <summary>
        /// The most recently measured connection round trip, in
        /// milliseconds.
        /// </summary>
        public double RoundTripTime
        {
            get
            {
                lock (m_lock)
                {
                    return m_roundTripTime;
                }
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (m_disposed)
            {
                return;
            }

            m_disposed = true;
            await m_stop.CancelAsync().ConfigureAwait(false);

            try
            {
                await m_scheduler.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
            }

            m_stop.Dispose();
            m_wakeUp.Dispose();

            foreach (DataChannel channel in m_channels.Values)
            {
                channel.Dispose();
            }

            m_channels.Clear();

            lock (m_lock)
            {
                foreach (PendingFrame pending in m_pendingFrames)
                {
                    m_transport.BufferManager.ReturnBuffer(
                        pending.Buffer,
                        nameof(DataChannelManager));
                }

                m_pendingFrames.Clear();
                m_pendingBytes = 0;
            }
        }

        private void DispatchToChannel(DataChannel channel, in DataChannelFrame frame)
        {
            // A CREDIT frame on a non-zero ChannelId replenishes the
            // connection window as well as the channel's own (Part 6
            // errata 5.8.2). The channel cannot do this itself: the
            // connection window belongs to the manager, and dropping the
            // grant leaves the window to be exhausted once and never
            // refilled, which stalls every channel permanently.
            if (frame.FrameType == DataChannelFrameType.Credit &&
                !m_transport.HasTransportFlowControl &&
                frame.ConnectionCredit != 0)
            {
                bool overflow;

                lock (m_lock)
                {
                    overflow = !m_connectionSend.TryGrant(frame.ConnectionCredit);
                    m_connectionCreditReceived = true;
                }

                if (overflow)
                {
                    m_logger.DataChannelManagerCreditOverflow();
                    AbortAll(StatusCodes.BadDataChannelCreditExceeded);
                    return;
                }
            }

            DataChannelFrameAction action = channel.HandleFrame(frame, out StatusCode status);

            switch (action)
            {
                case DataChannelFrameAction.ResetChannel:
                    channel.Reset(status);
                    break;
                case DataChannelFrameAction.CloseSecureChannel:
                    m_transport.OnProtocolFault(DataChannelFrameError.MalformedHeader);
                    break;
                default:
                    break;
            }
        }

        private void HandleControlChannelFrame(in DataChannelFrame frame)
        {
            switch (frame.FrameType)
            {
                case DataChannelFrameType.Credit:
                    if (!m_transport.HasTransportFlowControl)
                    {
                        bool overflow;

                        lock (m_lock)
                        {
                            overflow = !m_connectionSend.TryGrant(frame.ConnectionCredit);
                            m_connectionCreditReceived = true;
                        }

                        if (overflow)
                        {
                            m_logger.DataChannelManagerCreditOverflow();
                            AbortAll(StatusCodes.BadDataChannelCreditExceeded);
                            return;
                        }

                        Wake();
                    }

                    break;
                case DataChannelFrameType.Ping:
                    // Part 6 errata §5.11 bounds PING to one per second per
                    // ChannelId, and ChannelId 0 is a ChannelId. Answering
                    // unconditionally here would leave the connection-level
                    // amplification surface open even once every data channel
                    // enforces its own bound. There is no channel to RESET on
                    // the control channel, so an over-rate PING is simply
                    // discarded and the connection carries on.
                    bool answer;

                    lock (m_lock)
                    {
                        long now = m_transport.TimeProvider.GetTimestamp();

                        answer = !m_hasAnsweredConnectionPing ||
                            m_transport.TimeProvider
                                .GetElapsedTime(m_lastConnectionPingAnswered, now)
                                .TotalMilliseconds >=
                            DataChannelConstants.MinPingInterval *
                                DataChannelConstants.PingResponseIntervalTolerance;

                        if (answer)
                        {
                            m_lastConnectionPingAnswered = now;
                            m_hasAnsweredConnectionPing = true;

                            m_controlQueue.Enqueue(DataChannelFrame.Pong(
                                DataChannelConstants.ConnectionControlChannelId,
                                TakeControlSequenceNumber(),
                                frame.Timestamp));
                        }
                    }

                    if (answer)
                    {
                        Wake();
                    }

                    break;
                case DataChannelFrameType.Pong:
                    lock (m_lock)
                    {
                        m_connectionPingOutstanding = false;
                        m_roundTripTime = m_transport.TimeProvider
                            .GetElapsedTime(frame.Timestamp).TotalMilliseconds;
                    }

                    break;
                default:
                    m_transport.OnProtocolFault(
                        DataChannelFrameError.InvalidControlChannelFrame);
                    break;
            }
        }

        private bool TryBufferPendingFrame(in DataChannelFrame frame)
        {
            lock (m_lock)
            {
                if (!m_pendingOpens.Contains(frame.ChannelId))
                {
                    return false;
                }

                int encodedSize = frame.EncodedSize;
                long bound = (long)m_transport.MaxFrameBodySize *
                    DataChannelConstants.UnknownChannelBufferFrames;

                if (m_pendingBytes > bound - encodedSize)
                {
                    // The excess is discarded rather than buffered: the
                    // rule would otherwise be an unbounded state
                    // primitive.
                    return true;
                }

                byte[] buffer = m_transport.BufferManager.TakeBuffer(
                    frame.Payload.Length > 0 ? frame.Payload.Length : 1,
                    nameof(DataChannelManager));

                frame.Payload.Span.CopyTo(buffer.AsSpan(0, frame.Payload.Length));

                m_pendingFrames.Add(new PendingFrame(frame, buffer, frame.Payload.Length));
                m_pendingBytes += encodedSize;
                return true;
            }
        }

        private uint TakeControlSequenceNumber()
        {
            uint value = m_controlSequence;
            m_controlSequence = DataChannelSequence.Next(m_controlSequence);
            return value;
        }

        private void OnSendReady(object? sender, EventArgs e)
        {
            Wake();
        }

        private void OnChannelStateChanged(
            object? sender,
            DataChannelStateChangedEventArgs e)
        {
            ChannelStateChanged?.Invoke(this, e);

            if (IsTerminal(e.State))
            {
                if (sender is DataChannel channel &&
                    !channel.HasPendingControlFrames &&
                    !channel.TryPeekPayloadLength(out _))
                {
                    Remove(channel.ChannelId);
                    return;
                }

                Wake();
            }
        }

        private void Wake()
        {
            if (m_wakeUp.CurrentCount == 0)
            {
                try
                {
                    m_wakeUp.Release();
                }
                catch (SemaphoreFullException)
                {
                    // Another thread released first; the loop is awake.
                }
                catch (ObjectDisposedException)
                {
                    // Shutting down.
                }
            }
        }

        private async Task RunSchedulerAsync(CancellationToken ct)
        {
            var ready = new List<DataChannel>();

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await m_wakeUp.WaitAsync(SchedulerTick, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                try
                {
                    await RunRoundAsync(ready, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
#pragma warning disable CA1031 // The scheduler must survive a fault on one channel.
                catch (Exception ex)
#pragma warning restore CA1031
                {
                    m_logger.DataChannelManagerSchedulerFault(ex);
                }
            }
        }

        private async ValueTask RunRoundAsync(List<DataChannel> ready, CancellationToken ct)
        {
            Interlocked.Increment(ref m_schedulerRounds);

            long nowTicks = m_transport.TimeProvider.GetUtcNow().UtcDateTime.ToFileTimeUtc();
            bool more = false;

            ready.Clear();

            foreach (DataChannel channel in m_channels.Values)
            {
                // A channel whose OpenDataChannel response has not reached the
                // transport may not carry a frame yet (§7.4), so it is not
                // scheduled at all until MarkOpen admits it.
                if (channel.State == DataChannelState.Opening)
                {
                    continue;
                }

                channel.ExpireAndReportGaps(nowTicks);
                channel.PumpClosing();
                channel.Deficit += channel.Quantum;
                ready.Add(channel);
            }

            ready.Sort(static (x, y) => y.Settings.Priority.CompareTo(x.Settings.Priority));

            await DrainControlQueueAsync(ct).ConfigureAwait(false);

            for (int ii = 0; ii < ready.Count; ii++)
            {
                DataChannel channel = ready[ii];

                // Control frames are exempt from credit and from the
                // deficit: a CREDIT frame stuck behind a credit blocked
                // DATA frame would never be sent, and a credit stall is
                // exactly what caused the expiry a GAP reports.
                while (channel.TryDequeueControl(out DataChannelFrame control))
                {
                    await m_transport.SendFrameAsync(control, ct).ConfigureAwait(false);
                }

                while (channel.TryPeekPayloadLength(out int length))
                {
                    if (length > channel.Deficit)
                    {
                        break;
                    }

                    if (!CanSendPayload(channel, length))
                    {
                        channel.SetPaused(true);
                        break;
                    }

                    if (!channel.TryDequeuePayload(out DataChannelFrame frame, out byte[]? buffer))
                    {
                        break;
                    }

                    channel.SetPaused(false);
                    channel.Deficit -= length;

                    if (!m_transport.HasTransportFlowControl)
                    {
                        lock (m_lock)
                        {
                            m_connectionSend.TryConsume(length);
                        }
                    }

                    try
                    {
                        await m_transport.SendFrameAsync(frame, ct).ConfigureAwait(false);
                    }
                    finally
                    {
                        channel.ReleaseSendBuffer(buffer);
                    }
                }

                if (!channel.TryPeekPayloadLength(out _))
                {
                    channel.Deficit = 0;
                }
                else
                {
                    // The deficit bounds how much this channel may send
                    // in one round, not how often rounds may happen. A
                    // channel that still has payload and is not blocked
                    // gets the next round immediately rather than waiting
                    // for the idle tick, which would otherwise cap a
                    // media stream at one quantum per tick.
                    more = true;
                }

                if (IsTerminal(channel.State) &&
                    !channel.HasPendingControlFrames &&
                    !channel.TryPeekPayloadLength(out _))
                {
                    Remove(channel.ChannelId);
                }
            }

            if (more)
            {
                Wake();
            }
        }

        private async ValueTask DrainControlQueueAsync(CancellationToken ct)
        {
            while (true)
            {
                DataChannelFrame frame;

                lock (m_lock)
                {
                    if (m_controlQueue.Count == 0)
                    {
                        return;
                    }

                    frame = m_controlQueue.Dequeue();
                }

                await m_transport.SendFrameAsync(frame, ct).ConfigureAwait(false);
            }
        }

        private bool CanSendPayload(DataChannel channel, int length)
        {
            if (m_transport.HasTransportFlowControl)
            {
                return true;
            }

            lock (m_lock)
            {
                // Before either peer transmits its first DATA frame its
                // connection credit is zero, and it stays zero until the
                // peer grants it (5.8.1).
                if (!m_connectionCreditReceived)
                {
                    return false;
                }

                if (m_connectionSend.IsBlockedBy(length))
                {
                    return false;
                }
            }

            return !channel.IsSendBlocked(length);
        }

        private readonly struct PendingFrame
        {
            public PendingFrame(in DataChannelFrame frame, byte[] buffer, int length)
            {
                ChannelId = frame.ChannelId;
                FrameType = frame.FrameType;
                Flags = frame.Flags;
                FrameSequenceNumber = frame.FrameSequenceNumber;
                Deadline = frame.Deadline;
                Value1 = frame.FrameType switch
                {
                    DataChannelFrameType.Credit => frame.ChannelCredit,
                    DataChannelFrameType.Gap => frame.FirstDiscarded,
                    DataChannelFrameType.Reset => frame.Status.Code,
                    DataChannelFrameType.Ping or DataChannelFrameType.Pong
                        => (uint)((ulong)frame.Timestamp & 0xFFFFFFFFu),
                    _ => 0
                };
                Value2 = frame.FrameType switch
                {
                    DataChannelFrameType.Credit => frame.ConnectionCredit,
                    DataChannelFrameType.Gap => frame.LastDiscarded,
                    DataChannelFrameType.Ping or DataChannelFrameType.Pong
                        => (uint)((ulong)frame.Timestamp >> 32),
                    _ => 0
                };
                Buffer = buffer;
                Length = length;
                EncodedSize = frame.EncodedSize;
            }

            public uint ChannelId { get; }

            public DataChannelFrameType FrameType { get; }

            public DataChannelFrameFlags Flags { get; }

            public uint FrameSequenceNumber { get; }

            public long Deadline { get; }

            public uint Value1 { get; }

            public uint Value2 { get; }

            public byte[] Buffer { get; }

            public int Length { get; }

            public int EncodedSize { get; }

            public DataChannelFrame ToFrame()
            {
                return DataChannelFrame.Decoded(
                    ChannelId,
                    FrameType,
                    Flags,
                    FrameSequenceNumber,
                    Deadline,
                    Value1,
                    Value2,
                    new ReadOnlyMemory<byte>(Buffer, 0, Length));
            }
        }

        private static readonly TimeSpan SchedulerTick = TimeSpan.FromMilliseconds(20);

        private readonly ConcurrentDictionary<uint, DataChannel> m_channels = new();
        private readonly Queue<DataChannelFrame> m_controlQueue = new();
        private readonly HashSet<uint> m_pendingOpens = [];
        private readonly List<PendingFrame> m_pendingFrames = [];
        private readonly DataChannelSendWindow m_connectionSend;
        private readonly DataChannelReceiveCredit m_connectionReceive;
        private readonly IDataChannelTransport m_transport;
        private readonly SemaphoreSlim m_wakeUp;
        private readonly CancellationTokenSource m_stop;
        private readonly Task m_scheduler;
        private readonly ILogger m_logger;
        private readonly Lock m_lock = new();
        private readonly bool m_isServer;
        private uint m_nextChannelId = DataChannelConstants.FirstChannelId;
        private uint m_controlSequence = DataChannelConstants.FirstFrameSequenceNumber;
        private int m_pendingBytes;
        private bool m_connectionCreditGranted;
        private bool m_connectionCreditReceived;
        private bool m_connectionPingOutstanding;
        private long m_lastConnectionPing;
        private long m_lastConnectionPingAnswered;
        private bool m_hasAnsweredConnectionPing;
        private long m_schedulerRounds;
        private double m_roundTripTime;
        private bool m_disposed;
    }
}
