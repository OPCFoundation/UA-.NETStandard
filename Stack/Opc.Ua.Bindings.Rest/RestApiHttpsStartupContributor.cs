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
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua;
using Opc.Ua.Bindings;
using Opc.Ua.Bindings.Rest.Controllers;

namespace Opc.Ua.Bindings.Rest
{
    /// <summary>
    /// Bridges the OPC UA REST MVC pipeline to the
    /// <see cref="HttpsTransportListener"/> Kestrel host. Installed on
    /// every HTTPS / WSS listener factory by
    /// <c>AddRestApiTransport()</c>; ASP.NET Core invokes
    /// <see cref="Configure(IApplicationBuilder, HttpsTransportListener)"/>
    /// once per listener instance, between
    /// <c>UseWebSockets()</c> and the terminal binary / JSON dispatcher.
    /// </summary>
    internal sealed class RestApiHttpsStartupContributor :
        IHttpsListenerStartupContributor,
        IHttpsListenerServiceContributor
    {
        private readonly RestApiServer m_server;

        public RestApiHttpsStartupContributor(RestApiServer server)
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
            services.TryAddSingleton<IRestApiServer>(m_server);
            services
                .AddControllers()
                .AddApplicationPart(typeof(AttributeController).Assembly);
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

            // Pick the listener's SM=None HTTPS endpoint as the
            // default invocation context — the server pipeline (e.g.
            // SessionManager.CreateSession) dereferences
            // SecureChannelContext.EndpointDescription, so a null
            // endpoint surfaces as a server-side NRE / BadUnexpectedError.
            if (listener.Descriptions is { } descriptions)
            {
                EndpointDescription? defaultEndpoint = null;
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
                defaultEndpoint ??= descriptions.Count > 0 ? descriptions[0] : null;
                m_server.UpdateDefaultEndpoint(defaultEndpoint);
            }

            // Mount routing + MVC endpoints. Unmatched paths fall through
            // to the listener's terminal binary / JSON dispatcher below,
            // so the REST surface is additive — existing routes and
            // sub-protocols are unaffected.
            appBuilder.UseRouting();
            appBuilder.UseEndpoints(endpoints => endpoints.MapControllers());
        }
    }
}
