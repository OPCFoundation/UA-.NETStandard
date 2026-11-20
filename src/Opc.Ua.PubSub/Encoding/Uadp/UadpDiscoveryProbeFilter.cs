// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.PubSub.Encoding.Uadp
{
    /// <summary>
    /// Optional probe filter sent inside a
    /// <see cref="UadpDiscoveryRequestMessage"/> when
    /// <see cref="UadpDiscoveryRequestMessage.DiscoveryType"/> is
    /// <see cref="UadpDiscoveryType.Probe"/>.
    /// </summary>
    /// <remarks>
    /// Implements the probe filter from
    /// <see href="https://reference.opcfoundation.org/Core/Part14/v105/docs/7.2.4.6.12">
    /// Part 14 §7.2.4.6.12 Table 180</see>.
    /// </remarks>
    public sealed record UadpDiscoveryProbeFilter
    {
        /// <summary>
        /// Optional ApplicationUri filter; empty means no constraint.
        /// </summary>
        public string ApplicationUri { get; init; } = string.Empty;

        /// <summary>
        /// Optional ProductUri filter; empty means no constraint.
        /// </summary>
        public string ProductUri { get; init; } = string.Empty;

        /// <summary>
        /// Optional capability filter (single token); empty means no
        /// constraint.
        /// </summary>
        public string Capability { get; init; } = string.Empty;

        /// <summary>
        /// Optional WriterGroupId for WriterGroup configuration probes.
        /// </summary>
        public ushort? WriterGroupId { get; init; }

        /// <summary>
        /// Requests WriterGroups in PubSubConnection announcements.
        /// </summary>
        public bool IncludeWriterGroups { get; init; }

        /// <summary>
        /// Requests DataSetWriters in WriterGroup or PubSubConnection announcements.
        /// </summary>
        public bool IncludeDataSetWriters { get; init; }

        /// <summary>
        /// Optional TransportProfileUri filters for PubSubConnection announcements.
        /// </summary>
        public ArrayOf<string> TransportProfileUris { get; init; } = [];
    }
}
