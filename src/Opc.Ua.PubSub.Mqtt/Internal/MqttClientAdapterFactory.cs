/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;

namespace Opc.Ua.PubSub.Mqtt.Internal
{
    /// <summary>
    /// Default <see cref="IMqttClientFactory"/> implementation backed
    /// by MQTTnet (v4 on net48, v5 on net8+).
    /// </summary>
    /// <remarks>
    /// Wired into the DI container by the PubSub transport composition;
    /// tests may instantiate it directly or substitute a
    /// fake factory to avoid an actual broker connection.
    /// </remarks>
    internal sealed class MqttClientAdapterFactory : IMqttClientFactory
    {
        private readonly IMqttTrustedIssuerResolver? m_trustedIssuerResolver;

        /// <summary>
        /// Initializes a new <see cref="MqttClientAdapterFactory"/>.
        /// </summary>
        /// <param name="trustedIssuerResolver">
        /// Optional resolver used to materialize the CA trust chain referenced by
        /// <see cref="MqttTlsOptions.TrustedIssuerCertificateSubjects"/>. When
        /// <see langword="null"/> the adapter relies on the platform default trust store.
        /// </param>
        public MqttClientAdapterFactory(IMqttTrustedIssuerResolver? trustedIssuerResolver = null)
        {
            m_trustedIssuerResolver = trustedIssuerResolver;
        }

        /// <inheritdoc/>
        public IMqttClientAdapter CreateAdapter(
            MqttConnectionOptions options,
            ITelemetryContext telemetry,
            TimeProvider timeProvider)
        {
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            if (telemetry is null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            if (timeProvider is null)
            {
                throw new ArgumentNullException(nameof(timeProvider));
            }
            return new MqttClientAdapter(telemetry, timeProvider, m_trustedIssuerResolver);
        }
    }
}
