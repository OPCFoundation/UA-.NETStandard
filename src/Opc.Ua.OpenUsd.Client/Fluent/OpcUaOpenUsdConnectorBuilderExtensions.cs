// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.OpenUsd.Client;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Dependency-injection extensions provided by <c>Opc.Ua.OpenUsd.Client</c>: register
    /// the singleton <see cref="OpenUsdConnectorFactory"/> so DI-hosted client hosts can
    /// resolve it and produce an <see cref="OpenUsdConnector"/> per connected session.
    /// The public <see cref="OpenUsdConnector"/> constructors remain the non-DI fallback.
    /// </summary>
    public static class OpcUaOpenUsdConnectorBuilderExtensions
    {
        /// <summary>
        /// Registers the singleton <see cref="OpenUsdConnectorFactory"/> and the
        /// <see cref="OpenUsdConnectorOptions"/> it uses, optionally configured via
        /// <paramref name="configure"/>. Idempotent; repeated calls do not add a second
        /// descriptor.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="services"/> is <c>null</c>.</exception>
        public static IServiceCollection AddOpenUsdConnector(
            this IServiceCollection services, Action<OpenUsdConnectorOptions>? configure = null)
        {
            if (services is null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            services.AddOpcUa();

            var options = new OpenUsdConnectorOptions();
            configure?.Invoke(options);
            services.TryAddSingleton(options);
            services.TryAddSingleton<OpenUsdConnectorFactory>();
            return services;
        }

        /// <summary>
        /// Registers the OpenUSD connector factory on the OPC UA builder.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        public static IOpcUaBuilder AddOpenUsdConnector(
            this IOpcUaBuilder builder, Action<OpenUsdConnectorOptions>? configure = null)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.Services.AddOpenUsdConnector(configure);
            return builder;
        }

        /// <summary>
        /// Registers the OpenUSD connector factory and keeps the client builder chain
        /// flowing (e.g. <c>AddClient(...).AddOpenUsdConnector()</c>).
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        public static IOpcUaClientBuilder AddOpenUsdConnector(
            this IOpcUaClientBuilder builder, Action<OpenUsdConnectorOptions>? configure = null)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.Services.AddOpenUsdConnector(configure);
            return builder;
        }
    }
}
