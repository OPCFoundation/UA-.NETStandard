// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Bindings.Native;
using Opc.Ua.DataChannels;
using Opc.Ua.Native.QuicGo;

namespace Opc.Ua.Bindings
{
    public sealed partial class WebTransportDataChannelConnection
    {
        /// <summary>
        /// Maps only defined common results from the standard WebTransport application-error range.
        /// Outbound reset actions are already mapped by H3 and are never passed through this inverse.
        /// </summary>
        internal static DataChannelResult MapPeerReset(ulong wireError)
        {
            const ulong first = 0x52e4a40fa8db;
            const ulong last = 0x52e5ac983162;
            if (wireError < first || wireError > last)
            {
                return DataChannelResult.Closed;
            }
            ulong offset = wireError - first;
            if (offset % 31 == 30)
            {
                return DataChannelResult.Closed;
            }
            ulong application = offset - offset / 31;
            if (application > uint.MaxValue)
            {
                return DataChannelResult.Closed;
            }
            var result = (DataChannelResult)application;
            return result != DataChannelResult.Good && Enum.IsDefined(result) ? result : DataChannelResult.Closed;
        }

        /// <summary>
        /// Starts real receive/accept pumps and drains actual initial control/QPACK writes before HELLO.
        /// </summary>
        private void StartIngress()
        {
            lock (m_gate)
            {
                StartJobLocked(() => AcceptStreamsAsync(false));
                StartJobLocked(() => AcceptStreamsAsync(true));
                StartJobLocked(ReceiveDatagramsAsync);
                StartJobLocked(MonitorNativeConnectionAsync);
                DrainLocked();
            }
        }

        /// <summary>
        /// Admits only actual allocated/accepted streams and bounded provisional association state.
        /// </summary>
        private StreamState AddPhysicalStream(NativeQuicStream native)
        {
            lock (m_gate)
            {
                if (m_closing || !ReferenceEquals(native.Connection, NativeConnection) ||
                    m_streams.Count >= m_options.MaxStreamHistory ||
                    m_streams.ContainsKey(native.StreamId))
                {
                    throw new WebTransportException("The physical native stream cannot be admitted.");
                }
                bool provisional = native.IsBidirectional && native.StreamId != 0;
                if (provisional && m_pendingStreams >= m_options.MaxPendingStreams)
                {
                    throw new WebTransportException("The provisional WebTransport stream limit was reached.");
                }
                var state = new StreamState(native);
                m_streams.Add(native.StreamId, state);
                if (provisional)
                {
                    m_pendingStreams++;
                    StartJobLocked(() => MonitorBindingAsync(state));
                }
                else
                {
                    state.Bound.TrySetResult();
                    state.PrefixComplete.TrySetResult();
                }
                if (native.IsBidirectional || !native.IsLocal)
                {
                    StartJobLocked(() => RunStreamIngressAsync(state, false));
                }
                if (native.IsBidirectional || native.IsLocal)
                {
                    StartJobLocked(() => RunStreamIngressAsync(state, true));
                }
                if (native.IsBidirectional)
                {
                    StartJobLocked(() => ObservePeerResetsAsync(state));
                }
                return state;
            }
        }

        /// <summary>
        /// Keeps cancellation of an explicitly closed flow local, including a pre-admission read cancellation.
        /// </summary>
        private async ValueTask RunStreamIngressAsync(StreamState state, bool stopObserver)
        {
            try
            {
                if (stopObserver)
                {
                    await ObserveStopAsync(state).ConfigureAwait(false);
                }
                else
                {
                    await ReceiveStreamAsync(state).ConfigureAwait(false);
                }
            }
            catch (WebTransportException exception) when (exception.Scope == 2 && !m_closing &&
                !state.Closing && !state.Associated && state.Stream.IsBidirectional &&
                state.Stream.StreamId != 0 && !ReferenceEquals(state, m_management))
            {
                await RejectUnassociatedStreamAsync(state, exception.ProtocolCode).ConfigureAwait(false);
            }
            catch (Exception) when (state.Closing || state.Retired || m_closing)
            {
                // The explicit flow/connection teardown owns these independent native directions.
            }
        }

