// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

#if NET8_0_OR_GREATER
using System;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Opc.Ua;
using Opc.Ua.Bindings;
using Opc.Ua.Bindings.WebApi;
using Opc.Ua.Schema.OpenApi;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Extension methods that install the OPC UA REST binding
    /// (OPC UA Part 6 §G.3 "OpenAPI Mapping") on an
    /// <see cref="IOpcUaBuilder"/>. Composes with
    /// <c>AddHttpsTransport()</c>: by default the REST Minimal-API
    /// pipeline is mounted into the existing
    /// <c>HttpsTransportListener</c> Kestrel host so a single port
    /// serves binary, <c>opcua+uajson</c>, and REST traffic. The
    /// binding does not use MVC controllers and is fully
    /// NativeAOT-compatible.
    /// </summary>
    public static class OpcUaWebApiBuilderExtensions
    {
        private static readonly string[] s_httpsUriSchemes =
        [
            Utils.UriSchemeHttps,
            Utils.UriSchemeOpcHttps,
            Utils.UriSchemeWss,
            Utils.UriSchemeOpcWss
        ];

        /// <summary>
        /// Registers the OPC UA REST binding with the default
        /// shared-with-HTTPS hosting mode and Compact default encoding.
        /// </summary>
        /// <param name="builder">The OPC UA builder.</param>
        /// <returns>The same <paramref name="builder"/> instance.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="builder"/> is <c>null</c>.
        /// </exception>
        public static IOpcUaBuilder AddWebApiTransport(this IOpcUaBuilder builder)
        {
            return AddWebApiTransport(builder, configure: null);
        }

        /// <summary>
        /// Registers the OPC UA REST binding and returns the same specialized builder.
        /// </summary>
        /// <typeparam name="TBuilder">The specialized transport builder type.</typeparam>
        /// <param name="builder">The OPC UA transport builder.</param>
        /// <returns>The same <paramref name="builder"/> instance.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        public static TBuilder AddWebApiTransport<TBuilder>(this TBuilder builder)
            where TBuilder : IOpcUaTransportBuilder
        {
            ArgumentNullException.ThrowIfNull(builder);

            IOpcUaBuilder rootBuilder = builder.Services.AddOpcUa();
            rootBuilder.AddWebApiTransport();
            return builder;
        }

        /// <summary>
        /// Registers the OPC UA REST binding with caller-supplied options.
        /// </summary>
        /// <param name="builder">The OPC UA builder.</param>
        /// <param name="configure">
        /// Optional callback that customizes the
        /// <see cref="WebApiTransportOptions"/>. Pass <c>null</c> to use
        /// the defaults (shared HTTPS listener, Compact encoding).
        /// </param>
        /// <returns>The same <paramref name="builder"/> instance.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="builder"/> is <c>null</c>.
        /// </exception>
        public static IOpcUaBuilder AddWebApiTransport(
            this IOpcUaBuilder builder,
            Action<WebApiTransportOptions>? configure)
        {
            ArgumentNullException.ThrowIfNull(builder);

            IServiceCollection services = builder.Services;

            if (configure != null)
            {
                services.Configure(configure);
            }
            else
            {
                services.AddOptions<WebApiTransportOptions>();
            }

            // WebApiServer is the host-shared dispatcher; the listener
            // startup contributor (added below to every HTTPS / WSS
            // listener factory) attaches the host server's
            // ITransportListenerCallback to it when Kestrel starts.
            services.TryAddSingleton(sp =>
            {
                ITelemetryContext telemetry = sp.GetRequiredService<ITelemetryContext>();
                IServiceMessageContext context = sp.GetService<IServiceMessageContext>()
                    ?? ServiceMessageContext.Create(telemetry);
                return new WebApiServer(context, listenerId: "rest-api");
            });
            services.TryAddSingleton<IWebApiServer>(sp => sp.GetRequiredService<WebApiServer>());

            // Minimal-API endpoint mapping needs routing services only;
            // no MVC controllers or AddApplicationPart reflection scan.
            services.AddRouting();
            // Describes the routes in the OpenAPI document served when
            // WebApiTransportOptions.OpenApiDocumentPath is set.
            services.TryAddSingleton(static _ => new WebApiOpenApiGenerator());
            services.TryAddSingleton(sp =>
                new WebApiHttpsStartupContributor(
                    sp.GetRequiredService<WebApiServer>(),
                    sp,
                    sp.GetRequiredService<IOptions<WebApiTransportOptions>>().Value,
                    sp.GetRequiredService<WebApiOpenApiGenerator>()));
            services.AddSingleton<IHttpsListenerStartupContributor>(
                sp => sp.GetRequiredService<WebApiHttpsStartupContributor>());

            // Install the shared-host startup contributor on every HTTPS /
            // WSS listener factory. The transport binding registry runs
            // the configurators in registration order. HTTPS/WSS factories
            // also attach registered IHttpsListenerStartupContributor
            // services when they are created, so AddWebApiTransport() and
            // AddHttpsTransport()/AddWssTransport() are order-independent.
            services.AddTransportBindingRegistry();
            services.AddSingleton<ITransportBindingConfigurator>(sp =>
            {
                WebApiHttpsStartupContributor contributor =
                    sp.GetRequiredService<WebApiHttpsStartupContributor>();

                return new TransportBindingConfigurator(registry =>
                {
                    foreach (string scheme in s_httpsUriSchemes)
                    {
                        if (registry.GetListenerFactory(scheme) is HttpsServiceHost factory &&
                            !factory.StartupContributors.Contains(contributor))
                        {
                            factory.StartupContributors.Add(contributor);
                        }
                    }
                });
            });

            return builder;
        }

        /// <summary>
        /// Registers the configured OPC UA REST binding and returns the same specialized builder.
        /// </summary>
        /// <typeparam name="TBuilder">The specialized transport builder type.</typeparam>
        /// <param name="builder">The OPC UA transport builder.</param>
        /// <param name="configure">Optional callback that customizes the REST binding.</param>
        /// <returns>The same <paramref name="builder"/> instance.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        public static TBuilder AddWebApiTransport<TBuilder>(
            this TBuilder builder,
            Action<WebApiTransportOptions>? configure)
            where TBuilder : IOpcUaTransportBuilder
        {
            ArgumentNullException.ThrowIfNull(builder);

            IOpcUaBuilder rootBuilder = builder.Services.AddOpcUa();
            rootBuilder.AddWebApiTransport(configure);
            return builder;
        }
    }
}
#endif
