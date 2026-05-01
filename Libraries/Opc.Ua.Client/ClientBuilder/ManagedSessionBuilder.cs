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
            return UseEndpoint(new ConfiguredEndpoint(null, endpointDescription));
        }

        /// <summary>
        /// Set the user identity used when activating the session.
        /// </summary>
        public ManagedSessionBuilder WithUserIdentity(IUserIdentity identity)
        {
            if (identity == null)
            {
                throw new ArgumentNullException(nameof(identity));
            }
            m_options = m_options with { Identity = identity };
            return this;
        }

        /// <summary>
        /// Set the session display name.
        /// </summary>
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
        public ManagedSessionBuilder WithReconnectPolicy(IReconnectPolicy policy)
        {
            m_reconnectPolicy = policy
                ?? throw new ArgumentNullException(nameof(policy));
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
        /// Use a specific session factory. By default, the builder
        /// creates a new <see cref="DefaultSessionFactory"/> configured with
        /// the V2 subscription engine.
        /// </summary>
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
        public Task<ManagedSession> ConnectAsync(CancellationToken ct = default)
        {
            ManagedSessionOptions opts = m_options;
            if (opts.Endpoint == null)
            {
                throw new InvalidOperationException(
                    "ManagedSessionBuilder.UseEndpoint must be called before ConnectAsync.");
            }

            ISubscriptionEngineFactory engineFactory =
                opts.SubscriptionEngineFactory ?? DefaultSubscriptionEngineFactory.Instance;

            ISessionFactory sessionFactory = m_sessionFactory ??
                new DefaultSessionFactory(m_telemetry)
                {
                    SubscriptionEngineFactory = engineFactory
                };

            IReconnectPolicy reconnect =
                m_reconnectPolicy ?? new ReconnectPolicy(opts.ReconnectPolicy);

            IServerRedundancyHandler? redundancy = m_redundancyHandler ??
                (opts.EnableServerRedundancy
                    ? new DefaultServerRedundancyHandler()
                    : null);

            ArrayOf<string> preferredLocales = default;
            if (opts.PreferredLocales is { Count: > 0 } locales)
            {
                var arr = new string[locales.Count];
                for (int i = 0; i < locales.Count; i++)
                {
                    arr[i] = locales[i];
                }
                preferredLocales = new ArrayOf<string>(arr);
            }

            return ManagedSession.CreateAsync(
                m_configuration,
                opts.Endpoint,
                sessionFactory,
                opts.Identity,
                reconnect,
                redundancy,
                m_telemetry,
                opts.SessionName,
                (uint)opts.SessionTimeout.TotalMilliseconds,
                preferredLocales,
                opts.CheckDomain,
                engineFactory,
                ct);
        }
    }
}

