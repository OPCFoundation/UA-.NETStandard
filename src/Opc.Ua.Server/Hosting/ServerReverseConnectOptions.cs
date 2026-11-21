// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Collections.Generic;

namespace Opc.Ua.Server.Hosting
{
    /// <summary>
    /// Server-side reverse-connect configuration exposed via the unified
    /// dependency-injection surface. Mirrors the fields of
    /// <see cref="ReverseConnectServerConfiguration"/> with binder-friendly
    /// (settable, default-constructible) properties so that the values can
    /// be loaded directly from an
    /// <see cref="Microsoft.Extensions.Configuration.IConfiguration"/>
    /// section such as <c>OpcUa:Server:ReverseConnect</c>.
    /// </summary>
    /// <remarks>
    /// Wiring is performed automatically by the hosted service when
    /// <see cref="OpcUaServerOptions.ReverseConnect"/> is non-null.
    /// </remarks>
    public sealed class ServerReverseConnectOptions
    {
        /// <summary>
        /// Reverse-connect clients to dial. Each entry produces a
        /// <see cref="ReverseConnectClient"/> in the underlying
        /// <see cref="ServerConfiguration.ReverseConnect"/>.
        /// </summary>
        public IList<ServerReverseConnectClientOptions> Clients { get; }
            = [];

        /// <summary>
        /// Interval in milliseconds between reverse-connect attempts.
        /// Defaults to 15&#160;000&#160;ms.
        /// </summary>
        public int ConnectIntervalMs { get; set; } = 15000;

        /// <summary>
        /// Default per-connection timeout in milliseconds when establishing
        /// a reverse connection. Defaults to 30&#160;000&#160;ms.
        /// </summary>
        public int ConnectTimeoutMs { get; set; } = 30000;

        /// <summary>
        /// Timeout in milliseconds before retrying a reverse connection
        /// that was rejected by the client. Defaults to 60&#160;000&#160;ms.
        /// </summary>
        public int RejectTimeoutMs { get; set; } = 60000;
    }

    /// <summary>
    /// One reverse-connect client entry. Mirrors
    /// <see cref="ReverseConnectClient"/>.
    /// </summary>
    public sealed class ServerReverseConnectClientOptions
    {
        /// <summary>
        /// Client endpoint URL the server should reverse-connect to (e.g.
        /// <c>opc.tcp://client.example.com:4841</c>). Required.
        /// </summary>
        public string? EndpointUrl { get; set; }

        /// <summary>
        /// Optional per-client connection timeout in milliseconds.
        /// Overrides <see cref="ServerReverseConnectOptions.ConnectTimeoutMs"/>
        /// when greater than zero.
        /// </summary>
        public int Timeout { get; set; }

        /// <summary>
        /// Maximum simultaneous reverse-connect sessions for this client.
        /// This setting is ignored.
        /// </summary>
        [System.Obsolete("MaxSessionCount is no longer supported and is ignored.")]
        public int MaxSessionCount { get; set; }

        /// <summary>
        /// Whether this client entry is active. Defaults to <c>true</c>.
        /// </summary>
        public bool Enabled { get; set; } = true;
    }
}
