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

#pragma warning disable IDE0005 // Imports are required by target frameworks without matching implicit global usings.
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Opc.Ua;
#pragma warning restore IDE0005
using Opc.Ua.Client;
using Opc.Ua.ISA95.Client;
using Opc.Ua.ISA95.Client.Hosting;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// ISA-95 client registration extensions.
    /// </summary>
    public static class OpcUaIsa95ClientBuilderExtensions
    {
        /// <summary>
        /// Registers ISA-95 client services on an OPC UA builder.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        public static IOpcUaBuilder AddIsa95Client(
            this IOpcUaBuilder builder,
            Action<Isa95ClientOptions>? configure = null)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (configure is null)
            {
                builder.Services.AddOptions<Isa95ClientOptions>();
            }
            else
            {
                builder.Services.AddOptions<Isa95ClientOptions>().Configure(configure);
            }

            RegisterCoreServices(builder.Services);
            return builder;
        }

        /// <summary>
        /// Registers ISA-95 client services on an OPC UA client builder.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        public static IOpcUaClientBuilder AddIsa95Client(
            this IOpcUaClientBuilder builder,
            Action<Isa95ClientOptions>? configure = null)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            new BuilderAdapter(builder.Services).AddIsa95Client(configure);
            return builder;
        }

        private static void RegisterCoreServices(IServiceCollection services)
        {
            services.TryAddSingleton<ITelemetryContext>(
                static serviceProvider => new ServiceProviderTelemetryContext(serviceProvider));

            services.TryAddSingleton<Func<ISession, Isa95Client>>(static serviceProvider =>
            {
                ITelemetryContext telemetry = serviceProvider.GetRequiredService<ITelemetryContext>();
                return session => new Isa95Client(session, telemetry);
            });

            services.TryAddSingleton<Func<ManagedSession, Isa95Client>>(static serviceProvider =>
            {
                Func<ISession, Isa95Client> factory =
                    serviceProvider.GetRequiredService<Func<ISession, Isa95Client>>();
                return session => factory(session);
            });

            services.TryAddSingleton<Func<CancellationToken, Task<Isa95Client>>>(
                static serviceProvider => new Isa95ClientAccessor(serviceProvider).ConnectAsync);

            services.AddOpcUa();
        }

        private sealed class BuilderAdapter : IOpcUaBuilder
        {
            public BuilderAdapter(IServiceCollection services)
            {
                Services = services;
            }

            public IServiceCollection Services { get; }
        }

        private sealed class Isa95ClientAccessor
        {
            public Isa95ClientAccessor(IServiceProvider serviceProvider)
            {
                m_serviceProvider = serviceProvider;
            }

            public Task<Isa95Client> ConnectAsync(CancellationToken ct)
            {
                lock (m_gate)
                {
                    m_connectTask ??= ConnectCoreAsync(ct);
                    return m_connectTask;
                }
            }

            private async Task<Isa95Client> ConnectCoreAsync(CancellationToken ct)
            {
                Isa95ClientOptions options =
                    m_serviceProvider.GetRequiredService<IOptions<Isa95ClientOptions>>().Value;
                if (!options.LazyConnect)
                {
                    throw new InvalidOperationException(
                        "Isa95ClientOptions.LazyConnect is false. Resolve Func<ISession, Isa95Client> " +
                        "and supply an existing session.");
                }

                Func<CancellationToken, Task<ManagedSession>> sessionFactory =
                    m_serviceProvider.GetService<Func<CancellationToken, Task<ManagedSession>>>()
                    ?? throw new InvalidOperationException(
                        "AddIsa95Client requires AddClient to register a ManagedSession factory.");
                ManagedSession session = await sessionFactory(ct).ConfigureAwait(false);
                Func<ManagedSession, Isa95Client> factory =
                    m_serviceProvider.GetRequiredService<Func<ManagedSession, Isa95Client>>();
                return factory(session);
            }

            private readonly IServiceProvider m_serviceProvider;
            private readonly Lock m_gate = new();
            private Task<Isa95Client>? m_connectTask;
        }
    }
}
