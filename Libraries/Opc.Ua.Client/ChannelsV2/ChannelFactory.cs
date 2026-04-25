#if OPCUA_CLIENT_V2
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

using System.Security.Cryptography.X509Certificates;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Default channel factory implementation that creates transport channels
    /// using the configured bindings.
    /// </summary>
    internal class ChannelFactory : IChannelFactory
    {
        private readonly ApplicationConfiguration m_configuration;
        private readonly ITelemetryContext m_telemetry;

        /// <summary>
        /// Create a channel factory with the specified configuration.
        /// </summary>
        /// <param name="configuration"></param>
        /// <param name="telemetry"></param>
        public ChannelFactory(ApplicationConfiguration configuration,
            ITelemetryContext telemetry)
        {
            m_configuration = configuration;
            m_telemetry = telemetry;
        }

        /// <inheritdoc/>
        public ITransportChannel CreateChannel(ConfiguredEndpoint endpoint,
            IServiceMessageContext messageContext,
            X509Certificate2? clientCertificate,
            X509Certificate2Collection? clientCertificateChain)
        {
#pragma warning disable CS0618 // Type or member is obsolete
            return SessionChannel.Create(
                m_configuration,
                endpoint.Description,
                endpoint.Configuration,
                clientCertificate,
                clientCertificateChain,
                messageContext);
#pragma warning restore CS0618
        }
    }
}
#endif
