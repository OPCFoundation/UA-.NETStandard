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
using System.Collections.Generic;
#if NET8_0_OR_GREATER
using Microsoft.AspNetCore.RateLimiting;
#endif
using Opc.Ua;
using Opc.Ua.Bindings;
#if NET8_0_OR_GREATER
using Opc.Ua.Bindings.WebApi;
#endif

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Extension methods that install the HTTPS and WSS transport
    /// bindings (Kestrel-backed listener factories plus the UASC channel
    /// factories) into the host's <see cref="ITransportBindingRegistry"/>.
    /// Both <c>https://</c> + <c>opc.https://</c> and
    /// <c>wss://</c> + <c>opc.wss://</c> URL schemes are covered.
    /// </summary>
    public static class OpcUaHttpsBuilderExtensions
    {
#if NET8_0_OR_GREATER
        private static readonly string[] s_httpsUriSchemes =
        [
            Utils.UriSchemeHttps,
            Utils.UriSchemeOpcHttps,
            Utils.UriSchemeWss,
            Utils.UriSchemeOpcWss
        ];
#endif

        /// <summary>
        /// Registers the HTTPS listener factories
        /// (<see cref="HttpsTransportListenerFactory"/>,
        /// <see cref="OpcHttpsTransportListenerFactory"/>) and the
        /// matching channel factories
        /// (<see cref="HttpsTransportChannelFactory"/>,
        /// <see cref="OpcHttpsTransportChannelFactory"/>) on the host's
        /// <see cref="ITransportBindingRegistry"/>.
        /// </summary>
        /// <remarks>
        /// The same Kestrel pipeline serves both HTTPS-binary
        /// (<c>application/opcua+uabinary</c>) and HTTPS-JSON
        /// (<c>application/opcua+uajson</c>) requests via content-type
        /// negotiation, so a single
        /// <see cref="AddHttpsTransport(IOpcUaBuilder)"/>
        /// call enables both sub-profiles.
        /// </remarks>
        /// <param name="builder">The OPC UA builder.</param>
        /// <returns>The same <paramref name="builder"/> instance.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        public static IOpcUaBuilder AddHttpsTransport(this IOpcUaBuilder builder)
        {
            return AddHttpsBindings(builder, includeWss: false);
        }

        /// <summary>
        /// Registers the one-shot Kestrel-backed HTTPS/WSS transport stack
        /// and optionally attaches the OPC UA REST binding and
        /// authentication services.
        /// </summary>
        /// <param name="builder">The OPC UA builder.</param>
        /// <param name="configure">Callback used to configure the composed
        /// HTTPS transport registration.</param>
        /// <returns>The same <paramref name="builder"/> instance.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/>
        /// or <paramref name="configure"/> is <c>null</c>.</exception>
        public static IOpcUaBuilder AddHttpsTransport(
            this IOpcUaBuilder builder,
            Action<OpcUaHttpsTransportOptions> configure)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }

            var options = new OpcUaHttpsTransportOptions();
            configure(options);
            AddHttpsBindings(builder, options.IncludeWss);
#if NET8_0_OR_GREATER
            if (options.IncludeWebApi)
            {
                builder.AddWebApiTransport(options.ConfigureWebApi);
            }
