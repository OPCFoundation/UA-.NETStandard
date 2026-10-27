/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Bindings;
using Opc.Ua.DataChannels;
using Opc.Ua.Server.DataChannels;

namespace Opc.Ua.Server
{
    public partial class StandardServer
    {
        /// <summary>
        /// Gets or sets the borrowed opt-in flow provider. Configured flows do not require the model.
        /// Configure before starting the server; the application or DI container owns its lifetime.
        /// </summary>
        public ServerDataChannelProvider? DataChannelProvider
        {
            get => m_dataChannelProvider;
            set
            {
                if (m_detachDataChannelHandlers != null || m_serverInternal?.IsRunning == true)
                {
                    throw new InvalidOperationException("Data channel hosting cannot change while the server runs.");
                }
                m_dataChannelProvider = value;
                ConfigureDataChannelModelFactory();
            }
        }

        /// <summary>
        /// Gets or sets whether to expose the optional generated manager, source, capability and metadata model.
        /// This does not enable a carrier or implicitly grant any resource.
        /// </summary>
        public bool EnableDataChannelModel
        {
            get => m_enableDataChannelModel;
            set
            {
                if (m_detachDataChannelHandlers != null || m_serverInternal?.IsRunning == true)
                {
                    throw new InvalidOperationException("The data channel model is fixed at server startup.");
                }
                m_enableDataChannelModel = value;
                ConfigureDataChannelModelFactory();
            }
        }

        /// <inheritdoc/>
        public override async ValueTask CreateServiceHostEndpointAsync(
            Uri endpointUri,
            List<EndpointDescription> endpoints,
            EndpointConfiguration endpointConfiguration,
            ITransportListener listener,
            ICertificateValidatorEx certificateValidator,
            CancellationToken ct = default)
        {
            ServerDataChannelProvider? provider = DataChannelProvider;
            bool handedToBase = false;
            bool opened = false;
            List<IDisposable> registrations = [];
            try
            {
                List<EndpointDescription> dataEndpoints = PrepareDataChannelEndpoints(endpoints, provider);
                if (UascDataChannelEndpoint.IsFlowResource(endpointUri) && dataEndpoints.Count == 0)
                {
                    throw new ServiceResultException(StatusCodes.BadSecurityModeRejected);
                }
                handedToBase = true;
                await base.CreateServiceHostEndpointAsync(
                    endpointUri, endpoints, endpointConfiguration, listener, certificateValidator, ct)
                    .ConfigureAwait(false);
                opened = true;
                if (provider != null)
                {
                    foreach (EndpointDescription endpoint in dataEndpoints)
                    {
                        registrations.Add(provider.RegisterEndpoint(
                            endpoint, () => TransportListeners.Contains(listener) && ServerInternal.IsRunning));
                    }
                }
                m_dataChannelEndpoints.AddRange(registrations);
            }
            catch
            {
                foreach (IDisposable registration in registrations)
                {
                    registration.Dispose();
                }
                if (opened)
                {
                    TransportListeners.Remove(listener);
                }
                if (opened || !handedToBase)
                {
                    try
                    {
                        await listener.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        m_logger.DataChannelEndpointCleanupFailed(exception);
                    }
                }
                throw;
            }
        }

        /// <summary>
        /// Selects explicit UASC resources without weakening their advertised security configurations.
        /// </summary>
        private static List<EndpointDescription> PrepareDataChannelEndpoints(
            List<EndpointDescription> endpoints,
            ServerDataChannelProvider? provider)
        {
            List<EndpointDescription> dataEndpoints = [];
            for (int ii = endpoints.Count - 1; ii >= 0; ii--)
            {
                EndpointDescription endpoint = endpoints[ii];
                if (!Uri.TryCreate(endpoint.EndpointUrl, UriKind.Absolute, out Uri? uri) ||
                    !UascDataChannelEndpoint.IsFlowResource(uri))
                {
                    continue;
                }
                string? profile = UascDataChannelEndpoint.GetResourceProfile(uri.AbsolutePath);
                DataChannelCarrierKind? kind = UascDataChannelEndpoint.GetResourceKind(uri);
                if (provider == null || kind == null || profile == null ||
                    uri.Scheme is not (Utils.UriSchemeOpcTcp or Utils.UriSchemeOpcWss))
                {
                    throw new ServiceResultException(StatusCodes.BadNotSupported);
                }
                endpoint.TransportProfileUri = profile;
                if (endpoint.SecurityMode != MessageSecurityMode.SignAndEncrypt &&
                    !(endpoint.SecurityMode == MessageSecurityMode.Sign && provider.AllowIntegrity))
                {
                    endpoints.RemoveAt(ii);
                    continue;
                }
                if (kind == DataChannelCarrierKind.DedicatedUasc)
                {
                    endpoint.UserIdentityTokens = [];
                }
                if (endpoint.Server != null)
                {
                    endpoint.Server = (ApplicationDescription)endpoint.Server.Clone();
                    List<string> discoveryUrls = [];
                    foreach (string address in endpoint.Server.DiscoveryUrls)
                    {
                        if (Uri.TryCreate(address, UriKind.Absolute, out Uri? discovery) &&
                            !UascDataChannelEndpoint.IsFlowResource(discovery) && discovery.Scheme != "opc.quic")
                        {
                            discoveryUrls.Add(address);
                        }
                    }
                    endpoint.Server.DiscoveryUrls = [.. discoveryUrls];
                }
                dataEndpoints.Add(endpoint);
            }
            return dataEndpoints;
        }

