/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Generic;

namespace Opc.Ua.Lds.Server
{
    /// <summary>
    /// In-memory record of a server registered with the LDS via
    /// <see cref="LdsServer.RegisterServerAsync"/> or
    /// <see cref="LdsServer.RegisterServer2Async"/>.
    /// </summary>
    /// <remarks>
    /// Registrations are keyed by <see cref="ServerUri"/>. Setting <see cref="IsOnline"/>
    /// to <c>false</c> in a subsequent register call removes the entry from the store.
    /// </remarks>
    public sealed class RegistrationEntry
    {
        /// <summary>
        /// The server's <c>ApplicationUri</c>. Acts as the unique key for the registration.
        /// </summary>
        public string ServerUri { get; set; } = null!;

        /// <summary>
        /// The server's product URI.
        /// </summary>
        public string? ProductUri { get; set; }

        /// <summary>
        /// The server's localized application names.
        /// </summary>
        public IList<LocalizedText> ServerNames { get; set; } = [];

        /// <summary>
        /// The server's <see cref="ApplicationType"/>. LDS rejects
        /// <see cref="ApplicationType.Client"/> registrations.
        /// </summary>
        public ApplicationType ServerType { get; set; } = ApplicationType.Server;

        /// <summary>
        /// Optional gateway server URI for non-OPC UA server registrations.
        /// </summary>
        public string? GatewayServerUri { get; set; }

        /// <summary>
        /// One or more discovery endpoint URLs.
        /// </summary>
        public IList<string> DiscoveryUrls { get; set; } = [];

        /// <summary>
        /// Optional file path used to keep the registration alive while the file exists.
        /// </summary>
        public string? SemaphoreFilePath { get; set; }

        /// <summary>
        /// Whether the server is currently online and accepting connections.
        /// </summary>
        public bool IsOnline { get; set; }

        /// <summary>
        /// Wall-clock timestamp of the most recent register call.
        /// </summary>
        public DateTime LastSeenUtc { get; set; }

        /// <summary>
        /// The capabilities advertised by the registered server, derived from the most
        /// recent <c>MdnsDiscoveryConfiguration</c> if any. Empty for plain
        /// <c>RegisterServer</c> calls.
        /// </summary>
        public IList<string> ServerCapabilities { get; set; } = [];

        /// <summary>
        /// The mDNS server name advertised by the most recent
        /// <c>MdnsDiscoveryConfiguration</c>. Null for plain <c>RegisterServer</c> calls.
        /// </summary>
        public string? MdnsServerName { get; set; }
    }
}
