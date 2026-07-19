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
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Source-generated log messages that preserve the identity of the legacy
    /// "OPC-UA-Client" EventSource provider (Guid 8CFA469E-18C6-480F-9B74-B005DACDE3D3),
    /// which has been replaced by <see cref="Microsoft.Extensions.Logging.ILogger"/>
    /// based logging. Call sites obtain a logger for the
    /// <see cref="ClientEventIds.LegacyCategoryName"/> category (see
    /// <c>ITelemetryContext.CreateLogger(string)</c>) and invoke the extension methods
    /// below. The C# method names are prefixed with <c>ClientEvent</c> to avoid
    /// colliding with other per-class extension methods on <see cref="ILogger"/>, but
    /// the generated <c>EventId.Name</c> is set explicitly to the original EventSource
    /// event name for consumers that migrate to <see cref="ILogger"/>.
    /// </summary>
    internal static partial class OpcUaClientCompatibilityLog
    {
        /// <summary>
        /// The state of the client subscription.
        /// </summary>
        [LoggerMessage(
            EventId = ClientEventIds.LegacySubscriptionStateId,
            EventName = "SubscriptionState",
            Level = LogLevel.Trace,
            Message = "Subscription {Context}, Id={Id}, LastNotificationTime={LastNotificationTime:HH:mm:ss}, " +
                "GoodPublishRequestCount={GoodPublishRequestCount}, PublishingInterval={CurrentPublishingInterval}, " +
                "KeepAliveCount={CurrentKeepAliveCount}, PublishingEnabled={CurrentPublishingEnabled}, " +
                "MonitoredItemCount={MonitoredItemCount}")]
        public static partial void ClientEventSubscriptionState(
            this ILogger logger,
            string context,
            uint id,
            DateTime lastNotificationTime,
            int goodPublishRequestCount,
            double currentPublishingInterval,
            uint currentKeepAliveCount,
            bool currentPublishingEnabled,
            uint monitoredItemCount);

        /// <summary>
        /// The notification message. Callers must guard the call with
        /// <c>logger.IsEnabled(LogLevel.Trace)</c> because the <paramref name="value"/>
        /// argument requires eager <c>Variant</c>/string formatting by the caller.
        /// </summary>
        [LoggerMessage(
            EventId = ClientEventIds.LegacyNotificationId,
            EventName = "Notification",
            Level = LogLevel.Trace,
            Message = "Notification: ClientHandle={ClientHandle}, Value={Value}")]
        public static partial void ClientEventNotification(this ILogger logger, int clientHandle, string value);

        /// <summary>
        /// A notification received in Publish complete.
        /// </summary>
        [LoggerMessage(
            EventId = ClientEventIds.LegacyNotificationReceivedId,
            EventName = "NotificationReceived",
            Level = LogLevel.Trace,
            Message = "NOTIFICATION RECEIVED: SubId={SubscriptionId}, SeqNo={SequenceNumber}")]
        public static partial void ClientEventNotificationReceived(
            this ILogger logger,
            int subscriptionId,
            int sequenceNumber);

        /// <summary>
        /// A Publish begin received.
        /// </summary>
        [LoggerMessage(
            EventId = ClientEventIds.LegacyPublishStartId,
            EventName = "PublishStart",
            Level = LogLevel.Trace,
            Message = "PUBLISH #{RequestHandle} SENT")]
        public static partial void ClientEventPublishStart(this ILogger logger, int requestHandle);

        /// <summary>
        /// A Publish complete received.
        /// </summary>
        [LoggerMessage(
            EventId = ClientEventIds.LegacyPublishStopId,
            EventName = "PublishStop",
            Level = LogLevel.Trace,
            Message = "PUBLISH #{RequestHandle} RECEIVED")]
        public static partial void ClientEventPublishStop(this ILogger logger, int requestHandle);
    }
}
