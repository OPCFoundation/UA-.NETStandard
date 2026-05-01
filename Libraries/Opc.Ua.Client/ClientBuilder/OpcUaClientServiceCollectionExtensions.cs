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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Opc.Ua.Client
{
    /// <summary>
    /// <see cref="IServiceCollection"/> extensions to register OPC UA client
    /// services (telemetry, session factory, ManagedSession factory) for
    /// dependency-injected applications.
    /// </summary>
    /// <remarks>
    /// Mirrors the server-side
    /// <c>OpcUaServerServiceCollectionExtensions.AddOpcUaServer</c> pattern.
    /// The returned <see cref="IOpcUaClientBuilder"/> can be extended with
    /// further registrations.
    /// </remarks>
    public static class OpcUaClientServiceCollectionExtensions
    {
        /// <summary>
        /// Registers OPC UA client services and a lazy
        /// <see cref="Func{T, TResult}"/> factory for
        /// <see cref="ManagedSession"/>. The first call to the factory
        /// connects and caches the session; subsequent calls return the
        /// cached instance.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configure">Configuration delegate for
        /// <see cref="OpcUaClientOptions"/>. Must set
        /// <see cref="OpcUaClientOptions.Configuration"/> and
        /// <see cref="ManagedSessionOptions.Endpoint"/>.</param>
        /// <returns>An <see cref="IOpcUaClientBuilder"/> for chaining.</returns>
        public static IOpcUaClientBuilder AddOpcUaClient(
            this IServiceCollection services,
            Action<OpcUaClientOptions> configure)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }

            var options = new OpcUaClientOptions();
            configure(options);
            services.TryAddSingleton(options);

            services.TryAddSingleton<ITelemetryContext>(
                sp => new ServiceProviderTelemetryContext(sp));

            services.TryAddSingleton<ISessionFactory>(sp =>
            {
                ITelemetryContext telemetry = sp.GetRequiredService<ITelemetryContext>();
                return new DefaultSessionFactory(telemetry)
                {
                    SubscriptionEngineFactory =
                        options.Session.SubscriptionEngineFactory
                            ?? DefaultSubscriptionEngineFactory.Instance
                };
            });

            services.TryAddSingleton<ManagedSessionFactory>(sp =>
            {
                ITelemetryContext telemetry = sp.GetRequiredService<ITelemetryContext>();
                return new ManagedSessionFactory(telemetry);
            });

            services.TryAddSingleton<Func<CancellationToken, Task<ManagedSession>>>(
                sp => new ManagedSessionAccessor(sp).ConnectAsync);

            return new OpcUaClientBuilder(services);
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
            private readonly IServiceProvider m_sp;
            private Task<ManagedSession>? m_connectTask;
            private readonly object m_gate = new();

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
                if (options.Session.Identity != null)
                {
                    builder.WithUserIdentity(options.Session.Identity);
                }
                if (options.Session.PreferredLocales is { Count: > 0 } locales)
                {
                    var arr = new string[locales.Count];
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

                return builder.ConnectAsync(ct);
            }
        }

        /// <summary>
        /// <see cref="ITelemetryContext"/> implementation that obtains the
        /// host's <see cref="Microsoft.Extensions.Logging.ILoggerFactory"/>
        /// from DI when available, otherwise a no-op logger factory.
        /// </summary>
        private sealed class ServiceProviderTelemetryContext : ITelemetryContext
        {
            private readonly IServiceProvider m_sp;
            private readonly System.Diagnostics.ActivitySource m_activitySource = new("Opc.Ua.Client");

            public ServiceProviderTelemetryContext(IServiceProvider sp)
            {
                m_sp = sp;
            }

            public Microsoft.Extensions.Logging.ILoggerFactory LoggerFactory =>
                m_sp.GetService<Microsoft.Extensions.Logging.ILoggerFactory>()
                    ?? Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance;

            public System.Diagnostics.ActivitySource ActivitySource => m_activitySource;

            public System.Diagnostics.Metrics.Meter CreateMeter()
            {
                return new System.Diagnostics.Metrics.Meter("Opc.Ua.Client");
            }
        }
    }
}
