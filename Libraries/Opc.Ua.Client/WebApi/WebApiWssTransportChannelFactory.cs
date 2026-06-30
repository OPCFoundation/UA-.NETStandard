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
    /// <see cref="ITransportChannelFactory"/> for the WSS Web API /
    /// OpenAPI sub-protocol (OPC UA Part 6 §7.5.2 <c>opcua+openapi</c>).
    /// Registered under the synthetic
    /// <see cref="Utils.UriSchemeOpcWssOpenApi"/> registry key so the
    /// client channel manager can map
    /// <see cref="Profiles.WssOpenApiTransport"/> to this factory while
    /// sharing the wire-level <c>wss://</c> URL scheme with the binary
    /// <c>opcua+uacp</c> channel.
    /// </summary>
    public sealed class WebApiWssTransportChannelFactory : ITransportChannelFactory
    {
        private readonly WebApiClientOptions? m_options;
        private readonly TimeProvider? m_timeProvider;

        /// <summary>
        /// Initializes a new instance.
        /// </summary>
        /// <param name="options">Default options forwarded to each
        /// constructed <see cref="WebApiWssTransportChannel"/>.</param>
        /// <param name="timeProvider">Optional time provider forwarded
        /// to the channel.</param>
        public WebApiWssTransportChannelFactory(
            WebApiClientOptions? options = null,
            TimeProvider? timeProvider = null)
        {
            m_options = options;
            m_timeProvider = timeProvider;
        }

        /// <inheritdoc/>
        public string UriScheme => Utils.UriSchemeOpcWssOpenApi;

        /// <inheritdoc/>
        public ITransportChannel Create(ITelemetryContext telemetry)
        {
            return new WebApiWssTransportChannel(telemetry, m_options, m_timeProvider);
        }
    }
}
