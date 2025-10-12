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
using Microsoft.Extensions.Logging;

namespace Opc.Ua
{
    /// <summary>
    /// An object used by clients to access a UA discovery service.
    /// </summary>
    public partial class DiscoveryClient
    {
        /// <summary>
        /// Intializes the object with a channel and a message context.
        /// </summary>
        public DiscoveryClient(ITransportChannel channel, ITelemetryContext telemetry)
            : this(channel)
        {
            m_logger = telemetry.CreateLogger<DiscoveryClient>();
        }

        /// <summary>
        /// Creates a binding for to use for discovering servers.
        /// </summary>
        /// <param name="discoveryUrl">The discovery URL.</param>
        [Obsolete("Use Create with telemetry parameter instead.")]
        public static DiscoveryClient Create(
            Uri discoveryUrl)
        {
            return Create(discoveryUrl, null, (ITelemetryContext)null);
        }

        /// <summary>
        /// Creates a binding for to use for discovering servers.
        /// </summary>
        /// <param name="discoveryUrl">The discovery URL.</param>
        /// <param name="configuration">The configuration.</param>
        [Obsolete("Use Create with telemetry parameter instead.")]
        public static DiscoveryClient Create(
            Uri discoveryUrl,
            EndpointConfiguration configuration)
        {
            return Create(discoveryUrl, configuration, (ITelemetryContext)null);
        }

        /// <summary>
        /// Creates a binding for to use for discovering servers.
        /// </summary>
        [Obsolete("Use Create with telemetry parameter instead.")]
        public static DiscoveryClient Create(
            ITransportWaitingConnection connection,
            EndpointConfiguration configuration)
        {
            return Create(connection, configuration, null);
        }

        /// <summary>
        /// Creates a binding for to use for discovering servers.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="application"/> is <c>null</c>.</exception>
        public static DiscoveryClient Create(
            ApplicationConfiguration application,
            Uri discoveryUrl)
        {
            if (application == null)
            {
                throw new ArgumentNullException(nameof(application));
            }

            var configuration = EndpointConfiguration.Create();
            ServiceMessageContext messageContext = application.CreateMessageContext();
            ITransportChannel channel = DiscoveryChannel.Create(
                application,
                discoveryUrl,
                configuration,
                messageContext);
            return new DiscoveryClient(channel, messageContext.Telemetry);
        }

        /// <summary>
        /// Creates a binding for to use for discovering servers.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="application"/> is <c>null</c>.</exception>
        public static DiscoveryClient Create(
            ApplicationConfiguration application,
            Uri discoveryUrl,
            EndpointConfiguration configuration)
        {
            if (application == null)
            {
                throw new ArgumentNullException(nameof(application));
            }

            configuration ??= EndpointConfiguration.Create();

            ServiceMessageContext messageContext = application.CreateMessageContext();
            ITransportChannel channel = DiscoveryChannel.Create(
                application,
                discoveryUrl,
                configuration,
                messageContext);
            return new DiscoveryClient(channel, messageContext.Telemetry);
        }

        /// <summary>
        /// Creates a binding for to use for discovering servers.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="application"/> is <c>null</c>.</exception>
        public static DiscoveryClient Create(
            ApplicationConfiguration application,
            ITransportWaitingConnection connection,
            EndpointConfiguration configuration)
        {
            if (application == null)
            {
                throw new ArgumentNullException(nameof(application));
            }

            configuration ??= EndpointConfiguration.Create();

            ServiceMessageContext messageContext = application.CreateMessageContext();
            ITransportChannel channel = DiscoveryChannel.Create(
                application,
                connection,
                configuration,
                messageContext);
            return new DiscoveryClient(channel, messageContext.Telemetry);
        }

        /// <summary>
        /// Creates a binding to use for discovering servers.
        /// </summary>
        /// <param name="discoveryUrl">The discovery URL.</param>
        /// <param name="endpointConfiguration">The endpoint configuration.</param>
        /// <param name="applicationConfiguration">The application configuration.</param>
        /// <exception cref="ArgumentNullException"><paramref name="applicationConfiguration"/> is <c>null</c>.</exception>
        public static DiscoveryClient Create(
            Uri discoveryUrl,
            EndpointConfiguration endpointConfiguration,
            ApplicationConfiguration applicationConfiguration)
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
                clientCertificate = applicationConfiguration
                    .SecurityConfiguration?
                    .ApplicationCertificate?
                    .FindAsync(true, telemetry: messageContext.Telemetry)
                    .GetAwaiter()
                    .GetResult();
            }
            catch
            {
                //ignore errors
            }

