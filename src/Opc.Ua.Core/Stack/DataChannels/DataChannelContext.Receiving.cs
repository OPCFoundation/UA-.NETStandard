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
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.DataChannels.Wire;

namespace Opc.Ua.DataChannels
{
    public sealed partial class DataChannelContext
    {
        /// <summary>
        /// Receives one canonical MSG application body after carrier authentication.
        /// </summary>
        private async ValueTask<DataChannelResult> ReceiveUascCoreAsync(
            DataChannelUascMessageKind kind,
            byte chunkType,
            uint requestId,
            ByteString plaintext,
            CancellationToken cancellationToken = default)
        {
            if (IsNative && m_carrier.Kind != DataChannelCarrierKind.WebTransport)
            {
                return DataChannelResult.Unsupported;
            }
            try
            {
                UascFlowMessage message = m_carrier.Kind == DataChannelCarrierKind.WebTransport
                    ? UascFlowMessageCodec.DecodeManagement(kind, chunkType, requestId, plaintext, m_codecLimits)
                    : UascFlowMessageCodec.Decode(
                        m_wireCarrier, kind, chunkType, requestId, plaintext, m_codecLimits);
                if (message.Control is { } control)
                {
                    return await ReceiveControlAsync(
                        control,
                        message.Record,
                        DataChannelPath.Management,
                        m_carrier.ManagementStreamId,
                        cancellationToken).ConfigureAwait(false);
                }

                FlowWireEnvelope envelope;
                lock (m_gate)
                {
                    if (m_closed)
                    {
                        return DataChannelResult.Closed;
                    }
                    m_lastVerifiedAt = m_clock.GetTimestamp();
                    DataChannelResult route = CreateUascDataEnvelopeLocked(
                        message.ChannelId, message.ApplicationBody, out envelope, out bool silent);
                    if (route != DataChannelResult.Good)
                    {
                        return RefuseUnroutableUascDataLocked(message.ApplicationBody.Length, route, silent);
                    }
                    if (!m_flows.TryGetValue(envelope.ChannelId, out FlowState? flow) ||
                        flow.LastRoutedDataId == uint.MaxValue)
                    {
                        return DataChannelResult.InvalidState;
                    }
                    envelope = new FlowWireEnvelope(
                        flow.Id,
                        new FlowRecord(
                            FlowRecordKind.Data,
                            flow.ReceiveRevision,
                            ++flow.LastRoutedDataId,
                            envelope.Record.Body),
                        null);
                }
                return await ReceiveDataAsync(
                    envelope.ChannelId, envelope.Record, false, cancellationToken).ConfigureAwait(false);
            }
            catch (FlowCodecException exception)
            {
                if (TryRefuseMalformedUascControl(plaintext))
                {
                    return DataChannelResult.InvalidArgument;
                }
                lock (m_gate)
                {
                    CloseLocked(FlowModel.FromWire(exception.Status));
                    return FlowModel.FromWire(exception.Status);
                }
            }
        }

