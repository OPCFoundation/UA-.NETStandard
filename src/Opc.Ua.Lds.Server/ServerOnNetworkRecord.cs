// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;

namespace Opc.Ua.Lds.Server
{
    /// <summary>
    /// Network-level record exposed to clients via
    /// <see cref="LdsServer.FindServersOnNetworkAsync"/>. Each
    /// <see cref="MdnsDiscoveryConfiguration"/> attached to a register call
    /// produces one record; observed mDNS peers also surface as records.
    /// </summary>
    public sealed class ServerOnNetworkRecord
    {
        /// <summary>
        /// The monotonic record id assigned by the store.
        /// </summary>
        public uint RecordId { get; internal set; }

        /// <summary>
        /// The server's <c>ApplicationUri</c>. Used to correlate the record back
        /// to a <see cref="RegistrationEntry"/> when one exists.
        /// </summary>
        public string? ServerUri { get; set; }

        /// <summary>
        /// The mDNS server name (display string) for this record.
        /// </summary>
        public string? ServerName { get; set; }

        /// <summary>
        /// One discovery URL the client can use to reach the server.
        /// </summary>
        public string? DiscoveryUrl { get; set; }

        /// <summary>
        /// The capabilities advertised by the server.
        /// </summary>
        public IList<string> ServerCapabilities { get; set; } = [];

        /// <summary>
        /// Wall-clock timestamp of the most recent observation. Used for TTL-based
        /// pruning of mDNS-observed records.
        /// </summary>
        public DateTime LastSeenUtc { get; internal set; }

        /// <summary>
        /// True if the record originated from an mDNS network observation as
        /// opposed to an explicit RegisterServer2 call.
        /// </summary>
        public bool ObservedViaMulticast { get; set; }
    }
}
