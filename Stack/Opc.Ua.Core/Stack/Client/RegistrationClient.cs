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
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua
{
    /// <summary>
    /// An object used by clients to access a UA discovery service.
    /// </summary>
    public partial class RegistrationClient
    {
        /// <summary>
        /// Intializes the object with a channel and a message context.
        /// </summary>
        public RegistrationClient(ITransportChannel channel, ITelemetryContext telemetry)
            : this(channel)
        {
            m_logger = telemetry.CreateLogger<RegistrationClient>();
        }

        /// <summary>
        /// Creates a binding for to use for discovering servers.
        /// </summary>
        /// <param name="configuration">The configuration.</param>
        /// <param name="description">The description.</param>
        /// <param name="endpointConfiguration">The endpoint configuration.</param>
        /// <param name="instanceCertificate">The instance certificate.</param>
        /// <param name="returnDiagnostics">Return diagnostics to sent in the responses</param>
        /// <param name="ct"></param>
        /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <c>null</c>.</exception>
        public static async Task<RegistrationClient> CreateAsync(
            ApplicationConfiguration configuration,
            EndpointDescription description,
            EndpointConfiguration endpointConfiguration,
            X509Certificate2 instanceCertificate,
            DiagnosticsMasks returnDiagnostics = DiagnosticsMasks.None,
            CancellationToken ct = default)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            if (description == null)
            {
                throw new ArgumentNullException(nameof(description));
            }

            ServiceMessageContext context = configuration.CreateMessageContext();

            ITransportChannel channel = await ClientChannelManager.CreateUaBinaryChannelAsync(
                configuration,
                description,
                endpointConfiguration,
                instanceCertificate,
                null,
                context,
                null,
                ct).ConfigureAwait(false);

            return new RegistrationClient(channel, context.Telemetry)
            {
                ReturnDiagnostics = returnDiagnostics
            };
        }
    }

    /// <summary>
    /// A channel object used by clients to access a UA registration service.
    /// </summary>
    [Obsolete("Use RegistrationClient.CreateAsync instead to create a registrations client.")]
    public partial class RegistrationChannel
    {
        /// <summary>
        /// Creates a new transport channel that supports registration
        /// </summary>
        [Obsolete("Use ClientChannelFactory.CreateChannelAsync instead.")]
        public static ITransportChannel Create(
            ApplicationConfiguration configuration,
            EndpointDescription description,
            EndpointConfiguration endpointConfiguration,
            X509Certificate2 clientCertificate,
            IServiceMessageContext messageContext)
        {
            return ClientChannelManager.CreateUaBinaryChannelAsync(
                configuration,
                description,
                endpointConfiguration,
                clientCertificate,
                null,
                messageContext,
                null).AsTask().GetAwaiter().GetResult();
        }
    }
}