#endif
            options.ConfigureAuthentication?.Invoke(builder);
            return builder;
        }

        /// <summary>
        /// Registers the WSS listener factories
        /// (<see cref="WssTransportListenerFactory"/>,
        /// <see cref="OpcWssTransportListenerFactory"/>) and the
        /// matching channel factories
        /// (<see cref="WssTransportChannelFactory"/>,
        /// <see cref="OpcWssTransportChannelFactory"/>,
        /// <see cref="WssJsonTransportChannelFactory"/>) on the host's
        /// <see cref="ITransportBindingRegistry"/>.
        /// </summary>
        /// <remarks>
        /// Covers both the WSS binary sub-protocol
        /// (<c>opcua+uacp</c>) and the JSON sub-protocol
        /// (<c>opcua+uajson</c>; Security Mode None only).
        /// </remarks>
        /// <param name="builder">The OPC UA builder.</param>
        /// <returns>The same <paramref name="builder"/> instance.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        public static IOpcUaBuilder AddWssTransport(this IOpcUaBuilder builder)
        {
            return AddWssBindings(builder);
        }

        /// <summary>
        /// Registers the one-shot Kestrel-backed WSS transport stack and optionally
        /// attaches the OPC UA REST binding and authentication services.
        /// </summary>
        /// <param name="builder">The OPC UA builder.</param>
        /// <param name="configure">Callback used to configure the composed WSS transport registration.</param>
        /// <returns>The same <paramref name="builder"/> instance.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/>
        /// or <paramref name="configure"/> is <c>null</c>.</exception>
        public static IOpcUaBuilder AddWssTransport(
            this IOpcUaBuilder builder,
            Action<OpcUaWssTransportOptions> configure)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }

            var options = new OpcUaWssTransportOptions();
            configure(options);
            AddWssBindings(builder);
#if NET8_0_OR_GREATER
            if (options.IncludeWebApi)
            {
                builder.AddWebApiTransport(options.ConfigureWebApi);
            }
#endif
            options.ConfigureAuthentication?.Invoke(builder);
            return builder;
        }

        private static IOpcUaBuilder AddHttpsBindings(IOpcUaBuilder builder, bool includeWss)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.Services.AddTransportBindingRegistry();
            builder.Services.AddSingleton<ITransportBindingConfigurator>(provider =>
                new TransportBindingConfigurator(registry =>
                {
                    RegisterHttpsListener(registry, provider, new HttpsTransportListenerFactory());
                    RegisterHttpsListener(registry, provider, new OpcHttpsTransportListenerFactory());
                    registry.RegisterChannelFactory(new HttpsTransportChannelFactory());
                    registry.RegisterChannelFactory(new OpcHttpsTransportChannelFactory());
                    if (includeWss)
                    {
                        RegisterWssFactories(registry, provider);
                    }
                }));
            return builder;
        }

        private static IOpcUaBuilder AddWssBindings(IOpcUaBuilder builder)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.Services.AddTransportBindingRegistry();
            builder.Services.AddSingleton<ITransportBindingConfigurator>(provider =>
                new TransportBindingConfigurator(registry => RegisterWssFactories(registry, provider)));
            return builder;
        }

        private static void RegisterWssFactories(ITransportBindingRegistry registry, IServiceProvider provider)
        {
            IBufferManagerFactory bufferManagerFactory =
                provider.GetRequiredService<IBufferManagerFactory>();
            RegisterHttpsListener(registry, provider, new WssTransportListenerFactory());
            RegisterHttpsListener(registry, provider, new OpcWssTransportListenerFactory());
            registry.RegisterChannelFactory(
                new WssTransportChannelFactory(bufferManagerFactory));
            registry.RegisterChannelFactory(
                new OpcWssTransportChannelFactory(bufferManagerFactory));
            registry.RegisterChannelFactory(new WssJsonTransportChannelFactory());
        }

        private static void RegisterHttpsListener(
            ITransportBindingRegistry registry,
            IServiceProvider provider,
            HttpsServiceHost factory)
        {
            factory.BufferManagerFactory =
                provider.GetService<IBufferManagerFactory>() ??
                DefaultBufferManagerFactory.Instance;
            foreach (IHttpsListenerStartupContributor contributor in
                provider.GetServices<IHttpsListenerStartupContributor>())
            {
                AddContributor(factory.StartupContributors, contributor);
            }
            registry.RegisterListenerFactory(factory);
        }

        private static void AddContributor(
            IList<IHttpsListenerStartupContributor> contributors,
            IHttpsListenerStartupContributor contributor)
        {
            if (!contributors.Contains(contributor))
            {
                contributors.Add(contributor);
            }
        }
