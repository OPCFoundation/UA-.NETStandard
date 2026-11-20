// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Collections.Generic;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Client-side reverse-connect configuration exposed via the unified
    /// dependency-injection surface. Mirrors the fields of
    /// <see cref="ReverseConnectClientConfiguration"/> with binder-friendly
    /// (settable, default-constructible) properties so the values can be
    /// loaded directly from an
    /// <see cref="Microsoft.Extensions.Configuration.IConfiguration"/>
    /// section such as <c>OpcUa:Client:ReverseConnect</c>.
    /// </summary>
    /// <remarks>
    /// When <see cref="OpcUaClientOptions.ReverseConnect"/> is set, the DI
    /// container registers a singleton
    /// <see cref="ReverseConnectManager"/> plus an internal hosted service
    /// that opens the configured listener endpoints asynchronously on host
    /// start. When no host is present the manager starts lazily on first
    /// use. Consumers awaiting an inbound reverse-hello message resolve the
    /// manager and call
    /// <see cref="ReverseConnectManager.WaitForConnectionAsync"/>, which
    /// ensures the manager is started.
    /// </remarks>
    public sealed class ClientReverseConnectOptions
    {
        /// <summary>
        /// Local listener endpoints (e.g. <c>opc.tcp://0.0.0.0:4841</c>)
        /// the manager should bind to. Each URL is registered via
        /// <see cref="ReverseConnectManager.AddEndpoint(System.Uri)"/>.
        /// </summary>
        public IList<string> ClientEndpointUrls { get; } = [];

        /// <summary>
        /// Time in milliseconds a reverse-hello port is held open while
        /// waiting for a server callback before the request is rejected.
        /// Defaults to 15&#160;000&#160;ms.
        /// </summary>
        public int HoldTimeMs { get; set; } = 15000;

        /// <summary>
        /// Maximum time in milliseconds to wait for a reverse-hello message
        /// before <see cref="ReverseConnectManager.WaitForConnectionAsync"/>
        /// times out. Defaults to 20&#160;000&#160;ms.
        /// </summary>
        public int WaitTimeoutMs { get; set; } = 20000;
    }
}
