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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.DataChannels.Wire;

namespace Opc.Ua.DataChannels
{
    /// <summary>
    /// State owned and serialized by one context. No synchronization primitives escape that owner.
    /// </summary>
    internal sealed class FlowState
    {
        /// <summary>
        /// Creates a provisional flow before authorization or native binding.
        /// </summary>
        internal FlowState(
            uint id,
            bool isLocalOpener,
            DataChannelParametersDataType parameters,
            DataChannelAuthorizationMode authorization,
            Guid? grant,
            long started,
            DateTime startedAt)
        {
            Id = id;
            IsLocalOpener = isLocalOpener;
            Parameters = parameters;
            Requested = FlowModel.Copy(parameters);
            Authorization = authorization;
            Grant = grant;
            OpenStarted = started;
            AuthorizedAt = started;
            LastAuthorizationCheck = started;
            StartedAt = startedAt;
        }

        /// <summary>
        /// Gets the immutable flow identifier.
        /// </summary>
        internal uint Id { get; }

        /// <summary>
        /// Gets the immutable opener role.
        /// </summary>
        internal bool IsLocalOpener { get; }

        /// <summary>
        /// Gets whether a data direction is permitted for this local endpoint.
        /// </summary>
        internal bool MaySend => Parameters.Direction == DataChannelDirection.Duplex ||
            (IsLocalOpener
                ? Parameters.Direction == DataChannelDirection.OpenerToAcceptor
                : Parameters.Direction == DataChannelDirection.AcceptorToOpener);

        /// <summary>
        /// Gets whether a data direction is permitted for the remote endpoint.
        /// </summary>
        internal bool MayReceive => Parameters.Direction == DataChannelDirection.Duplex ||
            (IsLocalOpener
                ? Parameters.Direction == DataChannelDirection.AcceptorToOpener
                : Parameters.Direction == DataChannelDirection.OpenerToAcceptor);

        /// <summary>
        /// Gets whether the flow is terminal.
        /// </summary>
        internal bool IsTerminal => State is DataChannelState.Closed or DataChannelState.Faulted;

        /// <summary>
        /// Gets whether datagrams are the admitted application delivery path.
        /// </summary>
        internal bool IsDatagram => Parameters.Delivery == DataChannelDeliveryMode.Datagram;

        internal DataChannel? Facade { get; set; }
        internal DataChannelParametersDataType Requested { get; }
        internal DataChannelParametersDataType Parameters { get; set; }
        internal DataChannelAuthorizationMode Authorization { get; }
        internal Guid? Grant { get; }
        internal ulong StreamId { get; set; }
        internal bool StreamReserved { get; set; }
        internal bool FlowReserved { get; set; }
        internal bool OpeningOperationReserved { get; set; }
        internal bool Ready { get; set; }

        /// <summary>
        /// Gets or sets the immutable typed receive associations admitted before shared-UASC streaming READY.
        /// </summary>
        internal ArrayOf<Variant> UascStreamingPublisherIds { get; set; }

        /// <summary>
        /// Gets or sets the selected authenticated ordered direct-lane owner.
        /// </summary>
        internal FlowDirectLane? DirectLane { get; set; }

