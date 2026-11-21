// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua.PubSub.Kafka.Internal
{
    /// <summary>
    /// Default <see cref="IKafkaClientFactory"/> implementation backed by
    /// Dekaf.
    /// </summary>
    /// <remarks>
    /// Wired into the DI container by the PubSub transport composition;
    /// tests may instantiate it directly or substitute a fake factory to
    /// avoid an actual broker connection.
    /// </remarks>
    internal sealed class DekafKafkaClientFactory : IKafkaClientFactory
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
            return new DekafKafkaClientAdapter(telemetry, timeProvider);
        }
    }
}
