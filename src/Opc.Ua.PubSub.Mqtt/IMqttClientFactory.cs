/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using Opc.Ua.PubSub.Mqtt.Internal;

namespace Opc.Ua.PubSub.Mqtt
{
    /// <summary>
    /// Provider-model factory for the MQTT client adapter used by
    /// <see cref="MqttBrokerTransport"/>. Test code can swap in a
    /// fake to drive the transport without an actual broker; the
    /// default implementation
    /// (<c>Internal.MqttClientAdapterFactory</c>) creates an
    /// MQTTnet-backed adapter.
    /// </summary>
    /// <remarks>
    /// Provides the adapter seam used by the MQTT broker transport
    /// per
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.3.4">
    /// Part 14 §7.3.4 Broker transport (MQTT)</see>.
    /// </remarks>
    public interface IMqttClientFactory
    {
        /// <summary>
        /// Creates a fresh adapter instance.
        /// </summary>
        /// <param name="options">Connection options.</param>
        /// <param name="telemetry">Telemetry context.</param>
        /// <param name="timeProvider">Clock for the adapter.</param>
        /// <returns>The new adapter.</returns>
        internal IMqttClientAdapter CreateAdapter(
            MqttConnectionOptions options,
            ITelemetryContext telemetry,
            TimeProvider timeProvider);
    }
}