        internal bool LocalEnded { get; set; }
        internal bool RemoteEnded { get; set; }
        internal bool Draining { get; set; }
        internal bool SendBusy { get; set; }
        internal bool AuthorizationChecking { get; set; }
        internal bool RecheckRequested { get; set; }
        internal bool ReleaseStarted { get; set; }
        internal long OpenStarted { get; }
        internal DateTime StartedAt { get; }
        internal long DrainStarted { get; set; }
        internal long AuthorizedAt { get; set; }
        internal long LastAuthorizationCheck { get; set; }
        internal TimeSpan? AuthorizationLifetime { get; set; }
        internal CancellationToken RevocationToken { get; set; }
        internal CancellationTokenRegistration RevocationRegistration { get; set; }
        internal ExpandedNodeId SourceNodeId { get; set; }
        internal NodeId SessionId { get; set; }
        internal DataChannelState State { get; set; } = DataChannelState.Opening;
        internal DataChannelResult Result { get; set; }
        internal uint Revision { get; set; } = 1;
        internal uint SendRevision { get; set; } = 1;
        internal uint ReceiveRevision { get; set; } = 1;
        internal uint LastDataId { get; set; }
        internal uint LastSentDataId { get; set; }
        internal uint LastReceivedDataId { get; set; }
        internal uint LastRoutedDataId { get; set; }
        internal uint LastControlId { get; set; }
        internal uint PeerControlId { get; set; }
        internal uint LastOperationId { get; set; }
        internal uint SendCredit { get; set; }
        internal uint ReceiveCredit { get; set; }
        internal uint LastCreditGrant { get; set; }
        internal long SendBytes { get; set; }
        internal long ReceiveBytes { get; set; }
        internal int SendMessages { get; set; }
        internal int ReceiveMessages { get; set; }
        internal ulong MessagesSent { get; set; }
        internal ulong MessagesReceived { get; set; }
        internal ulong BytesSent { get; set; }
        internal ulong BytesReceived { get; set; }
        internal ulong QueueExpiryDrops { get; set; }
        internal ulong AdmissionDrops { get; set; }
        internal ulong ReplayDrops { get; set; }
        internal ulong RevisionDrops { get; set; }
        internal ulong CreditStalls { get; set; }
        internal double RoundTripTime { get; set; }
        internal FlowWriteItem? ActiveWrite { get; set; }
        internal FlowWriteItem? Barrier { get; set; }
        internal FlowUpdate? Update { get; set; }
        internal FlowRevision? PreviousReceiveRevision { get; set; }
        internal FlowReplayWindow Replay { get; } = new();
        internal FlowTokenBucket SendRate { get; } = new();
        internal Dictionary<uint, FlowRevision> Revisions { get; } = [];
        internal Dictionary<uint, FlowUpdate> Operations { get; } = [];
        internal Queue<uint> OperationOrder { get; } = [];
        internal LinkedList<FlowWriteItem> Outgoing { get; } = [];
        internal Queue<DataChannelMessage> Incoming { get; } = [];
        internal TaskSignal ReadChanged { get; set; } = new();
        internal Task LastControlWrite { get; set; } = Task.CompletedTask;
        internal TaskCompletionSource<DataChannelResult> Opened { get; } = NewCompletion();
        internal TaskCompletionSource<DataChannelResult> Ended { get; } = NewCompletion();
        internal TaskCompletionSource<DataChannelResult> Released { get; } = NewCompletion();

