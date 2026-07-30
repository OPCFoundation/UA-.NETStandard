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
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.WebSockets;
using System.Collections.Concurrent;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Opc.Ua.Security.Certificates;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;

#if NETSTANDARD2_1 || NET472_OR_GREATER || NET5_0_OR_GREATER
using System.Security.Cryptography;
#endif

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Creates a new <see cref="HttpsTransportListener"/> with
    /// <see cref="ITransportListener"/> interface.
    /// </summary>
    public class HttpsTransportListenerFactory : HttpsServiceHost
    {
        /// <summary>
        /// The protocol supported by the listener.
        /// </summary>
        public override string UriScheme => Utils.UriSchemeHttps;

        /// <inheritdoc/>
        protected override string? JsonTransportProfileUri => Profiles.HttpsJsonTransport;

        /// <inheritdoc/>
        protected override string? OpenApiTransportProfileUri => Profiles.HttpsOpenApiTransport;

        /// <summary>
        /// The method creates a new instance of a <see cref="HttpsTransportListener"/>.
        /// </summary>
        /// <returns>The transport listener.</returns>
        public override ITransportListener Create(ITelemetryContext telemetry)
        {
            return new HttpsTransportListener(
                Utils.UriSchemeHttps,
                telemetry,
                [.. StartupContributors],
                BufferManagerFactory);
        }
    }

    /// <summary>
    /// Creates a new <see cref="HttpsTransportListener"/> with
    /// <see cref="ITransportListener"/> interface.
    /// </summary>
    public class OpcHttpsTransportListenerFactory : HttpsServiceHost
    {
        /// <summary>
        /// The protocol supported by the listener.
        /// </summary>
        public override string UriScheme => Utils.UriSchemeOpcHttps;

        /// <inheritdoc/>
        protected override string? JsonTransportProfileUri => Profiles.HttpsJsonTransport;

        /// <inheritdoc/>
        protected override string? OpenApiTransportProfileUri => Profiles.HttpsOpenApiTransport;

        /// <summary>
        /// The method creates a new instance of a <see cref="HttpsTransportListener"/>.
        /// </summary>
        /// <returns>The transport listener.</returns>
        public override ITransportListener Create(ITelemetryContext telemetry)
        {
            return new HttpsTransportListener(
                Utils.UriSchemeOpcHttps,
                telemetry,
                [.. StartupContributors],
                BufferManagerFactory);
        }
    }

    /// <summary>
    /// Creates a new <see cref="HttpsTransportListener"/> bound to the
    /// standard <c>wss://</c> URL scheme for the
    /// <see cref="Profiles.UaWssTransport"/> profile.
    /// </summary>
    public class WssTransportListenerFactory : HttpsServiceHost
    {
        /// <inheritdoc/>
        public override string UriScheme => Utils.UriSchemeWss;

        /// <inheritdoc/>
        protected override string TransportProfileUri => Profiles.UaWssTransport;

        /// <inheritdoc/>
        protected override string? JsonTransportProfileUri => Profiles.UaWssJsonTransport;

        /// <inheritdoc/>
        protected override string? OpenApiTransportProfileUri => Profiles.WssOpenApiTransport;

        /// <inheritdoc/>
        public override ITransportListener Create(ITelemetryContext telemetry)
        {
            return new HttpsTransportListener(
                Utils.UriSchemeWss,
                telemetry,
                [.. StartupContributors],
                BufferManagerFactory);
        }
    }

    /// <summary>
    /// Creates a new <see cref="HttpsTransportListener"/> bound to the
    /// OPC UA <c>opc.wss://</c> URL scheme alias for the
    /// <see cref="Profiles.UaWssTransport"/> profile.
    /// </summary>
    public class OpcWssTransportListenerFactory : HttpsServiceHost
    {
        /// <inheritdoc/>
        public override string UriScheme => Utils.UriSchemeOpcWss;

        /// <inheritdoc/>
        protected override string TransportProfileUri => Profiles.UaWssTransport;

        /// <inheritdoc/>
        protected override string? JsonTransportProfileUri => Profiles.UaWssJsonTransport;

        /// <inheritdoc/>
        protected override string? OpenApiTransportProfileUri => Profiles.WssOpenApiTransport;

        /// <inheritdoc/>
        public override ITransportListener Create(ITelemetryContext telemetry)
        {
            return new HttpsTransportListener(
                Utils.UriSchemeOpcWss,
                telemetry,
                [.. StartupContributors],
                BufferManagerFactory);
        }
    }

    /// <summary>
    /// Implements the kestrel startup of the Https listener.
    /// </summary>
    public class Startup
    {
        private const string kHttpsContentType = "text/plain";

        /// <summary>
        /// Configure the request pipeline for the listener.
        /// The <paramref name="listener"/> is resolved from the web host's DI container,
        /// which is populated via <c>IWebHostBuilder.ConfigureServices</c> in
        /// <see cref="HttpsTransportListener.ConfigureWebHost"/>.
        /// Using method injection here (rather than constructor injection) ensures the
        /// instance is resolved from the web-host app container on all target frameworks,
        /// including .NET 8+ where <c>HostBuilder</c> activates <c>Startup</c> from a
        /// separate host-level container that does not include web-host app services.
        /// </summary>
        /// <param name="appBuilder">The application builder.</param>
        /// <param name="listener">The transport listener that handles OPC UA requests.</param>
        public void Configure(IApplicationBuilder appBuilder, HttpsTransportListener listener)
        {
            // Enable WebSocket upgrades so the WSS handler added in p2-wss-listener-handler
            // can accept opcua+uacp / opcua+uajson sub-protocols.
            appBuilder.UseWebSockets();

            // Invoke companion-binding startup contributors (e.g. the
            // REST MVC pipeline from Opc.Ua.Bindings.WebApi) so their
            // middleware mounts inside this Kestrel host. Unmatched
            // requests fall through to the terminal binary / JSON
            // dispatcher.
            foreach (IHttpsListenerStartupContributor contributor in listener.StartupContributors)
            {
                contributor.Configure(appBuilder, listener);
            }

            appBuilder.Run(context => DispatchListenerRequestAsync(listener, context));
        }

        /// <summary>
        /// Dispatch logic shared between the single-listener
        /// <see cref="Configure"/> pipeline and the shared-host
        /// <see cref="SharedHostStartup"/> path-router. Routes the
        /// <paramref name="context"/> to the right listener handler based
        /// on whether it is a WebSocket upgrade, HTTP POST with
        /// <c>application/opcua+uajson</c>, or HTTP POST with the binary
        /// content-type.
        /// </summary>
        /// <param name="listener">The listener that owns the endpoint path.</param>
        /// <param name="context">The current HTTP context.</param>
        internal static Task DispatchListenerRequestAsync(
            HttpsTransportListener listener,
            HttpContext context)
        {
            // 1) WSS opcua+uacp / opcua+uajson upgrade (Part 6 §7.5.2)
            if (context.WebSockets.IsWebSocketRequest)
            {
                return listener.AcceptWebSocketAsync(context);
            }

            // 2) HTTP POST is the only verb defined for HTTPS binary / JSON
            //    (Part 6 §7.4.2 + §7.4.5).
            if (context.Request.Method != "POST")
            {
                context.Response.ContentLength = 0;
                context.Response.ContentType = kHttpsContentType;
                context.Response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
                return context.Response.WriteAsync(string.Empty);
            }

            // 3) Dispatch on Content-Type: 'application/opcua+uajson' is the
            //    HTTPS-JSON sub-protocol (Part 6 §7.4.5); anything else is
            //    treated as binary by SendBinaryAsync (which itself enforces
            //    the 'application/octet-stream' contract per Part 6 §7.4.4).
            string? contentType = context.Request.ContentType;
            if (!string.IsNullOrEmpty(contentType) &&
                contentType!.StartsWith(
                    Profiles.OpcUaJsonContentType,
                    StringComparison.OrdinalIgnoreCase))
            {
                return listener.SendJsonAsync(context);
            }

            return listener.SendBinaryAsync(context);
        }
    }

    /// <summary>
    /// Startup type used by <see cref="SharedKestrelHost"/>. Routes
    /// incoming requests to the listener whose
    /// <see cref="HttpsTransportListener.EndpointUrl"/> absolute path is
    /// the longest prefix of <see cref="HttpRequest.Path"/> and then
    /// hands off to <see cref="Startup.DispatchListenerRequestAsync"/>.
    /// </summary>
    internal class SharedHostStartup
    {
        private readonly ConcurrentDictionary<HttpsTransportListener, RequestDelegate> m_listenerPipelines
            = new();

        /// <summary>
        /// Configures the shared host's request pipeline.
        /// </summary>
        /// <param name="appBuilder">The application builder.</param>
        /// <param name="accessor">
        /// Late-bound accessor that resolves to the
        /// <see cref="SharedKestrelHost"/> serving this Kestrel host.
        /// Set by <see cref="SharedKestrelHostRegistry.AcquireAsync"/> before
        /// the host is started.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="accessor"/> is <c>null</c>.</exception>
        public void Configure(IApplicationBuilder appBuilder, SharedHostAccessor accessor)
        {
            if (accessor == null)
            {
                throw new ArgumentNullException(nameof(accessor));
            }
            appBuilder.UseWebSockets();
            appBuilder.Run(context =>
            {
                SharedKestrelHost? sharedHost = accessor.Instance;
                HttpsTransportListener? listener = sharedHost?.RouteByPath(context.Request.Path);
                if (listener == null)
                {
                    context.Response.ContentLength = 0;
                    context.Response.ContentType = "text/plain";
                    context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                    return context.Response.WriteAsync(string.Empty);
                }
                RequestDelegate pipeline = m_listenerPipelines.GetOrAdd(
                    listener,
                    value => BuildListenerPipeline(appBuilder, value));
                return pipeline(context);
            });
        }

        private static RequestDelegate BuildListenerPipeline(
            IApplicationBuilder appBuilder,
            HttpsTransportListener listener)
        {
            IApplicationBuilder branch = appBuilder.New();
            foreach (IHttpsListenerStartupContributor contributor in listener.StartupContributors)
            {
                contributor.Configure(branch, listener);
            }
            branch.Run(context => Startup.DispatchListenerRequestAsync(listener, context));
            return branch.Build();
        }
    }

    /// <summary>
    /// Manages the connections for a UA HTTPS server.
    /// </summary>
    public class HttpsTransportListener : ITransportListener, ITransportListenerCertificateRotation
    {
        private const string kHttpsContentType = "text/plain";
        private const string kApplicationContentType = "application/octet-stream";
        private const string kAuthorizationKey = "Authorization";
        private const string kBearerKey = "Bearer";

        /// <summary>
        /// Initializes a new instance of the <see cref="HttpsTransportListener"/> class.
        /// </summary>
        public HttpsTransportListener(string uriScheme, ITelemetryContext telemetry)
            : this(
                uriScheme,
                telemetry,
                startupContributors: [],
                DefaultBufferManagerFactory.Instance)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="HttpsTransportListener"/>
        /// class with the supplied startup contributors already applied, so the
        /// owning factory can construct and return the listener in a single
        /// expression (transferring ownership to the caller without an
        /// intermediate, undisposed local).
        /// </summary>
        internal HttpsTransportListener(
            string uriScheme,
            ITelemetryContext telemetry,
            IReadOnlyList<IHttpsListenerStartupContributor> startupContributors)
            : this(
                uriScheme,
                telemetry,
                startupContributors,
                DefaultBufferManagerFactory.Instance)
        {
        }

        /// <summary>
        /// Initializes a listener with startup contributors and a buffer-manager factory.
        /// </summary>
        internal HttpsTransportListener(
            string uriScheme,
            ITelemetryContext telemetry,
            IReadOnlyList<IHttpsListenerStartupContributor> startupContributors,
            IBufferManagerFactory bufferManagerFactory)
        {
            UriScheme = uriScheme;
            m_telemetry = telemetry;
            m_logger = telemetry.CreateLogger<HttpsTransportListener>();
            StartupContributors = startupContributors;
            m_bufferManagerFactory = bufferManagerFactory ??
                throw new ArgumentNullException(nameof(bufferManagerFactory));
        }

        /// <summary>
        /// Frees any unmanaged resources.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            await DisposeAsyncCore().ConfigureAwait(false);
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Asynchronously releases the shared-host lease and shuts down the
        /// owned Kestrel host before the synchronous cleanup runs.
        /// </summary>
        protected virtual async ValueTask DisposeAsyncCore()
        {
            ConnectionStatusChanged = null;
            ConnectionWaiting = null;

            // Drain outbound reverse-connect channels first so the
            // ServerCertificateChain handles loaded during the asymmetric
            // ChannelOpen handshake are released before m_pinnedServerCert.
            // Snapshot the set under the concurrent dictionary's enumerator
            // contract; subsequent OnReverseConnectChannelStatusChanged
            // callbacks against disposed channels are no-ops because the
            // dictionary has been cleared.
            TcpServerChannel[] reverseChannels = [.. m_reverseConnectChannels.Keys];
            m_reverseConnectChannels.Clear();
            foreach (TcpServerChannel channel in reverseChannels)
            {
                try
                {
                    channel.Dispose();
                }
                catch
                {
                    // best-effort; teardown must continue regardless.
                }
            }

            SharedHostLease? lease = m_sharedHostLease;
            m_sharedHostLease = null;
            if (lease != null)
            {
                await lease.DisposeAsync().ConfigureAwait(false);
            }

#if NET8_0_OR_GREATER
            IHost? host = m_host;
#else
            IWebHost? host = m_host;
#endif
            m_host = null;
            if (host != null)
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await host.StopAsync(cts.Token).ConfigureAwait(false);
                }
                catch
                {
                    // Best-effort shutdown.
                }
                host.Dispose();
            }
        }

        /// <summary>
        /// An overrideable version of the Dispose. Unmanaged-resource
        /// cleanup only; the async path (<see cref="DisposeAsyncCore"/>)
        /// handles the shared-host lease and the owned Kestrel host.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                m_pinnedServerCertX509?.Dispose();
                m_pinnedServerCertX509 = null;
                m_pinnedServerCert?.Dispose();
                m_pinnedServerCert = null;
            }
        }

        /// <inheritdoc/>
        public string UriScheme { get; }

        /// <inheritdoc/>
        public string ListenerId { get; private set; } = default!;

        internal byte[] ServerChannelCertificate { get; set; } = [];

        /// <summary>
        /// Per-listener collection of startup contributors propagated from
        /// the originating <see cref="HttpsServiceHost"/>. Invoked by
        /// <see cref="Startup.Configure(IApplicationBuilder, HttpsTransportListener)"/>
        /// between <c>UseWebSockets()</c> and the terminal binary / JSON
        /// dispatcher so additional middleware (e.g. REST MVC) can mount
        /// into the same Kestrel host.
        /// </summary>
        internal IReadOnlyList<IHttpsListenerStartupContributor> StartupContributors { get; set; }
            = [];

        /// <summary>
        /// The transport callback wired in by
        /// <see cref="OpenAsync(Uri, TransportListenerSettings, ITransportListenerCallback, CancellationToken)"/>.
        /// Exposed to <see cref="IHttpsListenerStartupContributor"/>
        /// implementations so they can forward requests through the same
        /// dispatcher used by the binary / JSON paths.
        /// </summary>
        internal ITransportListenerCallback? Callback => m_callback;

        /// <summary>
        /// The encoding context (namespace / server tables, quotas,
        /// telemetry) populated by
        /// <see cref="OpenAsync(Uri, TransportListenerSettings, ITransportListenerCallback, CancellationToken)"/>.
        /// Available to <see cref="IHttpsListenerStartupContributor"/>
        /// implementations after the listener is opened.
        /// </summary>
        internal IServiceMessageContext? MessageContext => m_quotas?.MessageContext;

        /// <summary>
        /// The endpoint descriptions advertised by this listener,
        /// populated by
        /// <see cref="OpenAsync(Uri, TransportListenerSettings, ITransportListenerCallback, CancellationToken)"/>.
        /// Available to <see cref="IHttpsListenerStartupContributor"/>
        /// implementations so they can pick a default endpoint
        /// (typically the first <see cref="MessageSecurityMode.None"/>
        /// HTTPS entry) for context construction.
        /// </summary>
        internal IReadOnlyList<EndpointDescription>? Descriptions => m_descriptions;

        /// <summary>
        /// Optional WSS <c>opcua+openapi+&lt;accesstoken&gt;</c> bearer-
        /// token validator. Registered by
        /// <see cref="IHttpsListenerStartupContributor"/> implementations
        /// (e.g. the WebApi contributor) so the listener can validate
        /// the bearer token presented in the WebSocket sub-protocol name
        /// against the application's auth scheme (JwtBearer) before
        /// accepting the upgrade. The callback receives the
        /// <see cref="HttpContext"/> and the raw access token; it must
        /// return <c>true</c> if the token authenticates, <c>false</c>
        /// otherwise. When no validator is registered the listener
        /// fail-closed rejects every bearer-prefix sub-protocol upgrade
        /// (so unconfigured deployments cannot silently accept tokens).
        /// </summary>
        internal Func<HttpContext, string, Task<bool>>? WssBearerTokenValidator { get; set; }

        /// <summary>
        /// Opens the listener and starts accepting connection.
        /// </summary>
        /// <param name="baseAddress">The base address.</param>
        /// <param name="settings">The settings to use when creating the listener.</param>
        /// <param name="callback">The callback to use when requests arrive via the channel.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <exception cref="ArgumentNullException">Thrown if any parameter is null.</exception>
        /// <exception cref="ServiceResultException">Thrown if any communication error occurs.</exception>
        public async ValueTask OpenAsync(
            Uri baseAddress,
            TransportListenerSettings settings,
            ITransportListenerCallback callback,
            CancellationToken ct = default)
        {
            // assign a unique guid to the listener.
            ListenerId = Guid.NewGuid().ToString();

            EndpointUrl = baseAddress;
            m_descriptions = settings.Descriptions ?? [];
            EndpointConfiguration configuration = settings.Configuration ?? EndpointConfiguration.Create();
            IEncodeableFactory factory = settings.Factory ?? ServiceMessageContext.Create(m_telemetry).Factory;
            NamespaceTable namespaceUris = settings.NamespaceUris ?? new NamespaceTable();

            // initialize the quotas.
            m_quotas = new ChannelQuotas(new ServiceMessageContext(m_telemetry, factory)
            {
                MaxArrayLength = configuration.MaxArrayLength,
                MaxByteStringLength = configuration.MaxByteStringLength,
                MaxMessageSize = configuration.MaxMessageSize,
                MaxStringLength = configuration.MaxStringLength,
                MaxEncodingNestingLevels = configuration.MaxEncodingNestingLevels,
                MaxDecoderRecoveries = configuration.MaxDecoderRecoveries,
                NamespaceUris = namespaceUris,
                ServerUris = new StringTable()
            })
            {
                MaxBufferSize = configuration.MaxBufferSize,
                MaxMessageSize = configuration.MaxMessageSize,
                ChannelLifetime = configuration.ChannelLifetime,
                SecurityTokenLifetime = configuration.SecurityTokenLifetime,
                CertificateValidator = settings.CertificateValidator
            };

            // save the callback to the server.
            m_callback = callback!;

            m_serverCertProvider = settings.ServerCertificates!;

            m_mutualTlsEnabled = settings.HttpsMutualTls;

            // reverse-connect listener mode dispatches accepted WSS
            // upgrades to TcpReverseConnectChannels and fires the
            // listener's ConnectionWaiting event when the server's
            // ReverseHello arrives.
            m_reverseConnectListener = settings.ReverseConnectListener;

            // buffer manager used by the WSS path to rent send / receive chunks.
            m_bufferManager = new BufferManager(
                m_bufferManagerFactory.Create(
                    "HttpsListener",
                    m_quotas.MaxBufferSize,
                    m_telemetry));

            // start the listener
            await StartAsync(ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Closes the listener and stops accepting connection.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        /// <exception cref="ServiceResultException">Thrown if any communication error occurs.</exception>
        public async ValueTask CloseAsync(CancellationToken ct = default)
        {
            await StopAsync(ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Raised when a new connection is waiting for a client. Fired by
        /// the WSS reverse-connect handoff in
        /// <see cref="HttpsTransportListener"/> when
        /// <c>settings.ReverseConnectListener</c> is set.
        /// </summary>
        public event ConnectionWaitingHandlerAsync? ConnectionWaiting;

        /// <summary>
        /// Raised when a monitored connection's status changed.
        /// </summary>
#pragma warning disable CS0067 // forward-mode listener does not currently report channel-status events.
        public event EventHandler<ConnectionStatusEventArgs>? ConnectionStatusChanged;
#pragma warning restore CS0067

        /// <inheritdoc/>
        /// <remarks>
        /// Reverse connect is supported for the WSS scheme variants
        /// (<c>opc.wss://</c>, <c>wss://</c>) only. Outbound HTTPS-binary
        /// reverse-connect is not defined by Part 6.
        /// </remarks>
        public void CreateReverseConnection(Uri url, int timeout)
        {
            if (url == null)
            {
                throw new ArgumentNullException(nameof(url));
            }
            if (!Utils.IsUriWssScheme(url.AbsoluteUri))
            {
                throw new NotImplementedException(
                    "Reverse connect is only implemented for the WSS transport variants.");
            }
            if (m_bufferManager == null || m_quotas == null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidState,
                    "HttpsTransportListener must be opened before initiating a reverse connection.");
            }

#pragma warning disable CA2000 // Ownership of channel + transport transfers to async callback via BeginReverseConnect
            TcpServerChannel? channel = null;
            WebSocketClientByteTransport? transport = null;
            try
            {
                channel = new TcpServerChannel(
                    ListenerId,
                    new ReverseConnectChannelOwner(this),
                    m_bufferManager,
                    m_quotas,
                    m_serverCertProvider,
                    m_descriptions,
                    m_telemetry);

                transport = new WebSocketClientByteTransport(
                    m_bufferManager,
                    m_quotas.MaxBufferSize,
                    m_telemetry)
                {
                    CertificateValidator = m_quotas.CertificateValidator,
                    ClientTlsCertificate = m_pinnedServerCert
                };

                uint channelId = (uint)Interlocked.Increment(ref m_nextChannelId);
                channel.StatusChanged += OnReverseConnectChannelStatusChanged;
                m_reverseConnectChannels.TryAdd(channel, 0);
                channel.BeginReverseConnect(
                    channelId,
                    url,
                    transport,
                    OnHttpsReverseHelloComplete,
                    channel,
                    Math.Min(timeout, m_quotas.ChannelLifetime));
                channel = null; // ownership transferred to async operation
                transport = null; // owned by the channel now
            }
            finally
            {
                channel?.Dispose();
                transport?.Close();
            }
#pragma warning restore CA2000
        }

        private void OnReverseConnectChannelStatusChanged(
            TcpServerChannel channel,
            ServiceResult status,
            bool closed)
        {
            if (closed && m_reverseConnectChannels.TryRemove(channel, out _))
            {
                // The natural-close path (transport tore down independent
                // of listener teardown) is the only opportunity to dispose
                // the channel because there's no other owner: the listener
                // doesn't hold a strong reference outside the tracking set,
                // and SetRequestReceivedCallback wires the channel into the
                // request pipeline but never disposes it. Releases the
                // ServerCertificateChain handles loaded during the
                // asymmetric ChannelOpen.
                try
                {
                    channel.Dispose();
                }
                catch
                {
                    // best-effort.
                }
            }
            ConnectionStatusChanged?.Invoke(
                this,
                new ConnectionStatusEventArgs(channel.ReverseConnectionUrl!, status, closed));
        }

        /// <summary>
        /// Completion callback for the WSS reverse-hello handshake.
        /// </summary>
        private void OnHttpsReverseHelloComplete(IAsyncResult result)
        {
            var channel = (TcpServerChannel?)result.AsyncState;
            try
            {
                channel!.EndReverseConnect(result);
                if (m_callback != null)
                {
                    channel.SetRequestReceivedCallback(
                        new TcpChannelRequestEventHandler(OnRequestReceivedAsync));
                    channel.SetReportOpenSecureChannelAuditCallback(
                        new ReportAuditOpenSecureChannelEventHandler(
                            OnReportAuditOpenSecureChannelEvent));
                    channel.SetReportCloseSecureChannelAuditCallback(
                        new ReportAuditCloseSecureChannelEventHandler(
                            OnReportAuditCloseSecureChannelEvent));
                    channel.SetReportCertificateAuditCallback(
                        new ReportAuditCertificateEventHandler(OnReportAuditCertificateEvent));
                }
                channel = null; // ownership transferred to the channel registry
            }
            catch (Exception e)
            {
                m_logger.ReverseConnectHandshakeFailed(e);
                ConnectionStatusChanged?.Invoke(
                    this,
                    new ConnectionStatusEventArgs(
                        channel!.ReverseConnectionUrl!,
                        new ServiceResult(e),
                        true));
            }
            finally
            {
                if (channel != null)
                {
                    m_reverseConnectChannels.TryRemove(channel, out _);
                }
                channel?.Dispose();
            }
        }

        /// <summary>
        /// Minimal <see cref="ITcpChannelListener"/> adapter that the WSS
        /// reverse-connect uses to give the <see cref="TcpServerChannel"/>
        /// a back-reference to this listener without going through the
        /// shared multi-channel <see cref="WssChannelListener"/> path.
        /// </summary>
        private sealed class ReverseConnectChannelOwner : ITcpChannelListener
        {
            internal ReverseConnectChannelOwner(HttpsTransportListener owner)
            {
                m_owner = owner;
            }

            public Uri EndpointUrl => m_owner.EndpointUrl;

            public void ChannelClosed(uint channelId)
            {
                // No-op: the WSS reverse channel is per-connection; the
                // listener does not maintain a channel registry for it.
            }

            public bool ReconnectToExistingChannel(
                IUaSCByteTransport transport,
                uint requestId,
                uint sequenceNumber,
                uint channelId,
                Certificate clientCertificate,
                ChannelToken token,
                OpenSecureChannelRequest request)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadTcpSecureChannelUnknown,
                    "Reverse-connect WSS channels do not support reconnect-to-existing-channel.");
            }

#pragma warning disable CS0618 // Type or member is obsolete
            public Task<bool> TransferListenerChannel(uint channelId, string serverUri, Uri endpointUrl)
            {
                return TransferListenerChannelAsync(channelId, serverUri, endpointUrl);
            }
#pragma warning restore CS0618

            public Task<bool> TransferListenerChannelAsync(uint channelId, string serverUri, Uri endpointUrl)
            {
                return Task.FromResult(false);
            }

            private readonly HttpsTransportListener m_owner;
        }

        /// <inheritdoc/>
        public void UpdateChannelLastActiveTime(string globalChannelId)
        {
            // intentionally not implemented
        }

        /// <summary>
        /// Gets the URL for the listener's endpoint.
        /// </summary>
        /// <value>The URL for the listener's endpoint.</value>
        public Uri EndpointUrl { get; private set; } = null!;

        /// <summary>
        /// Starts listening at the specified port.
        /// </summary>
        public async ValueTask StartAsync(CancellationToken ct = default)
        {
            // 1) Prepare the TLS certificate up front so the registry can
            //    key on its thumbprint when matching shared hosts.
            PrepareTlsCertificate();

            // 2) Try to share a Kestrel host with any other listener bound
            //    to the same (host, port). Random-port listeners (Port == 0)
            //    cannot meaningfully share - the OS allocates the port at
            //    bind time so the key would change every Start.
            if (EndpointUrl.Port != 0 && m_pinnedServerCertX509 != null)
            {
                var key = new SharedHostKey(EndpointUrl.Host, EndpointUrl.Port);
                string thumbprint = m_pinnedServerCertX509.Thumbprint;
                try
                {
                    m_sharedHostLease = await SharedKestrelHostRegistry.Instance.AcquireAsync(
                        key,
                        this,
                        EndpointUrl.AbsolutePath,
                        BuildSharedHostInstance,
                        thumbprint,
                        ct).ConfigureAwait(false);
                    return;
                }
                catch (InvalidOperationException ex)
                {
                    m_logger.CannotShareKestrelHost(ex, key);
                    // fall through to legacy own-host path below
                }
            }

            await StartOwnHostAsync(ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Builds a Kestrel <see cref="IHost"/> configured as the
        /// SHARED host for the (host, port) the listener is bound to.
        /// Used by <see cref="SharedKestrelHostRegistry"/> via the
        /// <c>hostFactory</c> callback when no existing host is found
        /// for the key.
        /// </summary>
        private IHost BuildSharedHostInstance(SharedHostAccessor accessor)
        {
#if NET8_0_OR_GREATER
            return new HostBuilder()
                .ConfigureWebHostDefaults(builder => ConfigureSharedWebHost(builder, accessor))
                .Build();
#else
            // Legacy WebHostBuilder.Start() can't be split into Build+Start; use
            // Build() on the IWebHost equivalent and start in AttachAndStart.
            var sharedHostBuilder = new WebHostBuilder();
            ConfigureSharedWebHost(sharedHostBuilder, accessor);
            IWebHost webHost = sharedHostBuilder.UseUrls(Utils.ReplaceLocalhost(EndpointUrl.ToString())).Build();
            return new WebHostAsIHost(webHost);
#endif
        }

#if !NET8_0_OR_GREATER
        /// <summary>
        /// Adapts a legacy <see cref="IWebHost"/> to the
        /// <see cref="IHost"/> contract used by the shared-host registry.
        /// </summary>
        private sealed class WebHostAsIHost : IHost
        {
            private readonly IWebHost m_webHost;

            public WebHostAsIHost(IWebHost webHost)
            {
                m_webHost = webHost;
            }

            public IServiceProvider Services => m_webHost.Services;

            public Task StartAsync(CancellationToken ct = default)
            {
                return m_webHost.StartAsync(ct);
            }

            public Task StopAsync(CancellationToken ct = default)
            {
                return m_webHost.StopAsync(ct);
            }

            public void Dispose()
            {
                m_webHost.Dispose();
            }
        }
#endif

        /// <summary>
        /// Configures the underlying <see cref="IWebHostBuilder"/> for a
        /// SHARED host (uses <see cref="SharedHostStartup"/> + path-prefix
        /// routing). The TLS cert + Kestrel listen-options are taken from
        /// this listener's pinned state, established by
        /// <see cref="PrepareTlsCertificate"/>.
        /// </summary>
#pragma warning disable CA1859 // see ConfigureWebHost rationale
        private void ConfigureSharedWebHost(IWebHostBuilder webHostBuilder, SharedHostAccessor accessor)
#pragma warning restore CA1859
        {
            var httpsOptions = new HttpsConnectionAdapterOptions
            {
                // TLS-layer revocation is intentionally disabled: certificate
                // revocation (CRL) is enforced by the UA CertificateValidator in
                // ValidateClientCertificate, consistent with the raw-TCP UA
                // transport, to avoid duplicate / inconsistent checks.
                CheckCertificateRevocation = false,
                // HttpsMutualTls=true enables mTLS — Kestrel REQUESTS but does
                // not REQUIRE a client cert at the TLS handshake. Requiring the
                // cert at TLS time would block the discovery client (which
                // legitimately has no cert until after discovery) and the binary
                // UASC HTTPS path (which authenticates at the UA SecureChannel
                // layer, not at TLS). REST / WebApi clients that opt into
                // AddWebApiMutualTlsAuth() are enforced at the authorization
                // layer (RequireAuthorization) instead.
                ClientCertificateMode = m_mutualTlsEnabled
                    ? ClientCertificateMode.AllowCertificate
                    : ClientCertificateMode.NoCertificate,
                ServerCertificate = m_pinnedServerCertX509,
                ClientCertificateValidation = ValidateClientCertificate,
                SslProtocols = SslProtocols.None
            };

            UriHostNameType hostType = Uri.CheckHostName(EndpointUrl.Host);
            if (hostType is UriHostNameType.Dns or UriHostNameType.Unknown or UriHostNameType.Basic)
            {
                webHostBuilder.UseKestrel(options =>
                    options.ListenAnyIP(
                        EndpointUrl.Port,
                        listenOptions => listenOptions.UseHttps(httpsOptions)));
            }
            else
            {
                var ipAddress = IPAddress.Parse(EndpointUrl.Host);
                webHostBuilder.UseKestrel(options =>
                    options.Listen(
                        ipAddress,
                        EndpointUrl.Port,
                        listenOptions => listenOptions.UseHttps(httpsOptions)));
            }

            webHostBuilder.UseContentRoot(Directory.GetCurrentDirectory())
                .ConfigureServices(services =>
            {
                services.AddSingleton(accessor);
                ConfigureContributorServices(services);
            });
            webHostBuilder.UseStartup<SharedHostStartup>();
        }

        private async ValueTask StartOwnHostAsync(CancellationToken ct)
        {
#if NET8_0_OR_GREATER
            m_host = new HostBuilder()
                .ConfigureWebHostDefaults(ConfigureWebHost)
                .Build();
            await m_host.StartAsync(ct).ConfigureAwait(false);
#else
            var hostBuilder = new WebHostBuilder();
            ConfigureWebHost(hostBuilder);
            m_host = hostBuilder.Start(Utils.ReplaceLocalhost(EndpointUrl.ToString()));
            await Task.CompletedTask.ConfigureAwait(false);
#endif
        }

        /// <summary>
        /// Resolves and pins the TLS certificate the listener will use.
        /// Sets <c>m_pinnedServerCert</c>, <c>m_pinnedServerCertX509</c>
        /// and <see cref="ServerChannelCertificate"/>; safe to call
        /// before either the shared-host or own-host path runs.
        /// </summary>
        private void PrepareTlsCertificate()
        {
            // prepare the server TLS certificate. AcquireApplicationCertificateBySecurityPolicy
            // returns a caller-owned entry; take an independent handle on the
            // certificate so this listener owns it for its full lifetime,
            // independent of the entry (disposed below) and of any concurrent
            // registry hot-update that would otherwise free the OS handle
            // Kestrel still holds.
            using CertificateEntry? instanceEntry = m_serverCertProvider
                .AcquireApplicationCertificateBySecurityPolicy(SecurityPolicies.Https);
            Certificate? serverCertificate = instanceEntry?.Certificate?.AddRef();
#if NETSTANDARD2_1 || NET472_OR_GREATER || NET5_0_OR_GREATER
            try
            {
                // Create a copy of the certificate with the private key on platforms
                // which default to the ephemeral KeySet. Also a new certificate must be reloaded.
                // If the key fails to copy, its probably a non exportable key from the X509Store.
                // Then we can use the original certificate, the private key is already in the key store.
                using Certificate copy = X509Utils.CreateCopyWithPrivateKey(serverCertificate!, false);
                if (!ReferenceEquals(copy, serverCertificate))
                {
                    serverCertificate!.Dispose();
                    // Take an owned handle over the copy's core; the 'using
                    // copy' handle is released at block end, so capturing
                    // AddRef()'s result (rather than aliasing 'copy' and
                    // discarding the AddRef) keeps the core alive without
                    // leaking a handle.
                    serverCertificate = copy.AddRef();
                }
            }
            catch (CryptographicException ce)
            {
                m_logger.PrivateKeyCopyDenied(ce.Message);
            }
#endif
            // pin the cert for the lifetime of the listener so that the
            // OS-level private key handle backing the Kestrel-held
            // X509Certificate2 cannot be invalidated by a concurrent cert
            // reload elsewhere in the process.
            m_pinnedServerCert?.Dispose();
            m_pinnedServerCert = serverCertificate;
            m_pinnedServerCertX509?.Dispose();
            m_pinnedServerCertX509 = serverCertificate!.AsX509Certificate2();

            // save the server certificate so it can be used in the secure channel context.
            ServerChannelCertificate = serverCertificate!.RawData;
        }

        /// <summary>
        /// CA1859: The IWebHostBuilder interface cannot be narrowed to WebHostBuilder here because
        /// on NET8_0_OR_GREATER this method is called from HostBuilder.ConfigureWebHostDefaults()
        /// which passes an IWebHostBuilder, not WebHostBuilder.
        /// </summary>
        /// <param name="webHostBuilder"></param>
#pragma warning disable CA1859
        private void ConfigureWebHost(IWebHostBuilder webHostBuilder)
#pragma warning restore CA1859
        {
            // TLS cert was already pinned by PrepareTlsCertificate() at the
            // top of Start(); use the pinned cert directly.
            var httpsOptions = new HttpsConnectionAdapterOptions
            {
                // TLS-layer revocation is intentionally disabled: certificate
                // revocation (CRL) is enforced by the UA CertificateValidator in
                // ValidateClientCertificate, consistent with the raw-TCP UA
                // transport, to avoid duplicate / inconsistent checks.
                CheckCertificateRevocation = false,
                // HttpsMutualTls=true enables mTLS — Kestrel REQUESTS but does
                // not REQUIRE a client cert at the TLS handshake. Requiring the
                // cert at TLS time would block the discovery client (which
                // legitimately has no cert until after discovery) and the binary
                // UASC HTTPS path (which authenticates at the UA SecureChannel
                // layer, not at TLS). REST / WebApi clients that opt into
                // AddWebApiMutualTlsAuth() are enforced at the authorization
                // layer (RequireAuthorization) instead.
                ClientCertificateMode = m_mutualTlsEnabled
                    ? ClientCertificateMode.AllowCertificate
                    : ClientCertificateMode.NoCertificate,
                // note: this is the TLS certificate!
                ServerCertificate = m_pinnedServerCertX509,
                ClientCertificateValidation = ValidateClientCertificate,
                SslProtocols = SslProtocols.None
            };

            UriHostNameType hostType = Uri.CheckHostName(EndpointUrl.Host);
            if (hostType is UriHostNameType.Dns or UriHostNameType.Unknown or UriHostNameType.Basic)
            {
                // bind to any address
                webHostBuilder.UseKestrel(options =>
                    options.ListenAnyIP(
                        EndpointUrl.Port,
                        listenOptions => listenOptions.UseHttps(httpsOptions)));
            }
            else
            {
                // bind to specific address
                var ipAddress = IPAddress.Parse(EndpointUrl.Host);
                webHostBuilder.UseKestrel(options =>
                    options.Listen(
                        ipAddress,
                        EndpointUrl.Port,
                        listenOptions => listenOptions.UseHttps(httpsOptions)));
            }

            webHostBuilder.UseContentRoot(Directory.GetCurrentDirectory());
            // Register this listener instance in the web host DI container so it can be
            // injected into Startup.Configure as a method parameter — no static state needed.
            webHostBuilder.ConfigureServices(services =>
            {
                services.AddSingleton(this);
                ConfigureContributorServices(services);
            });
            webHostBuilder.UseStartup<Startup>();
        }

        private void ConfigureContributorServices(IServiceCollection services)
        {
            foreach (IHttpsListenerStartupContributor contributor in StartupContributors)
            {
                if (contributor is IHttpsListenerServiceContributor serviceContributor)
                {
                    serviceContributor.ConfigureServices(services, this);
                }
            }
        }

        /// <summary>
        /// Stops listening.
        /// </summary>
        public ValueTask StopAsync(CancellationToken ct = default)
        {
            return DisposeAsync();
        }

        /// <summary>
        /// Handles HTTPS POST requests carrying an OPC UA binary message
        /// (Part 6 §7.4.4 — <c>application/octet-stream</c>). Public for
        /// backwards compatibility with the previous <c>SendAsync</c> name;
        /// <see cref="SendAsync"/> remains as a thin synonym.
        /// </summary>
        public async Task SendBinaryAsync(HttpContext context)
        {
            string message = string.Empty;
            CancellationToken ct = context.RequestAborted;
            try
            {
                if (m_callback == null)
                {
                    await WriteResponseAsync(
                        context.Response,
                        message,
                        HttpStatusCode.NotImplemented)
                        .ConfigureAwait(false);
                    return;
                }

                if (context.Request.ContentType != kApplicationContentType)
                {
                    message = "HTTPSLISTENER - Unsupported content type.";
                    await WriteResponseAsync(context.Response, message, HttpStatusCode.BadRequest)
                        .ConfigureAwait(false);
                    return;
                }

                int length = (int)(context.Request.ContentLength ?? 0);
                int maxMessageSize = m_quotas.MessageContext.MaxMessageSize;
                if (maxMessageSize > 0 && length > maxMessageSize)
                {
                    message = "HTTPSLISTENER - Request body exceeds MaxMessageSize.";
                    await WriteResponseAsync(
                        context.Response,
                        message,
                        HttpStatusCode.RequestEntityTooLarge).ConfigureAwait(false);
                    return;
                }
                byte[] buffer = await ReadBodyAsync(context.Request, maxMessageSize, ct)
                    .ConfigureAwait(false);

                if (buffer.Length != length)
                {
                    message = "HTTPSLISTENER - Invalid buffer.";
                    await WriteResponseAsync(context.Response, message, HttpStatusCode.BadRequest)
                        .ConfigureAwait(false);
                    return;
                }

                IServiceRequest input = BinaryDecoder.DecodeMessage<IServiceRequest>(
                    buffer,
                    m_quotas.MessageContext);

                if (m_mutualTlsEnabled && input.TypeId == DataTypeIds.CreateSessionRequest)
                {
                    // Match tls client certificate against client application certificate provided in CreateSessionRequest
                    var tlsClientCertificate = ByteString.From(context.Connection.ClientCertificate?.RawData);
                    ByteString opcUaClientCertificate = ((CreateSessionRequest)input).ClientCertificate;

                    if (context.Connection.ClientCertificate?.RawData == null ||
                        tlsClientCertificate != opcUaClientCertificate)
                    {
                        message =
                            "Client TLS certificate does not match with ClientCertificate provided in CreateSessionRequest";
                        m_logger.ClientTlsCertificateMismatch(message);
                        await WriteResponseAsync(
                            context.Response,
                            message,
                            HttpStatusCode.Unauthorized)
                            .ConfigureAwait(false);
                        return;
                    }
                }

                // extract the JWT token from the HTTP headers.
                input.RequestHeader ??= new RequestHeader();

                if (input.RequestHeader.AuthenticationToken.IsNull &&
                    input.TypeId != DataTypeIds.CreateSessionRequest &&
                    context.Request.Headers.TryGetValue(
                        kAuthorizationKey,
                        out Microsoft.Extensions.Primitives.StringValues keys))
                {
                    foreach (string? value in keys)
                    {
                        if (value != null && value.StartsWith(kBearerKey, StringComparison.OrdinalIgnoreCase))
                        {
                            // note: use NodeId(string, uint) to avoid the NodeId.Parse call.
                            input.RequestHeader.AuthenticationToken = new NodeId(
                                value[(kBearerKey.Length + 1)..].Trim(),
                                0);
                        }
                    }
                }

                if (!context.Request.Headers.TryGetValue(
                        Profiles.HttpsSecurityPolicyHeader,
                        out Microsoft.Extensions.Primitives.StringValues header))
                {
                    header = SecurityPolicies.None;
                }

                EndpointDescription? endpoint = null;
                foreach (EndpointDescription ep in m_descriptions)
                {
                    if (Utils.IsUriHttpsScheme(ep.EndpointUrl!))
                    {
                        if (!string.IsNullOrEmpty(header) &&
                            !string.Equals(ep.SecurityPolicyUri, header, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        endpoint = ep;
                        break;
                    }
                }

                if (endpoint == null)
                {
                    ServiceResultException? serviceResultException = null;
                    if (input.TypeId != DataTypeIds.GetEndpointsRequest &&
                        input.TypeId != DataTypeIds.FindServersRequest &&
                        input.TypeId != DataTypeIds.FindServersOnNetworkRequest)
                    {
                        serviceResultException = new ServiceResultException(
                            StatusCodes.BadSecurityPolicyRejected,
                            "Channel can only be used for discovery.");
                    }
                    else if (length > TcpMessageLimits.DefaultDiscoveryMaxMessageSize)
                    {
                        serviceResultException = new ServiceResultException(
                            StatusCodes.BadSecurityPolicyRejected,
                            "Discovery Channel message size exceeded.");
                    }

                    if (serviceResultException != null)
                    {
                        IServiceResponse serviceResponse = EndpointBase.CreateFault(
                            m_logger,
                            null,
                            serviceResultException);
                        await WriteServiceResponseAsync(context, serviceResponse, ct)
                            .ConfigureAwait(false);
                        return;
                    }
                }

                var secureChannelContext = new SecureChannelContext(
                    ListenerId,
                    endpoint,
                    RequestEncoding.Binary,
                    context.Connection.ClientCertificate?.RawData,
                    ServerChannelCertificate);

                IServiceResponse output =
                    await m_callback.ProcessRequestAsync(
                        secureChannelContext,
                        input,
                        ct).ConfigureAwait(false);

                if (!ct.IsCancellationRequested)
                {
                    await WriteServiceResponseAsync(context, output, ct).ConfigureAwait(false);
                }

                return;
            }
            catch (Exception e)
            {
                message = "HTTPSLISTENER - Unexpected error processing request.";
                m_logger.UnexpectedErrorProcessingRequest(e, message);
            }

            await WriteResponseAsync(context.Response, message, HttpStatusCode.InternalServerError)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Backward-compatible alias for <see cref="SendBinaryAsync"/>.
        /// </summary>
        public Task SendAsync(HttpContext context)
        {
            return SendBinaryAsync(context);
        }

        /// <summary>
        /// Handles HTTPS POST requests carrying an OPC UA JSON message
        /// (Part 6 §7.4.5 — <c>application/opcua+uajson</c>). JSON over
        /// HTTPS does not use the UA SecureChannel layer, so only the
        /// <see cref="MessageSecurityMode.None"/> policy is accepted;
        /// transport security is provided by TLS at the HTTPS layer.
        /// </summary>
        public async Task SendJsonAsync(HttpContext context)
        {
            string message = string.Empty;
            CancellationToken ct = context.RequestAborted;
            try
            {
                if (m_callback == null)
                {
                    await WriteResponseAsync(
                        context.Response,
                        message,
                        HttpStatusCode.NotImplemented).ConfigureAwait(false);
                    return;
                }

                // Optional JWT extraction (same as the binary path).
                NodeId authenticationToken = NodeId.Null;
                if (context.Request.Headers.TryGetValue(
                        kAuthorizationKey,
                        out Microsoft.Extensions.Primitives.StringValues keys))
                {
                    foreach (string? value in keys)
                    {
                        if (value != null &&
                            value.StartsWith(kBearerKey, StringComparison.OrdinalIgnoreCase))
                        {
                            authenticationToken = new NodeId(
                                value[(kBearerKey.Length + 1)..].Trim(),
                                0);
                        }
                    }
                }

                IServiceRequest input;
                try
                {
                    input = await JsonRequestMapper
                        .DecodeRequestAsync(context.Request.Body, m_quotas.MessageContext, ct)
                        .ConfigureAwait(false);
                }
                catch (ServiceResultException sre)
                {
                    IServiceResponse fault = EndpointBase.CreateFault(
                        m_logger,
                        null,
                        sre);
                    await WriteJsonResponseAsync(context, fault, ct).ConfigureAwait(false);
                    return;
                }

                input.RequestHeader ??= new RequestHeader();
                if (input.RequestHeader.AuthenticationToken.IsNull &&
                    !authenticationToken.IsNull &&
                    input.TypeId != DataTypeIds.CreateSessionRequest)
                {
                    input.RequestHeader.AuthenticationToken = authenticationToken;
                }

                // The JSON sub-protocol is restricted to SecurityPolicy = None
                // (Part 6 §7.4.5); pick the matching endpoint up front.
                EndpointDescription? endpoint = null;
                foreach (EndpointDescription ep in m_descriptions)
                {
                    if (ep.TransportProfileUri == Profiles.HttpsJsonTransport &&
                        ep.SecurityMode == MessageSecurityMode.None)
                    {
                        endpoint = ep;
                        break;
                    }
                }

                if (endpoint == null && !IsDiscoveryRequest(input.TypeId))
                {
                    // Fail closed (mirror the binary path): a JSON channel with no
                    // matching MessageSecurityMode.None endpoint may only be used
                    // for discovery services (Part 4 §5.4 / §5.5).
                    IServiceResponse discoveryFault = EndpointBase.CreateFault(
                        m_logger,
                        null,
                        new ServiceResultException(
                            StatusCodes.BadSecurityPolicyRejected,
                            "Channel can only be used for discovery."));
                    await WriteJsonResponseAsync(context, discoveryFault, ct).ConfigureAwait(false);
                    return;
                }

                var secureChannelContext = new SecureChannelContext(
                    ListenerId,
                    endpoint,
                    RequestEncoding.Json,
                    context.Connection.ClientCertificate?.RawData,
                    ServerChannelCertificate);

                IServiceResponse output = await m_callback
                    .ProcessRequestAsync(secureChannelContext, input, ct)
                    .ConfigureAwait(false);

                if (!ct.IsCancellationRequested)
                {
                    await WriteJsonResponseAsync(context, output, ct).ConfigureAwait(false);
                }
                return;
            }
            catch (Exception e)
            {
                message = "HTTPSLISTENER - Unexpected error processing JSON request.";
                m_logger.UnexpectedErrorProcessingRequest(e, message);
            }

            await WriteResponseAsync(context.Response, message, HttpStatusCode.InternalServerError)
                .ConfigureAwait(false);
        }

        private async Task WriteJsonResponseAsync(
            HttpContext context,
            IServiceResponse response,
            CancellationToken ct)
        {
            byte[] payload = JsonRequestMapper.EncodeResponse(response, m_quotas.MessageContext);
            context.Response.ContentLength = payload.Length;
            context.Response.ContentType = Profiles.OpcUaJsonContentType;
            context.Response.StatusCode = (int)HttpStatusCode.OK;
#if NETSTANDARD2_1 || NET5_0_OR_GREATER
            await context.Response.Body
                .WriteAsync(payload.AsMemory(0, payload.Length), ct)
                .ConfigureAwait(false);
#else
            await context.Response.Body
                .WriteAsync(payload, 0, payload.Length, ct)
                .ConfigureAwait(false);
#endif
        }

        /// <summary>
        /// Handles WebSocket upgrade requests for the WSS transport
        /// (Part 6 §7.5). Negotiates the <c>opcua+uacp</c> sub-protocol
        /// (binary + UASC SecureChannel); the <c>opcua+uajson</c>
        /// sub-protocol returns 501 until <c>p3-wss-json-handler</c>.
        /// </summary>
        public async Task AcceptWebSocketAsync(HttpContext context)
        {
            // m_callback is null when this listener was opened in
            // reverse-connect mode (the listener side does not service
            // inbound UASC requests; it just hands off the established
            // WebSocket to the awaiting client via ConnectionWaiting).
            if (m_callback == null && !m_reverseConnectListener)
            {
                await WriteResponseAsync(
                    context.Response,
                    "HTTPSLISTENER - listener is not open.",
                    HttpStatusCode.NotImplemented).ConfigureAwait(false);
                return;
            }

            string? selected = SelectWebSocketSubProtocol(context);
            if (selected == null)
            {
                await WriteResponseAsync(
                    context.Response,
                    "HTTPSLISTENER - no supported WebSocket sub-protocol requested. " +
                    "Use 'opcua+uacp', 'opcua+uajson', 'opcua+openapi', or 'opcua+openapi+<accesstoken>'.",
                    HttpStatusCode.BadRequest).ConfigureAwait(false);
                return;
            }
            if (string.Equals(selected, Profiles.OpcUaWsSubProtocolUaJson, StringComparison.Ordinal))
            {
                await AcceptWebSocketJsonAsync(context).ConfigureAwait(false);
                return;
            }
            if (string.Equals(selected, Profiles.OpcUaWsSubProtocolOpenApi, StringComparison.Ordinal))
            {
                await AcceptWebSocketOpenApiAsync(context, accessToken: null).ConfigureAwait(false);
                return;
            }
            if (selected.StartsWith(Profiles.OpcUaWsSubProtocolOpenApiBearerPrefix, StringComparison.Ordinal))
            {
                // Refuse to accept a bearer access token in the WSS
                // sub-protocol name over plain HTTP — the WebSocket spec
                // requires the server to echo the selected sub-protocol
                // back in the 101 handshake, which would broadcast the
                // credential through every TCP intermediary (proxies,
                // WAFs, packet capture). HTTPS hides the credential from
                // the network at least; the credential still appears in
                // server access logs unless operators redact it, which
                // is why short-lived tokens (≤ 60s TTL) are recommended
                // even on the TLS path.
                if (!context.Request.IsHttps)
                {
                    m_logger.OpenApiTokenRejectedPlainHttp();
                    await WriteResponseAsync(
                        context.Response,
                        "HTTPSLISTENER - opcua+openapi+<accesstoken> requires HTTPS — bearer " +
                        "credentials must not flow over plain HTTP.",
                        HttpStatusCode.UpgradeRequired).ConfigureAwait(false);
                    return;
                }
                string accessToken = selected[Profiles.OpcUaWsSubProtocolOpenApiBearerPrefix.Length..];
                Func<HttpContext, string, Task<bool>>? bearerValidator = WssBearerTokenValidator;
                if (bearerValidator == null)
                {
                    // Fail-closed: no bearer validator registered (no
                    // AddWebApiBearerAuth() or similar). Refuse the
                    // upgrade rather than echo the token back.
                    m_logger.OpenApiTokenValidatorMissing();
                    await WriteResponseAsync(
                        context.Response,
                        "HTTPSLISTENER - opcua+openapi+<accesstoken> requires a bearer auth scheme " +
                        "(call AddWebApiBearerAuth on the server builder).",
                        HttpStatusCode.Unauthorized).ConfigureAwait(false);
                    return;
                }
                bool validated;
                try
                {
                    validated = await bearerValidator(context, accessToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    m_logger.OpenApiTokenValidatorThrew(ex);
                    validated = false;
                }
                if (!validated)
                {
                    await WriteResponseAsync(
                        context.Response,
                        "HTTPSLISTENER - bearer access token validation failed.",
                        HttpStatusCode.Unauthorized).ConfigureAwait(false);
                    return;
                }
                await AcceptWebSocketOpenApiAsync(context, accessToken).ConfigureAwait(false);
                return;
            }

            // selected == opcua+uacp
            WebSocket ws = await context.WebSockets
                .AcceptWebSocketAsync(selected)
                .ConfigureAwait(false);

            EndPoint? localEndpoint = MakeEndpoint(
                context.Connection.LocalIpAddress,
                context.Connection.LocalPort);
            EndPoint? remoteEndpoint = MakeEndpoint(
                context.Connection.RemoteIpAddress,
                context.Connection.RemotePort);

            var perWsListener = new WssChannelListener(this);
            uint channelId = (uint)Interlocked.Increment(ref m_nextChannelId);

            // In reverse-connect mode the inbound WSS connection is
            // initiated by the SERVER and starts with a ReverseHello;
            // wrap it in a TcpReverseConnectChannel so the channel's
            // ProcessReverseHelloMessage path triggers the handoff via
            // perWsListener.TransferListenerChannelAsync.
            TcpListenerChannel channel = m_reverseConnectListener
                ? new TcpReverseConnectChannel(
                    ListenerId,
                    perWsListener,
                    m_bufferManager,
                    m_quotas,
                    m_descriptions,
                    m_telemetry)
                : new TcpServerChannel(
                    ListenerId,
                    perWsListener,
                    m_bufferManager,
                    m_quotas,
                    m_serverCertProvider,
                    m_descriptions,
                    m_telemetry);

            if (channel is TcpServerChannel forwardChannel)
            {
                forwardChannel.SetRequestReceivedCallback(
                    new TcpChannelRequestEventHandler(OnRequestReceivedAsync));
                forwardChannel.SetReportOpenSecureChannelAuditCallback(
                    new ReportAuditOpenSecureChannelEventHandler(OnReportAuditOpenSecureChannelEvent));
                forwardChannel.SetReportCloseSecureChannelAuditCallback(
                    new ReportAuditCloseSecureChannelEventHandler(OnReportAuditCloseSecureChannelEvent));
                forwardChannel.SetReportCertificateAuditCallback(
                    new ReportAuditCertificateEventHandler(OnReportAuditCertificateEvent));
            }

            perWsListener.AttachChannel(channelId, channel);

            WebSocketServerByteTransport? transport = null;
            try
            {
                transport = new WebSocketServerByteTransport(
                    ws,
                    localEndpoint,
                    remoteEndpoint,
                    m_bufferManager,
                    m_quotas.MaxBufferSize,
                    m_telemetry);
                channel.Attach(channelId, transport);

                // Hold the request open until the channel is torn down (the
                // listener closes the channel when the WebSocket sees Close or
                // a fatal UASC error). Honor request abort to allow shutdown.
                await perWsListener
                    .WaitForChannelClosedAsync(context.RequestAborted)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Connection torn down by the request abort token; normal shutdown.
            }
            catch (Exception ex)
            {
                m_logger.UnexpectedWebSocketSessionError(ex);
            }
            finally
            {
                try
                {
                    channel.Dispose();
                }
                catch
                {
                    // Dispose is best-effort.
                }
                // The channel closes the attached transport on Dispose; this
                // direct, idempotent Dispose covers the path where Attach was
                // never reached (e.g. the transport ctor threw).
                transport?.Dispose();
            }
        }

#pragma warning disable IDE0051, RCS1213 // Kept for the Task-returning request callback path used by alternate listeners.
        private async Task<IServiceResponse> OnRequestReceivedAsyncShim(
            SecureChannelContext channelContext,
            IServiceRequest request)
        {
            return await m_callback!.ProcessRequestAsync(channelContext, request).ConfigureAwait(false);
        }
#pragma warning restore IDE0051, RCS1213

        private async void OnRequestReceivedAsync(
            TcpListenerChannel channel,
            uint requestId,
            IServiceRequest request)
        {
            try
            {
                if (m_callback == null)
                {
                    return;
                }
                if (channel is not TcpServerChannel serverChannel)
                {
                    return;
                }

                var context = new SecureChannelContext(
                    channel.GlobalChannelId,
                    channel.EndpointDescription,
                    RequestEncoding.Binary,
                    channel.ClientCertificate?.RawData,
                    channel.ServerCertificate?.RawData,
                    channel.ChannelThumbprint);

                IServiceResponse response = await m_callback
                    .ProcessRequestAsync(context, request)
                    .ConfigureAwait(false);

                serverChannel.SendResponse(requestId, response);
            }
            catch (Exception ex)
            {
                m_logger.ErrorProcessingRequest(ex, requestId);
            }
        }

        private void OnReportAuditOpenSecureChannelEvent(
            TcpServerChannel channel,
            OpenSecureChannelRequest request,
            Certificate? clientCertificate,
            Exception? exception)
        {
            if (m_callback == null)
            {
                return;
            }
            m_callback.ReportAuditOpenSecureChannelEvent(
                channel.GlobalChannelId,
                channel.EndpointDescription!,
                request,
                clientCertificate!,
                exception!);
        }

        private void OnReportAuditCloseSecureChannelEvent(
            TcpServerChannel channel,
            Exception? exception)
        {
            m_callback?.ReportAuditCloseSecureChannelEvent(
                channel.GlobalChannelId,
                exception!);
        }

        private void OnReportAuditCertificateEvent(
            Certificate? clientCertificate,
            Exception? exception)
        {
            m_callback?.ReportAuditCertificateEvent(clientCertificate!, exception!);
        }

        private static IPEndPoint? MakeEndpoint(IPAddress? address, int port)
        {
            return address == null ? null : new IPEndPoint(address, port);
        }

        private static string? SelectWebSocketSubProtocol(HttpContext context)
        {
            string? openApiBearer = null;
            foreach (string requested in context.WebSockets.WebSocketRequestedProtocols)
            {
                if (string.Equals(requested, Profiles.OpcUaWsSubProtocolUacp, StringComparison.Ordinal))
                {
                    return Profiles.OpcUaWsSubProtocolUacp;
                }
                if (string.Equals(requested, Profiles.OpcUaWsSubProtocolUaJson, StringComparison.Ordinal))
                {
                    return Profiles.OpcUaWsSubProtocolUaJson;
                }
                if (string.Equals(requested, Profiles.OpcUaWsSubProtocolOpenApi, StringComparison.Ordinal))
                {
                    return Profiles.OpcUaWsSubProtocolOpenApi;
                }
                // Bearer-token variant: opcua+openapi+<accesstoken>.
                // Defer until the loop completes in case the client also
                // offered the plain opcua+openapi or opcua+uacp; prefer
                // the more-secure binary / non-bearer options first.
                if (openApiBearer == null &&
                    requested != null &&
                    requested.StartsWith(Profiles.OpcUaWsSubProtocolOpenApiBearerPrefix, StringComparison.Ordinal) &&
                    requested.Length > Profiles.OpcUaWsSubProtocolOpenApiBearerPrefix.Length)
                {
                    openApiBearer = requested;
                }
            }
            return openApiBearer;
        }

        /// <summary>
        /// Handles a WebSocket upgrade for the <c>opcua+uajson</c>
        /// sub-protocol (Part 6 §7.5.2). Each WS message carries one
        /// JSON-encoded OPC UA request; the JSON sub-protocol does not
        /// use the UA Secure Conversation layer so only Security Mode
        /// None is supported (transport security is TLS).
        /// </summary>
        private async Task AcceptWebSocketJsonAsync(HttpContext context)
        {
            WebSocket ws = await context.WebSockets
                .AcceptWebSocketAsync(Profiles.OpcUaWsSubProtocolUaJson)
                .ConfigureAwait(false);

            CancellationToken ct = context.RequestAborted;

            // Pick the JSON endpoint description for the active scheme.
            EndpointDescription? endpoint = null;
            foreach (EndpointDescription ep in m_descriptions)
            {
                if (ep.TransportProfileUri == Profiles.UaWssJsonTransport &&
                    ep.SecurityMode == MessageSecurityMode.None)
                {
                    endpoint = ep;
                    break;
                }
            }

            var channelContext = new SecureChannelContext(
                ListenerId,
                endpoint,
                RequestEncoding.Json,
                context.Connection.ClientCertificate?.RawData,
                ServerChannelCertificate);

            byte[]? receiveBuffer = null;
            try
            {
                while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    receiveBuffer ??= m_bufferManager.TakeBuffer(
                        m_quotas.MaxBufferSize,
                        nameof(AcceptWebSocketJsonAsync),
                        ct);

                    int totalRead = 0;
                    bool completed;
                    do
                    {
                        WebSocketReceiveResult result = await ws
                            .ReceiveAsync(
                                new ArraySegment<byte>(
                                    receiveBuffer,
                                    totalRead,
                                    receiveBuffer.Length - totalRead),
                                ct)
                            .ConfigureAwait(false);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            await ws.CloseAsync(
                                WebSocketCloseStatus.NormalClosure,
                                "Client requested close.",
                                ct).ConfigureAwait(false);
                            return;
                        }
                        totalRead += result.Count;
                        if (totalRead > m_quotas.MaxBufferSize)
                        {
                            await ws.CloseAsync(
                                WebSocketCloseStatus.MessageTooBig,
                                "Message exceeded configured limit.",
                                ct).ConfigureAwait(false);
                            return;
                        }
                        completed = result.EndOfMessage;

                        // Guard against zero-progress / buffer-filling continuation
                        // frames that never terminate the message (CPU DoS); the
                        // cap check above uses '>'.
                        if (!completed &&
                            (result.Count == 0 || totalRead >= m_quotas.MaxBufferSize))
                        {
                            await ws.CloseAsync(
                                WebSocketCloseStatus.MessageTooBig,
                                "Continuation frame made no progress or exceeded the configured limit.",
                                ct).ConfigureAwait(false);
                            return;
                        }
                    }
                    while (!completed);

                    IServiceResponse responseToSend;
                    try
                    {
                        byte[] messageBytes = new byte[totalRead];
                        Buffer.BlockCopy(receiveBuffer, 0, messageBytes, 0, totalRead);
                        IServiceRequest request = JsonDecoder.DecodeMessage<IServiceRequest>(
                            messageBytes,
                            m_quotas.MessageContext);
                        request.RequestHeader ??= new RequestHeader();

                        if (endpoint == null && !IsDiscoveryRequest(request.TypeId))
                        {
                            // Fail closed (mirror the binary path): discovery-only
                            // when no MessageSecurityMode.None JSON endpoint matches.
                            responseToSend = EndpointBase.CreateFault(
                                m_logger,
                                null,
                                new ServiceResultException(
                                    StatusCodes.BadSecurityPolicyRejected,
                                    "Channel can only be used for discovery."));
                        }
                        else
                        {
                            responseToSend = await m_callback!
                                .ProcessRequestAsync(channelContext, request, ct)
                                .ConfigureAwait(false);
                        }
                    }
                    catch (ServiceResultException sre)
                    {
                        responseToSend = EndpointBase.CreateFault(m_logger, null, sre);
                    }
                    catch (Exception ex)
                    {
                        m_logger.ErrorProcessingJsonRequest(ex);
                        responseToSend = EndpointBase.CreateFault(m_logger, null, ex);
                    }

                    byte[] responseBytes = JsonRequestMapper.EncodeResponse(
                        responseToSend,
                        m_quotas.MessageContext);
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
                    await ws.SendAsync(
                        new ReadOnlyMemory<byte>(responseBytes, 0, responseBytes.Length),
                        WebSocketMessageType.Text,
                        endOfMessage: true,
                        ct).ConfigureAwait(false);
#else
                    await ws.SendAsync(
                        new ArraySegment<byte>(responseBytes, 0, responseBytes.Length),
                        WebSocketMessageType.Text,
                        endOfMessage: true,
                        ct).ConfigureAwait(false);
#endif
                }
            }
            catch (OperationCanceledException)
            {
                // Request aborted; normal shutdown.
            }
            catch (Exception ex)
            {
                m_logger.UnexpectedJsonWebSocketError(ex);
            }
            finally
            {
                if (receiveBuffer != null)
                {
                    m_bufferManager.ReturnBuffer(receiveBuffer, nameof(AcceptWebSocketJsonAsync));
                }
                try
                {
                    if (ws.State is WebSocketState.Open or WebSocketState.CloseReceived)
                    {
                        await ws.CloseAsync(
                            WebSocketCloseStatus.NormalClosure,
                            string.Empty,
                            CancellationToken.None).ConfigureAwait(false);
                    }
                }
                catch
                {
                    // Best-effort.
                }
                ws.Dispose();
            }
        }

        /// <summary>
        /// Handles a WebSocket upgrade for the <c>opcua+openapi</c> /
        /// <c>opcua+openapi+&lt;accesstoken&gt;</c> sub-protocols (OPC UA
        /// OpenAPI mapping over WSS; Part 6 §7.5.2 + §G.3). The wire
        /// format reuses the standard <c>{TypeId, Body}</c> OPC UA JSON
        /// envelope (same as <see cref="AcceptWebSocketJsonAsync"/>); the
        /// OpenAPI sub-protocol distinguishes itself from
        /// <c>opcua+uajson</c> by the advertised
        /// <see cref="Profiles.WssOpenApiTransport"/> profile URI on the
        /// discovery endpoint description and by the per-request encoder
        /// flavor (Compact / Verbose per the Web API codec defaults).
        /// </summary>
        /// <param name="context">The HTTP context carrying the WebSocket upgrade.</param>
        /// <param name="accessToken">The bearer token extracted from the
        /// <c>opcua+openapi+&lt;accesstoken&gt;</c> sub-protocol name, or
        /// <c>null</c> for the plain <c>opcua+openapi</c> variant. Browser
        /// WebSocket APIs cannot send an <c>Authorization</c> header, so
        /// the bearer credential rides in the sub-protocol name (Part 6
        /// §7.5.2). The token is validated by the registered WSS bearer
        /// validator before this method is reached; the validated
        /// principal is published on <c>HttpContext.User</c> and flows
        /// through the standard <c>ISessionlessIdentityProvider</c> hook.</param>
        private async Task AcceptWebSocketOpenApiAsync(
            HttpContext context,
            string? accessToken)
        {
            string selectedSubProtocol = accessToken == null
                ? Profiles.OpcUaWsSubProtocolOpenApi
                : Profiles.OpcUaWsSubProtocolOpenApiBearerPrefix + accessToken;

            WebSocket ws = await context.WebSockets
                .AcceptWebSocketAsync(selectedSubProtocol)
                .ConfigureAwait(false);

            CancellationToken ct = context.RequestAborted;

            // Pick the OpenAPI endpoint description for the active scheme,
            // falling back to the JSON / SM=None description so the
            // SecureChannelContext.EndpointDescription is never null
            // (SessionManager.CreateSession dereferences SecurityMode).
            EndpointDescription? endpoint = null;
            foreach (EndpointDescription ep in m_descriptions)
            {
                if (Profiles.IsWssOpenApi(ep.TransportProfileUri) &&
                    ep.SecurityMode == MessageSecurityMode.None)
                {
                    endpoint = ep;
                    break;
                }
            }
            if (endpoint == null)
            {
                foreach (EndpointDescription ep in m_descriptions)
                {
                    if (ep.SecurityMode == MessageSecurityMode.None)
                    {
                        endpoint = ep;
                        break;
                    }
                }
            }

            var channelContext = new SecureChannelContext(
                ListenerId,
                endpoint,
                RequestEncoding.Json,
                context.Connection.ClientCertificate?.RawData,
                ServerChannelCertificate);

            byte[]? receiveBuffer = null;
            try
            {
                while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    receiveBuffer ??= m_bufferManager.TakeBuffer(
                        m_quotas.MaxBufferSize,
                        nameof(AcceptWebSocketOpenApiAsync),
                        ct);

                    int totalRead = 0;
                    bool completed;
                    do
                    {
                        WebSocketReceiveResult result = await ws
                            .ReceiveAsync(
                                new ArraySegment<byte>(
                                    receiveBuffer,
                                    totalRead,
                                    receiveBuffer.Length - totalRead),
                                ct)
                            .ConfigureAwait(false);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            await ws.CloseAsync(
                                WebSocketCloseStatus.NormalClosure,
                                "Client requested close.",
                                ct).ConfigureAwait(false);
                            return;
                        }
                        totalRead += result.Count;
                        if (totalRead > m_quotas.MaxBufferSize)
                        {
                            await ws.CloseAsync(
                                WebSocketCloseStatus.MessageTooBig,
                                "Message exceeded configured limit.",
                                ct).ConfigureAwait(false);
                            return;
                        }
                        completed = result.EndOfMessage;
                        if (!completed && result.Count == 0)
                        {
                            // Zero-progress continuation-frame guard. A peer
                            // that streams empty continuation frames without
                            // ever terminating the message would spin this
                            // loop (CPU DoS). Mirrors the
                            // WebSocketByteTransport guard for opcua+uacp.
                            await ws.CloseAsync(
                                WebSocketCloseStatus.MessageTooBig,
                                "WebSocket continuation frame made no progress.",
                                ct).ConfigureAwait(false);
                            return;
                        }
                    }
                    while (!completed);

                    IServiceResponse responseToSend;
                    try
                    {
                        byte[] messageBytes = new byte[totalRead];
                        Buffer.BlockCopy(receiveBuffer, 0, messageBytes, 0, totalRead);
                        IServiceRequest request = JsonDecoder.DecodeMessage<IServiceRequest>(
                            messageBytes,
                            m_quotas.MessageContext);
                        request.RequestHeader ??= new RequestHeader();

                        responseToSend = await m_callback!
                            .ProcessRequestAsync(channelContext, request, ct)
                            .ConfigureAwait(false);
                    }
                    catch (ServiceResultException sre)
                    {
                        responseToSend = EndpointBase.CreateFault(m_logger, null, sre);
                    }
                    catch (Exception ex)
                    {
                        m_logger.ErrorProcessingOpenApiRequest(ex);
                        responseToSend = EndpointBase.CreateFault(m_logger, null, ex);
                    }

                    byte[] responseBytes = JsonRequestMapper.EncodeResponse(
                        responseToSend,
                        m_quotas.MessageContext);
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
                    await ws.SendAsync(
                        new ReadOnlyMemory<byte>(responseBytes, 0, responseBytes.Length),
                        WebSocketMessageType.Text,
                        endOfMessage: true,
                        ct).ConfigureAwait(false);
#else
                    await ws.SendAsync(
                        new ArraySegment<byte>(responseBytes, 0, responseBytes.Length),
                        WebSocketMessageType.Text,
                        endOfMessage: true,
                        ct).ConfigureAwait(false);
#endif
                }
            }
            catch (OperationCanceledException)
            {
                // Request aborted; normal shutdown.
            }
            catch (Exception ex)
            {
                m_logger.UnexpectedOpenApiWebSocketError(ex);
            }
            finally
            {
                if (receiveBuffer != null)
                {
                    m_bufferManager.ReturnBuffer(receiveBuffer, nameof(AcceptWebSocketOpenApiAsync));
                }
                try
                {
                    if (ws.State is WebSocketState.Open or WebSocketState.CloseReceived)
                    {
                        await ws.CloseAsync(
                            WebSocketCloseStatus.NormalClosure,
                            string.Empty,
                            CancellationToken.None).ConfigureAwait(false);
                    }
                }
                catch
                {
                    // Best-effort.
                }
                ws.Dispose();
            }
        }

        /// <summary>
        /// Per-WebSocket adapter that lets a single <see cref="TcpServerChannel"/>
        /// run inside the HTTPS listener without needing a full
        /// <see cref="ITcpChannelListener"/>-shaped lifecycle. There is one
        /// <see cref="WssChannelListener"/> per accepted WebSocket; it tracks
        /// exactly one channel and signals the request handler when the
        /// channel closes so the request pipeline can clean up.
        /// </summary>
        private sealed class WssChannelListener : ITcpChannelListener
        {
            internal WssChannelListener(HttpsTransportListener owner)
            {
                m_owner = owner;
                m_closed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            public Uri EndpointUrl => m_owner.EndpointUrl;

            internal void AttachChannel(uint channelId, TcpListenerChannel channel)
            {
                m_channelId = channelId;
                m_channel = channel;
            }

            public bool ReconnectToExistingChannel(
                IUaSCByteTransport transport,
                uint requestId,
                uint sequenceNumber,
                uint channelId,
                Certificate clientCertificate,
                ChannelToken token,
                OpenSecureChannelRequest request)
            {
                // Each WSS upgrade owns exactly one channel; reconnect over a
                // different WebSocket would require a different listener.
                throw new ServiceResultException(
                    StatusCodes.BadTcpSecureChannelUnknown,
                    "Reconnect is not supported on the WSS transport.");
            }

            [Obsolete("Use TransferListenerChannelAsync instead.")]
            public Task<bool> TransferListenerChannel(uint channelId, string serverUri, Uri endpointUrl)
            {
                return TransferListenerChannelAsync(channelId, serverUri, endpointUrl);
            }

            public async Task<bool> TransferListenerChannelAsync(uint channelId, string serverUri, Uri endpointUrl)
            {
                // Forward (non-reverse) WSS upgrades never need a transfer.
                if (!m_owner.m_reverseConnectListener || m_channel == null || channelId != m_channelId)
                {
                    return false;
                }

                ConnectionWaitingHandlerAsync? handler = m_owner.ConnectionWaiting;
                if (handler == null)
                {
                    return false;
                }

                IUaSCByteTransport? transport = await m_channel.DetachTransportAsync()
                    .ConfigureAwait(false);
                if (transport == null)
                {
                    return false;
                }

                var args = new TcpConnectionWaitingEventArgs(serverUri, endpointUrl, transport);
                await handler(m_owner, args).ConfigureAwait(false);
                if (args.Accepted)
                {
                    // The application took ownership of the transport. Do NOT
                    // signal m_closed here - the request handler must stay
                    // alive until the new owner finishes using the WebSocket
                    // (when it closes the WS Kestrel fires RequestAborted
                    // which sets m_closed via WaitForChannelClosedAsync's
                    // ct.Register).
                    //
                    // Release the now-empty TcpReverseConnectChannel: its
                    // transport is gone and its only remaining state (the
                    // ServerCertificateChain loaded during the ReverseHello
                    // SetEndpointUrl) would otherwise stay alive until the
                    // request handler's finally clears it. Disposing here
                    // releases the cert-chain handles deterministically and
                    // breaks the otherwise hard-to-collect reference graph.
                    TcpListenerChannel? toDispose = Interlocked.Exchange(ref m_channel, null);
                    toDispose?.Dispose();
                    return true;
                }

                // Caller rejected: re-attach the transport so the channel
                // can keep working on retry.
                m_channel.Transport = transport;
                m_channel.StartReceiveLoop();
                return false;
            }

            public void ChannelClosed(uint channelId)
            {
                if (channelId == m_channelId)
                {
                    m_closed.TrySetResult(true);
                }
            }

            internal async Task WaitForChannelClosedAsync(CancellationToken ct)
            {
                using (ct.Register(static s => ((TaskCompletionSource<bool>)s!).TrySetResult(true), m_closed))
                {
                    await m_closed.Task.ConfigureAwait(false);
                }
            }

            private readonly HttpsTransportListener m_owner;
            private readonly TaskCompletionSource<bool> m_closed;
            private uint m_channelId;
            private TcpListenerChannel? m_channel;
        }

        /// <summary>
        /// Called when an UpdateCertificate event occurred. Performs the
        /// soft fan-out (refresh validator, registry, and per-endpoint
        /// blobs); see <see cref="CloseChannelsForCertificateAsync"/> for the
        /// post-ApplyChanges connection teardown that actually rebinds
        /// the Kestrel TLS certificate.
        /// </summary>
        public void CertificateUpdate(
            ICertificateValidatorEx validator,
            ICertificateRegistry serverCertificates)
        {
            m_quotas.CertificateValidator = validator;
            m_serverCertProvider = serverCertificates;

            foreach (EndpointDescription description in m_descriptions)
            {
                ServerBase.SetServerCertificateInEndpointDescription(
                    description,
                    serverCertificates,
                    false);
            }
        }

        /// <inheritdoc/>
        public async ValueTask<IReadOnlyList<string>> CloseChannelsForCertificateAsync(
            Certificate oldCertificate,
            CancellationToken ct = default)
        {
            if (oldCertificate == null)
            {
                throw new ArgumentNullException(nameof(oldCertificate));
            }

            // Nothing to do if the listener was never opened. This keeps
            // the call safe to invoke unconditionally from the
            // ConfigurationNodeManager fan-out.
            if (m_descriptions == null)
            {
                return [];
            }

            // HTTPS owns the server certificate at the Kestrel / HTTP.sys
            // listener level, so we can't surgically close individual TLS
            // connections without restarting the host. Per OPC UA Part 12
            // §7.10.9 a StopAsync()/StartAsync() cycle satisfies the
            // "force renegotiate" requirement; existing Sessions remain
            // valid and the client's reconnect logic re-binds them over
            // the freshly-issued TLS endpoint.
            await StopAsync(ct).ConfigureAwait(false);
            await StartAsync(ct).ConfigureAwait(false);

            // The HTTPS listener does not track per-channel ids that map
            // to OPC UA SecureChannels (the binding is request-scoped via
            // HTTP), so there is nothing meaningful to return for
            // diagnostics here.
            return [];
        }

        /// <summary>
        /// Encodes a service response and writes it back.
        /// </summary>
        private async Task WriteServiceResponseAsync(
            HttpContext context,
            IServiceResponse response,
            CancellationToken ct)
        {
            byte[] encodedResponse = BinaryEncoder.EncodeMessage(response, m_quotas.MessageContext);
            context.Response.ContentLength = encodedResponse.Length;
            context.Response.ContentType = context.Request.ContentType;
            context.Response.StatusCode = (int)HttpStatusCode.OK;
#if NETSTANDARD2_1 || NET5_0_OR_GREATER
            await context
                .Response.Body.WriteAsync(encodedResponse.AsMemory(0, encodedResponse.Length), ct)
                .ConfigureAwait(false);
#else
            await context
                .Response.Body.WriteAsync(encodedResponse, 0, encodedResponse.Length, ct)
                .ConfigureAwait(false);
#endif
        }

        private static Task WriteResponseAsync(
            HttpResponse response,
            string message,
            HttpStatusCode status)
        {
            response.ContentLength = message.Length;
            response.ContentType = kHttpsContentType;
            response.StatusCode = (int)status;
            return response.WriteAsync(message);
        }

        private static bool IsDiscoveryRequest(ExpandedNodeId typeId)
        {
            return typeId == DataTypeIds.GetEndpointsRequest ||
                typeId == DataTypeIds.FindServersRequest ||
                typeId == DataTypeIds.FindServersOnNetworkRequest;
        }

        private static async Task<byte[]> ReadBodyAsync(
            HttpRequest req,
            int maxLength,
            CancellationToken ct)
        {
            return await JsonRequestMapper
                .ReadAllBoundedAsync(req.Body, maxLength, ct)
                .ConfigureAwait(false);
        }

        internal static bool ValidateClientCertificateWithUaValidator(
            ICertificateValidatorEx? certificateValidator,
            X509Certificate2? clientCertificate,
            X509Chain? chain,
            SslPolicyErrors sslPolicyErrors)
        {
            if (clientCertificate == null)
            {
                return sslPolicyErrors == SslPolicyErrors.None;
            }

            if (certificateValidator == null)
            {
                return false;
            }

            try
            {
                using CertificateCollection validationChain = CreateCertificateChain(
                    clientCertificate,
                    chain);
                // CA2025: the TLS ClientCertificateValidation callback is
                // synchronous by contract on every supported TFM, so the async UA
                // validator is bridged with GetAwaiter().GetResult(); the validation
                // chain remains alive across the wait. The call is a single bounded
                // chain validation but blocks a handshake thread.
                // TODO: replace with a non-blocking validation bridge to remove the
                // thread-pool-starvation risk under connection floods.
#pragma warning disable CA2025
                CertificateValidationResult result = certificateValidator
                    .ValidateAsync(
                        validationChain,
                        TrustListIdentifier.Https,
                        ct: default)
                    .GetAwaiter()
                    .GetResult();
#pragma warning restore CA2025
                return result.IsValid;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static CertificateCollection CreateCertificateChain(
            X509Certificate2 clientCertificate,
            X509Chain? chain)
        {
            var validationChain = new CertificateCollection();
            try
            {
                // CertificateCollection.Add retains an independent AddRef-owned handle.
                if (chain?.ChainElements != null && chain.ChainElements.Count > 0)
                {
                    foreach (X509ChainElement element in chain.ChainElements)
                    {
                        using Certificate certificate = Certificate.FromRawData(
                            element.Certificate.RawData);
                        validationChain.Add(certificate);
                    }
                }
                else
                {
                    using Certificate certificate = Certificate.FromRawData(
                        clientCertificate.RawData);
                    validationChain.Add(certificate);
                }

                return validationChain;
            }
            catch
            {
                validationChain.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Validate TLS client certificate at TLS handshake.
        /// </summary>
        /// <param name="clientCertificate">Client certificate</param>
        /// <param name="chain">Certificate chain</param>
        /// <param name="sslPolicyErrors">SSl policy errors</param>
        private bool ValidateClientCertificate(
            X509Certificate2? clientCertificate,
            X509Chain? chain,
            SslPolicyErrors sslPolicyErrors)
        {
            return ValidateClientCertificateWithUaValidator(
                m_quotas.CertificateValidator,
                clientCertificate,
                chain,
                sslPolicyErrors);
        }

        private List<EndpointDescription> m_descriptions = null!;
        private ChannelQuotas m_quotas = null!;
        private BufferManager m_bufferManager = null!;
        private readonly IBufferManagerFactory m_bufferManagerFactory;
        private ITransportListenerCallback? m_callback;
#if NET8_0_OR_GREATER
        private IHost? m_host;
#else
        private IWebHost? m_host;
#endif
        private SharedHostLease? m_sharedHostLease;
        private ICertificateRegistry m_serverCertProvider = null!;
        private Certificate? m_pinnedServerCert;
        private X509Certificate2? m_pinnedServerCertX509;
        private bool m_mutualTlsEnabled;
        private bool m_reverseConnectListener;
        /// <summary>
        /// Tracks outbound reverse-connect TcpServerChannels owned by this
        /// listener. CreateReverseConnection registers each new channel;
        /// OnHttpsReverseHelloComplete + DisposeAsync drain the set so any
        /// ServerCertificateChain loaded during SetEndpointUrl is released
        /// deterministically — these channels live on outbound WS transports
        /// that are not bound to the Kestrel host lifecycle, so listener
        /// teardown must close them explicitly.
        /// </summary>
        private readonly ConcurrentDictionary<TcpServerChannel, byte> m_reverseConnectChannels = new();
        private int m_nextChannelId;
        private readonly ILogger m_logger;
        private readonly ITelemetryContext m_telemetry;
    }

    /// <summary>
    /// Source-generated log messages for <see cref="HttpsTransportListener"/>.
    /// </summary>
    internal static partial class HttpsTransportListenerLog
    {
        [LoggerMessage(EventId = BindingsHttpsEventIds.HttpsTransportListener + 0, Level = LogLevel.Error,
            Message = "HTTPSLISTENER reverse connect handshake failed.")]
        public static partial void ReverseConnectHandshakeFailed(this ILogger logger, Exception exception);

        [LoggerMessage(EventId = BindingsHttpsEventIds.HttpsTransportListener + 1, Level = LogLevel.Warning,
            Message = "HTTPSLISTENER cannot share Kestrel host on {Key}; falling back to private host.")]
        public static partial void CannotShareKestrelHost(this ILogger logger, Exception exception, SharedHostKey key);

        [LoggerMessage(EventId = BindingsHttpsEventIds.HttpsTransportListener + 2, Level = LogLevel.Trace,
            Message = "Copy of the private key for https was denied: {Message}")]
        public static partial void PrivateKeyCopyDenied(this ILogger logger, string message);

        [LoggerMessage(EventId = BindingsHttpsEventIds.HttpsTransportListener + 3, Level = LogLevel.Error,
            Message = "{Message}")]
        public static partial void ClientTlsCertificateMismatch(this ILogger logger, string message);

        [LoggerMessage(EventId = BindingsHttpsEventIds.HttpsTransportListener + 4, Level = LogLevel.Error,
            Message = "{Message}")]
        public static partial void UnexpectedErrorProcessingRequest(
            this ILogger logger,
            Exception exception,
            string message);

        [LoggerMessage(EventId = BindingsHttpsEventIds.HttpsTransportListener + 5, Level = LogLevel.Warning,
            Message = "WSSLISTENER - opcua+openapi+<accesstoken> upgrade rejected: " +
                "bearer credentials in the sub-protocol name must not flow over plain HTTP.")]
        public static partial void OpenApiTokenRejectedPlainHttp(this ILogger logger);

        [LoggerMessage(EventId = BindingsHttpsEventIds.HttpsTransportListener + 6, Level = LogLevel.Warning,
            Message = "WSSLISTENER - opcua+openapi+<accesstoken> upgrade rejected: " +
                "no WSS bearer token validator registered.")]
        public static partial void OpenApiTokenValidatorMissing(this ILogger logger);

        [LoggerMessage(EventId = BindingsHttpsEventIds.HttpsTransportListener + 7, Level = LogLevel.Error,
            Message = "WSSLISTENER - opcua+openapi+<accesstoken> upgrade rejected: " +
                "bearer token validator threw.")]
        public static partial void OpenApiTokenValidatorThrew(this ILogger logger, Exception exception);

        [LoggerMessage(EventId = BindingsHttpsEventIds.HttpsTransportListener + 8, Level = LogLevel.Error,
            Message = "WSSLISTENER - unexpected error on WebSocket session.")]
        public static partial void UnexpectedWebSocketSessionError(this ILogger logger, Exception exception);

        [LoggerMessage(EventId = BindingsHttpsEventIds.HttpsTransportListener + 9, Level = LogLevel.Error,
            Message = "WSSLISTENER - error processing request {RequestId}.")]
        public static partial void ErrorProcessingRequest(
            this ILogger logger,
            Exception exception,
            uint requestId);

        [LoggerMessage(EventId = BindingsHttpsEventIds.HttpsTransportListener + 10, Level = LogLevel.Error,
            Message = "WSSLISTENER - error processing JSON request.")]
        public static partial void ErrorProcessingJsonRequest(this ILogger logger, Exception exception);

        [LoggerMessage(EventId = BindingsHttpsEventIds.HttpsTransportListener + 11, Level = LogLevel.Error,
            Message = "WSSLISTENER - unexpected JSON WebSocket error.")]
        public static partial void UnexpectedJsonWebSocketError(this ILogger logger, Exception exception);

        [LoggerMessage(EventId = BindingsHttpsEventIds.HttpsTransportListener + 12, Level = LogLevel.Error,
            Message = "WSSLISTENER - error processing OpenAPI request.")]
        public static partial void ErrorProcessingOpenApiRequest(this ILogger logger, Exception exception);

        [LoggerMessage(EventId = BindingsHttpsEventIds.HttpsTransportListener + 13, Level = LogLevel.Error,
            Message = "WSSLISTENER - unexpected OpenAPI WebSocket error.")]
        public static partial void UnexpectedOpenApiWebSocketError(this ILogger logger, Exception exception);
    }
}
