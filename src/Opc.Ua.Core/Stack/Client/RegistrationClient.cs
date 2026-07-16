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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua
{
    /// <summary>
    /// An object used by clients to access a UA discovery service.
    /// </summary>
    public partial class RegistrationClient
    {
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
            Certificate instanceCertificate,
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

        /// <summary>
        /// Creates a binding to use for registering servers through a shared channel manager.
        /// </summary>
        /// <param name="manager">The client channel manager used to acquire the shared channel.</param>
        /// <param name="description">The endpoint description.</param>
        /// <param name="endpointConfiguration">The endpoint configuration.</param>
        /// <param name="telemetry">The telemetry context to use to create observability instruments.</param>
        /// <param name="instanceCertificate">The client instance certificate, if required by the endpoint.</param>
        /// <param name="returnDiagnostics">Return diagnostics to send in the requests.</param>
        /// <param name="ct">A cancellation token to cancel the operation with.</param>
        /// <returns>A registration client bound to a managed channel lease.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="manager"/>, <paramref name="description"/> or <paramref name="telemetry"/> is <c>null</c>.
        /// </exception>
        public static async Task<RegistrationClient> CreateAsync(
            IClientChannelManager manager,
            EndpointDescription description,
            EndpointConfiguration? endpointConfiguration,
            ITelemetryContext telemetry,
            Certificate? instanceCertificate = null,
            DiagnosticsMasks returnDiagnostics = DiagnosticsMasks.None,
            CancellationToken ct = default)
        {
            if (manager == null)
            {
                throw new ArgumentNullException(nameof(manager));
            }
            if (description == null)
            {
                throw new ArgumentNullException(nameof(description));
            }
            if (telemetry == null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }

            if (instanceCertificate != null &&
                description.SecurityPolicyUri is not null and not SecurityPolicies.None)
            {
                manager.UpdateClientCertificate(instanceCertificate, null);
            }

            var endpoint = new ConfiguredEndpoint(null, description, endpointConfiguration)
            {
                UpdateBeforeConnect = false
            };
            var participant = new ClientChannelReconnectParticipant(
                nameof(RegistrationClient),
                endpoint);
            IManagedTransportChannel channel = await manager.GetAsync(participant, ct).ConfigureAwait(false);
            try
            {
                return new RegistrationClient(channel, telemetry)
                {
                    ReturnDiagnostics = returnDiagnostics
                };
            }
            catch
            {
                channel.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Creates a binding to use for registering servers through a shared channel manager.
        /// </summary>
        /// <param name="manager">The client channel manager used to acquire the shared channel.</param>
        /// <param name="endpoint">The configured endpoint used to acquire the shared channel.</param>
        /// <param name="telemetry">The telemetry context to use to create observability instruments.</param>
        /// <param name="instanceCertificate">The client instance certificate, if required by the endpoint.</param>
        /// <param name="returnDiagnostics">Return diagnostics to send in the requests.</param>
        /// <param name="ct">A cancellation token to cancel the operation with.</param>
        /// <returns>A registration client bound to a managed channel lease.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="manager"/>, <paramref name="endpoint"/> or <paramref name="telemetry"/> is <c>null</c>.
        /// </exception>
        public static async Task<RegistrationClient> CreateAsync(
            IClientChannelManager manager,
            ConfiguredEndpoint endpoint,
            ITelemetryContext telemetry,
            Certificate? instanceCertificate = null,
            DiagnosticsMasks returnDiagnostics = DiagnosticsMasks.None,
            CancellationToken ct = default)
        {
            if (manager == null)
            {
                throw new ArgumentNullException(nameof(manager));
            }
            if (endpoint == null)
            {
                throw new ArgumentNullException(nameof(endpoint));
            }
            if (telemetry == null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }

            endpoint.Configuration ??= EndpointConfiguration.Create();
            EndpointDescription description = endpoint.Description;
            if (instanceCertificate != null &&
                description.SecurityPolicyUri is not null and not SecurityPolicies.None)
            {
                manager.UpdateClientCertificate(instanceCertificate, null);
            }

            var participant = new ClientChannelReconnectParticipant(
                nameof(RegistrationClient),
                endpoint);
            IManagedTransportChannel channel = await manager.GetAsync(participant, ct).ConfigureAwait(false);
            try
            {
                return new RegistrationClient(channel, telemetry)
                {
                    ReturnDiagnostics = returnDiagnostics
                };
            }
            catch
            {
                channel.Dispose();
                throw;
            }
        }
    }
}
