/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Server redundancy information read from the Server's
    /// <c>Server.ServerRedundancy</c> object.
    /// </summary>
    /// <remarks>
    /// See OPC 10000-4 §6.6.2 and OPC 10000-5 §6.3.7.
    /// </remarks>
    public sealed class ServerRedundancyInfo
    {
        /// <summary>
        /// The <c>RedundancySupport</c> mode reported by the Server.
        /// </summary>
        public RedundancySupport Mode { get; init; }

        /// <summary>
        /// The Servers in the <c>RedundantServerSet</c>.
        /// </summary>
        public ArrayOf<RedundantServer> RedundantServers { get; init; } = [];

        /// <summary>
        /// The current <c>ServiceLevel</c> of the connected Server.
        /// </summary>
        public byte ServiceLevel { get; init; }

        /// <summary>
        /// Whether <see cref="ServiceLevel"/> was read from the server.
        /// </summary>
        public bool ServiceLevelAccessible { get; init; }

        /// <summary>
        /// The OPC 10000-4 §6.6.2.4.2 subrange of the current Server's <c>ServiceLevel</c>.
        /// </summary>
        public ServiceLevelSubrange ServiceLevelSubrange { get; init; }

        /// <summary>
        /// The OPC 10000-4 §6.6.5 estimated time when a Server in Maintenance is expected to return.
        /// </summary>
        public DateTime EstimatedReturnTime { get; init; }

        /// <summary>
        /// The current Server identifier for Transparent redundancy.
        /// </summary>
        public string CurrentServerId { get; init; } = string.Empty;

        /// <summary>
        /// The status of each Node read from the Server in use to build this snapshot, keyed by its NodeId
        /// (for example <c>Server.ServerRedundancy.ServerUriArray</c>). A bad status explains why the
        /// corresponding value is empty or defaulted.
        /// </summary>
        public IReadOnlyDictionary<NodeId, StatusCode> ReadStatuses { get; init; } = s_noReadStatuses;

        private static readonly IReadOnlyDictionary<NodeId, StatusCode> s_noReadStatuses =
            new ReadOnlyDictionary<NodeId, StatusCode>(new Dictionary<NodeId, StatusCode>());
    }
}