#if NET8_0_OR_GREATER
        /// <summary>
        /// Adds the default ASP.NET Core rate limiter to Kestrel-hosted HTTPS and WSS transport listeners.
        /// </summary>
        /// <param name="builder">The OPC UA builder.</param>
        /// <returns>The same <paramref name="builder"/> instance.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        public static IOpcUaBuilder AddHttpsRateLimiter(this IOpcUaBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            return AddHttpsRateLimiter(builder, new HttpsRateLimiterStartupContributor());
        }

        /// <summary>
        /// Adds a caller-configured ASP.NET Core rate limiter to Kestrel-hosted HTTPS and WSS transport listeners.
        /// </summary>
        /// <param name="builder">The OPC UA builder.</param>
        /// <param name="configure">The callback that configures the listener host's rate limiter options.</param>
        /// <returns>The same <paramref name="builder"/> instance.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="builder"/> or <paramref name="configure"/> is <c>null</c>.
        /// </exception>
        public static IOpcUaBuilder AddHttpsRateLimiter(
            this IOpcUaBuilder builder,
            Action<RateLimiterOptions> configure)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentNullException.ThrowIfNull(configure);

            return AddHttpsRateLimiter(builder, new HttpsRateLimiterStartupContributor(configure));
        }

        private static IOpcUaBuilder AddHttpsRateLimiter(
            IOpcUaBuilder builder,
            IHttpsListenerStartupContributor contributor)
        {
            builder.Services.AddTransportBindingRegistry();
            builder.Services.AddSingleton<ITransportBindingConfigurator>(
                new TransportBindingConfigurator(registry =>
                {
                    foreach (string scheme in s_httpsUriSchemes)
                    {
                        if (registry.GetListenerFactory(scheme) is HttpsServiceHost factory)
                        {
                            factory.StartupContributors.Add(contributor);
                        }
                    }
                }));
            return builder;
        }
#endif
    }

    /// <summary>
    /// Options for the one-shot
    /// <see cref="OpcUaHttpsBuilderExtensions.AddHttpsTransport(IOpcUaBuilder, Action{OpcUaHttpsTransportOptions})"/>
    /// overload.
    /// </summary>
    public sealed class OpcUaHttpsTransportOptions
    {
        /// <summary>
        /// Gets or sets a value indicating whether the WSS listener and
        /// channel factories are registered together with HTTPS.
        /// </summary>
        public bool IncludeWss { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether the OPC UA REST binding
        /// is attached to the HTTPS/WSS Kestrel host.
        /// </summary>
        public bool IncludeWebApi { get; set; }

#if NET8_0_OR_GREATER
        /// <summary>
        /// Gets or sets the optional WebApi transport configuration.
        /// </summary>
        public Action<WebApiTransportOptions>? ConfigureWebApi { get; set; }
#endif

        /// <summary>
        /// Gets or sets optional authentication registrations, such as
        /// <c>builder.AddWebApiBearerAuth(...)</c>.
        /// </summary>
        public Action<IOpcUaBuilder>? ConfigureAuthentication { get; set; }
    }

    /// <summary>
    /// Options for the one-shot
    /// <see cref="OpcUaHttpsBuilderExtensions.AddWssTransport(IOpcUaBuilder, Action{OpcUaWssTransportOptions})"/>
    /// overload.
    /// </summary>
    public sealed class OpcUaWssTransportOptions
    {
        /// <summary>
        /// Gets or sets a value indicating whether the OPC UA REST binding is attached to the WSS Kestrel host.
        /// </summary>
        public bool IncludeWebApi { get; set; }

#if NET8_0_OR_GREATER
        /// <summary>
        /// Gets or sets the optional WebApi transport configuration.
        /// </summary>
        public Action<WebApiTransportOptions>? ConfigureWebApi { get; set; }
#endif

        /// <summary>
        /// Gets or sets optional authentication registrations, such as <c>builder.AddWebApiBearerAuth(...)</c>.
        /// </summary>
        public Action<IOpcUaBuilder>? ConfigureAuthentication { get; set; }
    }
}
