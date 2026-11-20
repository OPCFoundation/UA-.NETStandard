/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;

namespace Opc.Ua.PubSub.Kafka.Internal
{
    /// <summary>
    /// Optional <see cref="IKafkaClientFactory"/> implementation backed by
    /// Confluent.Kafka (native librdkafka).
    /// </summary>
    /// <remarks>
    /// Selected through <c>WithConfluentKafkaClient()</c>. The default
    /// transport registration uses the managed Dekaf client.
    /// </remarks>
    internal sealed class ConfluentKafkaClientFactory : IKafkaClientFactory
    {
        /// <inheritdoc/>
        public IKafkaClientAdapter Create(ITelemetryContext telemetry, TimeProvider timeProvider)
        {
            if (telemetry is null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            if (timeProvider is null)
            {
                throw new ArgumentNullException(nameof(timeProvider));
            }
            return new ConfluentKafkaClientAdapter(telemetry, timeProvider);
        }
    }
}
