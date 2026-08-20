/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using Microsoft.Extensions.Options;
using Opc.Ua;
using Opc.Ua.Aas.Client;
using Opc.Ua.Aas.Client.Registry;
using Opc.Ua.Aas.Client.Hosting;
using Opc.Ua.Client;
using AasV2Client = Opc.Ua.Aas.Client.V2.AasClient;
using AasV2ClientOptions = Opc.Ua.Aas.Client.V2.AasClientOptions;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// <see cref="IOpcUaBuilder"/> extensions that register the AAS metamodel client.
    /// </summary>
    public static class OpcUaAasClientBuilderExtensions
    {
        /// <summary>
        /// Default <see cref="IConfiguration"/> section name for <see cref="AddAasV3Client(IOpcUaBuilder, IConfiguration)"/>.
        /// </summary>
        public const string DefaultConfigurationSection = "OpcUa:Aas:Client";

        /// <summary>
        /// Registers AAS V2 metamodel client services.
        /// </summary>
        public static IOpcUaBuilder AddAasV2Client(
            this IOpcUaBuilder builder,
            Action<AasV2ClientOptions>? configure = null)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            if (configure is null)
            {
                builder.Services.AddOptions<AasV2ClientOptions>();
            }
            else
            {
                builder.Services.AddOptions<AasV2ClientOptions>().Configure(configure);
            }

            RegisterV2CoreServices(builder.Services);
            return builder;
        }

        /// <summary>
        /// Registers AAS V2 metamodel client services with options bound from the default section.
        /// </summary>
        public static IOpcUaBuilder AddAasV2Client(
            this IOpcUaBuilder builder,
            IConfiguration configuration)
        {
            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            return builder.AddAasV2Client(configuration.GetSection(DefaultConfigurationSection));
        }

        /// <summary>
        /// Registers AAS V2 metamodel client services with options bound from a configuration section.
        /// </summary>
        public static IOpcUaBuilder AddAasV2Client(
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

            builder.Services.AddOptions<AasV2ClientOptions>().Configure(
                options => ConfigureFromSection(options, section));
            RegisterV2CoreServices(builder.Services);
            return builder;
        }

        /// <summary>
        /// Registers AAS V2 metamodel client services on an existing OPC UA client builder.
        /// </summary>
        public static IOpcUaClientBuilder AddAasV2Client(
            this IOpcUaClientBuilder builder,
            Action<AasV2ClientOptions>? configure = null)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            new BuilderAdapter(builder.Services).AddAasV2Client(configure);
            return builder;
        }

        /// <summary>
        /// Registers AAS V2 metamodel client services on an existing OPC UA client builder.
        /// </summary>
        public static IOpcUaClientBuilder AddAasV2Client(
            this IOpcUaClientBuilder builder,
            IConfiguration configuration)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            new BuilderAdapter(builder.Services).AddAasV2Client(configuration);
            return builder;
        }

        /// <summary>
        /// Registers AAS V2 metamodel client services on an existing OPC UA client builder.
        /// </summary>
        public static IOpcUaClientBuilder AddAasV2Client(
            this IOpcUaClientBuilder builder,
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

            new BuilderAdapter(builder.Services).AddAasV2Client(section);
            return builder;
        }

        /// <summary>
        /// Registers AAS metamodel client services.
        /// </summary>
        public static IOpcUaBuilder AddAasV3Client(
            this IOpcUaBuilder builder,
            Action<AasClientOptions>? configure = null)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            if (configure is null)
            {
                builder.Services.AddOptions<AasClientOptions>();
            }
            else
            {
                builder.Services.AddOptions<AasClientOptions>().Configure(configure);
            }

            RegisterCoreServices(builder.Services);
            return builder;
        }

        /// <summary>
        /// Registers AAS metamodel client services with options bound from the default section.
        /// </summary>
        public static IOpcUaBuilder AddAasV3Client(
            this IOpcUaBuilder builder,
            IConfiguration configuration)
        {
            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            return builder.AddAasV3Client(configuration.GetSection(DefaultConfigurationSection));
        }

        /// <summary>
        /// Registers AAS metamodel client services with options bound from a configuration section.
        /// </summary>
        public static IOpcUaBuilder AddAasV3Client(
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

            builder.Services.AddOptions<AasClientOptions>().Configure(options => ConfigureFromSection(options, section));
            RegisterCoreServices(builder.Services);
            return builder;
        }

        /// <summary>
        /// Registers AAS metamodel client services on an existing OPC UA client builder.
        /// </summary>
        public static IOpcUaClientBuilder AddAasV3Client(
            this IOpcUaClientBuilder builder,
            Action<AasClientOptions>? configure = null)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            new BuilderAdapter(builder.Services).AddAasV3Client(configure);
            return builder;
        }

        /// <summary>
        /// Registers AAS metamodel client services on an existing OPC UA client builder.
        /// </summary>
        public static IOpcUaClientBuilder AddAasV3Client(
            this IOpcUaClientBuilder builder,
            IConfiguration configuration)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            new BuilderAdapter(builder.Services).AddAasV3Client(configuration);
            return builder;
        }

        /// <summary>
        /// Registers AAS metamodel client services on an existing OPC UA client builder.
        /// </summary>
        public static IOpcUaClientBuilder AddAasV3Client(
            this IOpcUaClientBuilder builder,
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

            new BuilderAdapter(builder.Services).AddAasV3Client(section);
            return builder;
        }

        /// <summary>
        /// Registers AAS registry client services.
        /// </summary>
        public static IOpcUaBuilder AddAasV3RegistryClient(
            this IOpcUaBuilder builder,
            Action<AasClientOptions>? configure = null)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            if (configure is null)
            {
                builder.Services.AddOptions<AasClientOptions>();
            }
            else
            {
                builder.Services.AddOptions<AasClientOptions>().Configure(configure);
            }

            RegisterRegistryServices(builder.Services);
            return builder;
        }

        /// <summary>
        /// Registers AAS registry client services with options bound from the default section.
        /// </summary>
        public static IOpcUaBuilder AddAasV3RegistryClient(
            this IOpcUaBuilder builder,
            IConfiguration configuration)
        {
            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            return builder.AddAasV3RegistryClient(configuration.GetSection(DefaultConfigurationSection));
        }

        /// <summary>
        /// Registers AAS registry client services with options bound from a configuration section.
        /// </summary>
        public static IOpcUaBuilder AddAasV3RegistryClient(
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

            builder.Services.AddOptions<AasClientOptions>().Configure(options => ConfigureFromSection(options, section));
            RegisterRegistryServices(builder.Services);
            return builder;
        }

        /// <summary>
        /// Registers AAS registry client services on an existing OPC UA client builder.
        /// </summary>
        public static IOpcUaClientBuilder AddAasV3RegistryClient(
            this IOpcUaClientBuilder builder,
            Action<AasClientOptions>? configure = null)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            new BuilderAdapter(builder.Services).AddAasV3RegistryClient(configure);
            return builder;
        }

        /// <summary>
        /// Registers AAS registry client services on an existing OPC UA client builder.
        /// </summary>
        public static IOpcUaClientBuilder AddAasV3RegistryClient(
            this IOpcUaClientBuilder builder,
            IConfiguration configuration)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            new BuilderAdapter(builder.Services).AddAasV3RegistryClient(configuration);
            return builder;
        }

        /// <summary>
        /// Registers AAS registry client services on an existing OPC UA client builder.
        /// </summary>
        public static IOpcUaClientBuilder AddAasV3RegistryClient(
            this IOpcUaClientBuilder builder,
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

            new BuilderAdapter(builder.Services).AddAasV3RegistryClient(section);
            return builder;
        }

        private static void RegisterCoreServices(IServiceCollection services)
        {
            services.TryAddSingleton<ITelemetryContext>(
                sp => new ServiceProviderTelemetryContext(sp));

            services.TryAddSingleton<Func<ManagedSession, CancellationToken, Task<AasClient>>>(sp =>
            {
                ITelemetryContext telemetry = sp.GetRequiredService<ITelemetryContext>();
                IOptions<AasClientOptions> options = sp.GetRequiredService<IOptions<AasClientOptions>>();
                return (session, ct) => Task.FromResult(CreateClient(session, telemetry, options.Value, ct));
            });

            services.TryAddSingleton(sp => new AasClientAccessor(sp));
            services.TryAddSingleton<Func<CancellationToken, Task<AasClient>>>(
                sp => sp.GetRequiredService<AasClientAccessor>().ConnectAsync);

            services.AddOpcUa();
        }

        private static void RegisterV2CoreServices(IServiceCollection services)
        {
            services.TryAddSingleton<ITelemetryContext>(
                sp => new ServiceProviderTelemetryContext(sp));

            services.TryAddSingleton<Func<ManagedSession, CancellationToken, Task<AasV2Client>>>(sp =>
            {
                ITelemetryContext telemetry = sp.GetRequiredService<ITelemetryContext>();
                IOptions<AasV2ClientOptions> options = sp.GetRequiredService<IOptions<AasV2ClientOptions>>();
                return (session, ct) => Task.FromResult(CreateV2Client(session, telemetry, options.Value, ct));
            });

            services.TryAddSingleton(sp => new AasV2ClientAccessor(sp));
            services.TryAddSingleton<Func<CancellationToken, Task<AasV2Client>>>(
                sp => sp.GetRequiredService<AasV2ClientAccessor>().ConnectAsync);

            services.AddOpcUa();
        }

        private static void RegisterRegistryServices(IServiceCollection services)
        {
            services.TryAddSingleton<ITelemetryContext>(
                sp => new ServiceProviderTelemetryContext(sp));

            services.TryAddSingleton<Func<ManagedSession, CancellationToken, Task<AasRegistryClient>>>(sp =>
            {
                ITelemetryContext telemetry = sp.GetRequiredService<ITelemetryContext>();
                return async (session, ct) => await AasRegistryClient
                    .ForServerAsync(session, telemetry, ct)
                    .ConfigureAwait(false);
            });

            services.TryAddSingleton(sp => new AasRegistryClientAccessor(sp));
            services.TryAddSingleton<Func<CancellationToken, Task<AasRegistryClient>>>(
                sp => sp.GetRequiredService<AasRegistryClientAccessor>().ConnectAsync);

            services.AddOpcUa();
        }

        private static AasClient CreateClient(
            ManagedSession session,
            ITelemetryContext telemetry,
            AasClientOptions options,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (session is null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            ushort ns = session.NamespaceUris.GetIndexOrAppend(options.InstanceNamespaceUri);
            return new AasClient(session, ns, telemetry);
        }

        private static AasV2Client CreateV2Client(
            ManagedSession session,
            ITelemetryContext telemetry,
            AasV2ClientOptions options,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (session is null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            ushort ns = session.NamespaceUris.GetIndexOrAppend(options.InstanceNamespaceUri);
            return new AasV2Client(session, ns, telemetry);
        }

        private static void ConfigureFromSection(AasClientOptions options, IConfigurationSection section)
        {
            if (bool.TryParse(section[nameof(AasClientOptions.LazyConnect)], out bool lazyConnect))
            {
                options.LazyConnect = lazyConnect;
            }

            string? instanceNamespaceUri = section[nameof(AasClientOptions.InstanceNamespaceUri)];
            if (!string.IsNullOrEmpty(instanceNamespaceUri))
            {
                options.InstanceNamespaceUri = instanceNamespaceUri;
            }
        }

        private static void ConfigureFromSection(AasV2ClientOptions options, IConfigurationSection section)
        {
            if (bool.TryParse(section[nameof(AasV2ClientOptions.LazyConnect)], out bool lazyConnect))
            {
                options.LazyConnect = lazyConnect;
            }

            string? instanceNamespaceUri = section[nameof(AasV2ClientOptions.InstanceNamespaceUri)];
            if (!string.IsNullOrEmpty(instanceNamespaceUri))
            {
                options.InstanceNamespaceUri = instanceNamespaceUri;
            }
        }

        private sealed class BuilderAdapter : IOpcUaBuilder
        {
            public BuilderAdapter(IServiceCollection services)
            {
                Services = services;
            }

            public IServiceCollection Services { get; }
        }

        private sealed class AasClientAccessor
        {
            public AasClientAccessor(IServiceProvider sp)
            {
                m_sp = sp;
            }

            public Task<AasClient> ConnectAsync(CancellationToken ct)
            {
                lock (m_gate)
                {
                    if (m_connectTask is null ||
                        (m_connectTask.IsCompleted && m_connectTask.Status != TaskStatus.RanToCompletion))
                    {
                        m_connectTask = ConnectCoreAsync(ct);
                    }

                    return m_connectTask;
                }
            }

            private async Task<AasClient> ConnectCoreAsync(CancellationToken ct)
            {
                AasClientOptions options = m_sp.GetRequiredService<IOptions<AasClientOptions>>().Value;
                if (!options.LazyConnect)
                {
                    throw new InvalidOperationException(
                        "AasV2ClientOptions.LazyConnect is false. Resolve " +
                        "Func<ManagedSession, CancellationToken, Task<AasClient>> " +
                        "and supply an already connected session.");
                }

                Func<CancellationToken, Task<ManagedSession>> sessionFactory =
                    m_sp.GetService<Func<CancellationToken, Task<ManagedSession>>>()
                    ?? throw new InvalidOperationException(
                        "AddAasV3Client requires AddClient to have been called first so a ManagedSession factory is registered.");

                ManagedSession session = await sessionFactory(ct).ConfigureAwait(false);
                ITelemetryContext telemetry = m_sp.GetRequiredService<ITelemetryContext>();
                return CreateClient(session, telemetry, options, ct);
            }

            private readonly IServiceProvider m_sp;
            private Task<AasClient>? m_connectTask;
            private readonly Lock m_gate = new();
        }

        private sealed class AasV2ClientAccessor
        {
            public AasV2ClientAccessor(IServiceProvider sp)
            {
                m_sp = sp;
            }

            public Task<AasV2Client> ConnectAsync(CancellationToken ct)
            {
                lock (m_gate)
                {
                    if (m_connectTask is null ||
                        (m_connectTask.IsCompleted && m_connectTask.Status != TaskStatus.RanToCompletion))
                    {
                        m_connectTask = ConnectCoreAsync(ct);
                    }

                    return m_connectTask;
                }
            }

            private async Task<AasV2Client> ConnectCoreAsync(CancellationToken ct)
            {
                AasV2ClientOptions options = m_sp.GetRequiredService<IOptions<AasV2ClientOptions>>().Value;
                if (!options.LazyConnect)
                {
                    throw new InvalidOperationException(
                        "AasClientOptions.LazyConnect is false. Resolve " +
                        "Func<ManagedSession, CancellationToken, Task<AasClient>> and supply an already connected session.");
                }

                Func<CancellationToken, Task<ManagedSession>> sessionFactory =
                    m_sp.GetService<Func<CancellationToken, Task<ManagedSession>>>()
                    ?? throw new InvalidOperationException(
                        "AddAasV2Client requires AddClient to have been called first so a ManagedSession factory is registered.");

                ManagedSession session = await sessionFactory(ct).ConfigureAwait(false);
                ITelemetryContext telemetry = m_sp.GetRequiredService<ITelemetryContext>();
                return CreateV2Client(session, telemetry, options, ct);
            }

            private readonly IServiceProvider m_sp;
            private Task<AasV2Client>? m_connectTask;
            private readonly Lock m_gate = new();
        }

        private sealed class AasRegistryClientAccessor
        {
            public AasRegistryClientAccessor(IServiceProvider sp)
            {
                m_sp = sp;
            }

            public Task<AasRegistryClient> ConnectAsync(CancellationToken ct)
            {
                lock (m_gate)
                {
                    if (m_connectTask is null ||
                        (m_connectTask.IsCompleted && m_connectTask.Status != TaskStatus.RanToCompletion))
                    {
                        m_connectTask = ConnectCoreAsync(ct);
                    }

                    return m_connectTask;
                }
            }

            private async Task<AasRegistryClient> ConnectCoreAsync(CancellationToken ct)
            {
                AasClientOptions options = m_sp.GetRequiredService<IOptions<AasClientOptions>>().Value;
                if (!options.LazyConnect)
                {
                    throw new InvalidOperationException(
                        "AasClientOptions.LazyConnect is false. Resolve " +
                        "Func<ManagedSession, CancellationToken, Task<AasRegistryClient>> " +
                        "and supply an already connected session.");
                }

                Func<CancellationToken, Task<ManagedSession>> sessionFactory =
                    m_sp.GetService<Func<CancellationToken, Task<ManagedSession>>>()
                    ?? throw new InvalidOperationException(
                        "AddAasV3RegistryClient requires AddClient to have been called first so a ManagedSession factory is registered.");

                ManagedSession session = await sessionFactory(ct).ConfigureAwait(false);
                ITelemetryContext telemetry = m_sp.GetRequiredService<ITelemetryContext>();
                return await AasRegistryClient.ForServerAsync(session, telemetry, ct).ConfigureAwait(false);
            }

            private readonly IServiceProvider m_sp;
            private Task<AasRegistryClient>? m_connectTask;
            private readonly Lock m_gate = new();
        }
    }
}
