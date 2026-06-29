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
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Opc.Ua.Bindings;
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
        /// Opt into token-reuse fast reconnect on failover (OPC UA Part 4 §6.6).
        /// When enabled, a failover to a redundant server
        /// re-activates the existing session by reusing the current
        /// <c>AuthenticationToken</c> instead of creating a new session, and
        /// falls back to re-authentication if the standby rejects the token.
        /// </summary>
        /// <remarks>
        /// Disabled by default (re-authentication on failover). Requires the
        /// server side to mirror session state; the standby still performs the
        /// full <c>ActivateSession</c> signature validation, so the token alone
        /// never admits a session.
        /// </remarks>
        public ManagedSessionBuilder WithTokenReuseFailover(bool enable = true)
        {
            m_options = m_options with
            {
                EnableTokenReuseFailover = enable
            };
            return this;
        }

        /// <summary>
        /// Configures non-transparent network redundancy endpoints for one logical server.
        /// </summary>
        public ManagedSessionBuilder WithNetworkRedundancy(
            ArrayOf<ConfiguredEndpoint> alternateEndpoints)
        {
            m_options = m_options with
            {
                NetworkRedundancy = new NetworkRedundancyOptions
                {
                    AlternateEndpoints = alternateEndpoints
                }
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

            // Redundancy is transparent and on by default: a non-redundant server simply
            // reports RedundancySupport.None and no failover occurs, while transparent and
            // non-transparent redundant servers are handled without any special connect API.
            IServerRedundancyHandler redundancy = m_redundancyHandler ??
                new DefaultServerRedundancyHandler(
                    new DefaultRedundantServerEndpointResolver(m_telemetry),
                    opts.TimeProvider);

            IClientChannelManager? channelManager = m_channelManager;
            ServiceProviderHttpClientFactory? ownedHttpClientFactory = null;
            try
            {
                if (channelManager == null && m_httpsResilience != null)
                {
                    ownedHttpClientFactory = CreateHttpsHttpClientFactory(m_httpsResilience);
#pragma warning disable CA2000 // Channel manager lifetime follows the managed session; TODO: model owned disposal explicitly.
                    channelManager = new ClientChannelManager(
                        m_configuration,
                        m_telemetry,
                        new HttpsTransportChannelBindings(ownedHttpClientFactory),
                        reconnectPolicy: null,
                        timeProvider: opts.TimeProvider);
#pragma warning restore CA2000
                    ownedHttpClientFactory = null;
                }
            }
            finally
            {
                ownedHttpClientFactory?.Dispose();
            }

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
                opts.EnableTokenReuseFailover,
                opts.IdentityProvider,
                opts.TimeProvider,
                channelManager,
                opts.NetworkRedundancy,
                ct).ConfigureAwait(false);

            if (opts.ModelChangeTracking)
            {
                await session.EnableModelChangeTrackingAsync(ct).ConfigureAwait(false);
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
