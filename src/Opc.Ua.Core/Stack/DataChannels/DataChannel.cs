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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// What a receiver decided to do with an inbound frame that the
    /// channel could not accept.
    /// </summary>
    public enum DataChannelFrameAction
    {
        /// <summary>
        /// The frame was consumed.
        /// </summary>
        Accepted,

        /// <summary>
        /// Reset this channel with the accompanying StatusCode. The
        /// connection continues.
        /// </summary>
        ResetChannel,

        /// <summary>
        /// Close the SecureChannel. The sender's framing can no longer be
        /// trusted.
        /// </summary>
        CloseSecureChannel
    }

    /// <summary>
    /// One data channel: a named, authorized, flow controlled,
    /// bidirectional stream of opaque bytes multiplexed onto one
    /// SecureChannel.
    /// </summary>
    /// <remarks>
    /// The channel owns the state machine of Part 6 errata 5.13, the
    /// per direction credit windows of 5.8, the send queue and deadline
    /// expiry of 5.9 and 5.10, and the receive window of 5.2.1. It does
    /// not own the transport: the enclosing
    /// <see cref="DataChannelManager"/> schedules it against its peers
    /// and writes its frames.
    /// </remarks>
    public sealed class DataChannel : IDisposable
    {
        /// <summary>
        /// Creates a data channel.
        /// </summary>
        /// <param name="channelId">The identifier assigned by
        /// OpenDataChannel, unique within the owning SecureChannel.</param>
        /// <param name="sourceNodeId">The data channel source the channel
        /// was opened on.</param>
        /// <param name="settings">The parameters in force.</param>
        /// <param name="transport">The transport that carries the
        /// frames.</param>
        /// <param name="isSource">True when this peer is the data channel
        /// source, which is normally the server.</param>
        /// <param name="transportChannelId">The underlying transport
        /// identifier, or zero for inline framing.</param>
        internal DataChannel(
            uint channelId,
            NodeId sourceNodeId,
            DataChannelSettings settings,
            IDataChannelTransport transport,
            bool isSource,
            ulong transportChannelId)
        {
            ChannelId = channelId;
            SourceNodeId = sourceNodeId;
            Settings = settings;
            TransportChannelId = transportChannelId;

            m_transport = transport;
            m_isSource = isSource;
            m_sendQueue = new DataChannelSendQueue(transport.BufferManager);
            m_receiveWindow = new DataChannelReceiveWindow(
                DataChannelConstants.MinReplayWindow,
                settings.MaxGapRuns);
            m_sendWindow = new DataChannelSendWindow(settings.InitialCredit);
            m_receiveCredit = new DataChannelReceiveCredit(settings.InitialCredit);
            m_delivery = Channel.CreateUnbounded<DataChannelMessage>(
                new UnboundedChannelOptions
                {
                    SingleReader = false,
                    SingleWriter = true
                });
            m_deliveryByteLimit = DeliveryQueueByteLimit(settings);
            m_previousMaxFrameSize = settings.MaxFrameSize;

            // A direction that carries no payload is considered ended at
            // open, so a single END closes the channel (5.11).
            m_outboundEnded = !settings.CanSendData(isSource);
            m_inboundEnded = !settings.CanSendData(!isSource);

            m_state = DataChannelState.Opening;
            StartTime = transport.TimeProvider.GetUtcNow().UtcDateTime;
        }

        /// <summary>
        /// The identifier of the channel within its SecureChannel. Never
        /// zero, which is reserved for connection control.
        /// </summary>
        public uint ChannelId { get; }

        /// <summary>
        /// The data channel source the channel was opened on.
        /// </summary>
        public NodeId SourceNodeId { get; }

        /// <summary>
        /// The parameters in force.
        /// </summary>
        public DataChannelSettings Settings { get; private set; }

        /// <summary>
        /// The underlying transport identifier: the QUIC stream id over
        /// opc.quic, zero for inline framing.
        /// </summary>
        public ulong TransportChannelId { get; }

        /// <summary>
        /// When the channel entered Open.
        /// </summary>
        public DateTime StartTime { get; private set; }

        /// <summary>
        /// The lifecycle state. On a channel whose send direction is
        /// stalled while its receive direction is open, this reports the
        /// state of the direction this peer is sending in.
        /// </summary>
        public DataChannelState State
        {
            get
            {
                lock (m_lock)
                {
                    return m_state;
                }
            }
        }

        /// <summary>
        /// The StatusCode that took the channel to Closed or Faulted.
        /// </summary>
        public StatusCode Status
        {
            get
            {
                lock (m_lock)
                {
                    return m_status;
                }
            }
        }

        /// <summary>
        /// Raised for every state transition except Open to Paused and
        /// back, which is rate limited to at most one Event per second.
        /// </summary>
        public event EventHandler<DataChannelStateChangedEventArgs>? StateChanged;

        /// <summary>
        /// Enqueues payload for transmission. The frame is assigned its
        /// FrameSequenceNumber here, which is what allows a GAP frame to
        /// name a frame that was never transmitted.
        /// </summary>
        /// <param name="payload">The payload, which shall not exceed the
        /// negotiated MaxFrameSize.</param>
        /// <param name="flags">The message delimiting and marker flags.
        /// Droppable is honoured only on a channel whose delivery mode
        /// permits discard and whose FrameDeadline is non zero.</param>
        /// <exception cref="ServiceResultException">The channel is not
        /// accepting payload in this direction, or the payload is
        /// larger than one frame.</exception>
        public void Write(ReadOnlySpan<byte> payload, DataChannelFrameFlags flags)
        {
            if ((flags & (DataChannelFrameFlags)DataChannelConstants.ReservedFlagMask) != 0)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidArgument,
                    "The flag bits held back for a future revision shall be zero.");
            }

            if (payload.Length > Settings.MaxFrameSize)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadDataChannelLimitsExceeded,
                    "The payload of {0} bytes exceeds the negotiated MaxFrameSize of {1}.",
                    payload.Length,
                    Settings.MaxFrameSize);
            }

            long deadline = 0;

            lock (m_lock)
            {
                if (m_state is DataChannelState.Closed or DataChannelState.Faulted)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadDataChannelClosed,
                        "The data channel is {0}.",
                        m_state);
                }

                if (!Settings.CanSendData(m_isSource))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadDataChannelDirectionUnsupported,
                        "This peer does not send payload on a {0} channel.",
                        Settings.Direction);
                }

                // A sender shall not enqueue new payload in a direction
                // that is Closing. A direction still Open because only the
                // peer half closed is unaffected (5.13).
                if (m_outboundClosing || m_outboundEnded)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadDataChannelClosed,
                        "The send direction of the data channel is closing.");
                }

                if ((flags & DataChannelFrameFlags.Droppable) != 0)
                {
                    if (!Settings.AllowsDiscard || Settings.FrameDeadline <= 0)
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadDeliveryModeUnsupported,
                            "Droppable requires a delivery mode that permits discard and a non zero FrameDeadline.");
                    }

                    deadline = Settings.ComputeDeadline(m_transport.TimeProvider);
                }

                m_sendQueue.Enqueue(payload, flags, deadline);
            }

            SendReady?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Reads the next unit of payload. The returned message shall be
        /// disposed: that is what returns its buffer and releases the flow
        /// control credit it occupied.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The message, or null when the peer has ended its
        /// direction and everything it sent has been read.</returns>
        public async ValueTask<DataChannelMessage?> ReadAsync(CancellationToken ct)
        {
            try
            {
                return await m_delivery.Reader.ReadAsync(ct).ConfigureAwait(false);
            }
            catch (ChannelClosedException)
            {
                return null;
            }
        }

        /// <summary>
        /// The counters this channel publishes through
        /// DataChannelDiagnosticsDataType.
        /// </summary>
        public DataChannelDiagnosticsDataType GetDiagnostics()
        {
            lock (m_lock)
            {
                return new DataChannelDiagnosticsDataType
                {
                    ChannelId = ChannelId,
                    FramesSent = m_framesSent,
                    FramesReceived = m_framesReceived,
                    BytesSent = m_bytesSent,
                    BytesReceived = m_bytesReceived,
                    FramesDiscarded = m_framesDiscarded,
                    CreditStalls = m_transport.HasTransportFlowControl ? 0 : m_sendWindow.Stalls,
                    RoundTripTime = m_roundTripTime,
                    LastGapSequenceNumber = m_lastGapSequenceNumber
                };
            }
        }

        /// <summary>
        /// The runtime state this channel publishes through
        /// DataChannelStatusDataType.
        /// </summary>
        public DataChannelStatusDataType GetStatus()
        {
            lock (m_lock)
            {
                return new DataChannelStatusDataType
                {
                    ChannelId = ChannelId,
                    SourceNodeId = SourceNodeId,
                    State = m_state,
                    Parameters = Settings.ToParameters(),
                    TransportChannelId = TransportChannelId,
                    StartTime = StartTime
                };
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            lock (m_lock)
            {
                m_sendQueue.Clear();
            }

            m_delivery.Writer.TryComplete();

            while (m_delivery.Reader.TryRead(out DataChannelMessage? message))
            {
                message.Dispose();
            }
        }

        /// <summary>
        /// Raised when payload or a control frame becomes available, so
        /// the manager's scheduler can wake.
        /// </summary>
        internal event EventHandler? SendReady;

        /// <summary>
        /// The deficit counter the scheduler maintains for this channel.
        /// </summary>
        internal long Deficit
        {
            get => m_sendQueue.Deficit;
            set => m_sendQueue.Deficit = value;
        }

        /// <summary>
        /// The quantum added to the deficit each scheduling round:
        /// (Priority + 1) x MaxFrameSize bytes (5.7).
        /// </summary>
        internal long Quantum => (Settings.Priority + 1L) * Settings.MaxFrameSize;

        /// <summary>
        /// True when this peer is the data channel source.
        /// </summary>
        internal bool IsSource => m_isSource;

        /// <summary>
        /// Moves the channel from Opening to Open once the
        /// OpenDataChannel response has been handed to the transport.
        /// </summary>
        internal void MarkOpen()
        {
            lock (m_lock)
            {
                if (m_state != DataChannelState.Opening)
                {
                    return;
                }

                m_state = DataChannelState.Open;
                StartTime = m_transport.TimeProvider.GetUtcNow().UtcDateTime;
            }

            RaiseStateChanged(DataChannelState.Open, StatusCodes.Good);
        }

        /// <summary>
        /// Applies parameters revised by ModifyDataChannel. A reduced
        /// MaxFrameSize takes effect from the next logical message
        /// boundary, and a revised FrameDeadline applies only to frames
        /// enqueued afterwards.
        /// </summary>
        /// <param name="settings">The new parameters.</param>
        internal void ApplyRevisedSettings(DataChannelSettings settings)
        {
            lock (m_lock)
            {
                m_previousMaxFrameSize = Settings.MaxFrameSize > settings.MaxFrameSize
                    ? Settings.MaxFrameSize
                    : settings.MaxFrameSize;
                Settings = settings;
            }
        }

        /// <summary>
        /// Handles one inbound frame addressed to this channel.
        /// </summary>
        /// <param name="frame">The decoded frame.</param>
        /// <param name="status">The StatusCode to reset the channel
        /// with, when the action calls for it.</param>
        internal DataChannelFrameAction HandleFrame(
            in DataChannelFrame frame,
            out StatusCode status)
        {
            status = StatusCodes.Good;

            switch (frame.FrameType)
            {
                case DataChannelFrameType.Data:
                    return HandleData(frame, out status);
                case DataChannelFrameType.Credit:
                    return HandleCredit(frame, out status);
                case DataChannelFrameType.Gap:
                    return HandleGap(frame, out status);
                case DataChannelFrameType.Reset:
                    HandleReset(frame.Status);
                    return DataChannelFrameAction.Accepted;
                case DataChannelFrameType.End:
                    HandleEnd();
                    return DataChannelFrameAction.Accepted;
                case DataChannelFrameType.Ping:
                    return HandlePing(frame, out status);
                case DataChannelFrameType.Pong:
                    HandlePong(frame.Timestamp);
                    return DataChannelFrameAction.Accepted;
                default:
                    status = StatusCodes.BadDataChannelLimitsExceeded;
                    return DataChannelFrameAction.ResetChannel;
            }
        }

        /// <summary>
        /// Takes the next control frame, which is exempt from credit and
        /// from the scheduler's deficit.
        /// </summary>
        /// <param name="frame">The frame.</param>
        internal bool TryDequeueControl(out DataChannelFrame frame)
        {
            lock (m_lock)
            {
                return m_sendQueue.TryDequeueControl(out frame);
            }
        }

        internal bool HasPendingControlFrames
        {
            get
            {
                lock (m_lock)
                {
                    return m_sendQueue.HasControlFrames;
                }
            }
        }

        /// <summary>
        /// The payload length of the frame at the head of the send queue.
        /// </summary>
        /// <param name="payloadLength">The length.</param>
        internal bool TryPeekPayloadLength(out int payloadLength)
        {
            lock (m_lock)
            {
                return m_sendQueue.TryPeekPayloadLength(out payloadLength);
            }
        }

        /// <summary>
        /// Takes the DATA frame at the head of the send queue and spends
        /// the channel window on it when the data channel layer owns flow
        /// control.
        /// </summary>
        /// <param name="frame">The frame.</param>
        /// <param name="buffer">The pooled payload buffer, to be released
        /// once the frame has been written.</param>
        internal bool TryDequeuePayload(out DataChannelFrame frame, out byte[]? buffer)
        {
            lock (m_lock)
            {
                if (!m_sendQueue.TryDequeuePayload(ChannelId, out frame, out buffer))
                {
                    return false;
                }

                if (!m_transport.HasTransportFlowControl)
                {
                    m_sendWindow.TryConsume(frame.Payload.Length);
                }

                m_framesSent++;
                m_bytesSent += (ulong)frame.Payload.Length;
                return true;
            }
        }

        /// <summary>
        /// Returns a payload buffer taken by
        /// <see cref="TryDequeuePayload"/>.
        /// </summary>
        /// <param name="buffer">The buffer.</param>
        internal void ReleaseSendBuffer(byte[]? buffer)
        {
            m_sendQueue.ReleaseFrame(buffer);
        }

        /// <summary>
        /// True when the data channel send window cannot carry a frame of
        /// the given size. Entry to and exit from Paused use this same test.
        /// </summary>
        /// <param name="payloadLength">The payload length of the head
        /// frame.</param>
        internal bool IsSendBlocked(int payloadLength)
        {
            if (m_transport.HasTransportFlowControl)
            {
                return false;
            }

            lock (m_lock)
            {
                return m_sendWindow.IsBlockedBy(payloadLength);
            }
        }

        /// <summary>
        /// Discards queued frames whose deadline has passed and queues
        /// one GAP frame per contiguous run.
        /// </summary>
        /// <param name="nowTicks">The sender's clock.</param>
        internal void ExpireAndReportGaps(long nowTicks)
        {
            if (!Settings.AllowsDiscard)
            {
                return;
            }

            lock (m_lock)
            {
                m_gapRuns.Clear();
                int discarded = m_sendQueue.ExpireDroppable(nowTicks, m_gapRuns);

                if (discarded == 0)
                {
                    return;
                }

                m_framesDiscarded += (ulong)discarded;

                for (int ii = 0; ii < m_gapRuns.Count; ii++)
                {
                    DataChannelGapRun run = m_gapRuns[ii];
                    m_lastGapSequenceNumber = run.Last;

                    m_sendQueue.EnqueueControl(DataChannelFrame.Gap(
                        ChannelId,
                        m_sendQueue.TakeSequenceNumber(),
                        run.First,
                        run.Last));
                }
            }
        }

        /// <summary>
        /// Marks the send direction Paused or Open, rate limiting the
        /// Event so that a saturated media channel does not generate one
        /// per credit stall.
        /// </summary>
        /// <param name="paused">True to enter Paused.</param>
        internal void SetPaused(bool paused)
        {
            bool raise = false;
            DataChannelState state;

            lock (m_lock)
            {
                if (m_state is not (DataChannelState.Open or DataChannelState.Paused))
                {
                    return;
                }

                state = paused ? DataChannelState.Paused : DataChannelState.Open;

                if (m_state == state)
                {
                    return;
                }

                m_state = state;

                long now = m_transport.TimeProvider.GetTimestamp();
                double elapsed = m_lastPauseEvent == 0
                    ? double.MaxValue
                    : m_transport.TimeProvider
                        .GetElapsedTime(m_lastPauseEvent, now).TotalMilliseconds;

                if (elapsed >= DataChannelConstants.PausedEventInterval)
                {
                    m_lastPauseEvent = now;
                    raise = true;
                }
            }

            if (raise)
            {
                RaiseStateChanged(state, StatusCodes.Good);
            }
        }

        /// <summary>
        /// Begins an orderly close of every direction this peer owns.
        /// Frames already queued still flow, and END follows the last of
        /// them.
        /// </summary>
        public void Close()
        {
            lock (m_lock)
            {
                if (m_state is DataChannelState.Closed or DataChannelState.Faulted)
                {
                    return;
                }

                if (m_outboundEnded || m_outboundClosing)
                {
                    return;
                }

                m_outboundClosing = true;
                m_closingSince = m_transport.TimeProvider.GetTimestamp();
                m_state = DataChannelState.Closing;
            }

            RaiseStateChanged(DataChannelState.Closing, StatusCodes.Good);
            SendReady?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Queues the END frame once the drain has completed, and
        /// enforces DrainTimeout on this peer's own drain. It never
        /// bounds the wait for the peer's reverse END.
        /// </summary>
        internal void PumpClosing()
        {
            bool faulted = false;
            bool ended = false;

            lock (m_lock)
            {
                if (!m_outboundClosing || m_outboundEnded)
                {
                    return;
                }

                if (m_sendQueue.HasPayload)
                {
                    double elapsed = m_transport.TimeProvider
                        .GetElapsedTime(m_closingSince).TotalMilliseconds;

                    if (elapsed > Settings.DrainTimeout)
                    {
                        m_sendQueue.Clear();
                        m_state = DataChannelState.Faulted;
                        m_status = StatusCodes.BadTimeout;
                        faulted = true;
                    }
                    else
                    {
                        return;
                    }
                }

                if (!faulted)
                {
                    m_sendQueue.EnqueueControl(DataChannelFrame.End(
                        ChannelId,
                        m_sendQueue.TakeSequenceNumber()));
                    m_outboundEnded = true;
                    m_outboundClosing = false;
                    ended = true;

                    if (m_inboundEnded)
                    {
                        m_state = DataChannelState.Closed;
                        m_status = StatusCodes.Good;
                    }
                }
            }

            if (faulted)
            {
                CompleteDelivery();
                RaiseStateChanged(DataChannelState.Faulted, StatusCodes.BadTimeout);
                return;
            }

            if (ended)
            {
                SendReady?.Invoke(this, EventArgs.Empty);

                if (State == DataChannelState.Closed)
                {
                    CompleteDelivery();
                    RaiseStateChanged(DataChannelState.Closed, StatusCodes.Good);
                }
            }
        }

        /// <summary>
        /// Queues a RESET frame and moves to Closed or Faulted according
        /// to the StatusCode it carries. A RESET carrying Good is an
        /// orderly discard and close; a Bad value is an abort.
        /// </summary>
        /// <param name="reason">The StatusCode.</param>
        public void Reset(StatusCode reason)
        {
            lock (m_lock)
            {
                if (m_state is DataChannelState.Closed or DataChannelState.Faulted)
                {
                    return;
                }

                m_sendQueue.Clear();
                m_sendQueue.EnqueueControl(DataChannelFrame.Reset(
                    ChannelId,
                    m_sendQueue.TakeSequenceNumber(),
                    reason));
            }

            SendReady?.Invoke(this, EventArgs.Empty);
            ApplyTerminalState(reason);
        }

        /// <summary>
        /// Aborts the channel without emitting a frame, used when the
        /// SecureChannel, the Session or the authorization behind it is
        /// gone.
        /// </summary>
        /// <param name="reason">Why.</param>
        internal void Abort(StatusCode reason)
        {
            lock (m_lock)
            {
                m_sendQueue.Clear();
            }

            ApplyTerminalState(reason);
        }

        /// <summary>
        /// Queues a PING, honouring the one outstanding probe and one per
        /// second bounds that keep it from becoming an amplification
        /// surface.
        /// </summary>
        public bool TryPing()
        {
            lock (m_lock)
            {
                if (m_state is DataChannelState.Closed or DataChannelState.Faulted)
                {
                    return false;
                }

                if (m_pingOutstanding)
                {
                    return false;
                }

                long now = m_transport.TimeProvider.GetTimestamp();

                if (m_lastPingSent != 0 &&
                    m_transport.TimeProvider.GetElapsedTime(m_lastPingSent, now)
                        .TotalMilliseconds < DataChannelConstants.MinPingInterval)
                {
                    return false;
                }

                m_lastPingSent = now;
                m_pingOutstanding = true;

                m_sendQueue.EnqueueControl(DataChannelFrame.Ping(
                    ChannelId,
                    m_sendQueue.TakeSequenceNumber(),
                    now));
            }

            SendReady?.Invoke(this, EventArgs.Empty);
            return true;
        }

        /// <summary>
        /// Returns a delivered payload buffer and releases the credit it
        /// occupied, which is what obliges a CREDIT frame once the
        /// outstanding grant has fallen below the threshold.
        /// </summary>
        /// <param name="buffer">The pooled buffer.</param>
        /// <param name="length">The payload length.</param>
        internal void ReleaseMessage(byte[]? buffer, int length)
        {
            if (buffer != null)
            {
                m_transport.BufferManager.ReturnBuffer(buffer, nameof(DataChannel));
            }

            lock (m_lock)
            {
                m_deliveryBytes -= length + DeliveryQueueFrameOverhead;

                if (m_deliveryBytes < 0)
                {
                    m_deliveryBytes = 0;
                }
            }

            if (m_transport.HasTransportFlowControl)
            {
                return;
            }

            bool queued = false;

            lock (m_lock)
            {
                m_receiveCredit.Release(length);

                if (m_receiveCredit.TryTakeReplenishment(Settings.MaxFrameSize, out uint amount))
                {
                    m_sendQueue.EnqueueControl(DataChannelFrame.Credit(
                        ChannelId,
                        m_sendQueue.TakeSequenceNumber(),
                        amount,
                        amount));
                    queued = true;
                }
            }

            if (queued)
            {
                SendReady?.Invoke(this, EventArgs.Empty);
            }
        }

        private DataChannelFrameAction HandleData(
            in DataChannelFrame frame,
            out StatusCode status)
        {
            status = StatusCodes.Good;

            // Direction enforcement (5.3). The peer sends payload only in
            // the direction the negotiated Direction permits it.
            if (!Settings.CanSendData(!m_isSource))
            {
                status = StatusCodes.BadDataChannelDirectionUnsupported;
                return DataChannelFrameAction.ResetChannel;
            }

            if (frame.IsDroppable && !Settings.AllowsDiscard)
            {
                status = StatusCodes.BadDeliveryModeUnsupported;
                return DataChannelFrameAction.ResetChannel;
            }

            DataChannelReceiveOutcome outcome;
            uint missingFrom;
            uint missingTo;
            byte[]? buffer = null;
            int length = frame.Payload.Length;

            lock (m_lock)
            {
                if (m_inboundEnded)
                {
                    status = StatusCodes.BadDataChannelClosed;
                    return DataChannelFrameAction.ResetChannel;
                }

                if (length > m_previousMaxFrameSize)
                {
                    status = StatusCodes.BadDataChannelLimitsExceeded;
                    return DataChannelFrameAction.ResetChannel;
                }

                if (!m_transport.HasTransportFlowControl &&
                    !m_receiveCredit.TryAccount(length))
                {
                    status = StatusCodes.BadDataChannelCreditExceeded;
                    return DataChannelFrameAction.ResetChannel;
                }

                outcome = m_receiveWindow.Accept(
                    frame.FrameSequenceNumber,
                    out missingFrom,
                    out missingTo);

                switch (outcome)
                {
                    case DataChannelReceiveOutcome.Reset:
                        status = StatusCodes.BadDataChannelClosed;
                        return DataChannelFrameAction.ResetChannel;
                    case DataChannelReceiveOutcome.DiscardGapped:
                    case DataChannelReceiveOutcome.DiscardDuplicate:
                        if (!m_transport.HasTransportFlowControl)
                        {
                            m_receiveCredit.Release(length);
                        }
                        return DataChannelFrameAction.Accepted;
                    default:
                        break;
                }

                m_framesReceived++;
                m_bytesReceived += (ulong)length;

                // A reduced MaxFrameSize takes effect from the first frame
                // carrying MessageStart that fits it (Part 4 errata 5.2).
                if (m_previousMaxFrameSize > Settings.MaxFrameSize &&
                    (frame.Flags & DataChannelFrameFlags.MessageStart) != 0 &&
                    length <= Settings.MaxFrameSize)
                {
                    m_previousMaxFrameSize = Settings.MaxFrameSize;
                }

                if (length > 0)
                {
                    buffer = m_transport.BufferManager.TakeBuffer(length, nameof(DataChannel));
                    frame.Payload.Span.CopyTo(buffer.AsSpan(0, length));
                }
            }

            DataChannelMessage? message = null;

            try
            {
                message = new DataChannelMessage(
                    this,
                    buffer,
                    length,
                    frame.Flags,
                    frame.FrameSequenceNumber,
                    outcome == DataChannelReceiveOutcome.DeliverWithGap
                        ? StatusCodes.UncertainDataDiscarded
                        : StatusCodes.Good,
                    outcome == DataChannelReceiveOutcome.DeliverWithGap ? missingFrom : 0,
                    outcome == DataChannelReceiveOutcome.DeliverWithGap ? missingTo : 0);

                long queued;

                // Accounted as soon as the message exists, because every
                // message is either handed to the queue and released by the
                // consumer or disposed below - and both paths run through
                // ReleaseMessage, which is the one place the budget is
                // given back.
                lock (m_lock)
                {
                    m_deliveryBytes += length + DeliveryQueueFrameOverhead;
                    queued = m_deliveryBytes;
                }

                if (queued > m_deliveryByteLimit)
                {
                    // Waiting for the application to consume would stall the
                    // reader that also carries MSG, OPN and CLO on this
                    // SecureChannel, turning per-channel backpressure into a
                    // connection-wide stall (§5.8). The channel is reset
                    // instead so the fault stays where it belongs.
                    status = StatusCodes.BadDataChannelCreditExceeded;
                    return DataChannelFrameAction.ResetChannel;
                }

                if (!m_delivery.Writer.TryWrite(message))
                {
                    status = StatusCodes.BadDataChannelClosed;
                    return DataChannelFrameAction.ResetChannel;
                }

                message = null;
            }
            catch (ChannelClosedException)
            {
                status = StatusCodes.BadDataChannelClosed;
                return DataChannelFrameAction.ResetChannel;
            }
            finally
            {
                message?.Dispose();
            }

            return DataChannelFrameAction.Accepted;
        }

        private DataChannelFrameAction HandleCredit(
            in DataChannelFrame frame,
            out StatusCode status)
        {
            status = StatusCodes.Good;

            // Over a transport with its own flow control no CREDIT frame
            // is sent and a receiver ignores one (7.4).
            if (m_transport.HasTransportFlowControl)
            {
                return DataChannelFrameAction.Accepted;
            }

            bool resumed;

            lock (m_lock)
            {
                if (!m_sendWindow.TryGrant(frame.ChannelCredit))
                {
                    status = StatusCodes.BadDataChannelCreditExceeded;
                    return DataChannelFrameAction.ResetChannel;
                }

                resumed = m_state == DataChannelState.Paused &&
                    m_sendQueue.TryPeekPayloadLength(out int head) &&
                    !m_sendWindow.IsBlockedBy(head);
            }

            if (resumed)
            {
                SetPaused(false);
            }

            SendReady?.Invoke(this, EventArgs.Empty);
            return DataChannelFrameAction.Accepted;
        }

        private DataChannelFrameAction HandleGap(
            in DataChannelFrame frame,
            out StatusCode status)
        {
            status = StatusCodes.Good;

            // On a reliable channel nothing may be discarded, so a GAP is
            // a protocol error (5.10).
            if (!Settings.AllowsDiscard)
            {
                status = StatusCodes.BadDeliveryModeUnsupported;
                return DataChannelFrameAction.ResetChannel;
            }

            // A GAP arriving in a direction that carries no DATA is what a
            // peer would otherwise use to create unbounded state at no
            // cost to itself, because control frames are credit exempt and
            // only DATA advances HighestReceived (5.2.1).
            if (!Settings.CanSendData(!m_isSource))
            {
                status = StatusCodes.BadDataChannelLimitsExceeded;
                return DataChannelFrameAction.ResetChannel;
            }

            lock (m_lock)
            {
                m_receiveWindow.RecordGap(frame.FirstDiscarded, frame.LastDiscarded);
                m_lastGapSequenceNumber = frame.LastDiscarded;
            }

            return DataChannelFrameAction.Accepted;
        }

        private void HandleReset(StatusCode reason)
        {
            lock (m_lock)
            {
                m_sendQueue.Clear();
            }

            ApplyTerminalState(reason);
        }

        private void HandleEnd()
        {
            bool closed = false;

            lock (m_lock)
            {
                // Receiving END marks the peer's direction ended and
                // nothing more. It never starts the local drain clock and
                // never stops the local application enqueueing: that is
                // what makes END a half close rather than a close (5.13).
                m_inboundEnded = true;

                if (m_outboundEnded &&
                    m_state is not (DataChannelState.Closed or DataChannelState.Faulted))
                {
                    m_state = DataChannelState.Closed;
                    m_status = StatusCodes.Good;
                    closed = true;
                }
            }

            CompleteDelivery();

            if (closed)
            {
                RaiseStateChanged(DataChannelState.Closed, StatusCodes.Good);
            }
        }

        /// <summary>
        /// Answers a PING with a PONG echoing its Timestamp verbatim, subject
        /// to the rate bound of Part 6 errata §5.11.
        /// </summary>
        /// <remarks>
        /// PING is exempt from flow control and compels a PONG ahead of queued
        /// payload, so without a bound it is an amplification surface: a peer
        /// emitting PING at line rate on every open ChannelId compels the other
        /// end to answer at line rate ahead of its own traffic, with no window
        /// to close against it. §5.11 bounds a sender to one unanswered PING
        /// and one PING per second per ChannelId, and lets a receiver discard
        /// what breaches that and reset the channel once the breach persists.
        /// Both halves are enforced here; enforcing only the sending half
        /// bounds a well-behaved peer and leaves a hostile one unbounded.
        /// </remarks>
        /// <param name="frame">The PING.</param>
        /// <param name="status">The StatusCode to reset with, when the rate
        /// bound has been breached often enough to be deliberate.</param>
        private DataChannelFrameAction HandlePing(
            in DataChannelFrame frame,
            out StatusCode status)
        {
            status = StatusCodes.Good;

            lock (m_lock)
            {
                long now = m_transport.TimeProvider.GetTimestamp();

                if (m_hasAnsweredPing &&
                    m_transport.TimeProvider.GetElapsedTime(m_lastPingAnswered, now)
                        .TotalMilliseconds <
                    DataChannelConstants.MinPingInterval *
                        DataChannelConstants.PingResponseIntervalTolerance)
                {
                    if (++m_pingRateViolations > DataChannelConstants.MaxPingRateViolations)
                    {
                        status = StatusCodes.BadDataChannelLimitsExceeded;
                        return DataChannelFrameAction.ResetChannel;
                    }

                    // Discarded, not answered. The prober keeps no state on the
                    // responder, so a dropped PONG costs it one measurement it
                    // was not entitled to take.
                    return DataChannelFrameAction.Accepted;
                }

                m_pingRateViolations = 0;
                m_lastPingAnswered = now;
                m_hasAnsweredPing = true;

                // The Timestamp is echoed verbatim: it is opaque to the
                // responder, which shall not interpret, validate or rescale it.
                m_sendQueue.EnqueueControl(DataChannelFrame.Pong(
                    ChannelId,
                    m_sendQueue.TakeSequenceNumber(),
                    frame.Timestamp));
            }

            SendReady?.Invoke(this, EventArgs.Empty);
            return DataChannelFrameAction.Accepted;
        }

        private void HandlePong(long timestamp)
        {
            lock (m_lock)
            {
                m_pingOutstanding = false;
                m_roundTripTime = m_transport.TimeProvider
                    .GetElapsedTime(timestamp).TotalMilliseconds;
            }
        }

        private void QueueControl(in DataChannelFrame frame)
        {
            lock (m_lock)
            {
                m_sendQueue.EnqueueControl(frame);
            }

            SendReady?.Invoke(this, EventArgs.Empty);
        }

        private void ApplyTerminalState(StatusCode reason)
        {
            DataChannelState state = StatusCode.IsBad(reason)
                ? DataChannelState.Faulted
                : DataChannelState.Closed;

            lock (m_lock)
            {
                if (m_state is DataChannelState.Closed or DataChannelState.Faulted)
                {
                    return;
                }

                m_state = state;
                m_status = reason;
            }

            CompleteDelivery();
            RaiseStateChanged(state, reason);
        }

        private void CompleteDelivery()
        {
            m_delivery.Writer.TryComplete();
        }

        private static long DeliveryQueueByteLimit(DataChannelSettings settings)
        {
            // Credit bounds the payload a receiver holds, but it does not
            // bound the queue: a frame carrying no payload consumes no credit
            // while still occupying a header and a queue slot. §7.4 draws the
            // same distinction for the unknown-ChannelId buffer, which it
            // bounds by encoded frame bytes rather than payload bytes and for
            // exactly that reason.
            //
            // The budget is therefore the granted window plus the same
            // headroom §7.4 allows for frames a receiver cannot yet place, so
            // payload arriving within credit is never refused and a flood of
            // empty frames is still bounded.
            return (long)settings.InitialCredit +
                ((long)Math.Max(1U, settings.MaxFrameSize) *
                    DataChannelConstants.UnknownChannelBufferFrames);
        }

        private void RaiseStateChanged(DataChannelState state, StatusCode status)
        {
            StateChanged?.Invoke(this, new DataChannelStateChangedEventArgs(ChannelId, state, status));
        }

        /// <summary>
        /// What one queued frame costs against the delivery budget on top of
        /// its payload, so an empty frame is never free.
        /// </summary>
        private const int DeliveryQueueFrameOverhead = DataChannelConstants.StreamHeaderSize;

        private readonly Lock m_lock = new();
        private readonly IDataChannelTransport m_transport;
        private readonly DataChannelSendQueue m_sendQueue;
        private readonly DataChannelReceiveWindow m_receiveWindow;
        private readonly DataChannelSendWindow m_sendWindow;
        private readonly DataChannelReceiveCredit m_receiveCredit;
        private readonly Channel<DataChannelMessage> m_delivery;
        private readonly long m_deliveryByteLimit;
        private readonly List<DataChannelGapRun> m_gapRuns = [];
        private readonly bool m_isSource;
        private long m_deliveryBytes;
        private DataChannelState m_state;
        private StatusCode m_status = StatusCodes.Good;
        private bool m_outboundEnded;
        private bool m_inboundEnded;
        private bool m_outboundClosing;
        private bool m_pingOutstanding;
        private long m_closingSince;
        private long m_lastPingSent;
        private long m_lastPingAnswered;
        private bool m_hasAnsweredPing;
        private int m_pingRateViolations;
        private long m_lastPauseEvent;
        private double m_roundTripTime;
        private uint m_lastGapSequenceNumber;
        private uint m_previousMaxFrameSize;
        private ulong m_framesSent;
        private ulong m_framesReceived;
        private ulong m_bytesSent;
        private ulong m_bytesReceived;
        private ulong m_framesDiscarded;
    }

    /// <summary>
    /// Reports a data channel state transition.
    /// </summary>
    public sealed class DataChannelStateChangedEventArgs : EventArgs
    {
        /// <summary>
        /// Creates the arguments.
        /// </summary>
        /// <param name="channelId">The channel whose state changed.</param>
        /// <param name="state">The state entered.</param>
        /// <param name="status">The StatusCode that caused a transition
        /// into Closed or Faulted.</param>
        public DataChannelStateChangedEventArgs(
            uint channelId,
            DataChannelState state,
            StatusCode status)
        {
            ChannelId = channelId;
            State = state;
            Status = status;
        }

        /// <summary>
        /// The channel whose state changed.
        /// </summary>
        public uint ChannelId { get; }

        /// <summary>
        /// The state entered.
        /// </summary>
        public DataChannelState State { get; }

        /// <summary>
        /// The StatusCode that caused the transition.
        /// </summary>
        public StatusCode Status { get; }
    }
}
