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
using Microsoft.Extensions.Logging;

namespace Opc.Ua
{
    /// <summary>
    /// An object used by clients to access a UA discovery service.
    /// </summary>
    public partial class DiscoveryClient
    {
        /// <summary>
        /// Creates a binding for to use for discovering servers.
        /// </summary>
        /// <param name="discoveryUrl">The discovery URL.</param>
        [Obsolete("Use CreateAsync with telemetry parameter instead.")]
        public static DiscoveryClient Create(
            Uri discoveryUrl)
        {
            return CreateAsync(
                discoveryUrl,
                null,
                (ITelemetryContext)null).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Creates a binding for to use for discovering servers.
        /// </summary>
        /// <param name="discoveryUrl">The discovery URL.</param>
        /// <param name="configuration">The configuration.</param>
        [Obsolete("Use CreateAsync with telemetry parameter instead.")]
        public static DiscoveryClient Create(
            Uri discoveryUrl,
            EndpointConfiguration configuration)
        {
            return CreateAsync(
                discoveryUrl,
                configuration,
                (ITelemetryContext)null).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Creates a binding for to use for discovering servers.
        /// </summary>
        [Obsolete("Use CreateAsync with telemetry parameter instead.")]
        public static DiscoveryClient Create(
            ITransportWaitingConnection connection,
            EndpointConfiguration configuration)
        {
            return CreateAsync(
                connection,
                configuration,
                null).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Creates a binding for to use for discovering servers.
        /// </summary>
        [Obsolete("Use CreateAsync instead.")]
        public static DiscoveryClient Create(
            ApplicationConfiguration application,
            Uri discoveryUrl)
        {
            return CreateAsync(
                application,
                discoveryUrl).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Creates a binding for to use for discovering servers.
        /// </summary>
        [Obsolete("Use CreateAsync instead.")]
        public static DiscoveryClient Create(
            ApplicationConfiguration application,
            Uri discoveryUrl,
            EndpointConfiguration configuration)
        {
            return CreateAsync(
                application,
                discoveryUrl,
                configuration).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Creates a binding for to use for discovering servers.
        /// </summary>
        [Obsolete("Use CreateAsync instead.")]
        public static DiscoveryClient Create(
            ApplicationConfiguration application,
            ITransportWaitingConnection connection,
            EndpointConfiguration configuration)
        {
            return CreateAsync(
                application,
                connection,
                configuration).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Creates a binding to use for discovering servers.
        /// </summary>
        /// <param name="discoveryUrl">The discovery URL.</param>
        /// <param name="endpointConfiguration">The endpoint configuration.</param>
        /// <param name="applicationConfiguration">The application configuration.</param>
        [Obsolete("Use CreateAsync instead.")]
        public static DiscoveryClient Create(
            Uri discoveryUrl,
            EndpointConfiguration endpointConfiguration,
            ApplicationConfiguration applicationConfiguration)
        {
            return CreateAsync(
                discoveryUrl,
                endpointConfiguration,
                applicationConfiguration).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Creates a binding for to use for discovering servers.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="application"/> is <c>null</c>.</exception>
        public static async Task<DiscoveryClient> CreateAsync(
            ApplicationConfiguration application,
            Uri discoveryUrl,
            DiagnosticsMasks returnDiagnostics = DiagnosticsMasks.None,
            CancellationToken ct = default)
        {
            if (application == null)
            {
                throw new ArgumentNullException(nameof(application));
            }

            var configuration = EndpointConfiguration.Create();
            ServiceMessageContext messageContext = application.CreateMessageContext();
            ITransportChannel channel = await CreateChannelAsync(
                application,
                discoveryUrl,
                configuration,
                messageContext,
                null,
                ct).ConfigureAwait(false);
            return new DiscoveryClient(channel, messageContext.Telemetry)
            {
                ReturnDiagnostics = returnDiagnostics
            };
        }

        /// <summary>
        /// Creates a binding for to use for discovering servers.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="application"/> is <c>null</c>.</exception>
        public static async Task<DiscoveryClient> CreateAsync(
            ApplicationConfiguration application,
            Uri discoveryUrl,
            EndpointConfiguration configuration,
            DiagnosticsMasks returnDiagnostics = DiagnosticsMasks.None,
            CancellationToken ct = default)
        {
            if (application == null)
            {
                throw new ArgumentNullException(nameof(application));
            }

            configuration ??= EndpointConfiguration.Create();

            ServiceMessageContext messageContext = application.CreateMessageContext();
            ITransportChannel channel = await CreateChannelAsync(
                application,
                discoveryUrl,
                configuration,
                messageContext,
                null,
                ct).ConfigureAwait(false);
            return new DiscoveryClient(channel, messageContext.Telemetry)
            {
                ReturnDiagnostics = returnDiagnostics
            };
        }

        /// <summary>
        /// Creates a binding for to use for discovering servers.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="application"/> is <c>null</c>.</exception>
        public static async Task<DiscoveryClient> CreateAsync(
            ApplicationConfiguration application,
            ITransportWaitingConnection connection,
            EndpointConfiguration configuration,
            DiagnosticsMasks returnDiagnostics = DiagnosticsMasks.None,
            CancellationToken ct = default)
        {
            if (application == null)
            {
                throw new ArgumentNullException(nameof(application));
            }

            configuration ??= EndpointConfiguration.Create();

            ServiceMessageContext messageContext = application.CreateMessageContext();
            ITransportChannel channel = await CreateChannelAsync(
                application,
                connection,
                configuration,
                messageContext,
                null,
                ct).ConfigureAwait(false);
            return new DiscoveryClient(channel, messageContext.Telemetry)
            {
                ReturnDiagnostics = returnDiagnostics
            };
        }

        /// <summary>
        /// Creates a binding to use for discovering servers.
        /// </summary>
        /// <param name="discoveryUrl">The discovery URL.</param>
        /// <param name="endpointConfiguration">The endpoint configuration.</param>
        /// <param name="applicationConfiguration">The application configuration.</param>
        /// <param name="returnDiagnostics">Diagnostics to return for each request</param>
        /// <param name="ct">A cancellation token to cancel the operation with</param>
        /// <exception cref="ArgumentNullException"><paramref name="applicationConfiguration"/> is <c>null</c>.</exception>
        public static async Task<DiscoveryClient> CreateAsync(
            Uri discoveryUrl,
            EndpointConfiguration endpointConfiguration,
            ApplicationConfiguration applicationConfiguration,
            DiagnosticsMasks returnDiagnostics = DiagnosticsMasks.None,
            CancellationToken ct = default)
        {
            if (applicationConfiguration == null)
            {
                throw new ArgumentNullException(nameof(applicationConfiguration));
            }

            endpointConfiguration ??= EndpointConfiguration.Create();

            // check if application configuration contains instance certificate.
            X509Certificate2 clientCertificate = null;

            ServiceMessageContext messageContext = applicationConfiguration.CreateMessageContext();
            try
            {
                // Will always use the first certificate
                CertificateIdentifier applicationCertificate = applicationConfiguration
                    .SecurityConfiguration?
                    .ApplicationCertificate;
                if (applicationCertificate != null)
                {
                    clientCertificate = await applicationCertificate.FindAsync(
                        true,
                        telemetry: messageContext.Telemetry,
                        ct: ct).ConfigureAwait(false);
                }
            }
            catch
            {
                // ignore errors
            }

            ITransportChannel channel = await CreateChannelAsync(
                applicationConfiguration,
                discoveryUrl,
                endpointConfiguration,
                messageContext,
                clientCertificate,
                ct).ConfigureAwait(false);
            return new DiscoveryClient(channel, messageContext.Telemetry)
            {
                ReturnDiagnostics = returnDiagnostics
            };
        }

        /// <summary>
        /// Creates a binding for to use for discovering servers.
        /// </summary>
        /// <param name="discoveryUrl">The discovery URL.</param>
        /// <param name="telemetry">The telemetry context to use to create obvservability instruments</param>
        /// <param name="returnDiagnostics">Diagnostics to return for each request</param>
        /// <param name="ct">A cancellation token to cancel the operation with</param>
        public static Task<DiscoveryClient> CreateAsync(
            Uri discoveryUrl,
            ITelemetryContext telemetry,
            DiagnosticsMasks returnDiagnostics = DiagnosticsMasks.None,
            CancellationToken ct = default)
        {
            return CreateAsync(discoveryUrl, null, telemetry, returnDiagnostics, ct);
        }

        /// <summary>
        /// Creates a binding for to use for discovering servers.
        /// </summary>
        public static async Task<DiscoveryClient> CreateAsync(
            ITransportWaitingConnection connection,
            EndpointConfiguration configuration,
            ITelemetryContext telemetry,
            DiagnosticsMasks returnDiagnostics = DiagnosticsMasks.None,
            CancellationToken ct = default)
        {
            configuration ??= EndpointConfiguration.Create();

            ITransportChannel channel = await CreateChannelAsync(
                null,
                connection,
                configuration,
                new ServiceMessageContext(telemetry),
                null,
                ct).ConfigureAwait(false);
            return new DiscoveryClient(channel, telemetry)
            {
                ReturnDiagnostics = returnDiagnostics
            };
        }

        /// <summary>
        /// Creates a binding to use for discovering servers.
        /// </summary>
        /// <param name="discoveryUrl">The discovery URL.</param>
        /// <param name="endpointConfiguration">The endpoint configuration.</param>
        /// <param name="telemetry">The telemetry context to use to create obvservability instruments</param>
        /// <param name="returnDiagnostics">Diagnostics to return for each request</param>
        /// <param name="ct">A cancellation token to cancel the operation with</param>
        public static async Task<DiscoveryClient> CreateAsync(
            Uri discoveryUrl,
            EndpointConfiguration endpointConfiguration,
            ITelemetryContext telemetry,
            DiagnosticsMasks returnDiagnostics = DiagnosticsMasks.None,
            CancellationToken ct = default)
        {
            endpointConfiguration ??= EndpointConfiguration.Create();

            // check if application configuration contains instance certificate.
            X509Certificate2 clientCertificate = null;

            ITransportChannel channel = await CreateChannelAsync(
                null,
                discoveryUrl,
                endpointConfiguration,
                new ServiceMessageContext(telemetry),
                clientCertificate,
                ct).ConfigureAwait(false);
            return new DiscoveryClient(channel, telemetry)
            {
                ReturnDiagnostics = returnDiagnostics
            };
        }

        /// <summary>
        /// Invokes the GetEndpoints service.
        /// </summary>
        /// <param name="profileUris">The collection of profile URIs.</param>
        [Obsolete("Use GetEndpointsAsync instead.")]
        public virtual EndpointDescriptionCollection GetEndpoints(ArrayOf<String> profileUris)
        {
            GetEndpoints(
                null,
                Endpoint.EndpointUrl,
                default,
                profileUris,
                out ArrayOf<EndpointDescription> endpoints);

            return PatchEndpointUrls(endpoints);
        }

        /// <summary>
        /// Invokes the GetEndpoints service async.
        /// </summary>
        /// <param name="profileUris">The collection of profile URIs.</param>
        /// <param name="ct">The cancellation token.</param>
        public virtual async Task<EndpointDescriptionCollection> GetEndpointsAsync(
            ArrayOf<String> profileUris,
            CancellationToken ct = default)
        {
            GetEndpointsResponse response = await GetEndpointsAsync(
                null,
                Endpoint.EndpointUrl,
                default,
                profileUris,
                ct)
                .ConfigureAwait(false);
            return PatchEndpointUrls(response.Endpoints);
        }

        /// <summary>
        /// Invokes the FindServers service.
        /// </summary>
        /// <param name="serverUris">The collection of server URIs.</param>
        [Obsolete("Use FindServersAsync instead.")]
        public virtual ApplicationDescriptionCollection FindServers(ArrayOf<string> serverUris)
        {
            FindServers(
                null,
                Endpoint.EndpointUrl,
                default,
                serverUris,
                out ArrayOf<ApplicationDescription> servers);

            return servers;
        }

        /// <summary>
        /// Invokes the FindServers service async.
        /// </summary>
        /// <param name="serverUris">The collection of server URIs.</param>
        /// <param name="ct">The cancellation token.</param>
        public virtual async Task<ApplicationDescriptionCollection> FindServersAsync(
            ArrayOf<String> serverUris,
            CancellationToken ct = default)
        {
            FindServersResponse response = await FindServersAsync(
                null,
                Endpoint.EndpointUrl,
                default,
                serverUris,
                ct)
                .ConfigureAwait(false);
            return response.Servers;
        }

        /// <summary>
        /// Invokes the FindServersOnNetwork service.
        /// </summary>
        [Obsolete("Use FindServersOnNetworkAsync instead.")]
        public virtual ServerOnNetworkCollection FindServersOnNetwork(
            uint startingRecordId,
            uint maxRecordsToReturn,
            ArrayOf<String> serverCapabilityFilter,
            out DateTime lastCounterResetTime)
        {
            FindServersOnNetwork(
                null,
                startingRecordId,
                maxRecordsToReturn,
                serverCapabilityFilter,
                out lastCounterResetTime,
                out ArrayOf<ServerOnNetwork> servers);

            return servers;
        }

        /// <summary>
        /// Invokes the FindServersOnNetwork service.
        /// </summary>
        public virtual async Task<(
            ServerOnNetworkCollection servers,
            DateTime lastCounterResetTime
            )> FindServersOnNetworkAsync(
                uint startingRecordId,
                uint maxRecordsToReturn,
                ArrayOf<String> serverCapabilityFilter,
                CancellationToken ct = default)
        {
            FindServersOnNetworkResponse response = await FindServersOnNetworkAsync(
                null,
                startingRecordId,
                maxRecordsToReturn,
                serverCapabilityFilter,
                ct)
                .ConfigureAwait(false);

            return (response.Servers, response.LastCounterResetTime);
        }

        /// <summary>
        /// Creates a new transport channel
        /// </summary>
        /// <param name="discoveryUrl">The discovery url.</param>
        /// <param name="endpointConfiguration">The configuration to use with the endpoint.</param>
        /// <param name="messageContext">The message context to use when serializing the messages.</param>
        /// <param name="clientCertificate">The client certificate to use.</param>
        /// <param name="ct">A cancellation token to cancel the operation with</param>
        internal static ValueTask<ITransportChannel> CreateChannelAsync(
            Uri discoveryUrl,
            EndpointConfiguration endpointConfiguration,
            IServiceMessageContext messageContext,
            X509Certificate2 clientCertificate = null,
            CancellationToken ct = default)
        {
            // create a default description.
            var endpoint = new EndpointDescription
            {
                EndpointUrl = discoveryUrl.OriginalString,
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None
            };
            endpoint.Server.ApplicationUri = endpoint.EndpointUrl;
            endpoint.Server.ApplicationType = ApplicationType.DiscoveryServer;

            return ClientChannelManager.CreateUaBinaryChannelAsync(
                null,
                endpoint,
                endpointConfiguration,
                clientCertificate,
                null,
                messageContext,
                null,
                ct);
        }

        /// <summary>
        /// Creates a new transport channel that supports the ITransportWaitingConnection service contract.
        /// </summary>
        internal static ValueTask<ITransportChannel> CreateChannelAsync(
            ApplicationConfiguration configuration,
            ITransportWaitingConnection connection,
            EndpointConfiguration endpointConfiguration,
            IServiceMessageContext messageContext,
            X509Certificate2 clientCertificate = null,
            CancellationToken ct = default)
        {
            // create a default description.
            var endpoint = new EndpointDescription
            {
                EndpointUrl = connection.EndpointUrl.OriginalString,
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None
            };
            endpoint.Server.ApplicationUri = endpoint.EndpointUrl;
            endpoint.Server.ApplicationType = ApplicationType.DiscoveryServer;

            return ClientChannelManager.CreateUaBinaryChannelAsync(
                configuration,
                connection,
                endpoint,
                endpointConfiguration,
                clientCertificate,
                null,
                messageContext,
                null,
                ct);
        }

        /// <summary>
        /// Creates a new transport channel.
        /// </summary>
        internal static ValueTask<ITransportChannel> CreateChannelAsync(
            ApplicationConfiguration configuration,
            Uri discoveryUrl,
            EndpointConfiguration endpointConfiguration,
            IServiceMessageContext messageContext,
            X509Certificate2 clientCertificate = null,
            CancellationToken ct = default)
        {
            // create a default description.
            var endpoint = new EndpointDescription
            {
                EndpointUrl = discoveryUrl.OriginalString,
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None
            };
            endpoint.Server.ApplicationUri = endpoint.EndpointUrl;
            endpoint.Server.ApplicationType = ApplicationType.DiscoveryServer;

            return ClientChannelManager.CreateUaBinaryChannelAsync(
                configuration,
                endpoint,
                endpointConfiguration,
                clientCertificate,
                null,
                messageContext,
                null,
                ct);
        }

        /// <summary>
        /// Patch returned endpoints urls with url used to reached the endpoint.
        /// </summary>
        private EndpointDescriptionCollection PatchEndpointUrls(
            EndpointDescriptionCollection endpoints)
        {
            // if a server is behind a firewall, can only be accessed with a FQDN or IP address
            // it may return URLs that are not accessible to the client. This problem can be avoided
            // by assuming that the domain in the URL used to call GetEndpoints can be used to
            // access any of the endpoints. This code patches the returned endpoints accordingly.
            Uri endpointUrl = Utils.ParseUri(Endpoint.EndpointUrl);
            if (endpointUrl != null)
            {
                // patch discovery Url to endpoint Url used for service call
                foreach (EndpointDescription discoveryEndPoint in endpoints)
                {
                    Uri discoveryEndPointUri = Utils.ParseUri(discoveryEndPoint.EndpointUrl);
                    if (discoveryEndPointUri == null)
                    {
                        m_logger.LogWarning(
                            "Discovery endpoint contains invalid Url: {EndpointUrl}",
                            discoveryEndPoint.EndpointUrl);
                        continue;
                    }

                    if ((endpointUrl.Scheme == discoveryEndPointUri.Scheme) &&
                        (endpointUrl.Port == discoveryEndPointUri.Port))
                    {
                        var builder = new UriBuilder(discoveryEndPointUri)
                        {
                            Host = endpointUrl.IdnHost
                        };
                        discoveryEndPoint.EndpointUrl = builder.Uri.OriginalString;
                    }

                    if (discoveryEndPoint.Server != null &&
                        discoveryEndPoint.Server.DiscoveryUrls != null)
                    {
                        discoveryEndPoint.Server.DiscoveryUrls.Clear();
                        discoveryEndPoint.Server.DiscoveryUrls.Add(Endpoint.EndpointUrl);
                    }
                }
            }
            return endpoints;
        }
    }
}