        /// <summary>
        /// Admits one genuinely unreliable native datagram without emulating it over a stream.
        /// </summary>
        private async ValueTask<DataChannelResult> ReceiveDatagramCoreAsync(
            ByteString datagram,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsNative && HasSelectedDirectStreams)
            {
                return await ReceiveDirectDatagramAsync(datagram, cancellationToken).ConfigureAwait(false);
            }
            if (!IsNative || m_carrier.MaxDatagramSize == 0)
            {
                return DataChannelResult.Unsupported;
            }
            try
            {
                FlowWireEnvelope envelope = FlowCarrierCodec.DecodeDatagram(
                    m_wireCarrier, datagram, m_carrier.MaxDatagramSize, m_codecLimits);
                return await ReceiveDataAsync(envelope.ChannelId, envelope.Record, true, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (FlowCodecException exception)
            {
                // A malformed individual datagram is isolated; it does not desynchronize an ordered carrier.
                return FlowModel.FromWire(exception.Status);
            }
        }

        /// <summary>
        /// Admits a borrowed complete record before copying its body into application ownership.
        /// Dedicated ordered input backpressures; shared exhaustion remains flow-scoped.
        /// </summary>
        private async ValueTask<DataChannelResult> ReceiveDataAsync(
            uint channelId,
            FlowRecord record,
            bool datagram,
            CancellationToken cancellationToken,
            DirectDeliveryFrame? directFrame = null)
        {
            RecordVerifiedMessage();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, m_shutdown.Token);
            long started = m_clock.GetTimestamp();
            while (true)
            {
                Task wait;
                Task applicationWait;
                lock (m_gate)
                {
                    if (m_closed)
                    {
                        return DataChannelResult.Closed;
                    }
                    if (!m_flows.TryGetValue(channelId, out FlowState? flow))
                    {
                        return datagram ? DataChannelResult.UnknownChannel :
                            RefuseLocked(channelId, record.Revision, DataChannelResult.UnknownChannel);
                    }
                    DataChannelResult valid = ValidateDataLocked(
                        flow, record.Revision, record.MessageId, record.Body.Length, datagram,
                        out FlowRevision? revision);
                    wait = m_capacityChanged.Task;
                    applicationWait = m_budget.CapacityChanged;
                    if (valid == DataChannelResult.WouldBlock && !datagram)
                    {
                        // Wait outside state synchronization for live authorization evidence.
                    }
                    else if (valid != DataChannelResult.Good)
                    {
                        if (datagram && valid == DataChannelResult.WouldBlock)
                        {
                            flow.AdmissionDrops++;
                        }
                        if (!datagram && UsesUascCredit && !directFrame.HasValue)
                        {
                            ReleaseDiscardedConnectionCreditLocked(record.Body.Length);
                        }
                        return valid;
                    }
                    else
                    {
                        int cost = checked(record.Body.Length + DataOverhead(flow));
                        if (datagram && revision!.ReceiveRate.Delay(cost, m_clock) > TimeSpan.Zero)
                        {
                            flow.AdmissionDrops++;
                            return DataChannelResult.WouldBlock;
                        }
                        if (UsesFlowCredit(flow) && (flow.ReceiveCredit < cost || m_receiveConnectionCredit < cost))
                        {
                            ReleaseDiscardedConnectionCreditLocked(cost);
                            return ResetLocked(flow, DataChannelResult.WouldBlock);
                        }
                        wait = m_capacityChanged.Task;
                        applicationWait = m_budget.CapacityChanged;
                        if (TryReserveMessageLocked(
                            flow, revision!, cost, false, applicationReserved: directFrame.HasValue))
                        {
                            try
                            {
                                if (directFrame is { } owned)
                                {
                                    owned.TransferMessageReservation();
                                }
                                ByteString body = directFrame.HasValue ? record.Body : record.Body.Copy();
                                return DeliverDataLocked(flow, record, body, cost, datagram);
                            }
                            catch
                            {
                                ReleaseMessageLocked(flow, revision!.Number, cost, false);
                                throw;
                            }
                        }
                        if (datagram)
                        {
                            flow.AdmissionDrops++;
                            return DataChannelResult.WouldBlock;
                        }
                        if (UsesFlowCredit(flow))
                        {
                            ReleaseDiscardedConnectionCreditLocked(cost);
                            return ResetLocked(flow, DataChannelResult.WouldBlock);
                        }
                    }
                }
                if (IsNative || directFrame.HasValue)
                {
                    await Task.WhenAny(wait, applicationWait).WaitAsync(linked.Token).ConfigureAwait(false);
                }
                else if (!await WaitForUascReceiveCapacityAsync(
                    Task.WhenAny(wait, applicationWait), started, linked.Token).ConfigureAwait(false))
                {
                    lock (m_gate)
                    {
                        return m_flows.TryGetValue(channelId, out FlowState? flow)
                            ? ResetLocked(flow, DataChannelResult.Timeout) : DataChannelResult.Closed;
                    }
                }
            }
        }

        /// <summary>
        /// Validates readiness, direction, revision and replay before reserving or allocating body storage.
        /// </summary>
        private DataChannelResult ValidateDataLocked(
            FlowState flow,
            uint revisionNumber,
            uint messageId,
            int length,
            bool datagram,
            out FlowRevision? revision)
        {
            revision = null;
            if (flow.RevocationToken.IsCancellationRequested)
            {
                return ResetLocked(flow, DataChannelResult.NotAuthorized);
            }
            if (!flow.Ready || flow.IsTerminal || flow.IsDatagram != datagram)
            {
                return datagram ? DataChannelResult.InvalidState : ResetLocked(flow, DataChannelResult.InvalidState);
            }
            if (!flow.MayReceive)
            {
                return datagram ? DataChannelResult.NotAuthorized : ResetLocked(flow, DataChannelResult.NotAuthorized);
            }
            if (flow.RemoteEnded)
            {
                return datagram ? DataChannelResult.Closed : ResetLocked(flow, DataChannelResult.Closed);
            }
            if (AuthorizationPendingLocked(flow))
            {
                return DataChannelResult.WouldBlock;
            }
            if (revisionNumber == flow.ReceiveRevision)
            {
                revision = flow.Revisions[revisionNumber];
            }
            else if (datagram &&
                flow.PreviousReceiveRevision is { Grace: true } previous &&
                previous.Number == revisionNumber &&
                m_clock.GetElapsedTime(previous.GraceStarted) < m_options.DatagramRevisionGrace &&
                messageId <= previous.LastOldMessageId)
            {
                revision = previous;
            }
            else
            {
                flow.RevisionDrops++;
                return datagram ? DataChannelResult.InvalidState : ResetLocked(flow, DataChannelResult.InvalidState);
            }
            if (length > revision.Parameters.MaxMessageSize)
            {
                return datagram ? DataChannelResult.TooLarge : ResetLocked(flow, DataChannelResult.TooLarge);
            }
            if (datagram ? !flow.Replay.IsNovel(messageId) : messageId <= flow.LastReceivedDataId)
            {
                flow.ReplayDrops++;
                return datagram ? DataChannelResult.InvalidState : ResetLocked(flow, DataChannelResult.InvalidState);
            }
            return DataChannelResult.Good;
        }

