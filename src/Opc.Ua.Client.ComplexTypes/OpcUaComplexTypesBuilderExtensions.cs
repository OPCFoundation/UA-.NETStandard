// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.ComplexTypes;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// <see cref="IOpcUaBuilder"/> extensions provided by
    /// <c>Opc.Ua.Client.ComplexTypes</c>: register a
    /// <see cref="ComplexTypeSystemFactory"/> so that dependency-injected
    /// client hosts (e.g. <c>ManagedSession</c> consumers) can resolve
    /// typed complex-type loaders.
    /// </summary>
    public static class OpcUaComplexTypesBuilderExtensions
    {
        /// <summary>
        /// Registers the singleton <see cref="ComplexTypeSystemFactory"/>
        /// service so consumers can resolve it and produce a
        /// <see cref="ComplexTypeSystem"/> per <see cref="ISession"/>.
        /// </summary>
        /// <remarks>
        /// Idempotent (<see cref="ServiceCollectionDescriptorExtensions.TryAddSingleton{TService}(IServiceCollection)"/>):
        /// repeated calls do not add a second descriptor. The root
        /// <c>AddOpcUa()</c> call is invoked to ensure
        /// <see cref="ITelemetryContext"/> is registered even if the
        /// caller skipped chaining through it.
        /// </remarks>
        /// <param name="builder">The OPC UA builder.</param>
        /// <returns>The same <paramref name="builder"/> instance.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        public static IOpcUaBuilder AddComplexTypes(this IOpcUaBuilder builder)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.Services.AddOpcUa();
            builder.Services.TryAddSingleton<IComplexTypeSystemFactory, ComplexTypeSystemFactory>();
            builder.Services.TryAddSingleton<ComplexTypeSystemFactory>();
            return builder;
        }

        /// <summary>
        /// Registers the singleton <see cref="ComplexTypeSystemFactory"/>
        /// service and keeps the client builder chain flowing.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaClientBuilder AddComplexTypes(this IOpcUaClientBuilder builder)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            new BuilderAdapter(builder.Services).AddComplexTypes();
            return builder;
        }

        /// <summary>
        /// Registers client services, reconnect defaults, and complex-type
        /// loading in one call.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaClientBuilder AddManagedClient(
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

            IOpcUaClientBuilder clientBuilder = builder.AddClient(options =>
            {
                configure(options);
                options.Session = options.Session with { LoadComplexTypes = true };
            });
            return clientBuilder.AddComplexTypes();
        }

        /// <summary>
        /// Registers a managed client from the default configuration section and enables complex-type loading.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaClientBuilder AddManagedClient(
            this IOpcUaBuilder builder,
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

            return builder.AddManagedClient(
                configuration.GetSection(OpcUaClientBuilderExtensions.DefaultConfigurationSection));
        }

        /// <summary>
        /// Registers a managed client from configuration and enables complex-type loading.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaClientBuilder AddManagedClient(
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

            IOpcUaClientBuilder clientBuilder = builder.AddClient(
                section,
                options => options.Session = options.Session with { LoadComplexTypes = true });
            return clientBuilder.AddComplexTypes();
        }

        private sealed class BuilderAdapter : IOpcUaBuilder
        {
            public BuilderAdapter(IServiceCollection services)
            {
                Services = services;
            }

            public IServiceCollection Services { get; }
        }
    }
}
