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
using Microsoft.Extensions.Logging;

namespace Opc.Ua.PubSub.Kafka.Internal
{
    /// <summary>
    /// Shared source-generated log messages for Kafka client adapters.
    /// </summary>
    internal static partial class KafkaClientAdapterLog
    {
        [LoggerMessage(EventId = PubSubKafkaEventIds.KafkaClientAdapter + 0, Level = LogLevel.Debug,
            Message = "Kafka adapter connected to {BootstrapServers} (protocol={Protocol}).")]
        public static partial void KafkaAdapterConnected(
            this ILogger logger,
            string bootstrapServers,
            KafkaSecurityProtocol protocol);

        [LoggerMessage(EventId = PubSubKafkaEventIds.KafkaClientAdapter + 1, Level = LogLevel.Debug,
            Message = "Kafka subscribed to {Count} topic(s).")]
        public static partial void KafkaSubscribed(this ILogger logger, int count);

        [LoggerMessage(EventId = PubSubKafkaEventIds.KafkaClientAdapter + 2, Level = LogLevel.Debug,
            Message = "Kafka consume loop cancellation raised an exception.")]
        public static partial void KafkaConsumeLoopCancellationRaisedException(
            this ILogger logger,
            Exception exception);

        [LoggerMessage(EventId = PubSubKafkaEventIds.KafkaClientAdapter + 3, Level = LogLevel.Debug,
            Message = "Kafka consume loop terminated with an exception.")]
        public static partial void KafkaConsumeLoopTerminatedWithException(this ILogger logger, Exception exception);

        [LoggerMessage(EventId = PubSubKafkaEventIds.KafkaClientAdapter + 4, Level = LogLevel.Debug,
            Message = "Kafka consumer close raised an exception.")]
        public static partial void KafkaConsumerCloseRaisedException(this ILogger logger, Exception exception);

        [LoggerMessage(EventId = PubSubKafkaEventIds.KafkaClientAdapter + 5, Level = LogLevel.Debug,
            Message = "Kafka producer flush raised an exception.")]
        public static partial void KafkaProducerFlushRaisedException(this ILogger logger, Exception exception);

        [LoggerMessage(EventId = PubSubKafkaEventIds.KafkaClientAdapter + 6, Level = LogLevel.Warning,
            Message = "Failed to dispatch inbound Kafka record.")]
        public static partial void FailedToDispatchInboundKafkaRecord(this ILogger logger, Exception exception);

        [LoggerMessage(EventId = PubSubKafkaEventIds.KafkaClientAdapter + 7, Level = LogLevel.Error,
            Message = "Kafka consume loop terminated unexpectedly.")]
        public static partial void KafkaConsumeLoopTerminatedUnexpectedly(this ILogger logger, Exception exception);
    }
}
