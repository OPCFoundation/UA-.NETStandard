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
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Identity;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// <see cref="IOpcUaBuilder"/> extensions provided by
    /// <c>Opc.Ua.Client</c>: register OPC UA client services (telemetry,
    /// session factory, <see cref="ManagedSession"/> factory) for
    /// dependency-injected applications.
    /// </summary>
    /// <remarks>
    /// Mirrors the server-side <c>.AddServer(...)</c> pattern. The
    /// returned <see cref="IOpcUaClientBuilder"/> can be extended with
    /// further registrations.
    /// </remarks>
    public static class OpcUaClientBuilderExtensions
    {
        /// <summary>
        /// Default <see cref="IConfiguration"/> section name used by the
        /// <see cref="AddClient(IOpcUaBuilder, IConfiguration)"/> overload.
        /// </summary>
        public const string DefaultConfigurationSection = "OpcUa:Client";

        /// <summary>
        /// Registers OPC UA client services and a lazy
        /// <see cref="Func{T, TResult}"/> factory for
        /// <see cref="ManagedSession"/>. The first call to the factory
        /// connects and caches the session; subsequent calls return the
        /// cached instance.
        /// </summary>
        /// <param name="builder">The OPC UA builder.</param>
        /// <param name="configure">Configuration delegate for
        /// <see cref="OpcUaClientOptions"/>. Must set
        /// <see cref="OpcUaClientOptions.Configuration"/> and
        /// <see cref="ManagedSessionOptions.Endpoint"/>.</param>
        /// <returns>An <see cref="IOpcUaClientBuilder"/> for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/>
        /// or <paramref name="configure"/> is <c>null</c>.</exception>
        public static IOpcUaClientBuilder AddClient(
            this IOpcUaBuilder builder,
            Action<OpcUaClientOptions> configure)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }

            var options = new OpcUaClientOptions();
            configure(options);
            builder.Services.TryAddSingleton(options);

            RegisterCoreServices(builder.Services);

            return new OpcUaClientBuilder(builder.Services);
        }

        /// <summary>
        /// Registers OPC UA client services with options bound from the
        /// supplied <paramref name="configuration"/> section
        /// <see cref="DefaultConfigurationSection"/> (<c>OpcUa:Client</c>).
        /// </summary>
        /// <remarks>
        /// AOT-safe: bound by the .NET 8+ configuration binding source
        /// generator (<c>EnableConfigurationBindingGenerator</c>).
        /// </remarks>
        /// <param name="builder">The OPC UA builder.</param>
        /// <param name="configuration">Configuration root containing
        /// the <c>OpcUa:Client</c> section.</param>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/>
        /// or <paramref name="configuration"/> is <c>null</c>.</exception>
        public static IOpcUaClientBuilder AddClient(
            this IOpcUaBuilder builder,
            IConfiguration configuration)
        {
            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }
            return builder.AddClient(configuration.GetSection(DefaultConfigurationSection));
        }

        /// <summary>
        /// Registers OPC UA client services with options bound from the
        /// supplied <paramref name="section"/>.
        /// </summary>
        /// <remarks>
        /// AOT-safe: bound by the .NET 8+ configuration binding source
        /// generator (<c>EnableConfigurationBindingGenerator</c>).
        /// </remarks>
        /// <param name="builder">The OPC UA builder.</param>
        /// <param name="section">Configuration section to bind.</param>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/>
        /// or <paramref name="section"/> is <c>null</c>.</exception>
        public static IOpcUaClientBuilder AddClient(
            this IOpcUaBuilder builder,
            IConfigurationSection section)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (section is null)
            {
                throw new ArgumentNullException(nameof(section));
            }

            var options = new OpcUaClientOptions();
            section.Bind(options);
            builder.Services.TryAddSingleton(options);

            RegisterCoreServices(builder.Services);

            return new OpcUaClientBuilder(builder.Services);
        }

        /// <summary>
        /// Registers a client identity provider implementation.
        /// </summary>
        public static IOpcUaClientBuilder AddIdentityProvider<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TProvider>(
            this IOpcUaClientBuilder builder)
            where TProvider : class, IClientIdentityProvider
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.Services.AddSingleton<TProvider>();
            builder.Services.AddSingleton<IClientIdentityProvider>(
                sp => sp.GetRequiredService<TProvider>());
            return builder;
        }

        /// <summary>
        /// Registers a composite client identity provider built from the
        /// supplied shortcut configuration.
        /// </summary>
        public static IOpcUaClientBuilder AddIdentityProvider(
            this IOpcUaClientBuilder builder,
            Action<CompositeClientIdentityProviderBuilder> configure)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }

            var compositeBuilder = new CompositeClientIdentityProviderBuilder();
            configure(compositeBuilder);
            builder.Services.AddSingleton<IClientIdentityProvider>(
                compositeBuilder.Build());
            return builder;
        }

        private static void RegisterCoreServices(IServiceCollection services)
        {
            services.TryAddSingleton<ITelemetryContext>(
                sp => new ServiceProviderTelemetryContext(sp));

            services.TryAddSingleton(TimeProvider.System);

            services.TryAddSingleton<ISessionFactory>(sp =>
            {
                ITelemetryContext telemetry = sp.GetRequiredService<ITelemetryContext>();
                OpcUaClientOptions options = sp.GetRequiredService<OpcUaClientOptions>();
                return new DefaultSessionFactory(telemetry)
                {
                    SubscriptionEngineFactory =
                        options.Session.SubscriptionEngineFactory
                        ?? DefaultSubscriptionEngineFactory.Instance
                };
            });

            services.TryAddSingleton(sp =>
            {
                ITelemetryContext telemetry = sp.GetRequiredService<ITelemetryContext>();
                return new ManagedSessionFactory(telemetry);
            });

            services.TryAddSingleton<Func<CancellationToken, Task<ManagedSession>>>(
                sp => new ManagedSessionAccessor(sp).ConnectAsync);

            services.TryAddSingleton(sp =>
            {
                ITelemetryContext telemetry = sp.GetRequiredService<ITelemetryContext>();
                OpcUaClientOptions options = sp.GetRequiredService<OpcUaClientOptions>();
                return ReverseConnectManagerActivator.Create(options, telemetry);
            });

            OpcUaServiceCollectionExtensions.AddOpcUa(services);
        }

        /// <summary>
        /// Builds a <see cref="ReverseConnectManager"/> on first resolution
        /// when client reverse-connect options are configured. The
        /// configured listener URLs are added, the manager's
        /// <see cref="ReverseConnectManager.StartService(ApplicationConfiguration)"/>
        /// is invoked using the application configuration from
        /// <see cref="OpcUaClientOptions"/>, and the options are mirrored
        /// into <see cref="ClientConfiguration.ReverseConnect"/> so any
        /// other consumer reading the application configuration sees the
        /// same data.
        /// </summary>
        private static class ReverseConnectManagerActivator
        {
            public static ReverseConnectManager Create(
                OpcUaClientOptions options,
                ITelemetryContext telemetry)
            {
                var manager = new ReverseConnectManager(telemetry);

                ClientReverseConnectOptions? rcOptions = options.ReverseConnect;
                if (rcOptions == null || rcOptions.ClientEndpointUrls.Count == 0)
                {
                    return manager;
                }

                ApplicationConfiguration? configuration = options.Configuration;
                if (configuration == null)
                {
                    throw new InvalidOperationException(
                        "OpcUaClientOptions.Configuration must be set before " +
                        "resolving ReverseConnectManager.");
                }

                configuration.ClientConfiguration ??= new ClientConfiguration();
                var clientEndpoints = new ReverseConnectClientEndpoint[
                    rcOptions.ClientEndpointUrls.Count];
                for (int i = 0; i < rcOptions.ClientEndpointUrls.Count; i++)
                {
                    clientEndpoints[i] = new ReverseConnectClientEndpoint
                    {
                        EndpointUrl = rcOptions.ClientEndpointUrls[i]
                    };
                }
                configuration.ClientConfiguration.ReverseConnect = new ReverseConnectClientConfiguration
                {
                    ClientEndpoints = new ArrayOf<ReverseConnectClientEndpoint>(clientEndpoints),
                    HoldTime = rcOptions.HoldTimeMs,
                    WaitTimeout = rcOptions.WaitTimeoutMs
                };

                foreach (string url in rcOptions.ClientEndpointUrls)
                {
                    manager.AddEndpoint(new Uri(url));
                }
                manager.StartService(configuration);
                return manager;
            }
        }

        private sealed class OpcUaClientBuilder : IOpcUaClientBuilder
        {
            public OpcUaClientBuilder(IServiceCollection services)
            {
                Services = services;
            }

            public IServiceCollection Services { get; }
        }

        /// <summary>
        /// Lazily creates and caches the connected <see cref="ManagedSession"/>.
        /// Multiple awaiters of the factory delegate share the single
        /// connection task.
        /// </summary>
        private sealed class ManagedSessionAccessor
        {
            public ManagedSessionAccessor(IServiceProvider sp)
            {
                m_sp = sp;
            }

            public Task<ManagedSession> ConnectAsync(CancellationToken ct)
            {
                lock (m_gate)
                {
                    m_connectTask ??= ConnectCoreAsync(ct);
                    return m_connectTask;
                }
            }

            private Task<ManagedSession> ConnectCoreAsync(CancellationToken ct)
            {
                OpcUaClientOptions options =
                    m_sp.GetRequiredService<OpcUaClientOptions>();
                ITelemetryContext telemetry =
                    m_sp.GetRequiredService<ITelemetryContext>();
                if (options.Configuration == null)
                {
                    throw new InvalidOperationException(
                        "OpcUaClientOptions.Configuration must be set before " +
                        "resolving ManagedSession.");
                }

                var builder = new ManagedSessionBuilder(options.Configuration, telemetry);
                if (options.Session.Endpoint != null)
                {
                    builder.UseEndpoint(options.Session.Endpoint);
                }
                builder.WithSessionName(options.Session.SessionName)
                       .WithSessionTimeout(options.Session.SessionTimeout)
                       .WithCheckDomain(options.Session.CheckDomain)
                       .WithReconnectPolicy(_ => options.Session.ReconnectPolicy);

                IClientIdentityProvider? identityProvider =
                    options.Session.IdentityProvider ?? ResolveIdentityProvider();
                if (identityProvider != null)
                {
                    builder.WithIdentityProvider(identityProvider);
                }
#pragma warning disable CS0618 // Legacy eager identity remains supported when no provider is configured.
                else if (options.Session.Identity != null)
                {
                    builder.WithUserIdentity(options.Session.Identity);
                }
#pragma warning restore CS0618

                TimeProvider timeProvider =
                    options.Session.TimeProvider ?? m_sp.GetRequiredService<TimeProvider>();
                builder.WithTimeProvider(timeProvider);

                if (options.Session.PreferredLocales is { Count: > 0 } locales)
                {
                    string[] arr = new string[locales.Count];
                    for (int i = 0; i < locales.Count; i++)
                    {
                        arr[i] = locales[i];
                    }
                    builder.WithPreferredLocales(arr);
                }
                if (options.Session.SubscriptionEngineFactory != null)
                {
                    builder.UseSubscriptionEngine(options.Session.SubscriptionEngineFactory);
                }
                if (options.Session.EnableServerRedundancy)
                {
                    builder.WithServerRedundancy();
                }
                if (options.Session.TransferSubscriptionsOnRecreate)
                {
                    builder.WithTransferSubscriptionsOnRecreate();
                }
                if (options.Session.PoolNotifications)
                {
                    builder.WithPoolNotifications();
                }

                return builder.ConnectAsync(ct);
            }

            private IClientIdentityProvider? ResolveIdentityProvider()
            {
                IEnumerable<IClientIdentityProvider> registered =
                    m_sp.GetServices<IClientIdentityProvider>();
                var providers = new List<IClientIdentityProvider>();
                foreach (IClientIdentityProvider provider in registered)
                {
                    providers.Add(provider);
                }

                if (providers.Count == 0)
                {
                    return null;
                }
                if (providers.Count == 1)
                {
                    return providers[0];
                }
                return new CompositeClientIdentityProvider(providers);
            }

            private readonly IServiceProvider m_sp;
            private Task<ManagedSession>? m_connectTask;
            private readonly Lock m_gate = new();
        }
    }
}
