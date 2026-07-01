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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Owns a subscription's sent-notification ring together with its sequence-number counter and the optional
    /// <see cref="ISubscriptionRetransmissionStore"/> that mirrors them for cross-replica republish. Keeping this
    /// bookkeeping here lets <see cref="Subscription"/> keep Publish/Acknowledge/Republish focused on orchestration
    /// while delegating the mechanical queue maintenance (sequence assignment, overflow trimming, payload recycling,
    /// retransmission mirroring) to a small, intent-revealing surface.
    /// </summary>
    /// <remarks>
    /// This type is not internally synchronized. The owning <see cref="Subscription"/> serializes every access under
    /// its subscription lock (or, for the restore path, before the subscription is published), exactly as the inlined
    /// state did before the extraction.
    /// </remarks>
    internal sealed class SentMessageQueue
    {
        /// <summary>
        /// Creates an empty queue for a new subscription (next sequence number 1, no sent messages).
        /// </summary>
        /// <param name="subscriptionIdProvider">Returns the owning subscription's id (read lazily so it is current).</param>
        /// <param name="maxMessageCount">The maximum number of sent messages retained for republish.</param>
        /// <param name="retransmissionStore">
        /// Optional store that mirrors retransmission state across a <c>RedundantServerSet</c>; <c>null</c> when the
        /// server is not distributed.
        /// </param>
        /// <param name="logger">The logger used to report queue overflow.</param>
        public SentMessageQueue(
            Func<uint> subscriptionIdProvider,
            uint maxMessageCount,
            ISubscriptionRetransmissionStore? retransmissionStore,
            ILogger logger)
            : this(subscriptionIdProvider, maxMessageCount, retransmissionStore, logger, [], 1, 0)
        {
        }

        private SentMessageQueue(
            Func<uint> subscriptionIdProvider,
            uint maxMessageCount,
            ISubscriptionRetransmissionStore? retransmissionStore,
            ILogger logger,
            List<NotificationMessage> sentMessages,
            uint nextSequenceNumber,
            int lastSentMessage)
        {
            m_subscriptionIdProvider = subscriptionIdProvider
                ?? throw new ArgumentNullException(nameof(subscriptionIdProvider));
            m_maxMessageCount = maxMessageCount;
            m_retransmissionStore = retransmissionStore;
            m_logger = logger ?? throw new ArgumentNullException(nameof(logger));
            m_sentMessages = sentMessages;
            m_sequenceNumber = nextSequenceNumber;
            m_lastSentMessage = lastSentMessage;
        }

        /// <summary>
        /// Creates a queue restored from persisted subscription state.
        /// </summary>
        public static SentMessageQueue CreateRestored(
            Func<uint> subscriptionIdProvider,
            uint maxMessageCount,
            ISubscriptionRetransmissionStore? retransmissionStore,
            ILogger logger,
            List<NotificationMessage> sentMessages,
            uint nextSequenceNumber,
            int lastSentMessage)
        {
            return new SentMessageQueue(
                subscriptionIdProvider,
                maxMessageCount,
                retransmissionStore,
                logger,
                sentMessages,
                nextSequenceNumber,
                lastSentMessage);
        }

        /// <summary>
        /// Gets the next sequence number that will be assigned (without consuming it).
        /// </summary>
        public uint NextSequenceNumber => m_sequenceNumber;

        /// <summary>
        /// Gets the maximum number of sent messages retained for republish.
        /// </summary>
        public uint MaxMessageCount => m_maxMessageCount;

        /// <summary>
        /// Gets the index of the next queued message that has not yet been returned to a publish request.
        /// </summary>
        public int LastSentMessage => m_lastSentMessage;

        /// <summary>
        /// Gets the number of sent messages currently retained.
        /// </summary>
        public int SentCount => m_sentMessages.Count;

        /// <summary>
        /// Gets the underlying sent-message list for persistence (read while the subscription is quiesced).
        /// </summary>
        public List<NotificationMessage> SentMessages => m_sentMessages;

        /// <summary>
        /// Consumes and returns the next sequence number, advancing the counter.
        /// </summary>
        public uint AssignSequenceNumber()
        {
            uint sequenceNumber = m_sequenceNumber;
            Utils.IncrementIdentifier(ref m_sequenceNumber);
            return sequenceNumber;
        }

        /// <summary>
        /// Returns the next already-queued (but not yet published) message, or <c>null</c> when none is queued.
        /// </summary>
        /// <param name="availableSequenceNumbers">Receives the sequence numbers still available for republish.</param>
        /// <param name="hasItemsToPublish">Whether the subscription still has monitored-item notifications pending.</param>
        /// <param name="moreNotifications">Set to <c>true</c> when more messages remain to be published.</param>
        public NotificationMessage? TryDequeueQueued(
            List<uint> availableSequenceNumbers,
            bool hasItemsToPublish,
            out bool moreNotifications)
        {
            moreNotifications = false;

            if (m_lastSentMessage < m_sentMessages.Count)
            {
                // return the available sequence numbers.
                for (int ii = 0; ii <= m_lastSentMessage && ii < m_sentMessages.Count; ii++)
                {
                    availableSequenceNumbers.Add(m_sentMessages[ii].SequenceNumber);
                }

                moreNotifications = (m_lastSentMessage < m_sentMessages.Count - 1) || hasItemsToPublish;

                return m_sentMessages[m_lastSentMessage++];
            }

            return null;
        }

        /// <summary>
        /// Adds the sequence numbers still available for republish to the supplied list (used for keep-alive replies).
        /// </summary>
        public void FillAvailableSequenceNumbers(List<uint> availableSequenceNumbers)
        {
            for (int ii = 0; ii <= m_lastSentMessage && ii < m_sentMessages.Count; ii++)
            {
                availableSequenceNumbers.Add(m_sentMessages[ii].SequenceNumber);
            }
        }

        /// <summary>
        /// Appends freshly constructed messages to the queue, dropping/trimming as needed to respect the queue limit,
        /// mirrors the retransmission state, and returns the next message to publish.
        /// </summary>
        /// <param name="messages">The messages to enqueue (may be trimmed in place on overflow).</param>
        /// <param name="availableSequenceNumbers">Receives the sequence numbers still available for republish.</param>
        /// <param name="moreNotifications">Set to <c>true</c> when more messages remain to be published.</param>
        /// <param name="newlyUnacknowledgedCount">
        /// The number of messages that displaced older unacknowledged ones (for diagnostics); <c>0</c> when the queue
        /// was not full.
        /// </param>
        public NotificationMessage Enqueue(
            List<NotificationMessage> messages,
            List<uint> availableSequenceNumbers,
            out bool moreNotifications,
            out uint newlyUnacknowledgedCount)
        {
            newlyUnacknowledgedCount = 0;

            // have to drop unsent messages if out of queue space.
            int overflowCount = messages.Count - (int)m_maxMessageCount;
            if (overflowCount > 0)
            {
                m_logger.LogWarning(
                    "WARNING: QUEUE OVERFLOW. Dropping {Count} Messages. Increase MaxMessageQueueSize. SubId={SubscriptionId}, MaxMessageQueueSize={MaxMessageCount}",
                    overflowCount,
                    Id,
                    m_maxMessageCount);
                for (int ii = 0; ii < overflowCount; ii++)
                {
                    ReuseNotificationPayloads(messages[ii]);
                }
                messages.RemoveRange(0, overflowCount);
            }

            ArrayOf<uint> removedSequenceNumbers = m_retransmissionStore == null ? default : [];

            // remove old messages if queue is full.
            if (m_sentMessages.Count > m_maxMessageCount - messages.Count)
            {
                newlyUnacknowledgedCount = (uint)messages.Count;

                if (m_maxMessageCount <= messages.Count)
                {
                    if (m_retransmissionStore != null)
                    {
                        removedSequenceNumbers = GetSequenceNumbers(m_sentMessages, m_sentMessages.Count);
                    }
                    for (int ii = 0; ii < m_sentMessages.Count; ii++)
                    {
                        ReuseNotificationPayloads(m_sentMessages[ii]);
                    }
                    m_sentMessages.Clear();
                }
                else
                {
                    if (m_retransmissionStore != null)
                    {
                        removedSequenceNumbers = GetSequenceNumbers(m_sentMessages, messages.Count);
                    }
                    for (int ii = 0; ii < messages.Count; ii++)
                    {
                        ReuseNotificationPayloads(m_sentMessages[ii]);
                    }
                    m_sentMessages.RemoveRange(0, messages.Count);
                }
            }

            // save new message
            m_lastSentMessage = m_sentMessages.Count;
            m_sentMessages.AddRange(messages);
            StoreRetransmissionState(messages, removedSequenceNumbers);

            // check if there are more notifications to send.
            moreNotifications = messages.Count > 1;

            // return the available sequence numbers.
            for (int ii = 0; ii <= m_lastSentMessage && ii < m_sentMessages.Count; ii++)
            {
                availableSequenceNumbers.Add(m_sentMessages[ii].SequenceNumber);
            }

            return m_sentMessages[m_lastSentMessage++];
        }

        /// <summary>
        /// Removes an acknowledged message from the queue.
        /// </summary>
        /// <returns><c>true</c> if the message was found and acknowledged; otherwise <c>false</c>.</returns>
        public bool TryAcknowledge(uint sequenceNumber)
        {
            // find message in queue.
            for (int ii = 0; ii < m_sentMessages.Count; ii++)
            {
                if (m_sentMessages[ii].SequenceNumber == sequenceNumber)
                {
                    if (m_lastSentMessage > ii)
                    {
                        m_lastSentMessage--;
                    }

                    NotificationMessage removed = m_sentMessages[ii];
                    m_sentMessages.RemoveAt(ii);
                    ReuseNotificationPayloads(removed);
                    m_retransmissionStore?.AcknowledgeNotification(Id, sequenceNumber);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Finds a previously sent message for republish, or <c>null</c> when it is no longer available.
        /// </summary>
        public NotificationMessage? FindForRepublish(uint retransmitSequenceNumber)
        {
            foreach (NotificationMessage sentMessage in m_sentMessages)
            {
                if (sentMessage.SequenceNumber == retransmitSequenceNumber)
                {
                    return sentMessage;
                }
            }

            return null;
        }

        /// <summary>
        /// Returns the available sequence numbers for retransmission (for example used in Transfer Subscription).
        /// </summary>
        public ArrayOf<uint> AvailableSequenceNumbersForRetransmission()
        {
            var availableSequenceNumbers = new List<uint>();
            // Assumption we do not check lastSentMessage < sentMessages.Count because
            // in case of subscription transfer original client might have crashed by handling message,
            // therefor new client should have to chance to process all available messages
            for (int ii = 0; ii < m_sentMessages.Count; ii++)
            {
                availableSequenceNumbers.Add(m_sentMessages[ii].SequenceNumber);
            }
            return availableSequenceNumbers;
        }

        /// <summary>
        /// Restores the retransmission state from the mirror, when one is configured and holds state.
        /// </summary>
        public async ValueTask LoadRetransmissionStateAsync(CancellationToken cancellationToken)
        {
            if (m_retransmissionStore == null)
            {
                return;
            }

            SubscriptionRetransmissionState? state = await m_retransmissionStore
                .LoadRetransmissionStateAsync(Id, cancellationToken)
                .ConfigureAwait(false);
            if (state == null)
            {
                return;
            }

            m_sentMessages.Clear();
            m_sentMessages.AddRange(state.SentMessages);
            m_sequenceNumber = state.NextSequenceNumber;
            m_lastSentMessage = m_sentMessages.Count;
        }

        /// <summary>
        /// Recycles and clears all sent messages (called when the subscription is disposed).
        /// </summary>
        public void Clear()
        {
            for (int ii = 0; ii < m_sentMessages.Count; ii++)
            {
                ReuseNotificationPayloads(m_sentMessages[ii]);
            }
            m_sentMessages.Clear();
        }

        private void StoreRetransmissionState(
            IList<NotificationMessage> addedMessages,
            ArrayOf<uint> removedSequenceNumbers)
        {
            if (m_retransmissionStore == null)
            {
                return;
            }

            if (m_retransmissionStore is ISubscriptionRetransmissionDeltaStore deltaStore)
            {
                deltaStore.StoreRetransmissionStateDelta(
                    Id,
                    m_sequenceNumber,
                    new ArrayOf<NotificationMessage>(addedMessages.ToArray()),
                    removedSequenceNumbers);
                return;
            }

            m_retransmissionStore.StoreRetransmissionState(Id, m_sequenceNumber, [.. m_sentMessages]);
        }

        private static ArrayOf<uint> GetSequenceNumbers(List<NotificationMessage> messages, int count)
        {
            var sequenceNumbers = new uint[count];
            for (int ii = 0; ii < count; ii++)
            {
                sequenceNumbers[ii] = messages[ii].SequenceNumber;
            }

            return new ArrayOf<uint>(sequenceNumbers);
        }

        private static void ReuseNotificationPayloads(NotificationMessage message)
        {
            ReadOnlySpan<ExtensionObject> data = message.NotificationData.Span;
            for (int i = 0; i < data.Length; i++)
            {
                if (data[i].TryGetValue(out DataChangeNotification? dcn))
                {
                    ReadOnlySpan<MonitoredItemNotification> items =
                        dcn.MonitoredItems.Span;
                    for (int j = 0; j < items.Length; j++)
                    {
                        (items[j] as IPooledEncodeable)?.Reuse();
                    }
                    (dcn as IPooledEncodeable)?.Reuse();
                }
                else if (data[i].TryGetValue(out EventNotificationList? enl))
                {
                    ReadOnlySpan<EventFieldList> events = enl.Events.Span;
                    for (int j = 0; j < events.Length; j++)
                    {
                        (events[j] as IPooledEncodeable)?.Reuse();
                    }
                    (enl as IPooledEncodeable)?.Reuse();
                }
            }
            (message as IPooledEncodeable)?.Reuse();
        }

        private uint Id => m_subscriptionIdProvider();

        private readonly Func<uint> m_subscriptionIdProvider;
        private readonly uint m_maxMessageCount;
        private readonly ISubscriptionRetransmissionStore? m_retransmissionStore;
        private readonly ILogger m_logger;
        private readonly List<NotificationMessage> m_sentMessages;
        private uint m_sequenceNumber;
        private int m_lastSentMessage;
    }
}
