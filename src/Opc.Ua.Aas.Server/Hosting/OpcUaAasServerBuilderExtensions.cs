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
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Opc.Ua;
using Opc.Ua.Aas.Server;
using Opc.Ua.Aas.Server.Hosting;
using Opc.Ua.Aas.Server.Materialization;
using Opc.Ua.Aas.Server.V2;
using Opc.Ua.Aas.Server.V2.Hosting;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Registers the OPC UA AAS metamodel server feature.
    /// </summary>
    public static class OpcUaAasServerBuilderExtensions
    {
        /// <summary>
        /// Default configuration section name.
        /// </summary>
        public const string DefaultConfigurationSection = "OpcUa:Aas:Server";

        /// <summary>
        /// Registers the AAS server with an options callback.
        /// </summary>
        public static IAasServerBuilder AddAasV3Server(
            this IOpcUaBuilder builder,
            Action<AasServerOptions>? configure = null)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (configure is not null)
            {
                builder.Services.AddOptions<AasServerOptions>().Configure(configure);
            }
            else
            {
                builder.Services.AddOptions<AasServerOptions>();
            }
            RegisterCommonServices(builder.Services);
            return new AasServerBuilder(builder.Services);
        }

        /// <summary>
        /// Registers the OPC 30270 AAS V2 server with an options callback.
        /// </summary>
        public static IAasV2ServerBuilder AddAasV2Server(
            this IOpcUaBuilder builder,
            Action<AasServerOptions>? configure = null)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (configure is not null)
            {
                builder.Services.AddOptions<AasServerOptions>().Configure(configure);
            }
            else
            {
                builder.Services.AddOptions<AasServerOptions>();
            }
            RegisterV2Services(builder.Services);
            return new AasV2ServerBuilder(builder.Services);
        }

        /// <summary>
        /// Registers the OPC 30270 AAS V2 server from the default configuration section.
        /// </summary>
        public static IAasV2ServerBuilder AddAasV2Server(this IOpcUaBuilder builder, IConfiguration configuration)
        {
            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }
            return builder.AddAasV2Server(configuration.GetSection(DefaultConfigurationSection));
        }

        /// <summary>
        /// Registers the OPC 30270 AAS V2 server from a configuration section.
        /// </summary>
        public static IAasV2ServerBuilder AddAasV2Server(this IOpcUaBuilder builder, IConfigurationSection section)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (section is null)
            {
                throw new ArgumentNullException(nameof(section));
            }
            builder.Services.AddOptions<AasServerOptions>().Configure(options =>
            {
                options.ControlNamespaceUri = section[nameof(AasServerOptions.ControlNamespaceUri)] ??
                    options.ControlNamespaceUri;
                options.EnvironmentFolder = section[nameof(AasServerOptions.EnvironmentFolder)] ??
                    options.EnvironmentFolder;
                string? retirementPolicy = section[nameof(AasServerOptions.RetirementPolicy)];
                if (Enum.TryParse(retirementPolicy, ignoreCase: true, out AasProjectionRetirementPolicy parsed))
                {
                    options.RetirementPolicy = parsed;
                }
            });
            RegisterV2Services(builder.Services);
            return new AasV2ServerBuilder(builder.Services);
        }

        /// <summary>
        /// Registers the AAS server from the default configuration section.
        /// </summary>
        public static IAasServerBuilder AddAasV3Server(this IOpcUaBuilder builder, IConfiguration configuration)
        {
            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }
            return builder.AddAasV3Server(configuration.GetSection(DefaultConfigurationSection));
        }

        /// <summary>
        /// Registers the AAS server from a configuration section.
        /// </summary>
        public static IAasServerBuilder AddAasV3Server(this IOpcUaBuilder builder, IConfigurationSection section)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (section is null)
            {
                throw new ArgumentNullException(nameof(section));
            }
            builder.Services.AddOptions<AasServerOptions>().Configure(options =>
            {
                options.ControlNamespaceUri = section[nameof(AasServerOptions.ControlNamespaceUri)] ??
                    options.ControlNamespaceUri;
                options.EnvironmentFolder = section[nameof(AasServerOptions.EnvironmentFolder)] ??
                    options.EnvironmentFolder;
                string? retirementPolicy = section[nameof(AasServerOptions.RetirementPolicy)];
                if (Enum.TryParse(retirementPolicy, ignoreCase: true, out AasProjectionRetirementPolicy parsed))
                {
                    options.RetirementPolicy = parsed;
                }
            });
            RegisterCommonServices(builder.Services);
            return new AasServerBuilder(builder.Services);
        }

        private static void RegisterCommonServices(IServiceCollection services)
        {
            services.TryAddSingleton<ITelemetryContext>(
                sp => new ServiceProviderTelemetryContext(sp));
            services.TryAddSingleton<IAasValueProvider, DocumentAasValueProvider>();
            services.TryAddSingleton<IAasOperationHandler, DefaultAasOperationHandler>();
            services.TryAddSingleton<IAasEnvironmentProjectionHost>(sp =>
                new LifecycleAasEnvironmentProjectionHost(
                    sp.GetRequiredService<INodeManagerLifecycle>()));
            services.TryAddSingleton<IAasEnvironmentProvider>(sp =>
            {
                AasServerOptions options = sp.GetRequiredService<IOptions<AasServerOptions>>().Value;
                return string.IsNullOrEmpty(options.EnvironmentFolder)
                    ? new InMemoryAasEnvironmentProvider([])
                    : new FolderAasEnvironmentProvider(options.EnvironmentFolder!);
            });
            services.TryAddSingleton(sp =>
            {
                AasServerOptions options = sp.GetRequiredService<IOptions<AasServerOptions>>().Value;
                return new AasEnvironmentNodeManagerFactory(
                    options,
                    sp.GetRequiredService<IAasEnvironmentProvider>(),
                    sp.GetRequiredService<IAasValueProvider>(),
                    sp.GetRequiredService<IAasOperationHandler>(),
                    sp.GetRequiredService<IAasEnvironmentProjectionHost>());
            });
            services.AddSingleton(sp =>
                new OpcUaServerNodeManagerRegistration(
                    sp.GetRequiredService<AasEnvironmentNodeManagerFactory>()));
            services.AddOpcUa();
        }

        private static void RegisterV2Services(IServiceCollection services)
        {
            services.TryAddSingleton<ITelemetryContext>(
                sp => new ServiceProviderTelemetryContext(sp));

            // Registered under the V2 contract rather than the shared one. Both
            // generations can be added to one host - the stack does not forbid
            // it - and a single IAasValueProvider registration would then be
            // won by whichever was added first, leaving the other AddressSpace
            // reading through the wrong generation's documents with nothing to
            // show for it.
            services.TryAddSingleton<IAasV2ValueProvider, DocumentAasV2ValueProvider>();
            services.TryAddSingleton<IAasOperationHandler, DefaultAasOperationHandler>();
            services.TryAddSingleton<IAasEnvironmentProjectionHost>(sp =>
                new LifecycleAasEnvironmentProjectionHost(
                    sp.GetRequiredService<INodeManagerLifecycle>()));
            services.TryAddSingleton<IAasV2EnvironmentProvider>(sp =>
            {
                AasServerOptions options = sp.GetRequiredService<IOptions<AasServerOptions>>().Value;
                return string.IsNullOrEmpty(options.EnvironmentFolder)
                    ? new InMemoryAasV2EnvironmentProvider([])
                    : new FolderAasV2EnvironmentProvider(options.EnvironmentFolder!);
            });
            services.TryAddSingleton(sp =>
            {
                AasServerOptions options = sp.GetRequiredService<IOptions<AasServerOptions>>().Value;
                return new AasV2EnvironmentNodeManagerFactory(
                    options,
                    sp.GetRequiredService<IAasV2EnvironmentProvider>(),
                    sp.GetRequiredService<IAasV2ValueProvider>(),
                    sp.GetRequiredService<IAasOperationHandler>(),
                    sp.GetRequiredService<IAasEnvironmentProjectionHost>());
            });
            services.AddSingleton(sp =>
                new OpcUaServerNodeManagerRegistration(
                    sp.GetRequiredService<AasV2EnvironmentNodeManagerFactory>()));
            services.AddOpcUa();
        }

        private sealed class AasServerBuilder : IAasServerBuilder
        {
            public AasServerBuilder(IServiceCollection services)
            {
                Services = services ?? throw new ArgumentNullException(nameof(services));
            }

            public IServiceCollection Services { get; }

            public IAasServerBuilder AddEnvironmentProvider<
                [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
                where T : class, IAasEnvironmentProvider
            {
                Services.AddSingleton<IAasEnvironmentProvider, T>();
                return this;
            }

            public IAasServerBuilder AddEnvironmentProvider(Func<IServiceProvider, IAasEnvironmentProvider> factory)
            {
                if (factory is null)
                {
                    throw new ArgumentNullException(nameof(factory));
                }
                Services.AddSingleton(factory);
                return this;
            }

            public IAasServerBuilder AddOperationHandler<
                [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
                where T : class, IAasOperationHandler
            {
                Services.AddSingleton<IAasOperationHandler, T>();
                return this;
            }
        }

        private sealed class AasV2ServerBuilder : IAasV2ServerBuilder
        {
            public AasV2ServerBuilder(IServiceCollection services)
            {
                Services = services ?? throw new ArgumentNullException(nameof(services));
            }

            public IServiceCollection Services { get; }

            public IAasV2ServerBuilder AddEnvironmentProvider<
                [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
                where T : class, IAasV2EnvironmentProvider
            {
                Services.AddSingleton<IAasV2EnvironmentProvider, T>();
                return this;
            }

            public IAasV2ServerBuilder AddEnvironmentProvider(
                Func<IServiceProvider, IAasV2EnvironmentProvider> factory)
            {
                if (factory is null)
                {
                    throw new ArgumentNullException(nameof(factory));
                }
                Services.AddSingleton(factory);
                return this;
            }

            public IAasV2ServerBuilder AddOperationHandler<
                [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
                where T : class, IAasOperationHandler
            {
                Services.AddSingleton<IAasOperationHandler, T>();
                return this;
            }
        }
    }
}
