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
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Client.Subscriptions
{
    /// <summary>
    /// Processes messages inside a subscription, it is the base class for all
    /// subscription objects but basis to support better testing in isolation.
    /// </summary>
    internal abstract class MessageProcessor : IMessageProcessor, IAsyncDisposable
    {
        /// <inheritdoc/>
        public uint Id
        {
            get => m_id;
            internal set
            {
                m_id = value;
                if (value != 0)
                {
                    // Save non-zero server id for completion
                    m_lastServerId = value;
                }
            }
        }

        /// <inheritdoc/>
        public long MessageGeneration => Volatile.Read(ref m_generation);

        /// <inheritdoc/>
        public AcknowledgementScope GetAcknowledgementScope(long generation)
        {
            return generation == MessageGeneration
                ? AcknowledgementScope.GetOrCreate(ref m_acknowledgementScope, this, generation, Id)
                : new AcknowledgementScope(this, generation, Id);
        }

        /// <summary>
        /// Number of notification messages detected as missing during
        /// gap-walking of the SequenceNumber on this subscription.
        /// </summary>
        public long MissingMessageCount => Volatile.Read(ref m_missingCount);

        /// <summary>
        /// Number of republish requests issued for this subscription
        /// (counts every attempt regardless of outcome).
        /// </summary>
        public long RepublishMessageCount => Volatile.Read(ref m_republishCount);

        /// <summary>
        /// Observability context
        /// </summary>
        protected ITelemetryContext Observability { get; }

        /// <summary>
        /// Whether the message processor should release pooled notification
        /// payload instances back to their activator pools after the handler
        /// dispatch completes. Reflects the current
        /// <see cref="IMessageAckQueue.PoolNotifications"/> setting on the
        /// subscription manager; toggling the manager-level setting takes
        /// effect on the next dispatch.
        /// </summary>
        protected bool PoolNotifications => AckQueue.PoolNotifications;

        /// <summary>
        /// Create subscription
        /// </summary>
        /// <param name="services"></param>
        /// <param name="completion"></param>
        /// <param name="telemetry"></param>
        /// <param name="timeProvider">Optional <see cref="TimeProvider"/>
        /// for elapsed-time and timestamp calculations. Defaults to
        /// <see cref="TimeProvider.System"/> when <c>null</c>.</param>
        protected MessageProcessor(
            ISubscriptionServiceSetClientMethods services,
            IMessageAckQueue completion,
            ITelemetryContext telemetry,
            TimeProvider? timeProvider = null)
        {
            TimeProvider = timeProvider ?? TimeProvider.System;
            Observability = telemetry;
            AvailableInRetransmissionQueue = [];
            Logger = Observability.LoggerFactory.CreateLogger<Subscription>();
            m_services = services;
            AckQueue = completion;
            m_messages = Channel.CreateUnboundedPrioritized(
                new UnboundedPrioritizedChannelOptions<IncomingMessage>
                {
                    SingleReader = true,
                    Comparer = Comparer<IncomingMessage>.Create(
                        IncomingMessage.Compare)
                });
            m_messageCapacity = new SemaphoreSlim(kIncomingMessageCapacity);
            m_messageWorkerTask = ProcessReceivedMessagesAsync(m_cts.Token);
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            if (IsDispatchingNotification)
            {
                throw new InvalidOperationException(
                    "A subscription cannot be disposed from one of its " +
                    "notification callbacks. Schedule disposal after the " +
                    "callback returns.");
            }
            GC.SuppressFinalize(this);
            return DisposeAsync(true);
        }

        /// <inheritdoc/>
        public virtual async ValueTask OnPublishReceivedAsync(
            NotificationMessage message,
            IReadOnlyList<uint>? availableSequenceNumbers,
            IReadOnlyList<string> stringTable)
        {
            CancellationToken shutdownToken = AckQueue.ShutdownToken;
            shutdownToken.ThrowIfCancellationRequested();
            long generation = Volatile.Read(ref m_generation);
            uint[] availableSequenceNumberSnapshot = availableSequenceNumbers is null
                ? []
                : [.. availableSequenceNumbers];
            if (availableSequenceNumbers != null)
            {
                AvailableInRetransmissionQueue = availableSequenceNumberSnapshot;
            }
            LastNotificationTimestamp = TimeProvider.GetTimestamp();
            if (!await m_messageCapacity.WaitAsync(0, m_cts.Token).ConfigureAwait(false))
            {
                // Link shutdown only while backpressured, not on the normal admission path.
                using CancellationTokenSource? admission = shutdownToken.CanBeCanceled
                    ? CancellationTokenSource.CreateLinkedTokenSource(m_cts.Token, shutdownToken)
                    : null;
                await m_messageCapacity.WaitAsync(admission?.Token ?? m_cts.Token).ConfigureAwait(false);
            }
            try
            {
                await m_messages.Writer.WriteAsync(new IncomingMessage(message, stringTable,
                    availableSequenceNumberSnapshot,
                    TimeProvider.GetUtcNow(),
                    generation))
                    .ConfigureAwait(false);
            }
            catch
            {
                m_messageCapacity.Release();
                throw;
            }
        }

        /// <summary>
        /// Dispose subscription
        /// </summary>
        /// <param name="disposing"></param>
        protected virtual async ValueTask DisposeAsync(bool disposing)
        {
            if (disposing && !Disposed)
            {
                try
                {
                    m_messages.Writer.TryComplete();
                    await m_cts.CancelAsync().ConfigureAwait(false);
                    await m_messageWorkerTask.ConfigureAwait(false);
                }
                finally
                {
                    m_messageDispatchGate.Dispose();
                    m_cts.Dispose();
                    m_messageCapacity.Dispose();
                    (m_messages as IDisposable)?.Dispose();
                    m_pendingMessages.Clear();
                    Disposed = true;
                }
            }
        }

        /// <summary>
        /// Process status change notification
        /// </summary>
        /// <param name="sequenceNumber"></param>
        /// <param name="publishTime"></param>
        /// <param name="notification"></param>
        /// <param name="publishStateMask"></param>
        /// <param name="stringTable"></param>
        /// <returns></returns>
        protected abstract ValueTask OnStatusChangeNotificationAsync(
            uint sequenceNumber,
            DateTime publishTime,
            StatusChangeNotification notification,
            PublishState publishStateMask,
            IReadOnlyList<string> stringTable);

        /// <summary>
        /// Process keep alive notification
        /// </summary>
        /// <param name="sequenceNumber"></param>
        /// <param name="publishTime"></param>
        /// <param name="publishStateMask"></param>
        /// <returns></returns>
        protected abstract ValueTask OnKeepAliveNotificationAsync(
            uint sequenceNumber,
            DateTime publishTime,
            PublishState publishStateMask);

        /// <summary>
        /// Process data change notification
        /// </summary>
        /// <param name="sequenceNumber"></param>
        /// <param name="publishTime"></param>
        /// <param name="notification"></param>
        /// <param name="publishStateMask"></param>
        /// <param name="stringTable"></param>
        /// <returns></returns>
        protected abstract ValueTask OnDataChangeNotificationAsync(
            uint sequenceNumber,
            DateTime publishTime,
            DataChangeNotification notification,
            PublishState publishStateMask,
            IReadOnlyList<string> stringTable);

        /// <summary>
        /// Process event notification
        /// </summary>
        /// <param name="sequenceNumber"></param>
        /// <param name="publishTime"></param>
        /// <param name="notification"></param>
        /// <param name="publishStateMask"></param>
        /// <param name="stringTable"></param>
        /// <returns></returns>
        protected abstract ValueTask OnEventDataNotificationAsync(
            uint sequenceNumber,
            DateTime publishTime,
            EventNotificationList notification,
            PublishState publishStateMask,
            IReadOnlyList<string> stringTable);

        /// <summary>
        /// Whether the processor has opted into complete-message delivery.
        /// </summary>
        protected virtual bool HandlesNotificationMessages => false;

        /// <summary>
        /// Delivers a complete message when <see cref="HandlesNotificationMessages"/> is true.
        /// </summary>
        protected virtual ValueTask OnNotificationMessageAsync(
            NotificationMessage message, PublishState publishStateMask,
            IReadOnlyList<string> stringTable, CancellationToken ct)
        {
            throw new NotSupportedException("Complete-message delivery is not enabled.");
        }

        /// <summary>
        /// On publish state changed
        /// </summary>
        /// <param name="stateMask"></param>
        /// <returns></returns>
        protected virtual void OnPublishStateChanged(PublishState stateMask)
        {
            if ((stateMask & PublishState.Stopped) == PublishState.Stopped)
            {
                Logger.SubscriptionSTOPPED(Id);
            }
            if ((stateMask & PublishState.Recovered) == PublishState.Recovered)
            {
                Logger.SubscriptionRECOVERED(Id);
            }
            if ((stateMask & PublishState.Completed) == PublishState.Completed)
            {
                Logger.SubscriptionCLOSED(Id);
            }
        }

        /// <summary>
        /// Processes all incoming messages and dispatch them.
        /// </summary>
        /// <param name="ct"></param>
        private async Task ProcessReceivedMessagesAsync(CancellationToken ct)
        {
            try
            {
                //
                // This can be optimized to peek when missing sequence number
                // and not in available sequence number als to support batching.
                // TODO This also needs to guard against overruns using some
                // form of semaphore to block the publisher.
                // Unless we get https://github.com/dotnet/runtime/issues/101292
                //
                // At most one read wait is outstanding. A wait that lost the
                // race against the gap timer is reused rather than abandoned,
                // because an abandoned wait can consume the only wake-up for
                // the next Publish response.
                Task<bool>? waitForMessage = null;
                while (true)
                {
                    if (m_messages.Reader.TryRead(out IncomingMessage incoming))
                    {
                        try
                        {
                            await ProcessMessageAsync(incoming, ct).ConfigureAwait(false);
                        }
                        finally
                        {
                            m_messageCapacity.Release();
                        }

                        // Check the deadline after every response so a steady
                        // backlog of keep-alives or duplicates cannot hold
                        // buffered data past the gap recovery timeout.
                        if (m_pendingMessages.Count >= kMaxPendingMessages ||
                            IsGapRecoveryExpired())
                        {
                            await SkipExpiredGapAsync(ct).ConfigureAwait(false);
                        }
                        continue;
                    }

                    if (IsGapRecoveryExpired())
                    {
                        await SkipExpiredGapAsync(ct).ConfigureAwait(false);
                        continue;
                    }

                    waitForMessage ??= m_messages.Reader.WaitToReadAsync(ct).AsTask();
                    if (!HasUnresolvedGap)
                    {
                        bool readable = await waitForMessage.ConfigureAwait(false);
                        waitForMessage = null;
                        if (!readable)
                        {
                            break;
                        }
                        continue;
                    }

                    TimeSpan remaining = kGapRecoveryTimeout -
                        TimeProvider.GetElapsedTime(m_gapStartedTimestamp);
                    var gapTimeout = new TaskCompletionSource<bool>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    using (TimeProvider.CreateTimer(
                        static state =>
                        {
                            ((TaskCompletionSource<bool>)state!).TrySetResult(true);
                        },
                        gapTimeout,
                        remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero,
                        Timeout.InfiniteTimeSpan))
                    {
                        if (IsGapRecoveryExpired())
                        {
                            await SkipExpiredGapAsync(ct).ConfigureAwait(false);
                            continue;
                        }
                        Task completed = await Task.WhenAny(waitForMessage, gapTimeout.Task)
                            .ConfigureAwait(false);
                        if (completed == gapTimeout.Task)
                        {
                            ct.ThrowIfCancellationRequested();
                            await SkipExpiredGapAsync(ct).ConfigureAwait(false);
                            continue;
                        }
                    }
                    bool hasMessages = await waitForMessage.ConfigureAwait(false);
                    waitForMessage = null;
                    if (!hasMessages)
                    {
                        await SkipExpiredGapAsync(ct).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Logger.SubscriptionErrorProcessingMessagesProcessorExiting(
                    ex,
                    Id);

                // Do not call complete here as we do not want the subscription
                // to be removed
                throw;
            }
            await AckQueue.CompleteAsync(this, m_lastServerId, default).ConfigureAwait(false);
            OnPublishStateChanged(PublishState.Completed);
        }

        /// <summary>
        /// Notify the owning manager/queue that the subscription's
        /// state has changed (e.g. it has just been created on the
        /// server). The manager uses this signal to re-evaluate the
        /// publish worker pool.
        /// </summary>
        /// <param name="createdAt">The server time of the CreateSubscription
        /// response, or null if unknown.</param>
        protected void NotifyManagerOfCreation(DateTimeUtc createdAt = default)
        {
            AckQueue.OnSubscriptionCreated(this, Id, createdAt);
            AckQueue.Update();
        }

        /// <summary>
        /// Process message. The logic checks whether any message was missed
        /// and try to republish. Any new messages received that already were
        /// handled are discarded. The message is then dispatched to the appropriate
        /// callbacks that must be overridden by the derived class.
        /// </summary>
        /// <remarks>
        /// Sequence numbers are circular per OPC UA Part 4 §7.30.5 — they
        /// wrap from <see cref="uint.MaxValue"/> to 1 (skipping 0). The
        /// implementation uses unsigned-difference arithmetic to detect
        /// "older vs newer" across a wraparound, and walks the
        /// missing-message range explicitly wrapping past
        /// <see cref="uint.MaxValue"/> to 1.
        /// </remarks>
        /// <param name="incoming"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        private async Task ProcessMessageAsync(IncomingMessage incoming, CancellationToken ct)
        {
            await m_messageDispatchGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (AckQueue.ShutdownToken.IsCancellationRequested ||
                    incoming.Generation != Volatile.Read(ref m_generation))
                {
                    return;
                }
                await ProcessMessageCoreAsync(incoming, ct).ConfigureAwait(false);
            }
            finally
            {
                m_messageDispatchGate.Release();
            }
        }

        private async Task ProcessMessageCoreAsync(
            IncomingMessage incoming, CancellationToken ct)
        {
            uint curSeqNum = incoming.Message.SequenceNumber;
            bool isKeepAlive = incoming.Message.NotificationData.Count == 0;
            const uint kBackwardThreshold = 1u << 31;

            if (isKeepAlive)
            {
                // Per OPC UA Part 4 §7.30, a keep-alive NotificationMessage
                // carries the SequenceNumber of the next NotificationMessage
                // to be sent.  Multiple consecutive keep-alives therefore
                // reuse the same SequenceNumber, and the next real data
                // message reuses it too (the slot is consumed only when data
                // is emitted).  Bypass the duplicate-message filter, surface
                // the keep-alive, and DO NOT advance the data-dedup gate
                // (LastDataSequenceNumberProcessed) — otherwise the next real
                // data message would be silently dropped as a duplicate.
                //
                // Because the keep-alive names the next sequence number, a
                // keep-alive more than one ahead of the last data message
                // proves the messages in between were sent but never
                // received (Part 4 §5.14.1.1). Recover them now rather than
                // waiting for the next data message, which on a quiet
                // subscription may never come before the server drops them.
                await RecoverMissingBeforeKeepAliveAsync(
                    curSeqNum,
                    incoming.AvailableSequenceNumbers,
                    ct)
                    .ConfigureAwait(false);
                LastSequenceNumberProcessed = curSeqNum;
                await OnNotificationReceivedAsync(
                    incoming.Message,
                    PublishState.KeepAlive,
                    incoming.StringTable ?? [],
                    ct).ConfigureAwait(false);
                return;
            }

            // Data / event message — apply the dedup / gap-walk logic
            // against the highest-consumed data SequenceNumber.
            uint prevDataSeq = LastDataSequenceNumberProcessed;
            if (prevDataSeq != 0)
            {
                uint delta = unchecked(curSeqNum - prevDataSeq);
                if (delta is 0 or >= kBackwardThreshold)
                {
                    if (curSeqNum == prevDataSeq)
                    {
                        Logger.SubscriptionReceivedDuplicateMessageSequenceNumber(
                            Id,
                            curSeqNum);
                    }
                    else
                    {
                        Logger.SubscriptionReceivedOlderMessageSequenceNumber(
                            Id,
                            curSeqNum,
                            prevDataSeq);
                    }
                    bool firstReceipt = m_notDispatched.Remove(curSeqNum);
                    firstReceipt |= TryConsumeSkippedSequenceNumber(curSeqNum);
                    if (firstReceipt)
                    {
                        // The gate moved past this message without delivering
                        // it. Admit its acknowledgement lifetime now so a
                        // tracking acknowledgement queue does not drop the
                        // acknowledgement and leave it on the server.
                        AckQueue.TrackAcknowledgement(this, MessageGeneration, curSeqNum);
                    }
                    await QueueAcknowledgementAsync(curSeqNum, ct).ConfigureAwait(false);
                    return;
                }

                // Account for the whole gap, but only try consecutive messages
                // from the Publish response that supplied this notification.
                // If a sequence is not in this response or Republish fails,
                // retain the current message and wait for its original.
                uint gap = delta - 1;
                if (gap != 0 && curSeqNum < prevDataSeq)
                {
                    // The skipped range crosses the wrap point. Sequence
                    // numbers roll over from uint.MaxValue straight to 1
                    // (Part 4 §5.14.5.1), so zero is in the arithmetic range
                    // but was never sent and is not a missing message.
                    gap--;
                }
                if (gap != 0)
                {
                    AccountMissingMessages(prevDataSeq, curSeqNum);

                    var candidates = new HashSet<uint>();
                    IReadOnlyList<uint> available = incoming.AvailableSequenceNumbers;
                    for (int i = 0; i < available.Count; i++)
                    {
                        uint seq = available[i];
                        uint offset = unchecked(seq - prevDataSeq);
                        if (offset != 0 && offset < delta)
                        {
                            candidates.Add(seq);
                        }
                    }
                    foreach (uint seq in m_pendingMessages.Keys)
                    {
                        uint offset = unchecked(seq - prevDataSeq);
                        if (offset != 0 && offset < delta)
                        {
                            candidates.Add(seq);
                        }
                    }
                    foreach (uint seq in SortAscendingWrapAware(candidates.ToArray()))
                    {
                        if (seq != GetNextSequenceNumber(LastDataSequenceNumberProcessed))
                        {
                            break;
                        }
                        if (m_pendingMessages.Remove(seq, out IncomingMessage pending))
                        {
                            await DispatchDataMessageAsync(pending, ct).ConfigureAwait(false);
                        }
                        else if (!await TryRepublishAsync(
                            seq,
                            curSeqNum,
                            incoming.AvailableSequenceNumbers,
                            ct).ConfigureAwait(false))
                        {
                            break;
                        }
                        LastDataSequenceNumberProcessed = seq;
                        LastSequenceNumberProcessed = seq;
                    }
                    if (GetNextSequenceNumber(LastDataSequenceNumberProcessed) != curSeqNum)
                    {
                        BufferDataMessage(incoming);
                        return;
                    }
                }
            }
            else
            {
                // First data message after subscription create / recreate /
                // transfer. We don't know what came before, but the
                // server may still hold older retransmissible messages
                // (e.g. on a transferred subscription where the first
                // received SequenceNumber is greater than 1). Walk only
                // the published AvailableInRetransmissionQueue so the
                // cost is bounded by the server's queue size, not the
                // raw gap. Mirrors the V1 fix in PR #3565.
                IReadOnlyList<uint> available = incoming.AvailableSequenceNumbers;
                for (int i = 0; i < available.Count; i++)
                {
                    uint seq = available[i];
                    uint delta = unchecked(curSeqNum - seq);
                    if (delta is not 0 and < kBackwardThreshold)
                    {
                        await TryRepublishAsync(
                            seq,
                            curSeqNum,
                            incoming.AvailableSequenceNumbers,
                            ct).ConfigureAwait(false);
                    }
                }
            }
            await DispatchDataMessageAsync(incoming, ct).ConfigureAwait(false);
            await DrainPendingMessagesAsync(ct).ConfigureAwait(false);
        }

        private void BufferDataMessage(IncomingMessage incoming)
        {
            uint sequenceNumber = incoming.Message.SequenceNumber;
            if (!m_pendingMessages.TryAdd(sequenceNumber, incoming))
            {
                Logger.SubscriptionReceivedDuplicateMessageSequenceNumber(
                    Id,
                    sequenceNumber);
                return;
            }
            if (m_pendingMessages.Count == 1 && m_keepAliveGapEnd == 0)
            {
                m_gapStartedTimestamp = TimeProvider.GetTimestamp();
            }
        }

        /// <summary>
        /// Dispatch a notification and advance the processed sequence numbers.
        /// </summary>
        private async ValueTask DispatchDataMessageAsync(
            IncomingMessage incoming,
            CancellationToken ct)
        {
            uint sequenceNumber = incoming.Message.SequenceNumber;
            LastDataSequenceNumberProcessed = sequenceNumber;
            LastSequenceNumberProcessed = sequenceNumber;
            m_notDispatched.Remove(sequenceNumber);
            m_pendingMessages.Remove(sequenceNumber);
            await OnNotificationReceivedAsync(
                incoming.Message,
                PublishState.None,
                incoming.StringTable ?? [],
                ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Dispatch buffered notifications that immediately follow the data gate.
        /// </summary>
        private async ValueTask DrainPendingMessagesAsync(CancellationToken ct)
        {
            RemoveStalePendingMessages();
            bool advanced = false;
            while (LastDataSequenceNumberProcessed != 0 &&
                m_pendingMessages.Remove(
                    GetNextSequenceNumber(LastDataSequenceNumberProcessed),
                    out IncomingMessage pending))
            {
                await DispatchDataMessageAsync(pending, ct).ConfigureAwait(false);
                advanced = true;
            }
            UpdateGapTimer(advanced);
        }

        private async Task SkipExpiredGapAsync(CancellationToken ct)
        {
            await m_messageDispatchGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                RemoveStalePendingMessages();
                if (!HasUnresolvedGap)
                {
                    return;
                }

                // Skip up to the newer of the oldest buffered message and the
                // keep-alive that proved a gap. Everything before it was sent.
                uint gapEnd = m_keepAliveGapEnd;
                if (m_pendingMessages.Count != 0)
                {
                    uint firstPending = GetOldestPendingSequenceNumber();
                    if (gapEnd == 0 || IsNewerSequenceNumber(firstPending, gapEnd))
                    {
                        gapEnd = firstPending;
                    }
                }
                uint firstMissing = GetNextSequenceNumber(LastDataSequenceNumberProcessed);
                Logger.SubscriptionGapRecoveryTimedOut(
                    Id,
                    firstMissing,
                    gapEnd);
                await RecoverAvailableBeforeAsync(gapEnd, ct).ConfigureAwait(false);
                await DrainPendingMessagesAsync(ct).ConfigureAwait(false);

                // A hole left behind the skipped range is a new gap and gets
                // its own recovery window.
                UpdateGapTimer(advanced: true);
            }
            finally
            {
                m_messageDispatchGate.Release();
            }
        }

        /// <summary>
        /// Move the data gate up to the message before
        /// <paramref name="gapEnd"/>. Buffered originals are delivered and
        /// messages the server still lists as available are republished in
        /// order; the holes between them are skipped and remembered so a
        /// late original is still acknowledged.
        /// </summary>
        private async ValueTask RecoverAvailableBeforeAsync(
            uint gapEnd,
            CancellationToken ct)
        {
            uint lastData = LastDataSequenceNumberProcessed;
            uint span = unchecked(gapEnd - lastData);
            IReadOnlyList<uint> available = AvailableInRetransmissionQueue;
            var candidates = new HashSet<uint>();
            for (int i = 0; i < available.Count; i++)
            {
                uint offset = unchecked(available[i] - lastData);
                if (offset != 0 && offset < span)
                {
                    candidates.Add(available[i]);
                }
            }
            foreach (uint seq in m_pendingMessages.Keys)
            {
                uint offset = unchecked(seq - lastData);
                if (offset != 0 && offset < span)
                {
                    candidates.Add(seq);
                }
            }
            foreach (uint seq in SortAscendingWrapAware(candidates.ToArray()))
            {
                uint next = GetNextSequenceNumber(LastDataSequenceNumberProcessed);
                if (seq != next)
                {
                    RecordSkippedSequenceNumbers(next, seq);
                }
                if (m_pendingMessages.Remove(seq, out IncomingMessage pending))
                {
                    await DispatchDataMessageAsync(pending, ct).ConfigureAwait(false);
                    continue;
                }
                if (!await TryRepublishAsync(seq, gapEnd, available, ct)
                    .ConfigureAwait(false))
                {
                    RecordSkippedSequenceNumbers(seq, GetNextSequenceNumber(seq));
                }
                LastDataSequenceNumberProcessed = seq;
            }
            uint firstSkipped = GetNextSequenceNumber(LastDataSequenceNumberProcessed);
            if (firstSkipped != gapEnd)
            {
                RecordSkippedSequenceNumbers(firstSkipped, gapEnd);
            }
            LastDataSequenceNumberProcessed = gapEnd == 1
                ? uint.MaxValue
                : gapEnd - 1;
        }

        /// <summary>
        /// Get the oldest pending sequence number relative to the data gate.
        /// </summary>
        private uint GetOldestPendingSequenceNumber()
        {
            uint oldest = 0;
            uint oldestOffset = uint.MaxValue;
            foreach (uint sequenceNumber in m_pendingMessages.Keys)
            {
                uint offset = unchecked(sequenceNumber - LastDataSequenceNumberProcessed);
                if (offset < oldestOffset)
                {
                    oldest = sequenceNumber;
                    oldestOffset = offset;
                }
            }
            return oldest;
        }

        private bool IsGapRecoveryExpired()
        {
            return HasUnresolvedGap &&
                TimeProvider.GetElapsedTime(m_gapStartedTimestamp) >= kGapRecoveryTimeout;
        }

        /// <summary>
        /// A gap is open while data is buffered behind it or a keep-alive
        /// proved messages missing that the server can still republish.
        /// </summary>
        private bool HasUnresolvedGap => m_pendingMessages.Count != 0 || m_keepAliveGapEnd != 0;

        /// <summary>
        /// Close a keep-alive gap the data gate has reached and keep the gap
        /// timer running only while a gap is open. A gap that just started,
        /// or that moved because the gate <paramref name="advanced"/>, gets
        /// a fresh recovery window.
        /// </summary>
        private void UpdateGapTimer(bool advanced)
        {
            if (m_keepAliveGapEnd != 0 &&
                (LastDataSequenceNumberProcessed == 0 ||
                !IsNewerSequenceNumber(
                    m_keepAliveGapEnd,
                    GetNextSequenceNumber(LastDataSequenceNumberProcessed))))
            {
                m_keepAliveGapEnd = 0;
            }
            if (!HasUnresolvedGap)
            {
                m_gapStartedTimestamp = 0;
            }
            else if (advanced || m_gapStartedTimestamp == 0)
            {
                m_gapStartedTimestamp = TimeProvider.GetTimestamp();
            }
        }

        /// <summary>
        /// Drop buffered copies the data gate has already passed. A message
        /// can be buffered and then delivered by another route (a newer
        /// copy, Republish or transfer recovery); the stale copy must not be
        /// delivered again when the gap timeout skips ahead.
        /// </summary>
        private void RemoveStalePendingMessages()
        {
            uint lastData = LastDataSequenceNumberProcessed;
            if (lastData == 0 || m_pendingMessages.Count == 0)
            {
                return;
            }
            List<uint>? stale = null;
            foreach (uint sequenceNumber in m_pendingMessages.Keys)
            {
                if (!IsNewerSequenceNumber(sequenceNumber, lastData))
                {
                    (stale ??= []).Add(sequenceNumber);
                }
            }
            if (stale == null)
            {
                return;
            }
            foreach (uint sequenceNumber in stale)
            {
                m_pendingMessages.Remove(sequenceNumber);
            }
            UpdateGapTimer(advanced: false);
        }

        /// <summary>
        /// Count the messages missing between <paramref name="previous"/> and
        /// <paramref name="next"/> exactly once, even when buffering or a
        /// keep-alive already counted part of the range.
        /// </summary>
        /// <param name="previous">The last data message processed.</param>
        /// <param name="next">The upper bound of the range.</param>
        /// <param name="nextReceived"><c>true</c> if <paramref name="next"/>
        /// is a received data message. A keep-alive only names the next
        /// sequence number; it does not consume it, so that message can
        /// still go missing and must be counted later.</param>
        private void AccountMissingMessages(uint previous, uint next, bool nextReceived = true)
        {
            uint start = previous;
            if (m_missingAccountedThrough != 0 &&
                IsNewerSequenceNumber(m_missingAccountedThrough, start))
            {
                start = m_missingAccountedThrough;
            }
            if (!IsNewerSequenceNumber(next, start))
            {
                return;
            }
            uint gap = unchecked(next - start) - 1;
            if (gap != 0 && next < start)
            {
                // Zero is skipped at the wrap point (Part 4 §5.14.5.1).
                gap--;
            }
            if (gap != 0)
            {
                Interlocked.Add(ref m_missingCount, gap);
            }
            m_missingAccountedThrough = nextReceived
                ? next
                : next == 1 ? uint.MaxValue : next - 1;
        }

        /// <summary>
        /// Remember the sequence numbers the gap timeout moved the data gate
        /// past without delivering them. Their acknowledgement lifetime was
        /// never admitted, so a late original must be tracked before it is
        /// acknowledged.
        /// </summary>
        private void RecordSkippedSequenceNumbers(uint firstMissing, uint firstPending)
        {
            uint sequenceNumber = firstMissing;
            for (int i = 0; i < kMaxSkippedSequenceNumbers && sequenceNumber != firstPending; i++)
            {
                m_skippedSequenceNumbers[m_skippedSequenceNumberIndex] = sequenceNumber;
                m_skippedSequenceNumberIndex =
                    (m_skippedSequenceNumberIndex + 1) % kMaxSkippedSequenceNumbers;
                sequenceNumber = GetNextSequenceNumber(sequenceNumber);
            }
        }

        private bool TryConsumeSkippedSequenceNumber(uint sequenceNumber)
        {
            if (sequenceNumber == 0)
            {
                return false;
            }
            for (int i = 0; i < m_skippedSequenceNumbers.Length; i++)
            {
                if (m_skippedSequenceNumbers[i] == sequenceNumber)
                {
                    m_skippedSequenceNumbers[i] = 0;
                    return true;
                }
            }
            return false;
        }

        private static uint GetNextSequenceNumber(uint sequenceNumber)
        {
            return sequenceNumber == uint.MaxValue ? 1 : sequenceNumber + 1;
        }

        /// <summary>
        /// Queue an acknowledgement for a processed notification.
        /// </summary>
        private ValueTask QueueAcknowledgementAsync(
            uint sequenceNumber,
            CancellationToken ct)
        {
            return AckQueue.QueueAsync(
                this,
                MessageGeneration,
                new SubscriptionAcknowledgement
                {
                    SequenceNumber = sequenceNumber,
                    SubscriptionId = Id
                },
                ct);
        }

        /// <summary>
        /// Republish the data messages a keep-alive proves are missing. The
        /// keep-alive carries the sequence number of the next message to be
        /// sent, so every sequence number between the last processed data
        /// message and the keep-alive was sent and is missing.
        /// </summary>
        /// <param name="keepAliveSeqNum">The sequence number of the
        /// keep-alive.</param>
        /// <param name="availableSequenceNumbers">The availability snapshot
        /// from the Publish response containing this keep-alive.</param>
        /// <param name="ct">Cancellation token.</param>
        private async ValueTask RecoverMissingBeforeKeepAliveAsync(
            uint keepAliveSeqNum,
            IReadOnlyList<uint> availableSequenceNumbers,
            CancellationToken ct)
        {
            const uint kBackwardThreshold = 1u << 31;
            uint prevDataSeq = LastDataSequenceNumberProcessed;
            if (prevDataSeq == 0)
            {
                // Nothing to compare against yet (first message after create).
                return;
            }
            uint delta = unchecked(keepAliveSeqNum - prevDataSeq);
            if (delta is 0 or 1 or >= kBackwardThreshold)
            {
                // No gap, or a stale keep-alive.
                return;
            }
            uint gap = delta - 1;
            if (keepAliveSeqNum < prevDataSeq)
            {
                // Zero is skipped at the wrap point (Part 4 §5.14.5.1).
                gap--;
            }
            if (gap == 0)
            {
                return;
            }
            IReadOnlyList<uint> available = availableSequenceNumbers;
            var candidates = new HashSet<uint>();
            for (int i = 0; i < available.Count; i++)
            {
                uint seq = available[i];
                uint offset = unchecked(seq - prevDataSeq);
                if (offset != 0 && offset < delta)
                {
                    candidates.Add(seq);
                }
            }
            foreach (uint seq in m_pendingMessages.Keys)
            {
                uint offset = unchecked(seq - prevDataSeq);
                if (offset != 0 && offset < delta)
                {
                    candidates.Add(seq);
                }
            }
            if (candidates.Count == 0)
            {
                // Nothing the server holds can be recovered now. Leave the
                // data gate where it is: a missing message may still be in
                // flight on another publish worker, and advancing the gate
                // would discard it as a duplicate when it arrives. The next
                // data message accounts for the gap.
                await DrainPendingMessagesAsync(ct).ConfigureAwait(false);
                return;
            }
            AccountMissingMessages(prevDataSeq, keepAliveSeqNum, nextReceived: false);

            // Recover strictly in sequence order and advance the data gate
            // only over what was actually delivered. Stop at the first hole:
            // a missing message may still arrive on another publish worker,
            // and the gap timeout skips it if it never does.
            uint[] ordered = SortAscendingWrapAware(candidates.ToArray());
            foreach (uint seq in ordered)
            {
                if (seq != GetNextSequenceNumber(LastDataSequenceNumberProcessed))
                {
                    break;
                }
                if (m_pendingMessages.Remove(seq, out IncomingMessage pending))
                {
                    await DispatchDataMessageAsync(pending, ct).ConfigureAwait(false);
                    continue;
                }
                if (!await TryRepublishAsync(
                    seq,
                    keepAliveSeqNum,
                    availableSequenceNumbers,
                    ct)
                    .ConfigureAwait(false))
                {
                    break;
                }
                LastDataSequenceNumberProcessed = seq;
            }

            // The walk stopped in front of messages the server can still
            // republish. Without buffered data nothing else would expire
            // this gap, so track it until the gap timeout skips the hole.
            if (IsNewerSequenceNumber(ordered[ordered.Length - 1], LastDataSequenceNumberProcessed) &&
                (m_keepAliveGapEnd == 0 ||
                IsNewerSequenceNumber(keepAliveSeqNum, m_keepAliveGapEnd)))
            {
                m_keepAliveGapEnd = keepAliveSeqNum;
            }

            await DrainPendingMessagesAsync(ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Retires the current server-side generation while message delivery is
        /// quiesced, resets generation-specific cursors, and initializes the
        /// replacement before queued messages can resume.
        /// </summary>
        /// <param name="retireAsync">Retires the current generation.</param>
        /// <param name="initializeAsync">Initializes the replacement generation.</param>
        /// <param name="ct">Cancellation token.</param>
        protected async ValueTask ResetMessageGenerationAsync(
            Func<CancellationToken, ValueTask> retireAsync,
            Func<CancellationToken, ValueTask> initializeAsync,
            CancellationToken ct)
        {
            if (!m_messageDispatchGate.Wait(0, CancellationToken.None))
            {
                // A notification callback is running. Callers hold the
                // owner's state lock here, so give the owner a chance to
                // release anything that callback may be awaiting which in
                // turn needs that lock - otherwise neither side progresses.
                OnDispatchGateContended();
                try
                {
                    await m_messageDispatchGate.WaitAsync(ct).ConfigureAwait(false);
                }
                catch
                {
                    // The reset gave up before it got the gate. Withdraw the
                    // release request so later callbacks are not treated as
                    // blocking a reset that no longer waits.
                    OnDispatchGateWaitAbandoned();
                    throw;
                }
            }
            try
            {
                OnDispatchGateAcquired();
                await retireAsync(ct).ConfigureAwait(false);
                Interlocked.Increment(ref m_generation);
                LastSequenceNumberProcessed = 0;
                LastDataSequenceNumberProcessed = 0;
                LastNotificationTimestamp = 0;
                AvailableInRetransmissionQueue = [];
                m_pendingMessages.Clear();
                m_gapStartedTimestamp = 0;
                m_keepAliveGapEnd = 0;
                m_missingAccountedThrough = 0;
                Array.Clear(m_skippedSequenceNumbers, 0, m_skippedSequenceNumbers.Length);
                m_skippedSequenceNumberIndex = 0;
                m_notDispatched.Clear();
                await initializeAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                m_messageDispatchGate.Release();
            }
        }

        /// <summary>
        /// Called by <see cref="ResetMessageGenerationAsync"/> when it has to
        /// wait for a notification callback to return before it can retire
        /// the current generation.
        /// </summary>
        protected virtual void OnDispatchGateContended()
        {
        }

        /// <summary>
        /// Called by <see cref="ResetMessageGenerationAsync"/> once it holds
        /// the dispatch gate, i.e. while no notification callback runs.
        /// </summary>
        protected virtual void OnDispatchGateAcquired()
        {
        }

        /// <summary>
        /// Called by <see cref="ResetMessageGenerationAsync"/> when its wait
        /// for the dispatch gate, announced through
        /// <see cref="OnDispatchGateContended"/>, ends without acquiring the
        /// gate (for example because it was cancelled).
        /// </summary>
        protected virtual void OnDispatchGateWaitAbandoned()
        {
        }

        /// <summary>
        /// Recover the notification messages the server still holds in its
        /// retransmission queue after a subscription was transferred to this
        /// session.
        /// </summary>
        /// <remarks>
        /// The gap-walking republish in <see cref="ProcessMessageCoreAsync"/>
        /// only runs when a data message arrives, so a subscription that stays
        /// quiet after the transfer - or that only emits keep-alives - would
        /// never recover the messages the previous session left behind. Those
        /// sequence numbers are known to be available the moment
        /// <c>TransferSubscriptions</c> returns, so republish them right away.
        /// The messages are recovered in ascending sequence order (wrap-aware
        /// per Part 4 §7.30.5) and the dedup gate is advanced past them so the
        /// first publish that follows is not mistaken for a gap.
        /// </remarks>
        /// <param name="availableSequenceNumbers">The sequence numbers the
        /// server reported as available in its retransmission queue.</param>
        /// <param name="ct">Cancellation token.</param>
        protected async ValueTask RecoverTransferredMessagesAsync(
            IReadOnlyList<uint> availableSequenceNumbers,
            CancellationToken ct)
        {
            if (availableSequenceNumbers.Count == 0)
            {
                return;
            }
            await m_messageDispatchGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                // Buffered originals older than the newest advertised
                // message are delivered in sequence with it; otherwise the
                // gate would pass them and the drain would drop them, even
                // when the server no longer holds a copy.
                RemoveStalePendingMessages();
                uint[] advertised = SortAscendingWrapAware(availableSequenceNumbers);
                uint newestAdvertised = advertised[advertised.Length - 1];
                var sequenceNumbers = new HashSet<uint>(advertised);
                foreach (uint sequenceNumber in m_pendingMessages.Keys)
                {
                    if (!IsNewerSequenceNumber(sequenceNumber, newestAdvertised))
                    {
                        sequenceNumbers.Add(sequenceNumber);
                    }
                }
                uint[] ordered = sequenceNumbers.Count == advertised.Length
                    ? advertised
                    : SortAscendingWrapAware(sequenceNumbers.ToArray());
                Logger.SubscriptionRecoveringTransferredMessages(Id, ordered.Length);

                // Advance the dedup gate after each completed attempt so
                // cancellation preserves progress already made and the
                // next message is not treated as the first after create.
                foreach (uint sequenceNumber in ordered)
                {
                    if (LastDataSequenceNumberProcessed != 0 &&
                        !IsNewerSequenceNumber(
                            sequenceNumber,
                            LastDataSequenceNumberProcessed) &&
                        !m_notDispatched.Contains(sequenceNumber))
                    {
                        // The subscription survived the transfer with its
                        // dedup gate intact (reconnect onto the same
                        // object), and this message was already dispatched
                        // - it is only still listed because its
                        // acknowledgement had not reached the server yet
                        // (Part 4 §5.14.7.2). Do not republish or
                        // re-dispatch it; just acknowledge it so the server
                        // can drop it from its retransmission queue.
                        // Messages whose republish failed earlier were
                        // never dispatched and are recovered below.
                        await AckQueue.QueueAsync(this, MessageGeneration, new SubscriptionAcknowledgement
                        {
                            SequenceNumber = sequenceNumber,
                            SubscriptionId = Id
                        }, ct).ConfigureAwait(false);
                        continue;
                    }
                    if (m_pendingMessages.Remove(sequenceNumber, out IncomingMessage pending))
                    {
                        // The original already arrived and was buffered
                        // behind a gap. Deliver that copy instead of
                        // republishing it, so the gap timeout does not
                        // deliver it a second time.
                        await DispatchDataMessageAsync(pending, ct).ConfigureAwait(false);
                    }
                    else
                    {
                        await RepublishKnownAvailableAsync(
                            sequenceNumber,
                            sequenceNumber,
                            ct)
                            .ConfigureAwait(false);
                    }
                    if (IsNewerSequenceNumber(
                        sequenceNumber,
                        LastDataSequenceNumberProcessed))
                    {
                        LastDataSequenceNumberProcessed = sequenceNumber;
                    }
                    if (IsNewerSequenceNumber(
                        sequenceNumber,
                        LastSequenceNumberProcessed))
                    {
                        LastSequenceNumberProcessed = sequenceNumber;
                    }
                    ct.ThrowIfCancellationRequested();
                }
                await DrainPendingMessagesAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                m_messageDispatchGate.Release();
            }
        }

        /// <summary>
        /// Order sequence numbers from oldest to newest, tolerating the
        /// wraparound from <see cref="uint.MaxValue"/> to 1.
        /// </summary>
        /// <param name="sequenceNumbers">The unordered set of sequence numbers
        /// the server reported as available in its retransmission queue.</param>
        private static uint[] SortAscendingWrapAware(IReadOnlyList<uint> sequenceNumbers)
        {
            if (sequenceNumbers.Count == 0)
            {
                return [];
            }
            const uint kBackwardThreshold = 1u << 31;

            // A retransmission queue always spans far less than half of the
            // sequence-number space. Keep the oldest candidate, replacing it
            // whenever the candidate is forward from the next entry.
            uint anchor = sequenceNumbers[0];
            for (int i = 1; i < sequenceNumbers.Count; i++)
            {
                if (unchecked(anchor - sequenceNumbers[i]) < kBackwardThreshold)
                {
                    anchor = sequenceNumbers[i];
                }
            }
            uint[] ordered = [.. sequenceNumbers];
            uint[] keys = new uint[ordered.Length];
            for (int i = 0; i < ordered.Length; i++)
            {
                keys[i] = unchecked(ordered[i] - anchor);
            }
            Array.Sort(keys, ordered);
            return ordered;
        }

        private static bool IsNewerSequenceNumber(uint sequenceNumber, uint currentSequenceNumber)
        {
            const uint kBackwardThreshold = 1u << 31;

            return currentSequenceNumber == 0 ||
                unchecked(sequenceNumber - currentSequenceNumber) is not 0 and < kBackwardThreshold;
        }

        /// <summary>
        /// Try republish a missing message
        /// </summary>
        /// <param name="missing"></param>
        /// <param name="curSeqNum"></param>
        /// <param name="availableSequenceNumbers"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        /// <returns><c>false</c> if the message is listed as available but
        /// could not be recovered; <c>true</c> if it was dispatched or the
        /// server no longer holds it.</returns>
        private async ValueTask<bool> TryRepublishAsync(
            uint missing,
            uint curSeqNum,
            IReadOnlyList<uint> availableSequenceNumbers,
            CancellationToken ct)
        {
            Interlocked.Increment(ref m_republishCount);
            if (!availableSequenceNumbers.Contains(missing))
            {
                Logger.SubscriptionMessageSequenceNumberSeqNumberNot(
                    Id,
                    missing);
                return false;
            }
            return await RepublishAvailableAsync(missing, curSeqNum, ct)
                .ConfigureAwait(false);
        }

        private async ValueTask RepublishKnownAvailableAsync(
            uint missing,
            uint curSeqNum,
            CancellationToken ct)
        {
            Interlocked.Increment(ref m_republishCount);
            await RepublishAvailableAsync(missing, curSeqNum, ct)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Republish a message the server lists as available and dispatch
        /// it. A message that cannot be recovered is remembered as never
        /// dispatched, so a later transfer recovery republishes it instead
        /// of acknowledging it as already processed.
        /// </summary>
        /// <returns><c>true</c> if the message was dispatched.</returns>
        private async ValueTask<bool> RepublishAvailableAsync(
            uint missing,
            uint curSeqNum,
            CancellationToken ct)
        {
            try
            {
                Logger.SubscriptionRepublishingMissingMessageSequenceNumber(
                    Id,
                    missing,
                    curSeqNum);
                RepublishResponse republish = await m_services.RepublishAsync(
                    null,
                    Id,
                    missing,
                    ct).ConfigureAwait(false);

                if (ServiceResult.IsGood(republish.ResponseHeader.ServiceResult))
                {
                    m_notDispatched.Remove(missing);
                    m_pendingMessages.Remove(missing);

                    // Delivery opens the acknowledgement lifetime. A late
                    // original must not open a second one.
                    _ = TryConsumeSkippedSequenceNumber(missing);
                    await OnNotificationReceivedAsync(
                        republish.NotificationMessage,
                        PublishState.Republish,
                        republish.ResponseHeader.StringTable.ToArray() ?? [],
                        ct).ConfigureAwait(false);
                    return true;
                }
                Logger.SubscriptionRepublishingMessageSequenceNumberSeqNumber(
                    Id,
                    missing);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                RememberNotDispatched(missing);
                throw;
            }
            catch (Exception ex)
            {
                Logger.SubscriptionErrorRepublishingMessageSequenceNumber(
                    ex,
                    Id,
                    missing);
            }
            RememberNotDispatched(missing);
            return false;
        }

        /// <summary>
        /// Remember a sequence number whose republish failed. Called with the
        /// dispatch gate held. The set is bounded: numbers the server no
        /// longer lists cannot be recovered any more and are dropped first.
        /// </summary>
        private void RememberNotDispatched(uint sequenceNumber)
        {
            if (m_notDispatched.Count >= kMaxNotDispatched)
            {
                IReadOnlyList<uint> available = AvailableInRetransmissionQueue;
                m_notDispatched.RemoveWhere(s => !available.Contains(s));
                if (m_notDispatched.Count >= kMaxNotDispatched)
                {
                    return;
                }
            }
            m_notDispatched.Add(sequenceNumber);
        }

        /// <summary>
        /// Dispatch notification message
        /// </summary>
        /// <param name="message"></param>
        /// <param name="publishStateMask"></param>
        /// <param name="stringTable">The string table of the publish or
        /// republish response the notification arrived in.</param>
        /// <param name="ct"></param>
        /// <returns></returns>
        private async ValueTask OnNotificationReceivedAsync(
            NotificationMessage message,
            PublishState publishStateMask,
            IReadOnlyList<string> stringTable,
            CancellationToken ct)
        {
            bool shouldAcknowledge = message.NotificationData.Count != 0;
            try
            {
                if (!shouldAcknowledge)
                {
                    publishStateMask |= PublishState.KeepAlive;
                    await DispatchCallbackAsync(
                        () => OnKeepAliveNotificationAsync(
                            message.SequenceNumber,
                            (DateTime)message.PublishTime,
                            publishStateMask)).ConfigureAwait(false);
                }
                else
                {
                    AckQueue.TrackAcknowledgement(this, MessageGeneration, message.SequenceNumber);
                    if (HandlesNotificationMessages)
                    {
                        await DispatchCallbackAsync(() => OnNotificationMessageAsync(
                            message, publishStateMask, stringTable, ct)).ConfigureAwait(false);
                    }
                    else
                    {
                        for (int i = 0; i < message.NotificationData.Count; i++)
                        {
                            await DispatchAsync(
                                message,
                                publishStateMask,
                                message.NotificationData[i],
                                stringTable).ConfigureAwait(false);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.SubscriptionErrorDispatchingNotificationData(
                    ex,
                    Id);
            }

            // Acknowledge even when a handler threw. The acknowledgement only
            // tells the server it may drop the message from its retransmission
            // queue (Part 4 §5.14.5.2); it is not a claim that the client
            // processed it. Withholding it does not redeliver anything - the
            // dedup gate has already advanced, so the message would only be
            // reachable via an explicit Republish and discarded as a duplicate
            // - it just pins the entry until the queue overflows.
            if (shouldAcknowledge)
            {
                try
                {
                    await AckQueue.QueueAsync(this, MessageGeneration, new SubscriptionAcknowledgement
                    {
                        SequenceNumber = message.SequenceNumber,
                        SubscriptionId = Id
                    }, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Logger.SubscriptionErrorDispatchingNotificationData(
                        ex,
                        Id);
                }
            }
        }

        /// <summary>
        /// Dispatch notification
        /// </summary>
        /// <param name="message"></param>
        /// <param name="publishStateMask"></param>
        /// <param name="notificationData"></param>
        /// <param name="stringTable">The string table of the publish or
        /// republish response the notification arrived in.</param>
        /// <returns></returns>
        private async ValueTask DispatchAsync(NotificationMessage message,
            PublishState publishStateMask, ExtensionObject? notificationData,
            IReadOnlyList<string> stringTable)
        {
            if (notificationData == null)
            {
                return;
            }
            if (notificationData.Value.TryGetValue(
                out DataChangeNotification? datachange))
            {
                await DispatchCallbackAsync(
                    () => OnDataChangeNotificationAsync(
                        message.SequenceNumber,
                        (DateTime)message.PublishTime,
                        datachange,
                        publishStateMask,
                        stringTable)).ConfigureAwait(false);
            }
            else if (notificationData.Value.TryGetValue(
                out EventNotificationList? events))
            {
                await DispatchCallbackAsync(
                    () => OnEventDataNotificationAsync(
                        message.SequenceNumber,
                        (DateTime)message.PublishTime,
                        events,
                        publishStateMask,
                        stringTable)).ConfigureAwait(false);
            }
            else if (notificationData.Value.TryGetValue(
                out StatusChangeNotification? statusChanged))
            {
                PublishState mask = publishStateMask;
                if (statusChanged.Status == StatusCodes.GoodSubscriptionTransferred)
                {
                    // Only happens if we did not initiate the transfer.
                    // TODO: Complete this subscription.
                    mask |= PublishState.Transferred;
                }
                else if (statusChanged.Status == StatusCodes.BadTimeout)
                {
                    // Timeout on the server.
                    // TODO: Also complete this subscription
                    mask |= PublishState.Timeout;
                }
                await DispatchCallbackAsync(
                    () => OnStatusChangeNotificationAsync(
                        message.SequenceNumber,
                        (DateTime)message.PublishTime,
                        statusChanged,
                        mask,
                        stringTable)).ConfigureAwait(false);
            }
        }

        private async ValueTask DispatchCallbackAsync(Func<ValueTask> callback)
        {
            DispatchScope? previous = s_dispatchScope.Value;
            var scope = new DispatchScope(this);
            s_dispatchScope.Value = scope;
            try
            {
                await callback().ConfigureAwait(false);
            }
            finally
            {
                Volatile.Write(ref scope.Active, 0);
                s_dispatchScope.Value = previous;
            }
        }

        private sealed class DispatchScope(MessageProcessor processor)
        {
            public MessageProcessor Processor { get; } = processor;

            public int Active = 1;
        }

        /// <summary>
        /// A message received from the server cached until is processed or discarded.
        /// </summary>
        /// <param name="Message"></param>
        /// <param name="StringTable"></param>
        /// <param name="AvailableSequenceNumbers"></param>
        /// <param name="Enqueued"></param>
        /// <param name="Generation"></param>
        private readonly record struct IncomingMessage(
            NotificationMessage Message,
            IReadOnlyList<string> StringTable,
            IReadOnlyList<uint> AvailableSequenceNumbers,
            DateTimeOffset Enqueued,
            long Generation)
        {
            public static int Compare(IncomingMessage message, IncomingMessage other)
            {
                // Greater than zero � message follows the
                // other message in sort order.
                return
                    (int)message.Message.SequenceNumber -
                    (int)other.Message.SequenceNumber;
            }
        }

        internal bool Disposed;
        internal long LastNotificationTimestamp;
        internal uint LastSequenceNumberProcessed;
        internal uint LastDataSequenceNumberProcessed;
        private uint m_id;
        private uint m_lastServerId;
        internal long m_missingCount;
        internal long m_republishCount;
        internal IReadOnlyList<uint> AvailableInRetransmissionQueue;
        internal readonly ILogger Logger;
        protected TimeProvider TimeProvider { get; }

        /// <summary>
        /// Exposes the ack queue / completion sink supplied to the
        /// processor so derived classes can interact with it without
        /// reaching for a private field — used today by the recovery
        /// path to drop stale acknowledgements before recreating a
        /// subscription that the server has invalidated under us.
        /// </summary>
        protected IMessageAckQueue AckQueue { get; }

        /// <summary>
        /// Whether the current asynchronous flow is dispatching one of this
        /// processor's notification callbacks.
        /// </summary>
        protected bool IsDispatchingNotification
        {
            get
            {
                DispatchScope? scope = s_dispatchScope.Value;
                return scope != null &&
                    ReferenceEquals(scope.Processor, this) &&
                    Volatile.Read(ref scope.Active) != 0;
            }
        }

        private static readonly AsyncLocal<DispatchScope?> s_dispatchScope = new();
        private readonly ISubscriptionServiceSetClientMethods m_services;
        // CA2213: fields are disposed in DisposeAsync(bool) — suppressed
        // because the analyzer does not track IAsyncDisposable disposal paths.
#pragma warning disable CA2213
        private readonly SemaphoreSlim m_messageDispatchGate = new(1, 1);
        private readonly CancellationTokenSource m_cts = new();
        private readonly SemaphoreSlim m_messageCapacity;
#pragma warning restore CA2213
        private long m_generation;
        private AcknowledgementScope? m_acknowledgementScope;
        private readonly Task m_messageWorkerTask;
        private readonly Channel<IncomingMessage> m_messages;
        private const int kIncomingMessageCapacity = 1024;
        private const int kMaxPendingMessages = kIncomingMessageCapacity;
        private const int kMaxNotDispatched = 1024;
        private static readonly TimeSpan kGapRecoveryTimeout = TimeSpan.FromMilliseconds(2500);

        /// <summary>
        /// Sequence numbers of the current generation whose republish failed,
        /// so they were never dispatched although the data gate may have
        /// moved past them. Only accessed with the dispatch gate held.
        /// </summary>
        private readonly HashSet<uint> m_notDispatched = [];
        private readonly Dictionary<uint, IncomingMessage> m_pendingMessages = [];
        private long m_gapStartedTimestamp;

        /// <summary>
        /// The sequence number of a keep-alive that proved messages missing
        /// which the server can still republish; zero if there is none. The
        /// gap timeout skips the hole up to this message.
        /// </summary>
        private uint m_keepAliveGapEnd;
        /// <summary>
        /// The newest sequence number already counted as the end of a gap,
        /// so the same missing messages are not counted twice.
        /// </summary>
        private uint m_missingAccountedThrough;

        /// <summary>
        /// Ring of sequence numbers the gap timeout skipped without
        /// delivering them; zero marks an empty slot. Only accessed with the
        /// dispatch gate held.
        /// </summary>
        private readonly uint[] m_skippedSequenceNumbers = new uint[kMaxSkippedSequenceNumbers];
        private int m_skippedSequenceNumberIndex;
        private const int kMaxSkippedSequenceNumbers = 1024;
    }

    /// <summary>
    /// Source-generated log messages for <see cref="MessageProcessor"/>.
    /// </summary>
    internal static partial class MessageProcessorLog
    {
        [LoggerMessage(EventId = ClientEventIds.MessageProcessor + 0, Level = LogLevel.Information,
            Message = "{SubscriptionId} STOPPED!")]
        public static partial void SubscriptionSTOPPED(this ILogger logger, uint subscriptionId);

        [LoggerMessage(EventId = ClientEventIds.MessageProcessor + 1, Level = LogLevel.Information,
            Message = "{SubscriptionId} RECOVERED!")]
        public static partial void SubscriptionRECOVERED(this ILogger logger, uint subscriptionId);

        [LoggerMessage(EventId = ClientEventIds.MessageProcessor + 2, Level = LogLevel.Information,
            Message = "{SubscriptionId} CLOSED!")]
        public static partial void SubscriptionCLOSED(this ILogger logger, uint subscriptionId);

        [LoggerMessage(EventId = ClientEventIds.MessageProcessor + 3, Level = LogLevel.Critical,
            Message = "{SubscriptionId}: Error processing messages. Processor is exiting!!!")]
        public static partial void SubscriptionErrorProcessingMessagesProcessorExiting(
            this ILogger logger,
            Exception? exception,
            uint subscriptionId);

        [LoggerMessage(EventId = ClientEventIds.MessageProcessor + 4, Level = LogLevel.Debug,
            Message = "{SubscriptionId}: Received duplicate message with sequence number #{SeqNumber}.")]
        public static partial void SubscriptionReceivedDuplicateMessageSequenceNumber(
            this ILogger logger,
            uint subscriptionId,
            uint seqNumber);

        [LoggerMessage(EventId = ClientEventIds.MessageProcessor + 5, Level = LogLevel.Debug,
            Message = "{SubscriptionId}: Received older message with sequence number #{SeqNumber} but already" +
                " processed message with sequence number #{Old}.")]
        public static partial void SubscriptionReceivedOlderMessageSequenceNumber(
            this ILogger logger,
            uint subscriptionId,
            uint seqNumber,
            uint old);

        [LoggerMessage(EventId = ClientEventIds.MessageProcessor + 6, Level = LogLevel.Warning,
            Message = "{SubscriptionId}: Message with sequence number #{SeqNumber} is not in server retransmission" +
                " queue and was dropped.")]
        public static partial void SubscriptionMessageSequenceNumberSeqNumberNot(
            this ILogger logger,
            uint subscriptionId,
            uint seqNumber);

        [LoggerMessage(EventId = ClientEventIds.MessageProcessor + 7, Level = LogLevel.Information,
            Message = "{SubscriptionId}: Republishing missing message with sequence number #{Missing} " +
                "to catch up to message with sequence number #{SeqNumber}...")]
        public static partial void SubscriptionRepublishingMissingMessageSequenceNumber(
            this ILogger logger,
            uint subscriptionId,
            uint missing,
            uint seqNumber);

        [LoggerMessage(EventId = ClientEventIds.MessageProcessor + 8, Level = LogLevel.Warning,
            Message = "{SubscriptionId}: Republishing message with sequence number #{SeqNumber} failed.")]
        public static partial void SubscriptionRepublishingMessageSequenceNumberSeqNumber(
            this ILogger logger,
            uint subscriptionId,
            uint seqNumber);

        [LoggerMessage(EventId = ClientEventIds.MessageProcessor + 9, Level = LogLevel.Error,
            Message = "{SubscriptionId}: Error republishing message with sequence number #{SeqNumber}.")]
        public static partial void SubscriptionErrorRepublishingMessageSequenceNumber(
            this ILogger logger,
            Exception? exception,
            uint subscriptionId,
            uint seqNumber);

        [LoggerMessage(EventId = ClientEventIds.MessageProcessor + 10, Level = LogLevel.Error,
            Message = "{SubscriptionId}: Error dispatching notification data.")]
        public static partial void SubscriptionErrorDispatchingNotificationData(
            this ILogger logger,
            Exception? exception,
            uint subscriptionId);

        [LoggerMessage(EventId = ClientEventIds.MessageProcessor + 11, Level = LogLevel.Information,
            Message = "{SubscriptionId}: Recovering {Count} transferred message(s) from the server " +
                "retransmission queue.")]
        public static partial void SubscriptionRecoveringTransferredMessages(
            this ILogger logger,
            uint subscriptionId,
            int count);

        [LoggerMessage(EventId = ClientEventIds.MessageProcessor + 12, Level = LogLevel.Warning,
            Message = "{SubscriptionId}: Gap recovery timed out at sequence number #{Missing}; " +
                "resuming with sequence number #{Next}.")]
        public static partial void SubscriptionGapRecoveryTimedOut(
            this ILogger logger,
            uint subscriptionId,
            uint missing,
            uint next);
    }
}