        /// <summary>
        /// Retires a genuinely stream-scoped malformed prefix without inventing a WT session or reset receipt.
        /// Connection/session/critical-stream errors never take this path.
        /// </summary>
        private async ValueTask RejectUnassociatedStreamAsync(StreamState state, ulong errorCode)
        {
            Task? stop;
            lock (m_gate)
            {
                if (errorCode == 0)
                {
                    throw new WebTransportException("The stream-scoped rejection lacks a wire error.");
                }
                state.Closing = true;
                state.Bound.TrySetResult();
                stop = state.StopAction;
                errorCode = state.StopError ?? errorCode;
            }
            if (stop != null)
            {
                await stop.ConfigureAwait(false);
            }
            else
            {
                await StopNativeReceiveAsync(state, errorCode).ConfigureAwait(false);
            }
            NativeQuicCompletion reset = await state.Stream.ResetSendAsync(errorCode).ConfigureAwait(false);
            if (reset.NativeStatus is not (NativeQuicStatus.Success or NativeQuicStatus.Closed))
            {
                throw new NativeQuicTransportException(reset);
            }
            await state.Stream.CloseAsync(errorCode).ConfigureAwait(false);
            lock (m_gate)
            {
                RetireStreamLocked(state);
            }
        }

        /// <summary>
        /// Verifies an engine-parsed remote WT prefix against the accepted native handle and role.
        /// </summary>
        private void AssociatePeerStreamLocked(Http3Event metadata)
        {
            StreamState state = GetActualStreamLocked(metadata.Stream);
            if (metadata.Session != 0 || !state.Stream.IsBidirectional || state.Stream.IsLocal ||
                state.Associated || state.Stream.StreamId == 0 || state.Closing ||
                !m_established.Task.IsCompletedSuccessfully)
            {
                throw new WebTransportException("The native stream and WebTransport opener association disagree.");
            }
            state.Associated = true;
            state.PrefixComplete.TrySetResult();
            if (!NativeConnection.Peer.IsClient)
            {
                if (m_management == null)
                {
                    m_management = state;
                    m_pendingStreams--;
                    state.Bound.TrySetResult();
                    m_managementReady.TrySetResult();
                }
                else if (state.Stream.StreamId < m_management.Stream.StreamId)
                {
                    throw new WebTransportException(
                        "Management was not the initiator's first WT bidirectional stream.");
                }
            }
        }

