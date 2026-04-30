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
        /// Observability context
        /// </summary>
        protected ITelemetryContext Observability { get; }

        /// <summary>
        /// Create subscription
        /// </summary>
        /// <param name="services"></param>
        /// <param name="completion"></param>
        /// <param name="telemetry"></param>
        protected MessageProcessor(ISubscriptionServiceSetClientMethods services,
            IMessageAckQueue completion, ITelemetryContext telemetry)
        {
            Observability = telemetry;
            _availableInRetransmissionQueue = [];
            _logger = Observability.LoggerFactory.CreateLogger<Subscription>();
            m_services = services;
            m_completion = completion;
#if NET8_0_OR_GREATER
            m_messages = Channel.CreateUnboundedPrioritized(
                new UnboundedPrioritizedChannelOptions<IncomingMessage>
                {
                    SingleReader = true,
                    Comparer = Comparer<IncomingMessage>.Create(
                        IncomingMessage.Compare)
                });
#else
            m_messages = PrioritizedChannelHelper
                .CreateUnboundedPrioritized(
                    new UnboundedPrioritizedChannelOptions<IncomingMessage>
                    {
                        SingleReader = true,
                        Comparer = Comparer<IncomingMessage>.Create(
                            IncomingMessage.Compare)
                    });
#endif
            m_messageWorkerTask = ProcessReceivedMessagesAsync(m_cts.Token);
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            GC.SuppressFinalize(this);
            return DisposeAsync(true);
        }

        /// <inheritdoc/>
        public virtual async ValueTask OnPublishReceivedAsync(NotificationMessage message,
            IReadOnlyList<uint>? availableSequenceNumbers,
            IReadOnlyList<string> stringTable)
        {
            if (availableSequenceNumbers != null)
            {
                _availableInRetransmissionQueue = availableSequenceNumbers;
            }
            _lastNotificationTimestamp = TimeProvider.System.GetTimestamp();
            await m_messages.Writer.WriteAsync(new IncomingMessage(message, stringTable,
                TimeProvider.System.GetUtcNow())).ConfigureAwait(false);
        }

        /// <summary>
        /// Dispose subscription
        /// </summary>
        /// <param name="disposing"></param>
        protected virtual async ValueTask DisposeAsync(bool disposing)
        {
            if (disposing && !_disposed)
            {
                try
                {
                    m_messages.Writer.TryComplete();
                    m_cts.Cancel();

                    await m_messageWorkerTask.ConfigureAwait(false);
                }
                finally
                {
                    m_cts.Dispose();
                    _disposed = true;
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
        protected abstract ValueTask OnStatusChangeNotificationAsync(uint sequenceNumber,
            DateTime publishTime, StatusChangeNotification notification,
            PublishState publishStateMask, IReadOnlyList<string> stringTable);

        /// <summary>
        /// Process keep alive notification
        /// </summary>
        /// <param name="sequenceNumber"></param>
        /// <param name="publishTime"></param>
        /// <param name="publishStateMask"></param>
        /// <returns></returns>
        protected abstract ValueTask OnKeepAliveNotificationAsync(uint sequenceNumber,
            DateTime publishTime, PublishState publishStateMask);

        /// <summary>
        /// Process data change notification
        /// </summary>
        /// <param name="sequenceNumber"></param>
        /// <param name="publishTime"></param>
        /// <param name="notification"></param>
        /// <param name="publishStateMask"></param>
        /// <param name="stringTable"></param>
        /// <returns></returns>
        protected abstract ValueTask OnDataChangeNotificationAsync(uint sequenceNumber,
            DateTime publishTime, DataChangeNotification notification,
            PublishState publishStateMask, IReadOnlyList<string> stringTable);

        /// <summary>
        /// Process event notification
        /// </summary>
        /// <param name="sequenceNumber"></param>
        /// <param name="publishTime"></param>
        /// <param name="notification"></param>
        /// <param name="publishStateMask"></param>
        /// <param name="stringTable"></param>
        /// <returns></returns>
        protected abstract ValueTask OnEventDataNotificationAsync(uint sequenceNumber,
            DateTime publishTime, EventNotificationList notification,
            PublishState publishStateMask, IReadOnlyList<string> stringTable);

        /// <summary>
        /// On publish state changed
        /// </summary>
        /// <param name="stateMask"></param>
        /// <returns></returns>
        protected virtual void OnPublishStateChanged(PublishState stateMask)
        {
            if (stateMask.HasFlag(PublishState.Stopped))
            {
                _logger.LogInformation("{Subscription} STOPPED!", this);
            }
            if (stateMask.HasFlag(PublishState.Recovered))
            {
                _logger.LogInformation("{Subscription} RECOVERED!", this);
            }
            if (stateMask.HasFlag(PublishState.Completed))
            {
                _logger.LogInformation("{Subscription} CLOSED!", this);
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
                _logger.LogCritical(ex,
                    "{Subscription}: Error processing messages. Processor is exiting!!!",
                    this);

                // Do not call complete here as we do not want the subscription
                // to be removed
                throw;
            }
            await m_completion.CompleteAsync(Id, default).ConfigureAwait(false);
            OnPublishStateChanged(PublishState.Completed);
        }

        /// <summary>
        /// Process message. The logic will chewck whether any message was missed
        /// and try to republish. Any new messages received that already were
        /// handled are discarded. The message is then dispatched to the appropriate
        /// callbacks that must be overridden by the derived class.
        /// </summary>
        /// <param name="incoming"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        private async Task ProcessMessageAsync(IncomingMessage incoming, CancellationToken ct)
        {
            uint prevSeqNum = _lastSequenceNumberProcessed;
            uint curSeqNum = incoming.Message.SequenceNumber;
            if (prevSeqNum != 0)
            {
                for (uint missing = prevSeqNum + 1; missing < curSeqNum; missing++)
                {
                    // Try to republish missing messages from retransmission queue
                    await TryRepublishAsync(missing, curSeqNum, ct).ConfigureAwait(false);
                }
                if (prevSeqNum >= curSeqNum)
                {
                    // Can occur if we republished a message
                    if (!_logger.IsEnabled(LogLevel.Debug))
                    {
                        return;
                    }
                    if (curSeqNum == prevSeqNum)
                    {
                        _logger.LogDebug("{Subscription}: Received duplicate message " +
                            "with sequence number #{SeqNumber}.", this, curSeqNum);
                    }
                    else
                    {
                        _logger.LogDebug("{Subscription}: Received older message with " +
                            "sequence number #{SeqNumber} but already processed message with " +
                            "sequence number #{Old}.", this, curSeqNum, prevSeqNum);
                    }
                    return;
                }
            }
            _lastSequenceNumberProcessed = curSeqNum;
            await OnNotificationReceivedAsync(incoming.Message, PublishState.None,
                ct).ConfigureAwait(false);
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
            if (!_availableInRetransmissionQueue.Contains(missing))
            {
                _logger.LogWarning("{Subscription}: Message with sequence number " +
                    "#{SeqNumber} is not in server retransmission queue and was dropped.",
                    this, missing);
                return;
            }
            try
            {
                _logger.LogInformation("{Subscription}: Republishing missing message " +
                    "with sequence number #{Missing} to catch up to message " +
                    "with sequence number #{SeqNumber}...", this, missing, curSeqNum);
                RepublishResponse republish = await m_services.RepublishAsync(null, Id, missing,
                    ct).ConfigureAwait(false);

                if (ServiceResult.IsGood(republish.ResponseHeader.ServiceResult))
                {
                    await OnNotificationReceivedAsync(republish.NotificationMessage,
                        PublishState.Republish, ct).ConfigureAwait(false);
                }
                else
                {
                    _logger.LogWarning("{Subscription}: Republishing message with " +
                        "sequence number #{SeqNumber} failed.", this, missing);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Subscription}: Error republishing message with " +
                    "sequence number #{SeqNumber}.", this, missing);
            }
        }

        /// <summary>
        /// Dispatch notification message
        /// </summary>
        /// <param name="message"></param>
        /// <param name="publishStateMask"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        private async ValueTask OnNotificationReceivedAsync(NotificationMessage message,
            PublishState publishStateMask, CancellationToken ct)
        {
            try
            {
                if (message.NotificationData.Count == 0)
                {
                    publishStateMask |= PublishState.KeepAlive;
                    await OnKeepAliveNotificationAsync(message.SequenceNumber,
                        (DateTime)message.PublishTime, publishStateMask).ConfigureAwait(false);
                }
                else
                {
                    for (int i = 0; i < message.NotificationData.Count; i++)
                    {
                        await DispatchAsync(message, publishStateMask,
                            message.NotificationData[i]).ConfigureAwait(false);
                    }
                }
                await m_completion.QueueAsync(new SubscriptionAcknowledgement
                {
                    SequenceNumber = message.SequenceNumber,
                    SubscriptionId = Id
                }, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "{Subscription}: Error dispatching notification data.", this);
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
            if (notificationData.Value.TryGetEncodeable(out DataChangeNotification datachange))
            {
                await OnDataChangeNotificationAsync(message.SequenceNumber,
                    (DateTime)message.PublishTime, datachange, publishStateMask,
                    message.StringTable.ToArray() ?? []).ConfigureAwait(false);
            }
            else if (notificationData.Value.TryGetEncodeable(out EventNotificationList events))
            {
                await OnEventDataNotificationAsync(message.SequenceNumber,
                    (DateTime)message.PublishTime, events, publishStateMask,
                    message.StringTable.ToArray() ?? []).ConfigureAwait(false);
            }
            else if (notificationData.Value.TryGetEncodeable(out StatusChangeNotification statusChanged))
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
                await OnStatusChangeNotificationAsync(message.SequenceNumber,
                    (DateTime)message.PublishTime, statusChanged, mask,
                    message.StringTable.ToArray() ?? []).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// A message received from the server cached until is processed or discarded.
        /// </summary>
        /// <param name="Message"></param>
        /// <param name="StringTable"></param>
        /// <param name="Enqueued"></param>
        private readonly record struct IncomingMessage(NotificationMessage Message,
            IReadOnlyList<string> StringTable, DateTimeOffset Enqueued)
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

        internal bool _disposed;
        internal long _lastNotificationTimestamp;
        internal uint _lastSequenceNumberProcessed;
        internal IReadOnlyList<uint> _availableInRetransmissionQueue;
#pragma warning disable IDE1006 // Naming Styles
        internal readonly ILogger _logger;
#pragma warning restore IDE1006 // Naming Styles
        private readonly ISubscriptionServiceSetClientMethods m_services;
        // CA2213: m_cts is disposed in DisposeAsync(bool) — suppressed because
        // the analyzer does not track IAsyncDisposable disposal paths.
#pragma warning disable CA2213
        private readonly CancellationTokenSource m_cts = new();
#pragma warning restore CA2213
        private readonly IMessageAckQueue m_completion;
        private readonly Task m_messageWorkerTask;
        private readonly Channel<IncomingMessage> m_messages;
    }
}
