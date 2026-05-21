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
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Bindings;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Lds.Server
{
    /// <summary>
    /// OPC UA Local Discovery Server (LDS / LDS-ME) implementation per Part 12.
    /// </summary>
    /// <remarks>
    /// Subclasses <see cref="DiscoveryServerBase"/> so we get the discovery
    /// service entry points (FindServers, FindServersOnNetwork, GetEndpoints,
    /// RegisterServer, RegisterServer2) without inheriting Session,
    /// Subscription, or NodeManager infrastructure from
    /// <c>SessionServerBase</c>/<c>StandardServer</c>.
    /// </remarks>
    public class LdsServer : DiscoveryServerBase
    {
        private readonly ITelemetryContext m_telemetry;
        private ILogger m_log;
        private SemaphoreSlim m_lock;
        private MulticastDiscovery m_multicast;

        /// <summary>
        /// Creates a new LDS server.
        /// </summary>
        /// <param name="telemetry">Telemetry context for logging.</param>
        public LdsServer(ITelemetryContext telemetry = null)
            : base(telemetry)
        {
            m_telemetry = telemetry;
            m_lock = new SemaphoreSlim(1, 1);
            Store = new RegisteredServerStore();
        }

        /// <summary>
        /// In-memory database of registered servers and network records.
        /// Exposed for tests so they can deterministically seed state.
        /// </summary>
        public RegisteredServerStore Store { get; }

        /// <summary>
        /// Optional multicast discovery layer (LDS-ME). Null when multicast
        /// is disabled.
        /// </summary>
        public MulticastDiscovery Multicast => m_multicast;

        /// <summary>
        /// Optional hook tests use to plug in a multicast layer prior to
        /// <see cref="ServerBase.StartAsync(ApplicationConfiguration, CancellationToken)"/>.
        /// </summary>
        public Func<LdsServer, MulticastDiscovery> MulticastFactory { get; set; }

        /// <inheritdoc />
        protected override void OnServerStarting(ApplicationConfiguration configuration)
        {
            base.OnServerStarting(configuration);

            m_log = m_telemetry?.CreateLogger<LdsServer>()
                ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<LdsServer>.Instance;

            // mark capabilities so this server self-advertises as an LDS / LDS-ME.
            if (ServerCapabilities.IsNull || ServerCapabilities.Count == 0)
            {
                ServerCapabilities = new[] { "LDS" };
            }

            // wire up the optional multicast layer.
            if (MulticastFactory != null)
            {
                m_multicast = MulticastFactory(this);
            }
        }

        /// <inheritdoc />
        protected override async ValueTask StartApplicationAsync(
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            await base.StartApplicationAsync(configuration, cancellationToken).ConfigureAwait(false);

            if (m_multicast != null)
            {
                IList<string> capabilities = ServerCapabilities.IsNull
                    ? new List<string>()
                    : ServerCapabilities.ToList();
                IList<string> baseUris = BaseAddresses
                    .Select(b => b.Url?.ToString())
                    .Where(u => !string.IsNullOrEmpty(u))
                    .ToList();
                await m_multicast
                    .StartAsync(configuration.ApplicationUri, baseUris, capabilities, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        /// <inheritdoc />
        protected override async ValueTask OnServerStoppingAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                if (m_multicast != null)
                {
                    await m_multicast.StopAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                Store.Dispose();
                await base.OnServerStoppingAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        /// <inheritdoc />
        public override async ValueTask<FindServersResponse> FindServersAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader requestHeader,
            string endpointUrl,
            ArrayOf<string> localeIds,
            ArrayOf<string> serverUris,
            RequestLifetime requestLifetime)
        {
            ValidateRequest(requestHeader);

            var servers = new List<ApplicationDescription>();

            await m_lock.WaitAsync(requestLifetime.CancellationToken).ConfigureAwait(false);
            try
            {
                IList<BaseAddress> baseAddresses = BaseAddresses;

                Uri parsedEndpointUrl = Utils.ParseUri(endpointUrl);
                if (parsedEndpointUrl != null)
                {
                    baseAddresses = FilterByEndpointUrl(parsedEndpointUrl, baseAddresses);
                }

                ICollection<string> uriFilter = serverUris.IsNull
                    ? Array.Empty<string>()
                    : serverUris.ToList();

                // include the LDS itself unless filtered out.
                if (baseAddresses.Count > 0 &&
                    (uriFilter.Count == 0 || uriFilter.Contains(ServerDescription.ApplicationUri)))
                {
                    servers.Add(TranslateApplicationDescription(
                        parsedEndpointUrl,
                        ServerDescription,
                        baseAddresses,
                        ServerDescription.ApplicationName));
                }

                ICollection<string> requestedLocales = localeIds.IsNull
                    ? Array.Empty<string>()
                    : localeIds.ToList();

                // append registered servers that pass the filter.
                foreach (ApplicationDescription registered in Store.Find(uriFilter, requestedLocales))
                {
                    servers.Add(registered);
                }
            }
            finally
            {
                m_lock.Release();
            }

            return new FindServersResponse
            {
                ResponseHeader = CreateResponse(requestHeader, StatusCodes.Good),
                Servers = servers
            };
        }

        /// <inheritdoc />
        public override async ValueTask<GetEndpointsResponse> GetEndpointsAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader requestHeader,
            string endpointUrl,
            ArrayOf<string> localeIds,
            ArrayOf<string> profileUris,
            RequestLifetime requestLifetime)
        {
            ValidateRequest(requestHeader);

            ArrayOf<EndpointDescription> endpoints;

            await m_lock.WaitAsync(requestLifetime.CancellationToken).ConfigureAwait(false);
            try
            {
                IList<BaseAddress> baseAddresses = FilterByProfile(profileUris, BaseAddresses);
                endpoints = BuildEndpointDescriptions(endpointUrl, baseAddresses);
            }
            finally
            {
                m_lock.Release();
            }

            return new GetEndpointsResponse
            {
                ResponseHeader = CreateResponse(requestHeader, StatusCodes.Good),
                Endpoints = endpoints
            };
        }

        /// <inheritdoc />
        public override async ValueTask<RegisterServerResponse> RegisterServerAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader requestHeader,
            RegisteredServer server,
            RequestLifetime requestLifetime)
        {
            ValidateRequest(requestHeader);

            ServiceResult validation = ValidateRegistration(secureChannelContext, server);
            if (ServiceResult.IsBad(validation))
            {
                throw new ServiceResultException(validation);
            }

            _ = await Store
                .RegisterAsync(server, mdnsConfig: null, requestLifetime.CancellationToken)
                .ConfigureAwait(false);

            return new RegisterServerResponse
            {
                ResponseHeader = CreateResponse(requestHeader, StatusCodes.Good)
            };
        }

        /// <inheritdoc />
        public override async ValueTask<RegisterServer2Response> RegisterServer2Async(
            SecureChannelContext secureChannelContext,
            RequestHeader requestHeader,
            RegisteredServer server,
            ArrayOf<ExtensionObject> discoveryConfiguration,
            RequestLifetime requestLifetime)
        {
            ValidateRequest(requestHeader);

            ServiceResult validation = ValidateRegistration(secureChannelContext, server);
            if (ServiceResult.IsBad(validation))
            {
                throw new ServiceResultException(validation);
            }

            var configResults = new List<StatusCode>();

            // gather mdns configurations; non-mdns configs are returned as Good but ignored.
            var mdnsConfigs = new List<MdnsDiscoveryConfiguration>();
            if (!discoveryConfiguration.IsNull)
            {
                foreach (ExtensionObject ext in discoveryConfiguration)
                {
                    if (!ext.IsNull && ext.TryGetValue(out MdnsDiscoveryConfiguration mdns))
                    {
                        mdnsConfigs.Add(mdns);
                    }
                    configResults.Add(StatusCodes.Good);
                }
            }

            if (mdnsConfigs.Count == 0)
            {
                // no MdnsDiscoveryConfiguration provided — fall back to a plain registration.
                _ = await Store
                    .RegisterAsync(server, mdnsConfig: null, requestLifetime.CancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                foreach (MdnsDiscoveryConfiguration mdns in mdnsConfigs)
                {
                    _ = await Store
                        .RegisterAsync(server, mdns, requestLifetime.CancellationToken)
                        .ConfigureAwait(false);
                }
            }

            return new RegisterServer2Response
            {
                ResponseHeader = CreateResponse(requestHeader, StatusCodes.Good),
                ConfigurationResults = configResults
            };
        }

        /// <inheritdoc />
        public override async ValueTask<FindServersOnNetworkResponse> FindServersOnNetworkAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader requestHeader,
            uint startingRecordId,
            uint maxRecordsToReturn,
            ArrayOf<string> serverCapabilityFilter,
            RequestLifetime requestLifetime)
        {
            ValidateRequest(requestHeader);

            await Task.Yield();

            ICollection<string> capFilter = serverCapabilityFilter.IsNull
                ? Array.Empty<string>()
                : serverCapabilityFilter.ToList();

            (IList<ServerOnNetwork> records, DateTime lastReset) =
                Store.ListOnNetwork(startingRecordId, maxRecordsToReturn, capFilter);

            return new FindServersOnNetworkResponse
            {
                ResponseHeader = CreateResponse(requestHeader, StatusCodes.Good),
                LastCounterResetTime = lastReset,
                Servers = records.ToArray()
            };
        }

        /// <summary>
        /// Validates a <see cref="RegisteredServer"/> against Part 12 §6.4.2/§6.4.5
        /// requirements, returning the appropriate status code on failure.
        /// </summary>
        protected virtual ServiceResult ValidateRegistration(
            SecureChannelContext secureChannelContext,
            RegisteredServer server)
        {
            if (server == null)
            {
                throw new ArgumentNullException(nameof(server));
            }

            // Per Part 12 §6.4.2: registration must occur on a secure channel
            // (Sign or SignAndEncrypt). None is rejected.
            MessageSecurityMode mode = secureChannelContext?.EndpointDescription?.SecurityMode
                ?? MessageSecurityMode.Invalid;
            if (mode == MessageSecurityMode.None || mode == MessageSecurityMode.Invalid)
            {
                return new ServiceResult(StatusCodes.BadSecurityChecksFailed,
                    new LocalizedText("RegisterServer requires a signed secure channel."));
            }

            if (string.IsNullOrEmpty(server.ServerUri))
            {
                return new ServiceResult(StatusCodes.BadServerUriInvalid,
                    new LocalizedText("ServerUri is empty."));
            }

            if (server.ServerNames.IsNull || server.ServerNames.Count == 0)
            {
                return new ServiceResult(StatusCodes.BadServerNameMissing,
                    new LocalizedText("ServerNames is empty."));
            }

            if (server.DiscoveryUrls.IsNull || server.DiscoveryUrls.Count == 0)
            {
                return new ServiceResult(StatusCodes.BadDiscoveryUrlMissing,
                    new LocalizedText("DiscoveryUrls is empty."));
            }

            if (server.ServerType == ApplicationType.Client)
            {
                return new ServiceResult(StatusCodes.BadInvalidArgument,
                    new LocalizedText("ServerType=Client is not allowed."));
            }

            if ((int)server.ServerType is < 0 or > (int)ApplicationType.DiscoveryServer)
            {
                return new ServiceResult(StatusCodes.BadInvalidArgument,
                    new LocalizedText("ServerType is out of range."));
            }

            // Match the client cert ApplicationUri against the ServerUri.
            if (secureChannelContext?.ClientChannelCertificate is { Length: > 0 } certBytes)
            {
                try
                {
                    using var cert = Certificate.FromRawData(certBytes);
                    IReadOnlyList<string> applicationUris = X509Utils.GetApplicationUrisFromCertificate(cert);
                    if (applicationUris.Count > 0 &&
                        !applicationUris.Any(uri => string.Equals(uri, server.ServerUri, StringComparison.Ordinal)))
                    {
                        return new ServiceResult(StatusCodes.BadServerUriInvalid,
                            new LocalizedText("ServerUri does not match the certificate ApplicationUri."));
                    }
                }
                catch (Exception ex)
                {
                    m_log?.LogDebug(ex, "Failed to inspect client cert ApplicationUri.");
                }
            }

            return ServiceResult.Good;
        }

        private ArrayOf<EndpointDescription> BuildEndpointDescriptions(
            string endpointUrl,
            IList<BaseAddress> baseAddresses)
        {
            Uri parsedEndpointUrl = Utils.ParseUri(endpointUrl);
            if (parsedEndpointUrl != null)
            {
                baseAddresses = FilterByEndpointUrl(parsedEndpointUrl, baseAddresses);
            }

            if (baseAddresses.Count == 0)
            {
                return new ArrayOf<EndpointDescription>(Array.Empty<EndpointDescription>());
            }

            ApplicationDescription application = TranslateApplicationDescription(
                parsedEndpointUrl,
                ServerDescription,
                baseAddresses,
                ServerDescription.ApplicationName);

            return TranslateEndpointDescriptions(
                parsedEndpointUrl,
                baseAddresses,
                Endpoints,
                application);
        }

        /// <inheritdoc />
        protected override IList<ServiceHost> InitializeServiceHosts(
            ApplicationConfiguration configuration,
            ITransportListenerBindings bindingFactory,
            out ApplicationDescription serverDescription,
            out ArrayOf<EndpointDescription> endpoints)
        {
            var hosts = new Dictionary<string, ServiceHost>();

            // Mirror StandardServer's defaults so the LDS opens a real listener.
            if (configuration.ServerConfiguration.SecurityPolicies.IsEmpty)
            {
                configuration.ServerConfiguration.SecurityPolicies =
                    configuration.ServerConfiguration.SecurityPolicies.AddItem(new ServerSecurityPolicy());
            }

            if (configuration.ServerConfiguration.UserTokenPolicies.IsEmpty)
            {
                var userTokenPolicy = new UserTokenPolicy { TokenType = UserTokenType.Anonymous };
                userTokenPolicy.PolicyId = userTokenPolicy.TokenType.ToString();

                configuration.ServerConfiguration.UserTokenPolicies += userTokenPolicy;
            }

            serverDescription = new ApplicationDescription
            {
                ApplicationUri = configuration.ApplicationUri,
                ApplicationName = new LocalizedText("en-US", configuration.ApplicationName),
                ApplicationType = configuration.ApplicationType,
                ProductUri = configuration.ProductUri,
                DiscoveryUrls = GetDiscoveryUrls()
            };

            var endpointsList = new List<EndpointDescription>();
            ArrayOf<string> baseAddresses = configuration.ServerConfiguration.BaseAddresses;
            foreach (string scheme in Utils.DefaultUriSchemes)
            {
                bool hasAddress = false;
                foreach (string a in baseAddresses)
                {
                    if (a.StartsWith(scheme, StringComparison.Ordinal))
                    {
                        hasAddress = true;
                        break;
                    }
                }
                if (!hasAddress)
                {
                    continue;
                }

                ITransportListenerFactory binding = bindingFactory.GetBinding(scheme, MessageContext.Telemetry);
                if (binding != null)
                {
                    List<EndpointDescription> endpointsForHost = binding.CreateServiceHost(
                        this,
                        hosts,
                        configuration,
                        configuration.ServerConfiguration.BaseAddresses,
                        serverDescription,
                        configuration.ServerConfiguration.SecurityPolicies,
                        CertificateManager,
                        configuration.CertificateManager);
                    endpointsList.AddRange(endpointsForHost);
                }
            }

            endpoints = endpointsList.ToArray();
            return hosts.Values.ToList();
        }

        /// <inheritdoc />
        public override ServiceHost CreateServiceHost(ServerBase server, params Uri[] addresses)
        {
            return new ServiceHost(this, addresses);
        }

        /// <inheritdoc />
        protected override EndpointBase GetEndpointInstance(ServerBase server)
        {
            return new DiscoveryEndpoint(server);
        }

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                m_lock?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
