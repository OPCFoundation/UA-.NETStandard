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
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Opc.Ua;
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

            services.TryAddSingleton<IIsa95ClientFactory>(
                static serviceProvider => new Isa95ClientFactory(serviceProvider));

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

        private sealed class Isa95ClientFactory : IIsa95ClientFactory
        {
            public Isa95ClientFactory(IServiceProvider serviceProvider)
            {
                m_serviceProvider = serviceProvider;
                m_telemetry =
                    serviceProvider.GetRequiredService<ITelemetryContext>();
            }

            public Isa95Client Create(ISession session)
            {
                return new Isa95Client(session, m_telemetry);
            }

            public ValueTask<Isa95Client> ConnectAsync(
                CancellationToken cancellationToken = default)
            {
                lock (m_gate)
                {
                    m_connectTask ??= ConnectCoreAsync(cancellationToken);
                    return new ValueTask<Isa95Client>(m_connectTask);
                }
            }

            private async Task<Isa95Client> ConnectCoreAsync(CancellationToken ct)
            {
                Isa95ClientOptions options =
                    m_serviceProvider.GetRequiredService<IOptions<Isa95ClientOptions>>().Value;
                if (!options.LazyConnect)
                {
                    throw new InvalidOperationException(
                        "Isa95ClientOptions.LazyConnect is false. Call " +
                        "IIsa95ClientFactory.Create with an existing session.");
                }

                Func<CancellationToken, Task<ManagedSession>> sessionFactory =
                    m_serviceProvider.GetService<Func<CancellationToken, Task<ManagedSession>>>()
                    ?? throw new InvalidOperationException(
                        "AddIsa95Client requires AddClient to register a ManagedSession factory.");
                ManagedSession session = await sessionFactory(ct).ConfigureAwait(false);
                return Create(session);
            }

            private readonly IServiceProvider m_serviceProvider;
            private readonly ITelemetryContext m_telemetry;
            private readonly Lock m_gate = new();
            private Task<Isa95Client>? m_connectTask;
        }
    }
}
