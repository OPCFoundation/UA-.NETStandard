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
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Opc.Ua;
using Opc.Ua.Bindings;
using Opc.Ua.Bindings.WebApi;

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
            => AddWebApiTransport(builder, configure: null);

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
            services.TryAddSingleton<WebApiServer>(sp =>
            {
                ITelemetryContext telemetry = sp.GetRequiredService<ITelemetryContext>();
                IServiceMessageContext context = sp.GetService<IServiceMessageContext>()
                    ?? ServiceMessageContext.CreateEmpty(telemetry);
                return new WebApiServer(context, listenerId: "rest-api");
            });
            services.TryAddSingleton<IWebApiServer>(sp => sp.GetRequiredService<WebApiServer>());

            // Minimal-API endpoint mapping needs routing services only;
            // no MVC controllers or AddApplicationPart reflection scan.
            services.AddRouting();

            // Install the shared-host startup contributor on every HTTPS /
            // WSS listener factory. The transport binding registry runs
            // the configurators in registration order, so the contributor
            // is attached AFTER AddHttpsTransport()/AddWssTransport() have
            // registered the factories. If the user calls AddWebApi
            // before AddHttps, this becomes a no-op (factories not yet in
            // the registry) — documented in Docs/WebApi.md.
            services.AddTransportBindingRegistry();
            services.AddSingleton<ITransportBindingConfigurator>(sp =>
            {
                WebApiServer server = sp.GetRequiredService<WebApiServer>();
                var contributor = new WebApiHttpsStartupContributor(server);

                return new TransportBindingConfigurator(registry =>
                {
                    foreach (string scheme in s_httpsUriSchemes)
                    {
                        if (registry.GetListenerFactory(scheme) is HttpsServiceHost factory)
                        {
                            factory.StartupContributors.Add(contributor);
                        }
                    }
                });
            });

            return builder;
        }
    }
}
