// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua;
using Opc.Ua.Configuration;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// <see cref="IOpcUaBuilder"/> extensions provided by
    /// <c>Opc.Ua.Configuration</c>.
    /// </summary>
    public static class OpcUaConfigurationServiceCollectionExtensions
    {
        /// <summary>
        /// Configures the common OPC UA application identity and security
        /// settings used by subsequently registered client and server features.
        /// </summary>
        /// <remarks>
        /// Calling this method more than once on the same
        /// <see cref="IServiceCollection"/> (directly, or indirectly through
        /// <c>AddClient(...)</c> application identity options) reuses and
        /// mutates the single already-registered <see cref="OpcUaApplicationOptions"/>
        /// instance instead of registering a competing, order-dependent copy.
        /// Fields explicitly set by any caller win; the combination is
        /// independent of call order.
        /// </remarks>
        /// <param name="builder">The OPC UA builder.</param>
        /// <param name="configure">The application-options callback.</param>
        /// <returns>The same <paramref name="builder"/> instance.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/>
        /// or <paramref name="configure"/> is <c>null</c>.</exception>
        public static IOpcUaBuilder ConfigureApplication(
            this IOpcUaBuilder builder,
            Action<OpcUaApplicationOptions> configure)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }

            OpcUaApplicationOptions options = GetOrRegisterApplicationOptions(builder.Services);
            configure(options);
            builder.AddApplicationInstance();
            builder.Services.TryAddSingleton(sp =>
                new OpcUaApplicationConfigurationProvider(
                    sp.GetRequiredService<OpcUaApplicationOptions>(),
                    sp.GetRequiredService<IApplicationInstanceFactory>(),
                    sp.GetRequiredService<ITelemetryContext>(),
                    [.. sp.GetServices<IOpcUaApplicationConfigurationFeature>()],
                    sp.GetService<ICertificateManager>()));
            builder.Services.TryAddSingleton<IOpcUaApplicationConfigurationProvider>(
                sp => sp.GetRequiredService<OpcUaApplicationConfigurationProvider>());
            return builder;
        }

        /// <summary>
        /// Registers the default
        /// <see cref="IApplicationInstanceFactory"/> implementation so
        /// hosted OPC UA servers (regular, GDS, LDS, WotCon) can each
        /// create their own <see cref="IApplicationInstance"/>.
        /// </summary>
        /// <remarks>
        /// Idempotent (<c>TryAddSingleton</c>). A consumer that registers
        /// a custom <see cref="IApplicationInstanceFactory"/> before this
        /// call wins.
        /// </remarks>
        /// <param name="builder">The OPC UA builder.</param>
        /// <returns>The same <paramref name="builder"/> instance.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        public static IOpcUaBuilder AddApplicationInstance(this IOpcUaBuilder builder)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            builder.Services.TryAddSingleton<IApplicationInstanceFactory,
                DefaultApplicationInstanceFactory>();
            return builder;
        }

        /// <summary>
        /// Returns the <see cref="OpcUaApplicationOptions"/> singleton
        /// instance already registered on <paramref name="services"/> (by an
        /// earlier <see cref="ConfigureApplication"/> call), so repeated
        /// calls mutate one shared instance. Registers and returns a new
        /// instance when none exists yet.
        /// </summary>
        private static OpcUaApplicationOptions GetOrRegisterApplicationOptions(
            IServiceCollection services)
        {
            foreach (ServiceDescriptor descriptor in services)
            {
                if (descriptor.ServiceType == typeof(OpcUaApplicationOptions) &&
                    descriptor.ImplementationInstance is OpcUaApplicationOptions existing)
                {
                    return existing;
                }
            }

            var options = new OpcUaApplicationOptions();
            services.AddSingleton(options);
            return options;
        }
    }
}
