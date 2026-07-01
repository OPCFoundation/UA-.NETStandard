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
 *
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

namespace Opc.Ua.PubSub.Mqtt.Internal
{
    /// <summary>
    /// Default <see cref="IMqttClientFactory"/> implementation backed
    /// by MQTTnet (v4 on netstandard / net48, v5 on net8+).
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
