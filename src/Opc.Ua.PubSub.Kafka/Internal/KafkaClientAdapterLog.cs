/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using Microsoft.Extensions.Logging;
using Opc.Ua.Redaction;

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
            [NetworkAddress] string bootstrapServers,
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
