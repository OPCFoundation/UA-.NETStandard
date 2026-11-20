// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
