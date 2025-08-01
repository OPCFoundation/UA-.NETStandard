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
    public partial class DiscoveryClient
    {
        #region Constructors
        /// <summary>
        /// Creates a binding for to use for discovering servers.
        /// </summary>
        public static DiscoveryClient Create(
            ApplicationConfiguration application,
            Uri discoveryUrl)
        {
            var configuration = EndpointConfiguration.Create();
            ITransportChannel channel = DiscoveryChannel.Create(application, discoveryUrl, configuration, new ServiceMessageContext());
            return new DiscoveryClient(channel);
        }

        /// <summary>
        /// Creates a binding for to use for discovering servers.
        /// </summary>
        public static DiscoveryClient Create(
            ApplicationConfiguration application,
            Uri discoveryUrl,
            EndpointConfiguration configuration)
        {
            if (configuration == null)
            {
                configuration = EndpointConfiguration.Create();
            }

            ITransportChannel channel = DiscoveryChannel.Create(application, discoveryUrl, configuration, application.CreateMessageContext());
            return new DiscoveryClient(channel);
        }

        /// <summary>
        /// Creates a binding for to use for discovering servers.
        /// </summary>
        public static DiscoveryClient Create(
            ApplicationConfiguration application,
            ITransportWaitingConnection connection,
            EndpointConfiguration configuration)
        {
            if (configuration == null)
            {
                configuration = EndpointConfiguration.Create();
            }

            ITransportChannel channel = DiscoveryChannel.Create(application, connection, configuration, application.CreateMessageContext());
            return new DiscoveryClient(channel);
        }

        /// <summary>
        /// Creates a binding for to use for discovering servers.
        /// </summary>
        /// <param name="discoveryUrl">The discovery URL.</param>
        /// <returns></returns>
        public static DiscoveryClient Create(Uri discoveryUrl)
        {
            return DiscoveryClient.Create(discoveryUrl, null, null);
        }

        /// <summary>
        /// Creates a binding for to use for discovering servers.
        /// </summary>
        /// <param name="discoveryUrl">The discovery URL.</param>
        /// <param name="configuration">The configuration.</param>
        /// <returns></returns>
        public static DiscoveryClient Create(
            Uri discoveryUrl,
            EndpointConfiguration configuration)
        {
            return DiscoveryClient.Create(discoveryUrl, configuration, null);
        }

        /// <summary>
        /// Creates a binding for to use for discovering servers.
        /// </summary>
        public static DiscoveryClient Create(
            ITransportWaitingConnection connection,
            EndpointConfiguration configuration)
        {
            if (configuration == null)
            {
                configuration = EndpointConfiguration.Create();
            }

            ITransportChannel channel = DiscoveryChannel.Create(null, connection, configuration, new ServiceMessageContext());
            return new DiscoveryClient(channel);
        }

        /// <summary>
        /// Creates a binding to use for discovering servers.
        /// </summary>
        /// <param name="discoveryUrl">The discovery URL.</param>
        /// <param name="endpointConfiguration">The endpoint configuration.</param>
        /// /// <param name="applicationConfiguration">The application configuration.</param>
        /// <returns></returns>
        public static DiscoveryClient Create(
            Uri discoveryUrl,
            EndpointConfiguration endpointConfiguration,
            ApplicationConfiguration applicationConfiguration)
        {
            if (endpointConfiguration == null)
            {
                endpointConfiguration = EndpointConfiguration.Create();
            }

            // check if application configuration contains instance certificate.
            X509Certificate2 clientCertificate = null;

            try
            {
                // Will always use the first certificate
                clientCertificate = applicationConfiguration?.SecurityConfiguration?.ApplicationCertificate?.Find(true).Result;
            }
            catch
            {
                //ignore errors
            }

            ITransportChannel channel = DiscoveryChannel.Create(applicationConfiguration, discoveryUrl, endpointConfiguration, new ServiceMessageContext(), clientCertificate);
            return new DiscoveryClient(channel);
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Invokes the GetEndpoints service.
        /// </summary>
        /// <param name="profileUris">The collection of profile URIs.</param>
        /// <returns></returns>
        public virtual EndpointDescriptionCollection GetEndpoints(StringCollection profileUris)
        {
            EndpointDescriptionCollection endpoints = null;

            GetEndpoints(
                null,
                this.Endpoint.EndpointUrl,
                null,
                profileUris,
                out endpoints);

            return PatchEndpointUrls(endpoints);
        }

#if NET_STANDARD_ASYNC
        /// <summary>
        /// Invokes the GetEndpoints service async.
        /// </summary>
        /// <param name="profileUris">The collection of profile URIs.</param>
        /// <param name="ct">The cancellation token.</param>
        public virtual async Task<EndpointDescriptionCollection> GetEndpointsAsync(StringCollection profileUris, CancellationToken ct = default)
        {
            var response = await GetEndpointsAsync(null, this.Endpoint.EndpointUrl, null, profileUris, ct).ConfigureAwait(false);
            return PatchEndpointUrls(response.Endpoints);
        }
#endif

        /// <summary>
        /// Invokes the FindServers service.
        /// </summary>
        /// <param name="serverUris">The collection of server URIs.</param>
        /// <returns></returns>
        public virtual ApplicationDescriptionCollection FindServers(StringCollection serverUris)
        {
            ApplicationDescriptionCollection servers = null;

            FindServers(
                null,
                this.Endpoint.EndpointUrl,
                null,
                serverUris,
                out servers);

            return servers;
        }

#if NET_STANDARD_ASYNC
        /// <summary>
        /// Invokes the FindServers service async.
        /// </summary>
        /// <param name="serverUris">The collection of server URIs.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns></returns>
        public virtual async Task<ApplicationDescriptionCollection> FindServersAsync(StringCollection serverUris, CancellationToken ct = default)
        {
            var response = await FindServersAsync(
                null,
                this.Endpoint.EndpointUrl,
                null,
                serverUris,
                ct).ConfigureAwait(false);
            return response.Servers;
        }
#endif

        /// <summary>
        /// Invokes the FindServersOnNetwork service.
        /// </summary>
        /// <param name="startingRecordId"></param>
        /// <param name="maxRecordsToReturn"></param>
        /// <param name="serverCapabilityFilter"></param>
        /// <param name="lastCounterResetTime"></param>
        /// <returns></returns>
        public virtual ServerOnNetworkCollection FindServersOnNetwork(
            uint startingRecordId,
            uint maxRecordsToReturn,
            StringCollection serverCapabilityFilter,
            out DateTime lastCounterResetTime)
        {
            ServerOnNetworkCollection servers = null;

            FindServersOnNetwork(
                null,
                startingRecordId,
                maxRecordsToReturn,
                serverCapabilityFilter,
                out lastCounterResetTime,
                out servers);

            return servers;
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Patch returned endpoints urls with url used to reached the endpoint.
        /// </summary>
        private EndpointDescriptionCollection PatchEndpointUrls(EndpointDescriptionCollection endpoints)
        {
            // if a server is behind a firewall, can only be accessed with a FQDN or IP address
            // it may return URLs that are not accessible to the client. This problem can be avoided 
            // by assuming that the domain in the URL used to call GetEndpoints can be used to 
            // access any of the endpoints. This code patches the returned endpoints accordingly.
            Uri endpointUrl = Utils.ParseUri(this.Endpoint.EndpointUrl);
            if (endpointUrl != null)
            {
                // patch discovery Url to endpoint Url used for service call
                foreach (EndpointDescription discoveryEndPoint in endpoints)
                {
                    Uri discoveryEndPointUri = Utils.ParseUri(discoveryEndPoint.EndpointUrl);
                    if (discoveryEndPointUri == null)
                    {
                        Utils.LogWarning("Discovery endpoint contains invalid Url: {0}", discoveryEndPoint.EndpointUrl);
                        continue;
                    }

                    if ((endpointUrl.Scheme == discoveryEndPointUri.Scheme) &&
                        (endpointUrl.Port == discoveryEndPointUri.Port))
                    {
                        UriBuilder builder = new UriBuilder(discoveryEndPointUri);
                        builder.Host = endpointUrl.DnsSafeHost;
                        discoveryEndPoint.EndpointUrl = builder.Uri.OriginalString;
                    }

                    if (discoveryEndPoint.Server != null &&
                        discoveryEndPoint.Server.DiscoveryUrls != null)
                    {
                        discoveryEndPoint.Server.DiscoveryUrls.Clear();
                        discoveryEndPoint.Server.DiscoveryUrls.Add(this.Endpoint.EndpointUrl.ToString());
                    }
                }
            }
            return endpoints;
        }
        #endregion
    }

    /// <summary>
    /// A channel object used by clients to access a UA discovery service.
    /// </summary>
    public partial class DiscoveryChannel
    {
        #region Constructors
        /// <summary>
        /// Creates a new transport channel that supports the ISessionChannel service contract.
        /// </summary>
        /// <param name="discoveryUrl">The discovery url.</param>
        /// <param name="endpointConfiguration">The configuration to use with the endpoint.</param>
        /// <param name="messageContext">The message context to use when serializing the messages.</param>
        /// <param name="clientCertificate">The client certificate to use.</param>
        /// <returns></returns>
        public static ITransportChannel Create(
            Uri discoveryUrl,
            EndpointConfiguration endpointConfiguration,
            IServiceMessageContext messageContext,
            X509Certificate2 clientCertificate = null)
        {
            // create a default description.
            EndpointDescription endpoint = new EndpointDescription {
                EndpointUrl = discoveryUrl.OriginalString,
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None
            };
            endpoint.Server.ApplicationUri = endpoint.EndpointUrl;
            endpoint.Server.ApplicationType = ApplicationType.DiscoveryServer;

            ITransportChannel channel = CreateUaBinaryChannel(
                null,
                endpoint,
                endpointConfiguration,
                clientCertificate,
                messageContext);

            return channel;
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
            var endpoint = new EndpointDescription {
                EndpointUrl = connection.EndpointUrl.OriginalString,
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None
            };
            endpoint.Server.ApplicationUri = endpoint.EndpointUrl;
            endpoint.Server.ApplicationType = ApplicationType.DiscoveryServer;

            ITransportChannel channel = CreateUaBinaryChannel(
                configuration,
                connection,
                endpoint,
                endpointConfiguration,
                clientCertificate,
                (X509Certificate2Collection)null,
                messageContext);

            return channel;
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
            var endpoint = new EndpointDescription {
                EndpointUrl = discoveryUrl.OriginalString,
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None
            };
            endpoint.Server.ApplicationUri = endpoint.EndpointUrl;
            endpoint.Server.ApplicationType = ApplicationType.DiscoveryServer;

            ITransportChannel channel = CreateUaBinaryChannel(
                configuration,
                endpoint,
                endpointConfiguration,
                clientCertificate,
                (X509Certificate2Collection)null,
                messageContext);

            return channel;
        }

        #endregion
    }
}
