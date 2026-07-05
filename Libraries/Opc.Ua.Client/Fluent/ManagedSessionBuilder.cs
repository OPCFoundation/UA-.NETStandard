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
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Opc.Ua.Bindings;
using Opc.Ua.Client.WebApi;
using Opc.Ua.Identity;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Fluent builder for constructing a <see cref="ManagedSession"/>. The
    /// builder accumulates an immutable <see cref="ManagedSessionOptions"/>
    /// snapshot via <c>With*</c>/<c>Use*</c>/<c>Configure</c> methods. Call
    /// <see cref="Build"/> to obtain the snapshot, or
    /// <see cref="ConnectAsync"/> to construct and open the session in one
    /// step.
    /// </summary>
    /// <example>
    /// <code>
    /// var session = await new ManagedSessionBuilder(configuration, telemetry)
    ///     .UseEndpoint(endpoint)
    ///     .WithSessionName("My App")
    ///     .WithReconnectPolicy(p =&gt; p with { Strategy = BackoffStrategy.Linear })
    ///     .ConnectAsync(ct);
    /// </code>
    /// </example>
    public sealed class ManagedSessionBuilder
    {
        private readonly ApplicationConfiguration m_configuration;
        private readonly ITelemetryContext m_telemetry;
        private ManagedSessionOptions m_options = new();
        private IReconnectPolicy? m_reconnectPolicy;
        private IServerRedundancyHandler? m_redundancyHandler;
        private ISessionFactory? m_sessionFactory;
        private IClientChannelManager? m_channelManager;
        private Action<HttpStandardResilienceOptions>? m_httpsResilience;
        private WebApiClientOptions? m_webApiOptions;

        /// <summary>
        /// Initializes a new builder.
        /// </summary>
        public ManagedSessionBuilder(
            ApplicationConfiguration configuration,
            ITelemetryContext telemetry)
        {
            m_configuration = configuration
                ?? throw new ArgumentNullException(nameof(configuration));
            m_telemetry = telemetry
                ?? throw new ArgumentNullException(nameof(telemetry));
        }

        /// <summary>
        /// Use the supplied configured endpoint.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="endpoint"/> is <c>null</c>.</exception>
        public ManagedSessionBuilder UseEndpoint(ConfiguredEndpoint endpoint)
        {
            if (endpoint == null)
            {
                throw new ArgumentNullException(nameof(endpoint));
            }
            m_options = m_options with { Endpoint = endpoint };
            return this;
        }

        /// <summary>
        /// Use a discovery endpoint URL with optional security mode/policy.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="url"/> is <c>null</c>.</exception>
        public ManagedSessionBuilder UseEndpoint(
            string url,
            MessageSecurityMode securityMode = MessageSecurityMode.SignAndEncrypt,
            string securityPolicyUri = SecurityPolicies.Basic256Sha256)
        {
            if (url == null)
            {
                throw new ArgumentNullException(nameof(url));
            }
            var endpointDescription = new EndpointDescription
            {
                EndpointUrl = url,
                SecurityMode = securityMode,
                SecurityPolicyUri = securityPolicyUri
            };
            return UseEndpoint(new ConfiguredEndpoint(null, endpointDescription, null));
        }

        /// <summary>
        /// Shortcut that targets an OPC UA HTTPS Web API endpoint
        /// (OPC UA Part 6 §G.3 "OpenAPI Mapping"). The endpoint
        /// description is constructed with
        /// <see cref="Profiles.HttpsOpenApiTransport"/>,
        /// <see cref="MessageSecurityMode.None"/>, and
        /// <see cref="SecurityPolicies.None"/> — Web API relies on
        /// TLS / transport-layer authentication
        /// (configured via <see cref="WithWebApiAuthentication"/>).
        /// The endpoint advertises a permissive set of
        /// <see cref="UserTokenPolicy"/> entries (Anonymous + UserName +
        /// Certificate + IssuedToken / JWT) so the client can present
        /// any identity supported by the server; the server-side
        /// <c>DefaultSessionlessIdentityProvider</c> still enforces what
        /// it accepts.
        /// </summary>
        /// <param name="url">
        /// The server's base URL, e.g. <c>https://server:4843/</c>.
        /// </param>
        /// <param name="encoding">
        /// The OPC UA JSON encoding flavour to advertise. Defaults to
        /// <see cref="WebApiEncoding.Compact"/> per Part 6 §5.4.9.
        /// </param>
        /// <returns>The builder for chaining.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="url"/> is <c>null</c>.
        /// </exception>
        public ManagedSessionBuilder UseWebApiEndpoint(
            string url,
            WebApiEncoding encoding = WebApiEncoding.Compact)
        {
            if (url == null)
            {
                throw new ArgumentNullException(nameof(url));
            }
            var endpointDescription = new EndpointDescription
            {
                EndpointUrl = url,
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None,
                TransportProfileUri = Profiles.HttpsOpenApiTransport,
                UserIdentityTokens = new ArrayOf<UserTokenPolicy>(
                    new[]
                    {
                        new UserTokenPolicy
                        {
                            PolicyId = "Anonymous",
                            TokenType = UserTokenType.Anonymous,
                            SecurityPolicyUri = SecurityPolicies.None
                        },
                        new UserTokenPolicy
                        {
                            PolicyId = "Username",
                            TokenType = UserTokenType.UserName,
                            SecurityPolicyUri = SecurityPolicies.Basic256Sha256
                        },
                        new UserTokenPolicy
                        {
                            PolicyId = "Certificate",
                            TokenType = UserTokenType.Certificate,
                            SecurityPolicyUri = SecurityPolicies.Basic256Sha256
                        }
                    })
            };
            m_webApiOptions ??= new WebApiClientOptions();
            m_webApiOptions.Encoding = encoding;
            return UseEndpoint(new ConfiguredEndpoint(null, endpointDescription, null));
        }

        /// <summary>
        /// Shortcut that targets the WSS variant of the OPC UA OpenAPI
        /// mapping (Part 6 §7.5.2 sub-protocol <c>opcua+openapi</c>;
        /// surfaced via <see cref="Profiles.WssOpenApiTransport"/>,
        /// OPC Foundation profile/2339). Same envelope as
        /// <see cref="UseWebApiEndpoint(string, WebApiEncoding)"/> but
        /// multiplexed over a WebSocket text-frame transport.
        /// </summary>
        /// <param name="url">
        /// The server's WSS URL, e.g. <c>wss://server:4843/</c>.
        /// </param>
        /// <param name="encoding">
        /// The OPC UA JSON encoding flavour. Defaults to
        /// <see cref="WebApiEncoding.Compact"/>.
        /// </param>
        /// <returns>The builder for chaining.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="url"/> is <c>null</c>.
        /// </exception>
        public ManagedSessionBuilder UseWssOpenApiEndpoint(
            string url,
            WebApiEncoding encoding = WebApiEncoding.Compact)
        {
            if (url == null)
            {
                throw new ArgumentNullException(nameof(url));
            }
            var endpointDescription = new EndpointDescription
            {
                EndpointUrl = url,
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None,
                TransportProfileUri = Profiles.WssOpenApiTransport,
                UserIdentityTokens = new ArrayOf<UserTokenPolicy>(
                    new[]
                    {
                        new UserTokenPolicy
                        {
                            PolicyId = "Anonymous",
                            TokenType = UserTokenType.Anonymous,
                            SecurityPolicyUri = SecurityPolicies.None
                        },
                        new UserTokenPolicy
                        {
                            PolicyId = "Username",
                            TokenType = UserTokenType.UserName,
                            SecurityPolicyUri = SecurityPolicies.Basic256Sha256
                        },
                        new UserTokenPolicy
                        {
                            PolicyId = "Certificate",
                            TokenType = UserTokenType.Certificate,
                            SecurityPolicyUri = SecurityPolicies.Basic256Sha256
                        }
                    })
            };
            m_webApiOptions ??= new WebApiClientOptions();
            m_webApiOptions.Encoding = encoding;
            return UseEndpoint(new ConfiguredEndpoint(null, endpointDescription, null));
        }

        /// <summary>
        /// Configures Web API transport authentication
        /// (Bearer / Basic / mTLS via <c>HttpMessageHandler</c>).
        /// Applies to the per-session
        /// <see cref="WebApiTransportChannel"/> constructed when
        /// <see cref="ConnectAsync"/> resolves a
        /// <see cref="Profiles.HttpsOpenApiTransport"/> endpoint.
        /// </summary>
        /// <param name="configure">
        /// Callback that mutates a fresh
        /// <see cref="WebApiClientOptions"/> instance.
        /// </param>
        /// <returns>The builder for chaining.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="configure"/> is <c>null</c>.
        /// </exception>
        public ManagedSessionBuilder WithWebApiAuthentication(
            Action<WebApiClientOptions> configure)
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            m_webApiOptions ??= new WebApiClientOptions();
            configure(m_webApiOptions);
            return this;
        }

        /// <summary>
        /// Set the user identity used when activating the session.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="identity"/> is <c>null</c>.</exception>
        public ManagedSessionBuilder WithUserIdentity(IUserIdentity identity)
        {
            if (identity == null)
            {
                throw new ArgumentNullException(nameof(identity));
            }
#pragma warning disable CS0618 // Legacy eager identity remains supported for compatibility.
            m_options = m_options with { Identity = identity };
#pragma warning restore CS0618
            return this;
        }

        /// <summary>
        /// Set the lazy identity provider used to refresh identities after connect.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="provider"/> is <c>null</c>.</exception>
        public ManagedSessionBuilder WithIdentityProvider(IClientIdentityProvider provider)
        {
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
            m_options = m_options with { IdentityProvider = provider };
            return this;
        }

        /// <summary>
        /// Set the time provider used by proactive identity refresh.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="timeProvider"/> is <c>null</c>.</exception>
        public ManagedSessionBuilder WithTimeProvider(TimeProvider timeProvider)
        {
            if (timeProvider == null)
            {
                throw new ArgumentNullException(nameof(timeProvider));
            }
            m_options = m_options with { TimeProvider = timeProvider };
            return this;
        }

        /// <summary>
        /// Set the session display name.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        public ManagedSessionBuilder WithSessionName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("Value required", nameof(name));
            }
            m_options = m_options with { SessionName = name };
            return this;
        }

        /// <summary>
        /// Set the requested session timeout.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public ManagedSessionBuilder WithSessionTimeout(TimeSpan timeout)
        {
            if (timeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout));
            }
            m_options = m_options with { SessionTimeout = timeout };
            return this;
        }

        /// <summary>
        /// Set the preferred locales for the session.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="locales"/> is <c>null</c>.</exception>
        public ManagedSessionBuilder WithPreferredLocales(params string[] locales)
        {
            if (locales == null)
            {
                throw new ArgumentNullException(nameof(locales));
            }
            m_options = m_options with { PreferredLocales = locales };
            return this;
        }

        /// <summary>
        /// Enable or disable server-certificate domain validation.
        /// </summary>
        public ManagedSessionBuilder WithCheckDomain(bool checkDomain = true)
        {
            m_options = m_options with { CheckDomain = checkDomain };
            return this;
        }

        /// <summary>
        /// Configure the reconnect policy via a transformation of the
        /// underlying <see cref="ReconnectPolicyOptions"/> record.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <c>null</c>.</exception>
        public ManagedSessionBuilder WithReconnectPolicy(
            Func<ReconnectPolicyOptions, ReconnectPolicyOptions> configure)
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            m_options = m_options with
            {
                ReconnectPolicy = configure(m_options.ReconnectPolicy)
            };
            m_reconnectPolicy = null;
            return this;
        }

        /// <summary>
        /// Use the supplied <see cref="IReconnectPolicy"/> directly. Overrides
        /// any options-based reconnect configuration.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public ManagedSessionBuilder WithReconnectPolicy(IReconnectPolicy policy)
        {
            m_reconnectPolicy = policy
                ?? throw new ArgumentNullException(nameof(policy));
            return this;
        }

        /// <summary>
        /// Use a central <see cref="IClientChannelManager"/> so that
        /// multiple sessions to the same endpoint share one underlying
        /// transport channel and reconnect is coordinated centrally.
        /// </summary>
        /// <param name="channelManager">The channel manager.</param>
        /// <exception cref="ArgumentNullException"></exception>
        public ManagedSessionBuilder WithChannelManager(IClientChannelManager channelManager)
        {
            m_channelManager = channelManager
                ?? throw new ArgumentNullException(nameof(channelManager));
            return this;
        }

        /// <summary>
        /// Configure the standard HTTP resilience handler used by HTTPS transport channels.
        /// </summary>
        /// <param name="configure">The HTTP resilience configuration delegate.</param>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <c>null</c>.</exception>
        public ManagedSessionBuilder WithHttpsResilience(Action<HttpStandardResilienceOptions> configure)
        {
            m_httpsResilience = configure
                ?? throw new ArgumentNullException(nameof(configure));
            return this;
        }

        /// <summary>
        /// Enable server redundancy with a default handler.
        /// </summary>
        public ManagedSessionBuilder WithServerRedundancy()
        {
            m_options = m_options with { EnableServerRedundancy = true };
            m_redundancyHandler = null;
            return this;
        }

        /// <summary>
        /// Enable server redundancy with the supplied handler.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public ManagedSessionBuilder WithServerRedundancy(IServerRedundancyHandler handler)
        {
            m_redundancyHandler = handler
                ?? throw new ArgumentNullException(nameof(handler));
            m_options = m_options with { EnableServerRedundancy = true };
            return this;
        }

        /// <summary>
        /// Use a specific subscription engine factory. Defaults to the V2
        /// engine when not specified.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="factory"/> is <c>null</c>.</exception>
        public ManagedSessionBuilder UseSubscriptionEngine(ISubscriptionEngineFactory factory)
        {
            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }
            m_options = m_options with { SubscriptionEngineFactory = factory };
            return this;
        }

        /// <summary>
        /// Opt the V2 subscription engine into transfer-on-recreate.
        /// When enabled, the V2 <c>SubscriptionManager</c> attempts to
        /// transfer existing server-side subscriptions from the
        /// previous session to the new one on each session re-create
        /// (e.g. failover via
        /// <c>Session.RecreateInPlaceAsync</c>) and falls back
        /// to per-subscription recreate when transfer is not
        /// available. Disabled by default — recreate is the
        /// universal, server-agnostic fallback; transfer requires
        /// server support.
        /// </summary>
        /// <remarks>
        /// Has no effect when the classic subscription engine is in
        /// use; the classic engine drives recreate through the
        /// <see cref="Session"/>'s template-based path.
        /// </remarks>
        public ManagedSessionBuilder WithTransferSubscriptionsOnRecreate(
            bool transferOnRecreate = true)
        {
            m_options = m_options with
            {
                TransferSubscriptionsOnRecreate = transferOnRecreate
            };
            return this;
        }

        /// <summary>
        /// Enable activator-level pooling of V2 subscription
        /// notification payload instances. When enabled, the V2
        /// subscription dispatcher calls
        /// <see cref="IPooledEncodeable.Reuse"/> on notification
        /// payload objects (such as <c>MonitoredItemNotification</c>)
        /// after each handler dispatch, releasing them back to their
        /// activator's pool for reuse on the next publish. Handlers
        /// that retain values past the dispatch call must copy them.
        /// Disabled by default.
        /// </summary>
        /// <remarks>
        /// Has no effect when the classic subscription engine is in
        /// use; this option only applies to the V2
        /// <see cref="Subscriptions.ISubscriptionManager"/>.
        /// </remarks>
        public ManagedSessionBuilder WithPoolNotifications(
            bool poolNotifications = true)
        {
            m_options = m_options with
            {
                PoolNotifications = poolNotifications
            };
            return this;
        }

        /// <summary>
        /// Enables address-space model change tracking. The session
        /// auto-starts a <see cref="ModelChange.IModelChangeTracker"/>
        /// after connect that invalidates the node cache and exposes
        /// changes via <see cref="ManagedSession.ModelChange"/>.
        /// Disabled by default.
        /// </summary>
        public ManagedSessionBuilder WithModelChangeTracking(
            bool enabled = true)
        {
            m_options = m_options with
            {
                ModelChangeTracking = enabled
            };
            return this;
        }

        /// <summary>
        /// Enables or disables automatic complex-type loading after connect.
        /// Disabled by default.
        /// </summary>
        public ManagedSessionBuilder WithLoadComplexTypes(
            bool enabled = true)
        {
            m_options = m_options with
            {
                LoadComplexTypes = enabled
            };
            return this;
        }

        /// <summary>
        /// Use a specific session factory. By default, the builder
        /// creates a new <see cref="DefaultSessionFactory"/> configured with
        /// the V2 subscription engine.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public ManagedSessionBuilder UseSessionFactory(ISessionFactory factory)
        {
            m_sessionFactory = factory
                ?? throw new ArgumentNullException(nameof(factory));
            return this;
        }

        /// <summary>
        /// Returns the immutable options snapshot accumulated so far.
        /// </summary>
        public ManagedSessionOptions Build()
        {
            return m_options;
        }

        /// <summary>
        /// Construct and connect a <see cref="ManagedSession"/> using the
        /// accumulated options.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        public async Task<ManagedSession> ConnectAsync(CancellationToken ct = default)
        {
            ManagedSessionOptions opts = m_options;
            if (opts.Endpoint == null)
            {
                throw new InvalidOperationException(
                    "ManagedSessionBuilder.UseEndpoint must be called before ConnectAsync.");
            }

            ISubscriptionEngineFactory engineFactory =
                opts.SubscriptionEngineFactory ??
                (opts.TimeProvider == null
                    ? DefaultSubscriptionEngineFactory.Instance
                    : new DefaultSubscriptionEngineFactory(opts.TimeProvider));

            ISessionFactory sessionFactory = m_sessionFactory ??
                new DefaultSessionFactory(m_telemetry)
                {
                    SubscriptionEngineFactory = engineFactory,
                    TimeProvider = opts.TimeProvider
                };

            IReconnectPolicy reconnect =
                m_reconnectPolicy ?? new ReconnectPolicy(opts.ReconnectPolicy);

            IServerRedundancyHandler? redundancy = m_redundancyHandler ??
                (opts.EnableServerRedundancy
                    ? new DefaultServerRedundancyHandler()
                    : null);

            IClientChannelManager? channelManager = m_channelManager;
#pragma warning disable CA2000 // Ownership follows the managed session lifetime; TODO: model owned disposal explicitly.
            if (channelManager == null && m_httpsResilience != null)
            {
                ServiceProviderHttpClientFactory httpClientFactory = CreateHttpsHttpClientFactory(m_httpsResilience);
                channelManager = new ClientChannelManager(
                    m_configuration,
                    m_telemetry,
                    BuildChannelBindings(
                        new HttpsTransportChannelBindings(
                            DefaultTransportBindingRegistry.WithDefaultTcp(),
                            httpClientFactory)),
                    reconnectPolicy: null,
                    timeProvider: opts.TimeProvider);
            }
            else if (channelManager == null && IsWebApiEndpoint(opts.Endpoint))
            {
                channelManager = new ClientChannelManager(
                    m_configuration,
                    m_telemetry,
                    BuildChannelBindings(DefaultTransportBindingRegistry.WithDefaultTcp()),
                    reconnectPolicy: null,
                    timeProvider: opts.TimeProvider);
            }
#pragma warning restore CA2000

            ArrayOf<string> preferredLocales = default;
            if (opts.PreferredLocales is { Count: > 0 } locales)
            {
                string[] arr = new string[locales.Count];
                for (int i = 0; i < locales.Count; i++)
                {
                    arr[i] = locales[i];
                }
                preferredLocales = new ArrayOf<string>(arr);
            }

#pragma warning disable CS0618 // Legacy eager identity remains supported when no provider is configured.
            IUserIdentity? identity = opts.Identity;
#pragma warning restore CS0618
            ManagedSession session = await ManagedSession.CreateAsync(
                m_configuration,
                opts.Endpoint,
                sessionFactory,
                identity,
                reconnect,
                redundancy,
                m_telemetry,
                opts.SessionName,
                (uint)opts.SessionTimeout.TotalMilliseconds,
                preferredLocales,
                opts.CheckDomain,
                engineFactory,
                opts.TransferSubscriptionsOnRecreate,
                opts.PoolNotifications,
                opts.IdentityProvider,
                opts.TimeProvider,
                channelManager,
                ct).ConfigureAwait(false);

            if (opts.ModelChangeTracking)
            {
                await session.EnableModelChangeTrackingAsync(ct).ConfigureAwait(false);
            }
            if (opts.LoadComplexTypes)
            {
                var complexTypeSystem = new ComplexTypes.ComplexTypeSystem(session, m_telemetry);
                await complexTypeSystem.LoadAsync(ct: ct).ConfigureAwait(false);
            }

            return session;
        }

        private static ServiceProviderHttpClientFactory CreateHttpsHttpClientFactory(
            Action<HttpStandardResilienceOptions> configure)
        {
            var services = new ServiceCollection();
            services.AddHttpClient(OpcUaHttpClientDefaults.ClientName)
                .AddStandardResilienceHandler(configure);
            services.TryAddSingleton<IOpcUaHttpClientFactory, DefaultOpcUaHttpClientFactory>();

            ServiceProvider serviceProvider = services.BuildServiceProvider();
            try
            {
                return new ServiceProviderHttpClientFactory(serviceProvider);
            }
            catch
            {
                serviceProvider.Dispose();
                throw;
            }
        }

        private static bool IsWebApiEndpoint(ConfiguredEndpoint? endpoint)
        {
            string? profile = endpoint?.Description?.TransportProfileUri;
            return Profiles.IsHttpsOpenApi(profile) || Profiles.IsWssOpenApi(profile);
        }

        private ITransportChannelBindings BuildChannelBindings(ITransportChannelBindings inner)
        {
            // Wraps the inner registry so the synthetic
            // Utils.UriSchemeOpcHttpsWebApi / opc.wss+openapi keys resolve
            // to WebApi[Wss]TransportChannelFactory configured with the
            // auth options accumulated via WithWebApiAuthentication /
            // UseWebApiEndpoint / UseWssOpenApiEndpoint. Inner bindings
            // continue to handle every other URI scheme (opc.tcp,
            // opc.https, opc.wss, ...).
            if (m_webApiOptions == null && !IsWebApiEndpoint(m_options.Endpoint))
            {
                return inner;
            }
            return new WebApiAwareChannelBindings(
                inner,
                new WebApiTransportChannelFactory(m_webApiOptions),
                new WebApiWssTransportChannelFactory(m_webApiOptions));
        }

        private sealed class WebApiAwareChannelBindings : ITransportChannelBindings
        {
            public WebApiAwareChannelBindings(
                ITransportChannelBindings inner,
                WebApiTransportChannelFactory webApiFactory,
                WebApiWssTransportChannelFactory wssOpenApiFactory)
            {
                m_inner = inner;
                m_webApiFactory = webApiFactory;
                m_wssOpenApiFactory = wssOpenApiFactory;
            }

            public ITransportChannel? Create(string uriScheme, ITelemetryContext telemetry)
            {
                if (string.Equals(uriScheme, Utils.UriSchemeOpcHttpsWebApi, StringComparison.Ordinal))
                {
                    return m_webApiFactory.Create(telemetry);
                }
                if (string.Equals(uriScheme, Utils.UriSchemeOpcWssOpenApi, StringComparison.Ordinal))
                {
                    return m_wssOpenApiFactory.Create(telemetry);
                }
                return m_inner.Create(uriScheme, telemetry);
            }

            private readonly ITransportChannelBindings m_inner;
            private readonly WebApiTransportChannelFactory m_webApiFactory;
            private readonly WebApiWssTransportChannelFactory m_wssOpenApiFactory;
        }

        private sealed class ServiceProviderHttpClientFactory : IOpcUaHttpClientFactory, IDisposable
        {
            public ServiceProviderHttpClientFactory(ServiceProvider serviceProvider)
            {
                m_serviceProvider = serviceProvider
                    ?? throw new ArgumentNullException(nameof(serviceProvider));
                m_httpClientFactory = serviceProvider.GetRequiredService<IOpcUaHttpClientFactory>();
            }

            public HttpClient CreateClient(string name)
            {
                return m_httpClientFactory.CreateClient(name);
            }

            public void Dispose()
            {
                m_serviceProvider.Dispose();
            }

            private readonly ServiceProvider m_serviceProvider;
            private readonly IOpcUaHttpClientFactory m_httpClientFactory;
        }
    }
}
