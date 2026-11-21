// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua.PubSub.Kafka.Internal
{
    /// <summary>
    /// Provider-model factory for the Kafka client adapter used by
    /// <see cref="KafkaBrokerTransport"/>. Test code can swap in a fake
    /// to drive the transport without an actual broker; the default
    /// implementation creates a managed Dekaf adapter and DI can select
    /// the Confluent.Kafka alternative.
    /// </summary>
    /// <remarks>
    /// Provides the adapter seam used by the Kafka broker transport per
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/Annex-B.2">
    /// Part 14 Annex B.2 Apache Kafka transport</see>.
    /// </remarks>
    public interface IKafkaClientFactory
    {
        /// <summary>
        /// Creates a fresh adapter instance.
        /// </summary>
        /// <param name="telemetry">Telemetry context.</param>
        /// <param name="timeProvider">Clock for the adapter.</param>
        /// <returns>The new adapter.</returns>
        internal IKafkaClientAdapter Create(
            ITelemetryContext telemetry,
            TimeProvider timeProvider);
    }
}
