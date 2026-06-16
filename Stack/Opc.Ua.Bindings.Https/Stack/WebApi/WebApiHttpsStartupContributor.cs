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

#if NET8_0_OR_GREATER
using System;
using System.Linq;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Opc.Ua;
using Opc.Ua.Bindings;

namespace Opc.Ua.Bindings.WebApi
{
    /// <summary>
    /// Bridges the OPC UA REST Minimal-API endpoints to the
    /// <see cref="HttpsTransportListener"/> Kestrel host. Installed on
    /// every HTTPS / WSS listener factory by
    /// <c>AddWebApiTransport()</c>; ASP.NET Core invokes
    /// <see cref="Configure(IApplicationBuilder, HttpsTransportListener)"/>
    /// once per listener instance, between
    /// <c>UseWebSockets()</c> and the terminal binary / JSON dispatcher.
    /// </summary>
    /// <remarks>
    /// The contributor registers Minimal-API routing services and
    /// the OPC UA REST endpoints (see
    /// <c>MapWebApiEndpoints</c>) — no MVC reflection-based
    /// controller discovery — so the binding is fully
    /// NativeAOT-compatible.
    /// </remarks>
    internal sealed class WebApiHttpsStartupContributor :
        IHttpsListenerStartupContributor,
        IHttpsListenerServiceContributor
    {
        private readonly WebApiServer m_server;

        public WebApiHttpsStartupContributor(WebApiServer server)
        {
            ArgumentNullException.ThrowIfNull(server);
            m_server = server;
        }

        /// <inheritdoc/>
        public void ConfigureServices(IServiceCollection services, HttpsTransportListener listener)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(listener);

            services.TryAddSingleton(m_server);
            services.TryAddSingleton<IWebApiServer>(m_server);
            // Minimal-API endpoint mapping needs routing services; no
            // MVC controllers / AddApplicationPart reflection scan.
            services.AddRouting();
        }

        /// <inheritdoc/>
        public void Configure(IApplicationBuilder appBuilder, HttpsTransportListener listener)
        {
            ArgumentNullException.ThrowIfNull(appBuilder);
            ArgumentNullException.ThrowIfNull(listener);

            // Late-bind the dispatcher to the listener's transport
            // callback. By the time Configure runs Kestrel has been
            // built but not started, and listener.Callback has already
            // been wired by HttpsTransportListener.Open(...).
            m_server.Attach(listener.Callback);
            if (listener.MessageContext is { } context)
            {
                m_server.UpdateMessageContext(context);
            }
            if (!string.IsNullOrEmpty(listener.ListenerId))
            {
                m_server.UpdateListenerId(listener.ListenerId);
            }

            // Pick the listener's OpenAPI endpoint description (added by
            // HttpsServiceHost discovery emission) as the default
            // invocation context. The server pipeline (e.g.
            // SessionManager.CreateSession) dereferences
            // SecureChannelContext.EndpointDescription, so a null endpoint
            // surfaces as a server-side NRE / BadUnexpectedError. Falling
            // back to the SM=None HTTPS-binary description keeps things
            // working for older HttpsServiceHost versions that didn't
            // emit the OpenAPI twin.
            if (listener.Descriptions is { } descriptions)
            {
                EndpointDescription? defaultEndpoint = null;
                foreach (EndpointDescription endpoint in descriptions)
                {
                    if (Profiles.IsHttpsOpenApi(endpoint.TransportProfileUri))
                    {
                        defaultEndpoint = endpoint;
                        break;
                    }
                }
                if (defaultEndpoint == null)
                {
                    foreach (EndpointDescription endpoint in descriptions)
                    {
                        if (endpoint.SecurityMode == MessageSecurityMode.None &&
                            !string.IsNullOrEmpty(endpoint.EndpointUrl) &&
                            Utils.IsUriHttpsScheme(endpoint.EndpointUrl))
                        {
                            defaultEndpoint = endpoint;
                            break;
                        }
                    }
                }
                defaultEndpoint ??= descriptions.Count > 0 ? descriptions[0] : null;
                m_server.UpdateDefaultEndpoint(defaultEndpoint);
            }

            // Mount routing + Minimal-API endpoints. Unmatched paths
            // fall through to the listener's terminal binary / JSON
            // dispatcher below, so the REST surface is additive —
            // existing routes and sub-protocols are unaffected.
            appBuilder.UseRouting();

            // app.UseAuthentication() must run after UseRouting() and
            // before UseEndpoints() for the auth handler resolved per
            // request to populate HttpContext.User in time for the
            // ISessionlessIdentityProvider hook. Only insert it when
            // at least one non-Anonymous auth scheme is registered;
            // bare AddWebApiTransport() (no auth) skips the
            // middleware entirely to preserve the historical anonymous
            // request flow.
            if (HasNonAnonymousAuthScheme(appBuilder.ApplicationServices))
            {
                appBuilder.UseAuthentication();
            }
            appBuilder.UseEndpoints(endpoints => endpoints.MapWebApiEndpoints());
        }

        // Resolves the AuthenticationOptions snapshot so that bindings
        // composed without any AddWebApi*Auth() call don't pay the
        // UseAuthentication() middleware cost (and don't change
        // HttpContext.User semantics for the anonymous path). The
        // options-based path keeps this purely synchronous — no
        // sync-over-async on IAuthenticationSchemeProvider.
        private static bool HasNonAnonymousAuthScheme(IServiceProvider services)
        {
            IOptions<AuthenticationOptions>? options = services
                .GetService<IOptions<AuthenticationOptions>>();
            return options?.Value.Schemes.Any() == true;
        }
    }
}
#endif