        /// <summary>
        /// Transfers a reserved complete body to the bounded application queue exactly once.
        /// </summary>
        private DataChannelResult DeliverDataLocked(
            FlowState flow,
            FlowRecord record,
            ByteString ownedBody,
            int cost,
            bool datagram,
            int? storageCost = null)
        {
            int bufferCost = storageCost ?? cost;
            if (UsesFlowCredit(flow))
            {
                flow.ReceiveCredit -= (uint)cost;
                m_receiveConnectionCredit -= (uint)cost;
            }
            if (datagram)
            {
                flow.Revisions[record.Revision].ReceiveRate.TryConsume(cost, m_clock);
                flow.Replay.Accept(record.MessageId);
            }
            flow.LastReceivedDataId = Math.Max(flow.LastReceivedDataId, record.MessageId);
            flow.MessagesReceived++;
            flow.BytesReceived += (uint)cost;
            bool queued = false;
            DataChannelMessage? message = new(
                ownedBody, record.Revision, record.MessageId,
                () =>
                {
                    if (queued)
                    {
                        ReleaseReceived(flow, record.Revision, cost, bufferCost);
                    }
                });
            try
            {
                flow.Incoming.Enqueue(message);
                queued = true;
                message = null;
            }
            finally
            {
                // Before queue handoff the caller still owns the reservation, including on insertion failure.
                message?.Dispose();
            }
            flow.ReadChanged.Complete();
            flow.ReadChanged = new TaskSignal();
            if (UsesFlowCredit(flow))
            {
                GrantCreditLocked(flow);
            }
            return DataChannelResult.Good;
        }

        /// <summary>
        /// Reassembles arbitrary stream segmentation with reservation before decoder body allocation.
        /// </summary>
        private async ValueTask<DataChannelResult> ReceiveStreamCoreAsync(
            ulong streamId,
            ByteString segment,
            bool endOfStream,
            CancellationToken cancellationToken)
        {
            if (!IsNative)
            {
                return DataChannelResult.Unsupported;
            }
            FlowStreamInput input;
            lock (m_gate)
            {
                if (m_closed)
                {
                    return DataChannelResult.Closed;
                }
                if (!m_streams.TryGetValue(streamId, out FlowStreamInput? found))
                {
                    if (m_identity == null ||
                        m_provisionalStreams + m_flows.Count + m_pendingFlowReleases >= m_options.MaxFlows ||
                        m_usedStreams.Contains(streamId) ||
                        m_usedStreams.Count >= m_options.MaxStreamHistory ||
                        !m_budget.TryReserve(FlowResource.Stream, 1))
                    {
                        return DataChannelResult.UnknownChannel;
                    }
                    if (!TryCreateStreamInputLocked(0, streamId, out found))
                    {
                        m_budget.Release(FlowResource.Stream, 1);
                        return DataChannelResult.WouldBlock;
                    }
                    found.Provisional = true;
                    m_usedStreams.Add(streamId);
                    m_streams.Add(streamId, found);
                    m_provisionalStreams++;
                    ArmTimerLocked();
                }
                input = found;
                if (input.Reading || input.Disposed)
                {
                    return DataChannelResult.InvalidState;
                }
                input.Reading = true;
            }
            try
            {
                int offset = 0;
                DataChannelResult outcome = DataChannelResult.Good;
                while (offset < segment.Length)
                {
                    FlowRecordHeader header = default;
                    bool needReservation = false;
                    lock (m_gate)
                    {
                        if (input.Disposed || m_closed)
                        {
                            return DataChannelResult.Closed;
                        }
                        if (!input.BodyStarted)
                        {
                            int take = Math.Min(13 - input.HeaderBytes, segment.Length - offset);
                            segment.Span.Slice(offset, take).CopyTo(input.Header.AsSpan(input.HeaderBytes));
                            input.HeaderBytes += take;
                            offset += take;
                            FlowRecordCodec.InspectPrefix(input.Header.AsSpan(0, input.HeaderBytes), m_codecLimits);
                            if (input.HeaderBytes < 13)
                            {
                                continue;
                            }
                            header = FlowRecordCodec.ReadHeader(input.Header, m_codecLimits);
                            needReservation = true;
                        }
                    }
                    if (needReservation)
                    {
                        DataChannelResult reserved = await ReserveStreamRecordAsync(input, header, cancellationToken)
                            .ConfigureAwait(false);
                        if (reserved != DataChannelResult.Good)
                        {
                            lock (m_gate)
                            {
                                bool provisional = input.Provisional;
                                ReleaseStreamInputLocked(input, reserved);
                                if (provisional)
                                {
                                    m_streams.Remove(streamId);
                                }
                            }
                            if (streamId == m_carrier.ManagementStreamId)
                            {
                                await CloseCoreAsync(reserved, CancellationToken.None).ConfigureAwait(false);
                            }
                            else
                            {
                                await input.ReleaseTask.ConfigureAwait(false);
                            }
                            return reserved;
                        }
                    }
                    FlowRecord record;
                    bool complete;
                    FlowReceiveReservation? reservation;
                    lock (m_gate)
                    {
                        if (input.Disposed || m_closed)
                        {
                            return DataChannelResult.Closed;
                        }
                        if (!input.BodyStarted)
                        {
                            input.BodyStarted = true;
                            complete = input.Decoder.Read(input.Header, out _, out record);
                        }
                        else
                        {
                            complete = false;
                            record = default;
                        }
                        if (!complete)
                        {
                            complete = input.Decoder.Read(segment.Span[offset..], out int consumed, out record);
                            offset += consumed;
                        }
                        if (!complete)
                        {
                            continue;
                        }
                        input.HeaderBytes = 0;
                        input.BodyStarted = false;
                        reservation = input.Reservation;
                        input.Reservation = null;
                    }
                    DataChannelResult result = await ReceiveStreamRecordAsync(
                        input, streamId, record, reservation!, cancellationToken).ConfigureAwait(false);
                    if (result != DataChannelResult.Good)
                    {
                        outcome = result;
                        lock (m_gate)
                        {
                            if (streamId != m_carrier.ManagementStreamId || m_closed)
                            {
                                return result;
                            }
                        }
                    }
                }
                if (endOfStream)
                {
                    lock (m_gate)
                    {
                        if (input.HeaderBytes != 0 && !input.BodyStarted)
                        {
                            throw new FlowCodecException(FlowCodecError.Truncated);
                        }
                        input.Decoder.Complete();
                        if (input.Provisional)
                        {
                            ReleaseStreamInputLocked(input, DataChannelResult.Closed);
                            m_streams.Remove(streamId);
                            outcome = DataChannelResult.Closed;
                        }
                        else if (input.FlowId == 0)
                        {
                            CloseLocked(DataChannelResult.Closed);
                        }
                        else if (m_flows.TryGetValue(input.FlowId, out FlowState? flow) && !flow.RemoteEnded)
                        {
                            ResetLocked(flow, DataChannelResult.Closed);
                        }
                    }
                    await input.ReleaseTask.ConfigureAwait(false);
                }
                return outcome;
            }
            catch (FlowCodecException exception)
            {
                bool provisional;
                lock (m_gate)
                {
                    provisional = input.Provisional;
                    ReleaseStreamInputLocked(input, FlowModel.FromWire(exception.Status));
                    if (provisional)
                    {
                        m_streams.Remove(streamId);
                    }
                }
                if (provisional)
                {
                    await input.ReleaseTask.ConfigureAwait(false);
                    return FlowModel.FromWire(exception.Status);
                }
                return await CodecFailureAsync(exception, input.FlowId, false).ConfigureAwait(false);
            }
            finally
            {
                lock (m_gate)
                {
                    input.Reading = false;
                }
            }
        }

