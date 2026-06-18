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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.WotCon.Client;
using Opc.Ua.WotCon.Client.Hosting;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// <see cref="IOpcUaBuilder"/> extensions provided by
    /// <c>Opc.Ua.WotCon.Client</c>: register a WoT Connectivity
    /// (OPC 10100-1) client wrapped around the
    /// <see cref="ManagedSession"/> registered by <c>.AddClient(...)</c>.
    /// </summary>
    /// <remarks>
    /// The client surface is composable on top of the regular OPC UA
    /// client. The factory delegate
    /// <see cref="Func{T, TResult}"/> registered by this method resolves
    /// the connected <see cref="ManagedSession"/> lazily and wraps it in
    /// a <see cref="WotConnectivityClient"/> rooted at the server's
    /// <c>WoTAssetConnectionManagement</c> entry point.
    /// </remarks>
    public static class OpcUaWotConClientBuilderExtensions
    {
        /// <summary>
        /// Default <see cref="IConfiguration"/> section name used by the
        /// <see cref="AddWotConClient(IOpcUaBuilder, IConfiguration)"/>
        /// overload.
        /// </summary>
        public const string DefaultConfigurationSection = "OpcUa:WotCon:Client";

        /// <summary>
        /// Registers WoT Connectivity client services and a lazy
        /// factory delegate that resolves a
        /// <see cref="WotConnectivityClient"/> rooted at the connected
        /// server's <c>WoTAssetConnectionManagement</c> entry point.
        /// </summary>
        /// <param name="builder">The OPC UA builder.</param>
        /// <param name="configure">Optional callback used to populate
        /// <see cref="WotConClientOptions"/>.</param>
        /// <returns>The same <paramref name="builder"/> instance for
        /// fluent chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/>
        /// is <c>null</c>.</exception>
        public static IOpcUaBuilder AddWotConClient(
            this IOpcUaBuilder builder,
            Action<WotConClientOptions>? configure = null)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (configure is null)

            {
                builder.Services.AddOptions<WotConClientOptions>();
            }
            else
            {
                builder.Services.AddOptions<WotConClientOptions>().Configure(configure);
            }

            RegisterCoreServices(builder.Services);

            return builder;
        }

        /// <summary>
        /// Registers WoT Connectivity client services with options
        /// bound from the supplied <paramref name="configuration"/>
        /// section <see cref="DefaultConfigurationSection"/>
        /// (<c>OpcUa:WotCon:Client</c>).
        /// </summary>
        /// <remarks>
        /// AOT-safe: bound by the .NET 8+ configuration binding source
        /// generator (<c>EnableConfigurationBindingGenerator</c>).
        /// </remarks>
        /// <param name="builder">The OPC UA builder.</param>
        /// <param name="configuration">Configuration root containing
        /// the <c>OpcUa:WotCon:Client</c> section.</param>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/>
        /// or <paramref name="configuration"/> is <c>null</c>.</exception>
        public static IOpcUaBuilder AddWotConClient(
            this IOpcUaBuilder builder,
            IConfiguration configuration)
        {
            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }
            return builder.AddWotConClient(configuration.GetSection(DefaultConfigurationSection));
        }

        /// <summary>
        /// Registers WoT Connectivity client services with options
        /// bound from the supplied <paramref name="section"/>.
        /// </summary>
        /// <remarks>
        /// AOT-safe: bound by the .NET 8+ configuration binding source
        /// generator (<c>EnableConfigurationBindingGenerator</c>).
        /// </remarks>
        /// <param name="builder">The OPC UA builder.</param>
        /// <param name="section">Configuration section to bind.</param>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/>
        /// or <paramref name="section"/> is <c>null</c>.</exception>
        public static IOpcUaBuilder AddWotConClient(
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
            builder.Services.AddOptions<WotConClientOptions>().Bind(section);
            RegisterCoreServices(builder.Services);

            return builder;
        }

        private static void RegisterCoreServices(IServiceCollection services)
        {
            services.TryAddSingleton<ITelemetryContext>(
                sp => new ServiceProviderTelemetryContext(sp));

            services.TryAddSingleton<Func<CancellationToken, Task<WotConnectivityClient>>>(
                sp => new WotConnectivityClientAccessor(sp).ConnectAsync);

            services.AddOpcUa();
        }

        /// <summary>
        /// Lazily resolves the registered <see cref="ManagedSession"/>
        /// factory delegate, awaits it once and wraps the resulting
        /// session in a <see cref="WotConnectivityClient"/>. The wrapper
        /// is cached so repeated awaiters share the same instance.
        /// </summary>
        private sealed class WotConnectivityClientAccessor
        {
            public WotConnectivityClientAccessor(IServiceProvider sp)
            {
                m_sp = sp;
            }

            public Task<WotConnectivityClient> ConnectAsync(CancellationToken ct)
            {
                lock (m_gate)
                {
                    m_connectTask ??= ConnectCoreAsync(ct);
                    return m_connectTask;
                }
            }

            private async Task<WotConnectivityClient> ConnectCoreAsync(CancellationToken ct)
            {
                Func<CancellationToken, Task<ManagedSession>> sessionFactory =
                    m_sp.GetService<Func<CancellationToken, Task<ManagedSession>>>()
                    ?? throw new InvalidOperationException(
                        "AddWotConClient requires AddClient to have been called first " +
                        "so a ManagedSession factory is registered.");

                ITelemetryContext telemetry =
                    m_sp.GetRequiredService<ITelemetryContext>();

                ManagedSession session =
                    await sessionFactory(ct).ConfigureAwait(false);
                return await WotConnectivityClient
                    .ForServerAsync(session, telemetry, ct)
                    .ConfigureAwait(false);
            }

            private readonly IServiceProvider m_sp;
            private Task<WotConnectivityClient>? m_connectTask;
            private readonly Lock m_gate = new();
        }
    }
}