            ITransportChannel channel = DiscoveryChannel.Create(
                applicationConfiguration,
                discoveryUrl,
                endpointConfiguration,
                messageContext,
                clientCertificate);
            return new DiscoveryClient(channel, messageContext.Telemetry);
        }

        /// <summary>
        /// Creates a binding for to use for discovering servers.
        /// </summary>
        /// <param name="discoveryUrl">The discovery URL.</param>
        /// <param name="telemetry">The telemetry context to use to create obvservability instruments</param>
        public static DiscoveryClient Create(
            Uri discoveryUrl,
            ITelemetryContext telemetry)
        {
            return Create(discoveryUrl, null, telemetry);
        }

        /// <summary>
        /// Creates a binding for to use for discovering servers.
        /// </summary>
        public static DiscoveryClient Create(
            ITransportWaitingConnection connection,
            EndpointConfiguration configuration,
            ITelemetryContext telemetry)
        {
            configuration ??= EndpointConfiguration.Create();

            ITransportChannel channel = DiscoveryChannel.Create(
                null,
                connection,
                configuration,
                new ServiceMessageContext(telemetry));
            return new DiscoveryClient(channel, telemetry);
        }

        /// <summary>
        /// Creates a binding to use for discovering servers.
        /// </summary>
        /// <param name="discoveryUrl">The discovery URL.</param>
        /// <param name="endpointConfiguration">The endpoint configuration.</param>
        /// <param name="telemetry">The telemetry context to use to create obvservability instruments</param>
        public static DiscoveryClient Create(
            Uri discoveryUrl,
            EndpointConfiguration endpointConfiguration,
            ITelemetryContext telemetry)
        {
            endpointConfiguration ??= EndpointConfiguration.Create();

            // check if application configuration contains instance certificate.
            X509Certificate2 clientCertificate = null;

            ITransportChannel channel = DiscoveryChannel.Create(
                null,
                discoveryUrl,
                endpointConfiguration,
                new ServiceMessageContext(telemetry),
                clientCertificate);
            return new DiscoveryClient(channel, telemetry);
        }

        /// <summary>
        /// Invokes the GetEndpoints service.
        /// </summary>
        /// <param name="profileUris">The collection of profile URIs.</param>
        [Obsolete("Use GetEndpointsAsync instead.")]
        public virtual EndpointDescriptionCollection GetEndpoints(StringCollection profileUris)
        {
            GetEndpoints(
                null,
                Endpoint.EndpointUrl,
                null,
                profileUris,
                out EndpointDescriptionCollection endpoints);

            return PatchEndpointUrls(endpoints);
        }

        /// <summary>
        /// Invokes the GetEndpoints service async.
        /// </summary>
        /// <param name="profileUris">The collection of profile URIs.</param>
        /// <param name="ct">The cancellation token.</param>
        public virtual async Task<EndpointDescriptionCollection> GetEndpointsAsync(
            StringCollection profileUris,
            CancellationToken ct = default)
        {
            GetEndpointsResponse response = await GetEndpointsAsync(
                null,
                Endpoint.EndpointUrl,
                null,
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
        public virtual ApplicationDescriptionCollection FindServers(StringCollection serverUris)
        {
            FindServers(
                null,
                Endpoint.EndpointUrl,
                null,
                serverUris,
                out ApplicationDescriptionCollection servers);

            return servers;
        }

        /// <summary>
        /// Invokes the FindServers service async.
        /// </summary>
        /// <param name="serverUris">The collection of server URIs.</param>
        /// <param name="ct">The cancellation token.</param>
        public virtual async Task<ApplicationDescriptionCollection> FindServersAsync(
            StringCollection serverUris,
            CancellationToken ct = default)
        {
            FindServersResponse response = await FindServersAsync(
                null,
                Endpoint.EndpointUrl,
                null,
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
            StringCollection serverCapabilityFilter,
            out DateTime lastCounterResetTime)
        {
            FindServersOnNetwork(
                null,
                startingRecordId,
                maxRecordsToReturn,
                serverCapabilityFilter,
                out lastCounterResetTime,
                out ServerOnNetworkCollection servers);

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
                StringCollection serverCapabilityFilter,
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
                            Host = endpointUrl.DnsSafeHost
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

    /// <summary>
    /// A channel object used by clients to access a UA discovery service.
    /// </summary>
    public partial class DiscoveryChannel
    {
        /// <summary>
        /// Creates a new transport channel that supports the ISessionChannel service contract.
        /// </summary>
        /// <param name="discoveryUrl">The discovery url.</param>
        /// <param name="endpointConfiguration">The configuration to use with the endpoint.</param>
        /// <param name="messageContext">The message context to use when serializing the messages.</param>
        /// <param name="clientCertificate">The client certificate to use.</param>
        public static ITransportChannel Create(
            Uri discoveryUrl,
            EndpointConfiguration endpointConfiguration,
            IServiceMessageContext messageContext,
            X509Certificate2 clientCertificate = null)
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

            return CreateUaBinaryChannel(
                null,
                endpoint,
                endpointConfiguration,
                clientCertificate,
                messageContext);
        }

        /// <summary>
        /// Creates a new transport channel that supports the ITransportWaitingConnection service contract.
        /// </summary>
        public static ITransportChannel Create(
            ApplicationConfiguration configuration,
            ITransportWaitingConnection connection,
            EndpointConfiguration endpointConfiguration,
            IServiceMessageContext messageContext,
            X509Certificate2 clientCertificate = null)
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

            return CreateUaBinaryChannel(
                configuration,
                connection,
                endpoint,
                endpointConfiguration,
                clientCertificate,
                null,
                messageContext);
        }

        /// <summary>
        /// Creates a new transport channel that supports the IDiscoveryChannel service contract.
        /// </summary>
        public static ITransportChannel Create(
            ApplicationConfiguration configuration,
            Uri discoveryUrl,
            EndpointConfiguration endpointConfiguration,
            IServiceMessageContext messageContext,
            X509Certificate2 clientCertificate = null)
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

            return CreateUaBinaryChannel(
                configuration,
                endpoint,
                endpointConfiguration,
                clientCertificate,
                null,
                messageContext);
        }
    }
}