        /// <summary>
        /// Accepts both actual QUIC directions; HTTP/3 decides the valid control and WT stream types.
        /// </summary>
        private async ValueTask AcceptStreamsAsync(bool unidirectional)
        {
            while (!m_shutdown.IsCancellationRequested)
            {
                bool reserved = false;
                try
                {
                    if (!unidirectional)
                    {
                        while (true)
                        {
                            Task capacity;
                            lock (m_gate)
                            {
                                m_lifetime.ThrowIfCancellationRequested();
                                if (HasBidirectionalCapacityLocked() &&
                                    m_pendingStreams < m_options.MaxPendingStreams)
                                {
                                    m_reservedBidirectional++;
                                    reserved = true;
                                    break;
                                }
                                capacity = m_changed.Task;
                            }
                            await capacity.WaitAsync(m_lifetime).ConfigureAwait(false);
                        }
                    }
                    NativeQuicStream stream = await NativeConnection.AcceptStreamAsync(
                        unidirectional, m_lifetime).ConfigureAwait(false);
                    lock (m_gate)
                    {
                        if (reserved)
                        {
                            m_reservedBidirectional--;
                            reserved = false;
                        }
                        AddPhysicalStream(stream);
                    }
                }
                catch (NativeQuicTransportException exception)
                    when (exception.Completion?.Status == NativeQuicStatus.Timeout)
                {
                    // The native deadline is finite; retrying an empty accept does not replay any stream bytes.
                }
                finally
                {
                    if (reserved)
                    {
                        lock (m_gate)
                        {
                            m_reservedBidirectional--;
                            PulseLocked();
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Preserves actual data-before-FIN/reset and waits for per-stream Core admission before reading more.
        /// </summary>
        private async ValueTask ReceiveStreamAsync(StreamState state)
        {
            NativeQuicStream native = state.Stream;
            while (!m_shutdown.IsCancellationRequested && !native.IsClosed)
            {
                NativeQuicCompletion read = await native.ReadAsync(m_lifetime).ConfigureAwait(false);
                if (state.Retired || state.PeerReset != null)
                {
                    return;
                }
                if (read.Payload.Length != 0)
                {
                    lock (m_gate)
                    {
                        m_hostBytes += read.Payload.Length;
                        if (m_hostBytes > m_options.MaxHostBufferedBytes)
                        {
                            m_hostBytes -= read.Payload.Length;
                            throw new WebTransportException("The host input memory limit was reached.");
                        }
                    }
                    try
                    {
                        int offset = 0;
                        while (offset < read.Payload.Length)
                        {
                            int length = Math.Min(m_options.InputSegmentSize, read.Payload.Length - offset);
                            await FeedStreamAsync(state, read.Payload.Slice(offset, length), false)
                                .ConfigureAwait(false);
                            offset += length;
                        }
                    }
                    finally
                    {
                        lock (m_gate)
                        {
                            m_hostBytes -= read.Payload.Length;
                            PulseLocked();
                        }
                    }
                }
                if (read.ReadOutcome == 1)
                {
                    await FeedStreamAsync(state, default, true).ConfigureAwait(false);
                    if (!native.IsBidirectional)
                    {
                        // The H3 engine rejects closure of critical uni streams. An accepted FIN therefore
                        // belongs to an ignored/unknown uni resource whose actual handle can be released.
                        await native.CloseAsync().ConfigureAwait(false);
                        lock (m_gate)
                        {
                            RetireStreamLocked(state);
                        }
                    }
                    return;
                }
                if (read.ReadOutcome == 2)
                {
                    if (read.Remote)
                    {
                        lock (m_gate)
                        {
                            FeedPeerResetLocked(state, read);
                        }
                        await state.ReceiveTail.ConfigureAwait(false);
                    }
                    else if (!state.Closing && !m_closing)
                    {
                        throw new WebTransportException("The local native receive direction closed unexpectedly.");
                    }
                    return;
                }
                if (read.Status is not (NativeQuicStatus.Success or NativeQuicStatus.Timeout))
                {
                    if (!state.Closing && !m_closing)
                    {
                        throw new NativeQuicTransportException(read);
                    }
                    return;
                }
            }
        }

        /// <summary>
        /// Feeds one exact segment and retains its owner through serialized event delivery.
        /// Retryable H3 input admission is nonconsuming; stream/protocol failures are not replayed.
        /// </summary>
        private async ValueTask FeedStreamAsync(StreamState state, ByteString payload, bool fin)
        {
            while (true)
            {
                Task changed;
                Task? delivered = null;
                lock (m_gate)
                {
                    m_lifetime.ThrowIfCancellationRequested();
                    if (state.PeerReset != null || state.Retired)
                    {
                        return;
                    }
                    if (TryCommandLocked(1, state.Stream.StreamId, fin ? 1UL : 0UL, 0,
                        payload, out Http3Error error))
                    {
                        delivered = state.ReceiveTail;
                    }
                    else if (!error.Retryable)
                    {
                        throw new WebTransportException(error);
                    }
                    changed = m_changed.Task;
                }
                if (delivered != null)
                {
                    await delivered.ConfigureAwait(false);
                    return;
                }
                await changed.WaitAsync(m_lifetime).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Feeds actual QUIC datagrams atomically; the native H3 engine removes its quarter-stream prefix once.
        /// </summary>
        private async ValueTask ReceiveDatagramsAsync()
        {
            while (!m_shutdown.IsCancellationRequested)
            {
                NativeQuicCompletion read = await NativeConnection.ReceiveDatagramAsync(m_lifetime)
                    .ConfigureAwait(false);
                if (read.Status == NativeQuicStatus.Timeout)
                {
                    continue;
                }
                if (read.Status != NativeQuicStatus.Success)
                {
                    throw new NativeQuicTransportException(read);
                }
                await RetryCommandAsync(2, 0, 0, read.Payload, m_lifetime).ConfigureAwait(false);
                Task received;
                lock (m_gate)
                {
                    received = m_datagramReceiveTail;
                }
                await received.ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Delivers WT payload after association, in stream order, then returns exactly the consumed payload credit.
        /// WT prefixes and datagrams are never counted as stream-consumption credit.
        /// </summary>
        private async ValueTask DeliverCoreAsync(
            StreamState state,
            Task previous,
            Http3Event metadata,
            ByteString payload)
        {
            await previous.ConfigureAwait(false);
            await m_attached.Task.WaitAsync(m_lifetime).ConfigureAwait(false);
            DataChannelResult result = state.PeerReset == null && !state.Retired
                ? await Context.ReceiveStreamAsync(
                    metadata.Stream, payload, metadata.Kind == 16, m_lifetime).ConfigureAwait(false)
                : DataChannelResult.Closed;
            if (payload.Length != 0 && !m_closing)
            {
                // A scoped rejection/reset consumes and discards the input; it must not strand WT window credit.
                await RetryCommandAsync(18, 0, (ulong)payload.Length, default, m_lifetime).ConfigureAwait(false);
            }
            // Management also carries flow-scoped replies; Core alone classifies their context-fatal failures.
            if (result != DataChannelResult.Good && !ReferenceEquals(state, m_management))
            {
                await NotifyStreamFailureAsync(state, result).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Delivers one intact ChannelId+common-record datagram to Core's bounded admission path.
        /// </summary>
        private async ValueTask DeliverDatagramAsync(ByteString payload)
        {
            await m_attached.Task.WaitAsync(m_lifetime).ConfigureAwait(false);
            await Context.ReceiveDatagramAsync(payload, m_lifetime).ConfigureAwait(false);
        }

        /// <summary>
        /// Notifies Core of transport reset without disguising it as an orderly directional EOF.
        /// Core RESET uses management, independently of the failed native flow stream.
        /// </summary>
        private async ValueTask NotifyStreamFailureAsync(StreamState state, DataChannelResult result)
        {
            if (ReferenceEquals(state, m_management))
            {
                RequestClose(result);
                return;
            }
            await m_attached.Task.WaitAsync(m_lifetime).ConfigureAwait(false);
            await Context.ReceiveStreamResetAsync(state.Stream.StreamId, result, m_lifetime).ConfigureAwait(false);
            if (state.Associated && !state.Closing)
            {
                await CloseFlowAsync(state.Stream.StreamId, result, m_lifetime).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Observes actual peer STOP_SENDING even while no body write is pending.
        /// </summary>
        private async ValueTask ObserveStopAsync(StreamState state)
        {
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(m_lifetime, state.Closed);
            while (!m_shutdown.IsCancellationRequested && !state.Stream.IsClosed)
            {
                NativeQuicCompletion stopped = await state.Stream.WaitStoppedAsync(lifetime.Token)
                    .ConfigureAwait(false);
                if (stopped.Remote)
                {
                    state.PeerStopped.TrySetResult(stopped);
                    if (state.Stream.IsBidirectional && state.Stream.StreamId != 0)
                    {
                        await state.PrefixComplete.Task.WaitAsync(lifetime.Token).ConfigureAwait(false);
                    }
                    lock (m_gate)
                    {
                        if (!state.Closing && !state.Retired)
                        {
                            CommandLocked(4, state.Stream.StreamId, stopped.ErrorCode);
                        }
                    }
                    return;
                }
                if (stopped.Status == NativeQuicStatus.Timeout)
                {
                    continue;
                }
                if (stopped.NativeStatus is NativeQuicStatus.Limit or NativeQuicStatus.Busy)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(10), m_clock, lifetime.Token).ConfigureAwait(false);
                    continue;
                }
                if (!state.Closing && !state.Stream.IsClosed && !m_closing)
                {
                    throw new NativeQuicTransportException(stopped);
                }
                return;
            }
        }

        /// <summary>
        /// Observes real peer RESET independently of a read blocked on bounded Core admission.
        /// Prefix parsing still precedes H3 reset input; no EOF, STOP or guessed reset evidence is synthesized.
        /// </summary>
        private async ValueTask ObservePeerResetsAsync(StreamState state)
        {
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(m_lifetime, state.Closed);
            try
            {
                while (!lifetime.IsCancellationRequested)
                {
                    NativeQuicCompletion reset = await state.Stream.WaitResetReceivedAsync(lifetime.Token)
                        .ConfigureAwait(false);
                    if (reset.Remote && reset.ReadOutcome == 2)
                    {
                        if (reset.NativeStatus != NativeQuicStatus.Success ||
                            reset.StreamId != state.Stream.StreamId || !reset.Payload.IsEmpty ||
                            reset.BytesTransferred != 0)
                        {
                            throw new WebTransportException("The native peer-reset observation is inconsistent.");
                        }
                        await state.PrefixComplete.Task.WaitAsync(lifetime.Token).ConfigureAwait(false);
                        lock (m_gate)
                        {
                            if (!m_closing)
                            {
                                FeedPeerResetLocked(state, reset);
                            }
                        }
                        return;
                    }
                    if (reset.Status == NativeQuicStatus.Timeout)
                    {
                        continue;
                    }
                    if (reset.NativeStatus is NativeQuicStatus.Limit or NativeQuicStatus.Busy)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(10), m_clock, lifetime.Token).ConfigureAwait(false);
                        continue;
                    }
                    if (!state.Closing && !state.Stream.IsClosed && !m_closing)
                    {
                        throw new NativeQuicTransportException(reset);
                    }
                    return;
                }
            }
            catch (Exception) when (state.Closing || state.Retired || m_closing)
            {
                // Explicit resource closure owns cancellation and drainage of this independent observation.
            }
        }

        /// <summary>
        /// Deduplicates actual Read/RESET_RECEIVED evidence before applying one H3 receive-reset transition.
        /// </summary>
        private void FeedPeerResetLocked(StreamState state, NativeQuicCompletion reset)
        {
            if (!reset.Remote || reset.ReadOutcome != 2 ||
                reset.ReliableSize > reset.FinalSize ||
                (reset.ReliableSize != 0 && reset.ResetFrameType != 0x24) ||
                (reset.ReliableSize == 0 && reset.ResetFrameType is not (0x04 or 0x24)))
            {
                throw new WebTransportException("The transport did not report actual received reset evidence.");
            }
            if (state.PeerReset is { } previous)
            {
                if (previous.ErrorCode != reset.ErrorCode || previous.FinalSize != reset.FinalSize ||
                    previous.ReliableSize != reset.ReliableSize || previous.ResetFrameType != reset.ResetFrameType)
                {
                    throw new WebTransportException("Received native reset observations disagree.");
                }
                return;
            }
            state.PeerReset = reset;
            CommandLocked(3, state.Stream.StreamId, reset.ErrorCode, reset.FinalSize);
        }

        /// <summary>
        /// Bounds pre-association and pre-OPEN native state even for silent peers.
        /// </summary>
        private async ValueTask MonitorBindingAsync(StreamState state)
        {
            try
            {
                await state.Bound.Task.WaitAsync(m_options.EstablishmentTimeout, m_clock, m_lifetime)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                if (!state.Associated)
                {
                    RequestClose(DataChannelResult.Timeout);
                }
                else
                {
                    await CloseFlowAsync(state.Stream.StreamId, DataChannelResult.Timeout).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Propagates actual TLS/key/trust/transport loss independently of caller cancellation.
        /// </summary>
        private async ValueTask MonitorNativeConnectionAsync()
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, NativeConnection.Closed).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                RequestClose(NativeConnection.SecurityInvalidated
                    ? DataChannelResult.SecurityFailure : DataChannelResult.Closed);
            }
        }

        private Task m_datagramReceiveTail = Task.CompletedTask;
    }
}