        /// <summary>
        /// Keeps optional data discovery separate from ordinary endpoint translation.
        /// </summary>
        private ArrayOf<EndpointDescription> IncludeDataChannelEndpoints(
            ArrayOf<EndpointDescription> ordinary,
            ArrayOf<string> profileUris)
        {
            List<EndpointDescription> selected = [];
            foreach (EndpointDescription endpoint in ordinary)
            {
                if (ServerDataChannelProvider.IsDataProfile(endpoint.TransportProfileUri) ||
                    (Uri.TryCreate(endpoint.EndpointUrl, UriKind.Absolute, out Uri? uri) &&
                    UascDataChannelEndpoint.IsFlowResource(uri)))
                {
                    continue;
                }
                if (profileUris.Count == 0 ||
                    DataChannelSource.Contains(profileUris, endpoint.TransportProfileUri ?? string.Empty))
                {
                    selected.Add(endpoint);
                }
            }
            if (DataChannelProvider != null)
            {
                selected.AddRange(DataChannelProvider.GetEndpoints(profileUris));
            }
            return [.. selected];
        }

        /// <summary>
        /// Adds exactly one optional model node manager without modifying generated modelling rules.
        /// </summary>
        private void ConfigureDataChannelModelFactory()
        {
            if (m_dataChannelModelFactory != null)
            {
                RemoveNodeManager(m_dataChannelModelFactory);
                m_dataChannelModelFactory = null;
            }
            if (m_enableDataChannelModel && m_dataChannelProvider != null)
            {
                m_dataChannelModelFactory = new DataChannelNodeManagerFactory(m_dataChannelProvider);
                AddNodeManager(m_dataChannelModelFactory);
            }
        }

        /// <summary>
        /// Subscribes to the original manager instances, never to transient control connections.
        /// </summary>
        private void InitializeDataChannelServices()
        {
            ServerDataChannelProvider? provider = DataChannelProvider;
            if (provider == null)
            {
                return;
            }
            IServerInternal server = ServerInternal;
            provider.AttachServer(server);
            ISessionManager sessionManager = server.SessionManager;
            IRoleManager roleManager = server.RoleManager;
            void OnClosing(ISession session, SessionEventReason reason) => provider.RevokeSession(session.Id);
            void OnActivated(ISession session, SessionEventReason reason) => provider.SessionActivated(session);
            void OnRolesChanged(object? sender, RoleConfigurationChangedEventArgs args)
            {
                _ = provider.ObserveRecheckAsync();
            }
            sessionManager.SessionClosing += OnClosing;
            sessionManager.SessionActivated += OnActivated;
            roleManager.RoleConfigurationChanged += OnRolesChanged;
            m_detachDataChannelHandlers = () =>
            {
                sessionManager.SessionClosing -= OnClosing;
                sessionManager.SessionActivated -= OnActivated;
                roleManager.RoleConfigurationChanged -= OnRolesChanged;
                provider.DetachServer(server);
            };
            m_dataChannelAuthorizationTimer = TimeProvider.CreateTimer(
                _ => _ = provider.ObserveRecheckAsync(),
                null,
                provider.Options.AuthorizationRecheckInterval,
                provider.Options.AuthorizationRecheckInterval);
        }

        /// <summary>
        /// Detaches the original publishers exactly once, including after manager properties were cleared.
        /// Carrier shutdown, not Session shutdown, owns configured context closure.
        /// </summary>
        private void ShutdownDataChannelServices()
        {
            foreach (IDisposable endpoint in m_dataChannelEndpoints)
            {
                endpoint.Dispose();
            }
            m_dataChannelEndpoints.Clear();
            m_dataChannelAuthorizationTimer?.Dispose();
            m_dataChannelAuthorizationTimer = null;
            Interlocked.Exchange(ref m_detachDataChannelHandlers, null)?.Invoke();
        }

        private ServerDataChannelProvider? m_dataChannelProvider;
        private DataChannelNodeManagerFactory? m_dataChannelModelFactory;
        private bool m_enableDataChannelModel;
        private ITimer? m_dataChannelAuthorizationTimer;
        private Action? m_detachDataChannelHandlers;
        private readonly List<IDisposable> m_dataChannelEndpoints = [];
    }

    /// <summary>
    /// Source-generated endpoint cleanup diagnostics which preserve the original startup error.
    /// </summary>
    internal static partial class StandardServerDataChannelLog
    {
        /// <summary>
        /// Reports an isolated rollback failure.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.DataChannels + 3, Level = LogLevel.Warning,
            Message = "Data channel endpoint rollback failed.")]
        internal static partial void DataChannelEndpointCleanupFailed(this ILogger logger, Exception exception);
    }
}
