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
using Opc.Ua.Bindings;

namespace Opc.Ua.Client.WebApi
{
    /// <summary>
    /// <see cref="ITransportChannelFactory"/> for the HTTPS Web API
    /// binding (OPC UA Part 6 §G.3 "OpenAPI Mapping"). Registered under
    /// the synthetic URI scheme <see cref="Utils.UriSchemeOpcHttpsWebApi"/>
    /// so the client channel manager can map endpoints with
    /// <c>Profiles.HttpsOpenApiTransport</c> to this factory while sharing
    /// the wire-level <c>https://</c> URL scheme with the binary /
    /// JSON-envelope HTTPS channels.
    /// </summary>
    /// <remarks>
    /// Construct directly for unit tests and one-off client code, or let
    /// <c>services.AddWebApiTransport()</c> register one configured by
    /// <see cref="WebApiClientOptions"/> resolved from DI.
    /// </remarks>
    public sealed class WebApiTransportChannelFactory : ITransportChannelFactory
    {
        private readonly WebApiClientOptions? m_options;
        private readonly IOpcUaHttpClientFactory? m_httpClientFactory;
        private readonly TimeProvider? m_timeProvider;

        /// <summary>
        /// Initializes a new instance.
        /// </summary>
        /// <param name="options">Default options forwarded into each
        /// constructed <see cref="WebApiTransportChannel"/>.</param>
        /// <param name="httpClientFactory">Optional OPC UA HTTP client
        /// factory used when <paramref name="options"/> does not carry
        /// an explicit <see cref="System.Net.Http.HttpMessageHandler"/>.</param>
        /// <param name="timeProvider">Optional time provider forwarded
        /// to the channel.</param>
        public WebApiTransportChannelFactory(
            WebApiClientOptions? options = null,
            IOpcUaHttpClientFactory? httpClientFactory = null,
            TimeProvider? timeProvider = null)
        {
            m_options = options;
            m_httpClientFactory = httpClientFactory;
            m_timeProvider = timeProvider;
        }

        /// <inheritdoc/>
        public string UriScheme => Utils.UriSchemeOpcHttpsWebApi;

        /// <inheritdoc/>
        public ITransportChannel Create(ITelemetryContext telemetry)
        {
            return new WebApiTransportChannel(
                telemetry,
                m_options,
                m_httpClientFactory,
                m_timeProvider);
        }
    }
}
