/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

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

            ITransportChannel channel = await ClientChannelFactory.CreateUaBinaryChannelAsync(
                configuration,
                description,
                endpointConfiguration,
                instanceCertificate,
                null,
                context,
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
            return ClientChannelFactory.CreateUaBinaryChannelAsync(
                configuration,
                description,
                endpointConfiguration,
                clientCertificate,
                null,
                messageContext).AsTask().GetAwaiter().GetResult();
        }
    }
}
