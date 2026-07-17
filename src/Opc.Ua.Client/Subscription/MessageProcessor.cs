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
        public uint Id { get; internal set; }

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
            if (availableSequenceNumbers != null)
            {
                AvailableInRetransmissionQueue = availableSequenceNumbers;
            }
            LastNotificationTimestamp = TimeProvider.GetTimestamp();
            await m_messages.Writer.WriteAsync(new IncomingMessage(message, stringTable,
                TimeProvider.GetUtcNow(), Volatile.Read(ref m_generation)))
                .ConfigureAwait(false);
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
                    (m_messages as IDisposable)?.Dispose();
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
        /// On publish state changed
        /// </summary>
        /// <param name="stateMask"></param>
        /// <returns></returns>
        protected virtual void OnPublishStateChanged(PublishState stateMask)
        {
            if (stateMask.HasFlag(PublishState.Stopped))
            {
                Logger.SubscriptionSTOPPED(Id);
            }
            if (stateMask.HasFlag(PublishState.Recovered))
            {
                Logger.SubscriptionRECOVERED(Id);
            }
            if (stateMask.HasFlag(PublishState.Completed))
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
                IAsyncEnumerable<IncomingMessage> reader = m_messages.Reader.ReadAllAsync(ct);
                await foreach (IncomingMessage incoming in reader.ConfigureAwait(false))
                {
                    await ProcessMessageAsync(incoming, ct).ConfigureAwait(false);
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
            await AckQueue.CompleteAsync(Id, default).ConfigureAwait(false);
            OnPublishStateChanged(PublishState.Completed);
        }

        /// <summary>
        /// Notify the owning manager/queue that the subscription's
        /// state has changed (e.g. it has just been created on the
        /// server). The manager uses this signal to re-evaluate the
        /// publish worker pool.
        /// </summary>
        protected void NotifyManagerOfCreation()
        {
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
                if (incoming.Generation != Volatile.Read(ref m_generation))
                {
                    return;
                }
                bool wasDispatching = m_dispatchContext.Value;
                m_dispatchContext.Value = true;
                try
                {
                    await ProcessMessageCoreAsync(incoming, ct).ConfigureAwait(false);
                }
                finally
                {
                    m_dispatchContext.Value = wasDispatching;
                }
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
                LastSequenceNumberProcessed = curSeqNum;
                await OnNotificationReceivedAsync(
                    incoming.Message,
                    PublishState.KeepAlive,
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
                    return;
                }

                // Walk the gap (prevDataSeq, curSeqNum) wrapping past
                // uint.MaxValue to 1.
                uint missing = prevDataSeq;
                while (true)
                {
                    missing = missing == uint.MaxValue ? 1u : missing + 1u;
                    if (missing == curSeqNum)
                    {
                        break;
                    }
                    Interlocked.Increment(ref m_missingCount);
                    await TryRepublishAsync(missing, curSeqNum, ct).ConfigureAwait(false);
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
                IReadOnlyList<uint> available = AvailableInRetransmissionQueue;
                for (int i = 0; i < available.Count; i++)
                {
                    uint seq = available[i];
                    uint delta = unchecked(curSeqNum - seq);
                    if (delta is not 0 and < kBackwardThreshold)
                    {
                        await TryRepublishAsync(seq, curSeqNum, ct).ConfigureAwait(false);
                    }
                }
            }
            LastDataSequenceNumberProcessed = curSeqNum;
            LastSequenceNumberProcessed = curSeqNum;
            await OnNotificationReceivedAsync(
                incoming.Message,
                PublishState.None,
                ct).ConfigureAwait(false);
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
            await m_messageDispatchGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await retireAsync(ct).ConfigureAwait(false);
                Interlocked.Increment(ref m_generation);
                LastSequenceNumberProcessed = 0;
                LastDataSequenceNumberProcessed = 0;
                LastNotificationTimestamp = 0;
                AvailableInRetransmissionQueue = [];
                await initializeAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                m_messageDispatchGate.Release();
            }
        }

        /// <summary>
        /// Try republish a missing message
        /// </summary>
        /// <param name="missing"></param>
        /// <param name="curSeqNum"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        private async ValueTask TryRepublishAsync(uint missing, uint curSeqNum,
            CancellationToken ct)
        {
            Interlocked.Increment(ref m_republishCount);
            if (!AvailableInRetransmissionQueue.Contains(missing))
            {
                Logger.SubscriptionMessageSequenceNumberSeqNumberNot(
                    Id,
                    missing);
                return;
            }
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
                    await OnNotificationReceivedAsync(
                        republish.NotificationMessage,
                        PublishState.Republish,
                        ct).ConfigureAwait(false);
                }
                else
                {
                    Logger.SubscriptionRepublishingMessageSequenceNumberSeqNumber(
                        Id,
                        missing);
                }
            }
            catch (Exception ex)
            {
                Logger.SubscriptionErrorRepublishingMessageSequenceNumber(
                    ex,
                    Id,
                    missing);
            }
        }

        /// <summary>
        /// Dispatch notification message
        /// </summary>
        /// <param name="message"></param>
        /// <param name="publishStateMask"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        private async ValueTask OnNotificationReceivedAsync(
            NotificationMessage message,
            PublishState publishStateMask,
            CancellationToken ct)
        {
            try
            {
                bool shouldAcknowledge = message.NotificationData.Count != 0;
                if (!shouldAcknowledge)
                {
                    publishStateMask |= PublishState.KeepAlive;
                    await OnKeepAliveNotificationAsync(
                        message.SequenceNumber,
                        (DateTime)message.PublishTime,
                        publishStateMask).ConfigureAwait(false);
                }
                else
                {
                    for (int i = 0; i < message.NotificationData.Count; i++)
                    {
                        await DispatchAsync(
                            message,
                            publishStateMask,
                            message.NotificationData[i]).ConfigureAwait(false);
                    }
                }
                if (shouldAcknowledge)
                {
                    await AckQueue.QueueAsync(new SubscriptionAcknowledgement
                    {
                        SequenceNumber = message.SequenceNumber,
                        SubscriptionId = Id
                    }, ct).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Logger.SubscriptionErrorDispatchingNotificationData(
                    ex,
                    Id);
            }
        }

        /// <summary>
        /// Dispatch notification
        /// </summary>
        /// <param name="message"></param>
        /// <param name="publishStateMask"></param>
        /// <param name="notificationData"></param>
        /// <returns></returns>
        private async ValueTask DispatchAsync(NotificationMessage message,
            PublishState publishStateMask, ExtensionObject? notificationData)
        {
            if (notificationData == null)
            {
                return;
            }
            if (notificationData.Value.TryGetValue(
                out DataChangeNotification? datachange))
            {
                await OnDataChangeNotificationAsync(
                    message.SequenceNumber,
                    (DateTime)message.PublishTime,
                    datachange,
                    publishStateMask,
                    message.StringTable.ToArray() ?? []).ConfigureAwait(false);
            }
            else if (notificationData.Value.TryGetValue(
                out EventNotificationList? events))
            {
                await OnEventDataNotificationAsync(
                    message.SequenceNumber,
                    (DateTime)message.PublishTime,
                    events,
                    publishStateMask,
                    message.StringTable.ToArray() ?? []).ConfigureAwait(false);
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
                await OnStatusChangeNotificationAsync(
                    message.SequenceNumber,
                    (DateTime)message.PublishTime,
                    statusChanged,
                    mask,
                    message.StringTable.ToArray() ?? []).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// A message received from the server cached until is processed or discarded.
        /// </summary>
        /// <param name="Message"></param>
        /// <param name="StringTable"></param>
        /// <param name="Enqueued"></param>
        /// <param name="Generation"></param>
        private readonly record struct IncomingMessage(
            NotificationMessage Message,
            IReadOnlyList<string> StringTable,
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
        protected bool IsDispatchingNotification => m_dispatchContext.Value;

        private readonly ISubscriptionServiceSetClientMethods m_services;
        // CA2213: both fields are disposed in DisposeAsync(bool) — suppressed
        // because the analyzer does not track IAsyncDisposable disposal paths.
#pragma warning disable CA2213
        private readonly SemaphoreSlim m_messageDispatchGate = new(1, 1);
        private readonly CancellationTokenSource m_cts = new();
#pragma warning restore CA2213
        private readonly AsyncLocal<bool> m_dispatchContext = new();
        private long m_generation;
        private readonly Task m_messageWorkerTask;
        private readonly Channel<IncomingMessage> m_messages;
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
    }

}