        /// <summary>
        /// Creates an outcome signal without inline user continuations.
        /// </summary>
        internal static TaskCompletionSource<DataChannelResult> NewCompletion()
        {
            return new TaskCompletionSource<DataChannelResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    /// <summary>
    /// A revision's reservation survives while valid on the network or retained in application messages.
    /// </summary>
    internal sealed class FlowRevision(uint number, DataChannelParametersDataType parameters)
    {
        internal uint Number { get; } = number;
        internal DataChannelParametersDataType Parameters { get; } = parameters;
        internal bool SendActive { get; set; }
        internal bool ReceiveActive { get; set; }
        internal bool Pending { get; set; }
        internal bool Grace { get; set; }
        internal long GraceStarted { get; set; }
        internal uint LastOldMessageId { get; set; }
        internal long SendBytes { get; set; }
        internal long ReceiveBytes { get; set; }
        internal long ReservedSend { get; set; }
        internal long ReservedReceive { get; set; }
        internal FlowTokenBucket ReceiveRate { get; } = new();
    }

    /// <summary>
    /// Retained complete proposal and independent directional transaction progress.
    /// </summary>
    internal sealed class FlowUpdate(
        uint operationId,
        uint revision,
        DataChannelParametersDataType parameters,
        long started)
    {
        internal uint Id { get; } = operationId;
        internal uint Revision { get; } = revision;
        internal DataChannelParametersDataType Parameters { get; } = parameters;
        internal long Started { get; set; } = started;
        internal bool Authorized { get; set; }
        internal bool Accepted { get; set; }
        internal bool CommitQueued { get; set; }
        internal bool LocalBarrier { get; set; }
        internal bool RemoteBarrier { get; set; }
        internal bool Acknowledged { get; set; }
        internal bool AckSent { get; set; }
        internal bool Completed { get; set; }
        internal bool OperationReserved { get; set; }
        internal uint LocalLastOld { get; set; }
        internal uint RemoteLastOld { get; set; }
        internal DataChannelResult Result { get; set; }
        internal TaskCompletionSource<DataChannelResult> Completion { get; set; } = FlowState.NewCompletion();
        internal bool Committed => LocalBarrier || RemoteBarrier;
    }

    /// <summary>
    /// One owned outgoing record, retained until the actual carrier finishes using its bytes.
    /// </summary>
    internal sealed class FlowWriteItem
    {
        internal FlowState? Flow { get; init; }
        internal FlowControl? Control { get; set; }
        internal FlowRecord Record { get; set; }
        internal ByteString Encoded { get; set; }
        internal DataChannelPath Path { get; init; }
        internal ulong StreamId { get; init; }
        internal uint RequestId { get; set; }
        internal uint ChannelId { get; init; }
        internal int Cost { get; set; }
        internal int StorageCost { get; init; }
        internal int PayloadLength { get; init; }
        internal long QueuedAt { get; init; }
        internal TimeSpan? TimeToLive { get; init; }
        internal CancellationToken Cancellation { get; init; }
        internal Func<DataChannelResult>? CommitAction { get; init; }
        internal Action? DiscardAction { get; init; }
        internal bool Committed { get; set; }
        internal bool Released { get; set; }
        internal bool ControlReserved { get; set; }
        internal Task PreviousControl { get; set; } = Task.CompletedTask;
        internal TaskCompletionSource<DataChannelResult> Completion { get; } = FlowState.NewCompletion();
        internal bool IsData => Control == null;
    }

    /// <summary>
    /// A non-wrapping 64-message replay window shared across revisions of one data direction.
    /// </summary>
    internal sealed class FlowReplayWindow
    {
        /// <summary>
        /// Checks novelty without consuming an identifier on a rejected admission.
        /// </summary>
        internal bool IsNovel(uint messageId)
        {
            if (messageId == 0)
            {
                return false;
            }
            if (messageId > m_high)
            {
                return true;
            }
            uint distance = m_high - messageId;
            return distance < 64 && (m_bits & (1UL << (int)distance)) == 0;
        }

        /// <summary>
        /// Records an admitted identifier after all revision and capacity checks.
        /// </summary>
        internal void Accept(uint messageId)
        {
            if (messageId > m_high)
            {
                uint distance = messageId - m_high;
                m_bits = distance >= 64 ? 1UL : (m_bits << (int)distance) | 1UL;
                m_high = messageId;
            }
            else
            {
                m_bits |= 1UL << (int)(m_high - messageId);
            }
        }

        private uint m_high;
        private ulong m_bits;
    }

    /// <summary>
    /// Monotonic encoded-byte token bucket. Transport congestion control is not emulated here.
    /// </summary>
    internal sealed class FlowTokenBucket
    {
        /// <summary>
        /// Installs an admitted rate/burst without increasing existing credit on a revision.
        /// </summary>
        internal void Configure(double rate, uint burst, long now, bool initial)
        {
            m_rate = rate;
            m_burst = burst;
            m_tokens = initial ? burst : Math.Min(m_tokens, burst);
            m_updated = now;
        }

        /// <summary>
        /// Returns the required monotonic wait, without consuming capacity.
        /// </summary>
        internal TimeSpan Delay(int cost, TimeProvider clock)
        {
            return Delay(cost, clock, clock.GetTimestamp());
        }

        /// <summary>
        /// Calculates the wait using an existing clock snapshot without triggering clock-read callbacks.
        /// </summary>
        internal TimeSpan Delay(int cost, TimeProvider clock, long timestamp)
        {
            Refill(clock, timestamp);
            if (m_rate == 0 || m_tokens >= cost)
            {
                return TimeSpan.Zero;
            }
            double seconds = (cost - m_tokens) / m_rate;
            return TimeSpan.FromSeconds(Math.Min(seconds, int.MaxValue / 1000.0));
        }

        /// <summary>
        /// Charges admitted bytes once, at commitment/admission.
        /// </summary>
        internal bool TryConsume(int cost, TimeProvider clock)
        {
            if (Delay(cost, clock) != TimeSpan.Zero)
            {
                return false;
            }
            if (m_rate != 0)
            {
                m_tokens -= cost;
            }
            return true;
        }

        /// <summary>
        /// Refills to no more than the finite admitted burst.
        /// </summary>
        private void Refill(TimeProvider clock, long now)
        {
            m_tokens = Math.Min(m_burst, m_tokens + (clock.GetElapsedTime(m_updated, now).TotalSeconds * m_rate));
            m_updated = now;
        }

        private double m_rate;
        private double m_tokens;
        private uint m_burst;
        private long m_updated;
    }

    /// <summary>
    /// One unanswered bounded timestamp probe.
    /// </summary>
    internal sealed record FlowPing(uint Flow, ulong Timestamp, long Started)
    {
        internal TaskCompletionSource<DataChannelResult> Completion { get; } = FlowState.NewCompletion();
    }
}