        /// <summary>
        /// Backpressures a native stream at the header boundary, before the codec allocates its body.
        /// </summary>
        private async ValueTask<DataChannelResult> ReserveStreamRecordAsync(
            FlowStreamInput input,
            FlowRecordHeader header,
            CancellationToken cancellationToken)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, m_shutdown.Token);
            while (true)
            {
                Task wait;
                Task applicationWait;
                lock (m_gate)
                {
                    if (m_closed || input.Disposed)
                    {
                        return DataChannelResult.Closed;
                    }
                    wait = m_capacityChanged.Task;
                    applicationWait = m_budget.CapacityChanged;
                    if (header.Kind == FlowRecordKind.Control)
                    {
                        int cost = checked(3 * header.TotalLength);
                        if (TryReserveReceiveControlLocked(cost))
                        {
                            input.Reservation = new FlowReceiveReservation(null, header.Revision, cost);
                            return DataChannelResult.Good;
                        }
                    }
                    else
                    {
                        if (input.FlowId == 0 || !m_flows.TryGetValue(input.FlowId, out FlowState? flow))
                        {
                            return DataChannelResult.InvalidState;
                        }
                        DataChannelResult valid = ValidateDataLocked(
                            flow, header.Revision, header.MessageId, header.BodyLength, false,
                            out FlowRevision? revision);
                        if (valid is not DataChannelResult.Good and not DataChannelResult.WouldBlock)
                        {
                            return valid;
                        }
                        if (valid == DataChannelResult.Good &&
                            TryReserveMessageLocked(flow, revision!, header.TotalLength, false))
                        {
                            input.Reservation = new FlowReceiveReservation(flow, header.Revision, header.TotalLength);
                            return DataChannelResult.Good;
                        }
                    }
                }
                await Task.WhenAny(wait, applicationWait).WaitAsync(linked.Token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Dispatches a completed native record, preserving its pre-allocation reservation.
        /// </summary>
        /// <exception cref="FlowCodecException"></exception>
        private async ValueTask<DataChannelResult> ReceiveStreamRecordAsync(
            FlowStreamInput input,
            ulong streamId,
            FlowRecord record,
            FlowReceiveReservation reservation,
            CancellationToken cancellationToken)
        {
            if (record.Kind == FlowRecordKind.Data)
            {
                return await DeliverStreamDataAsync(record, reservation, cancellationToken).ConfigureAwait(false);
            }
            try
            {
                FlowControl control = FlowControlCodec.Decode(in record, m_codecLimits);
                FlowControlCodec.ValidateCarrier(control, m_wireCarrier, uascBaseline: !IsNative);
                if (input.Provisional &&
                    (control is not FlowOpen initial || initial.Stream != streamId))
                {
                    throw new FlowCodecException(FlowCodecError.InvalidStream);
                }
                if (input.FlowId != 0 && control.Flow != input.FlowId)
                {
                    throw new FlowCodecException(FlowCodecError.InvalidFlow);
                }
                DataChannelResult result = await ReceiveControlAsync(
                    control, record, input.FlowId == 0 && !input.Provisional
                        ? DataChannelPath.Management : DataChannelPath.Flow,
                    streamId, cancellationToken).ConfigureAwait(false);
                if (input.Provisional && result != DataChannelResult.Good)
                {
                    lock (m_gate)
                    {
                        ReleaseStreamInputLocked(input, result);
                        m_streams.Remove(streamId);
                    }
                    await input.ReleaseTask.ConfigureAwait(false);
                }
                return result;
            }
            finally
            {
                lock (m_gate)
                {
                    ReleaseReceiveControlLocked(reservation.Cost);
                }
            }
        }

        /// <summary>
        /// Rechecks freshness after segmented body arrival without losing its admitted memory reservation.
        /// </summary>
        private async ValueTask<DataChannelResult> DeliverStreamDataAsync(
            FlowRecord record,
            FlowReceiveReservation reservation,
            CancellationToken cancellationToken)
        {
            FlowState flow = reservation.Flow!;
            bool transferred = false;
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, m_shutdown.Token);
            try
            {
                while (true)
                {
                    Task wait;
                    lock (m_gate)
                    {
                        if (flow.IsTerminal || m_closed || flow.RevocationToken.IsCancellationRequested)
                        {
                            return DataChannelResult.Closed;
                        }
                        if (!AuthorizationPendingLocked(flow))
                        {
                            DataChannelResult result = DeliverDataLocked(
                                flow, record, record.Body, reservation.Cost, false);
                            transferred = true;
                            return result;
                        }
                        wait = m_capacityChanged.Task;
                    }
                    await wait.WaitAsync(linked.Token).ConfigureAwait(false);
                }
            }
            finally
            {
                if (!transferred)
                {
                    lock (m_gate)
                    {
                        ReleaseMessageLocked(flow, reservation.Revision, reservation.Cost, false);
                    }
                }
            }
        }

        /// <summary>
        /// Reserves finite inbound control storage independently of DATA windows.
        /// </summary>
        private bool TryReserveReceiveControlLocked(int cost)
        {
            if (m_pendingControls >= m_options.MaxPendingControls ||
                !m_controlHeadroom!.TryReserve(cost))
            {
                return false;
            }
            m_pendingControls++;
            return true;
        }

        /// <summary>
        /// Releases retained control capacity without granting DATA credit.
        /// </summary>
        private void ReleaseReceiveControlLocked(int cost)
        {
            m_pendingControls--;
            m_controlHeadroom!.Release(cost);
            PulseCapacityLocked();
        }

        /// <summary>
        /// Admits both fixed header staging areas before constructing a native stream parser.
        /// </summary>
        private bool TryCreateStreamInputLocked(
            uint flowId, ulong streamId, [NotNullWhen(true)] out FlowStreamInput? input)
        {
            if (!m_budget.TryReserve(FlowResource.BufferedBytes, FlowStreamInput.ParserStorageBytes))
            {
                input = null;
                return false;
            }
            input = new FlowStreamInput(flowId, m_codecLimits, streamId)
            {
                Started = m_clock.GetTimestamp()
            };
            return true;
        }

        /// <summary>
        /// Disposes one bounded incomplete record and any associated reservation.
        /// </summary>
        private void ReleaseStreamInputLocked(
            FlowStreamInput input,
            DataChannelResult result = DataChannelResult.Closed)
        {
            if (input.Disposed)
            {
                return;
            }
            input.Disposed = true;
            input.Decoder.Dispose();
            input.Header = [];
            m_budget.Release(FlowResource.BufferedBytes, FlowStreamInput.ParserStorageBytes);
            if (input.Provisional)
            {
                input.Provisional = false;
                m_provisionalStreams--;
                input.PendingStreamRelease = true;
                input.ReleaseTask = CloseFlowTransportAsync(input.StreamId, result, input);
            }
            if (input.Reservation is { } reservation)
            {
                input.Reservation = null;
                if (reservation.Flow != null)
                {
                    ReleaseMessageLocked(reservation.Flow, reservation.Revision, reservation.Cost, false);
                }
                else
                {
                    ReleaseReceiveControlLocked(reservation.Cost);
                }
            }
        }

        /// <summary>
        /// Contains malformed control to its flow unless outer framing or context confirmation is untrustworthy.
        /// </summary>
        private async ValueTask<DataChannelResult> CodecFailureAsync(
            FlowCodecException exception,
            uint channelId,
            bool uasc,
            bool trustedContainer = true)
        {
            DataChannelResult result = FlowModel.FromWire(exception.Status);
            bool fatal;
            lock (m_gate)
            {
                fatal = IsFatalCodecFailureLocked(exception, channelId, uasc, trustedContainer);
            }
            if (fatal)
            {
                await CloseCoreAsync(result, CancellationToken.None).ConfigureAwait(false);
            }
            else
            {
                lock (m_gate)
                {
                    if (m_flows.TryGetValue(channelId, out FlowState? flow))
                    {
                        ResetLocked(flow, result);
                    }
                    else
                    {
                        RefuseLocked(channelId, 1, result);
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// Distinguishes trustworthy addressed control from unusable framing or an unconfirmed context.
        /// </summary>
        private bool IsFatalCodecFailureLocked(
            FlowCodecException exception,
            uint channelId,
            bool uasc,
            bool trustedContainer = true)
        {
            return m_identity == null ||
                channelId == 0 ||
                (uasc &&
                    (!trustedContainer ||
                        exception.Error is
                            FlowCodecError.Truncated or FlowCodecError.InvalidLength or FlowCodecError.TrailingBytes or
                            FlowCodecError.NonFinalUasc or FlowCodecError.RequestIdMismatch or FlowCodecError.UnknownKind));
        }

        /// <summary>
        /// Determines whether a legacy addressed UASC container remains trustworthy after a size error.
        /// </summary>
        private static bool HasTrustedUascContainer(FlowCodecException exception, ByteString plaintext)
        {
            if (exception.Error != FlowCodecError.TooLarge)
            {
                return true;
            }
            if (plaintext.Length < 17)
            {
                return false;
            }
            try
            {
                FlowRecordCodec.DecodeExact(plaintext[4..], s_containerInspectionLimits);
                return true;
            }
            catch (FlowCodecException)
            {
                return false;
            }
        }

        /// <summary>
        /// Dispatches only explicit control operations, keeping revision replay separate from sender high-water.
        /// </summary>
        private async ValueTask<DataChannelResult> ReceiveControlAsync(
            FlowControl control,
            FlowRecord record,
            DataChannelPath path,
            ulong streamId,
            CancellationToken cancellationToken)
        {
            FlowControlCodec.ValidateCarrier(control, m_wireCarrier, uascBaseline: !IsNative);
            RecordVerifiedMessage();
            lock (m_gate)
            {
                if (m_closed)
                {
                    return DataChannelResult.Closed;
                }
                if (control is FlowHello hello)
                {
                    return ReceiveHelloLocked(hello, record.MessageId);
                }
                if (m_identity == null)
                {
                    CloseLocked(DataChannelResult.InvalidState);
                    return DataChannelResult.InvalidState;
                }
                if (control is FlowProbe { Operation: FlowOperation.Pong } pong &&
                    (!m_pings.TryGetValue(pong.Flow, out FlowPing? pendingProbe) ||
                        pendingProbe.Timestamp != pong.Timestamp))
                {
                    return DataChannelResult.Good;
                }
                if (IsRetiredChannelIdLocked(control.Flow))
                {
                    return DataChannelResult.Good;
                }
                if (control is FlowOutcome { Operation: FlowOperation.Status } status)
                {
                    if (m_pings.TryGetValue(status.Flow, out FlowPing? ping) &&
                        status.RelatedOperation != 0 &&
                        status.RelatedOperation == ping.OperationId &&
                        status.Status == FlowWireStatus.WouldBlock)
                    {
                        m_pings.Remove(status.Flow);
                        m_budget.Release(FlowResource.Probe, 1);
                        ping.Completion.TrySetResult(DataChannelResult.WouldBlock);
                        return DataChannelResult.Good;
                    }
                    if (status.Flow != 0)
                    {
                        if (status.RelatedOperation != 0 &&
                            m_flows.TryGetValue(status.Flow, out FlowState? target) &&
                            target.Operations.TryGetValue(status.RelatedOperation, out FlowUpdate? update))
                        {
                            DataChannelResult outcome = FlowModel.FromWire(status.Status);
                            if (!update.Committed && outcome != DataChannelResult.Good)
                            {
                                AbortUpdateLocked(target, update, outcome);
                            }
                        }
                        return DataChannelResult.Good;
                    }
                    if (status.Status is FlowWireStatus.WouldBlock or FlowWireStatus.UnknownChannel ||
                        (status.RelatedOperation != 0 &&
                            status.RelatedOperation != m_directBindingOperationId) ||
                        m_carrier.Kind is not (DataChannelCarrierKind.QuicUasc or
                            DataChannelCarrierKind.WebTransportUasc))
                    {
                        return DataChannelResult.Good;
                    }
                }
            }
            if (control is FlowBinding ||
                control is FlowOutcome { Operation: FlowOperation.Status, Flow: 0 })
            {
                return await ReceiveDirectBindingAsync(control, record, cancellationToken).ConfigureAwait(false);
            }
            if (control is FlowOpen open)
            {
                if (IsNative && path == DataChannelPath.Flow && streamId != open.Stream)
                {
                    return DataChannelResult.InvalidState;
                }
                return await ReceiveOpenAsync(open, record.MessageId, cancellationToken).ConfigureAwait(false);
            }
            if (control is FlowPrepare prepare)
            {
                return await ReceivePrepareAsync(prepare, record, cancellationToken).ConfigureAwait(false);
            }
            if (control is FlowAssociation association)
            {
                return await ReceiveDirectAssociationAsync(association, record).ConfigureAwait(false);
            }
            lock (m_gate)
            {
                FlowState? flow = null;
                if (control.Flow != 0 && !m_flows.TryGetValue(control.Flow, out flow))
                {
                    return control.Operation is FlowOperation.Reset or FlowOperation.Status
                        ? DataChannelResult.UnknownChannel
                        : QueueAdvisoryLocked(control.Flow, record.Revision, record.MessageId,
                            DataChannelResult.UnknownChannel);
                }
                if (flow?.RevocationToken.IsCancellationRequested == true &&
                    control.Operation is not (FlowOperation.Reset or FlowOperation.Status))
                {
                    return ResetLocked(flow, DataChannelResult.NotAuthorized, record.MessageId);
                }
                if (flow != null &&
                    control.Operation is FlowOperation.AcceptUpdate or FlowOperation.Commit or
                        FlowOperation.Ack or FlowOperation.AbortUpdate)
                {
                    if (flow.DirectLane != null && !flow.IsDatagram &&
                        control is FlowCommit && path != DataChannelPath.Flow)
                    {
                        return QueueAdvisoryLocked(control.Flow, record.Revision, record.MessageId,
                            DataChannelResult.InvalidState, flow);
                    }
                    if (IsNative &&
                        control.Operation is FlowOperation.Commit or FlowOperation.Ack &&
                        (path != DataChannelPath.Flow || streamId != flow.StreamId))
                    {
                        return DataChannelResult.InvalidState;
                    }
                    return ReceiveRevisionControlLocked(flow, control, record);
                }
                uint highWater = flow?.PeerControlId ?? m_peerControlId;
                if (record.MessageId <= highWater)
                {
                    return control.Operation is FlowOperation.Reset or FlowOperation.Pong
                        ? DataChannelResult.Good
                        : QueueAdvisoryLocked(control.Flow, record.Revision, record.MessageId,
                            DataChannelResult.InvalidState, flow);
                }
                if (control is FlowCredit credit)
                {
                    DataChannelResult credited = ReceiveCreditLocked(flow, credit, record.Revision);
                    if (credited != DataChannelResult.Good)
                    {
                        return QueueAdvisoryLocked(control.Flow, record.Revision, record.MessageId, credited, flow);
                    }
                }
                else if (control is FlowAccept accept && flow != null && record.Revision == 1)
                {
                    DataChannelResult accepted = ReceiveAcceptLocked(flow, accept, record.MessageId);
                    if (accepted != DataChannelResult.Good)
                    {
                        return accepted;
                    }
                }
                else if (control.Operation == FlowOperation.Ready && flow != null && record.Revision == 1)
                {
                    if (flow.IsLocalOpener ||
                        flow.State != DataChannelState.Opening ||
                        flow.Revisions.Count == 0 ||
                        (flow.DirectLane != null && !m_directAssociations.CanBecomeReady(flow)) ||
                        (IsNative && (path != DataChannelPath.Flow || streamId != flow.StreamId)))
                    {
                        return QueueAdvisoryLocked(control.Flow, record.Revision, record.MessageId,
                            DataChannelResult.InvalidState, flow);
                    }
                    MarkReadyLocked(flow);
                }
                else
                {
                    if (record.Revision != (flow?.ReceiveRevision ?? 0))
                    {
                        return control.Operation == FlowOperation.Reset
                            ? DataChannelResult.InvalidState
                            : QueueAdvisoryLocked(control.Flow, record.Revision, record.MessageId,
                                DataChannelResult.InvalidState, flow);
                    }
                    switch (control)
                    {
                        case FlowSignal { Operation: FlowOperation.End } when flow != null:
                            if (!flow.Ready ||
                                flow.RemoteEnded ||
                                (flow.DirectLane != null && !flow.IsDatagram && path != DataChannelPath.Flow) ||
                                (IsNative && (path != DataChannelPath.Flow || streamId != flow.StreamId)))
                            {
                                return QueueAdvisoryLocked(control.Flow, record.Revision, record.MessageId,
                                    DataChannelResult.InvalidState, flow);
                            }
                            flow.RemoteEnded = true;
                            flow.ReadChanged.Complete();
                            CheckEndedLocked(flow);
                            break;
                        case FlowOutcome { Operation: FlowOperation.Reset } outcome when flow != null:
                            DataChannelResult resetResult = FlowModel.FromWire(outcome.Status);
                            bool interruptNative = flow.ActiveWrite?.Committed == true;
                            bool finishNativeStream = !interruptNative &&
                                flow.Ready &&
                                IsNative &&
                                flow.StreamReserved &&
                                flow.DirectLane == null;
                            TerminateFlowLocked(flow, resetResult);
                            QueueFlowReleaseLocked(flow, Task.CompletedTask, resetResult,
                                interruptNative: interruptNative || !finishNativeStream,
                                finishNativeStream: finishNativeStream);
                            break;
                        case FlowProbe probe:
                            DataChannelResult probed = ReceiveProbeLocked(flow, probe, record.MessageId);
                            if (probed != DataChannelResult.Good)
                            {
                                return probed;
                            }
                            break;
                        default:
                            return QueueAdvisoryLocked(control.Flow, record.Revision, record.MessageId,
                                DataChannelResult.InvalidState, flow);
                    }
                }
                if (flow == null)
                {
                    m_peerControlId = record.MessageId;
                }
                else
                {
                    flow.PeerControlId = record.MessageId;
                }
                return DataChannelResult.Good;
            }
        }

        /// <summary>
        /// Validates an entire credit grant before changing either balance or its control high-water.
        /// </summary>
        private DataChannelResult ReceiveCreditLocked(FlowState? flow, FlowCredit credit, uint revision)
        {
            if (!UsesUascCredit || (flow != null && !UsesFlowCredit(flow)) ||
                (flow == null && (revision != 0 || credit.ChannelCredit != 0)) ||
                (flow != null && (!flow.Ready || revision != flow.SendRevision)))
            {
                return DataChannelResult.InvalidState;
            }
            if ((ulong)m_sendConnectionCredit + credit.ConnectionCredit > m_peerMaxReceiveBytes ||
                (flow != null &&
                    (ulong)flow.SendCredit + credit.ChannelCredit >
                        flow.Revisions[flow.SendRevision].Parameters.MaxReceiveBytes))
            {
                return DataChannelResult.InvalidArgument;
            }
            m_sendConnectionCredit += credit.ConnectionCredit;
            flow?.SendCredit += credit.ChannelCredit;
            WakeLocked();
            return DataChannelResult.Good;
        }

        /// <summary>
        /// Echoes a bounded PING timestamp with a fresh sender ID or completes the matching local probe.
        /// </summary>
        private DataChannelResult ReceiveProbeLocked(FlowState? flow, FlowProbe probe, uint operationId)
        {
            if (probe.Operation == FlowOperation.Ping)
            {
                if (m_peerPingReceived && m_clock.GetElapsedTime(m_lastPeerPingAt) < TimeSpan.FromSeconds(1))
                {
                    return QueueAdvisoryLocked(probe.Flow, flow?.ReceiveRevision ?? 0, operationId,
                        DataChannelResult.WouldBlock, flow);
                }
                m_peerPingReceived = true;
                m_lastPeerPingAt = m_clock.GetTimestamp();
                QueueControlLocked(
                    new FlowProbe(FlowOperation.Pong, probe.Flow, probe.Timestamp),
                    flow?.SendRevision ?? 0, 0, DataChannelPath.Management, flow: flow);
                return DataChannelResult.Good;
            }
            if (!m_pings.TryGetValue(probe.Flow, out FlowPing? pending) || pending.Timestamp != probe.Timestamp)
            {
                return DataChannelResult.Good;
            }
            m_pings.Remove(probe.Flow);
            m_budget.Release(FlowResource.Probe, 1);
            flow?.RoundTripTime = m_clock.GetElapsedTime(pending.SentAt ?? pending.Started).TotalMilliseconds;
            pending.Completion.TrySetResult(DataChannelResult.Good);
            return DataChannelResult.Good;
        }
    }

    /// <summary>
    /// Bounded native stream decoder state; the context serializes access and owns its lifetime.
    /// </summary>
    internal sealed class FlowStreamInput(uint flowId, FlowCodecLimits limits, ulong streamId = 0)
    {
        internal uint FlowId { get; set; } = flowId;
        internal ulong StreamId { get; } = streamId;
        internal bool Provisional { get; set; }
        internal bool PendingStreamRelease { get; set; }
        internal Task ReleaseTask { get; set; } = Task.CompletedTask;
        internal long Started { get; init; }
        internal byte[] Header { get; set; } = new byte[FlowRecordCodec.HeaderSize];
        internal int HeaderBytes { get; set; }
        internal bool BodyStarted { get; set; }
        internal bool Reading { get; set; }
        internal bool Disposed { get; set; }
        internal FlowRecordDecoder Decoder { get; } = new(limits);
        internal FlowReceiveReservation? Reservation { get; set; }

        /// <summary>
        /// Fixed stream-input and decoder header allocations, independent of an admitted record body.
        /// </summary>
        internal const int ParserStorageBytes = 2 * FlowRecordCodec.HeaderSize;
    }

    /// <summary>
    /// Tracks the exact reservation acquired before a stream record's body allocation.
    /// </summary>
    internal sealed record FlowReceiveReservation(FlowState? Flow, uint Revision, int Cost);
}
