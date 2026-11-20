/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using Opc.Ua.PubSub.Encoding.Uadp;

namespace Opc.Ua.PubSub.Application
{
    /// <summary>
    /// Subscriber-side PubSub discovery request options.
    /// </summary>
    /// <remarks>
    /// Maps directly to <see cref="UadpDiscoveryRequestMessage"/> from
    /// OPC UA Part 14 §7.2.4.6.
    /// </remarks>
    public sealed record PubSubDiscoveryRequest
    {
        /// <summary>
        /// Discovery payload type to request from publishers.
        /// </summary>
        public UadpDiscoveryType DiscoveryType { get; init; }

        /// <summary>
        /// DataSetWriterIds to request. An empty list requests all writers.
        /// </summary>
        public ArrayOf<ushort> DataSetWriterIds { get; init; } = [];

        /// <summary>
        /// Optional probe filter used when <see cref="DiscoveryType"/> is
        /// <see cref="UadpDiscoveryType.Probe"/>.
        /// </summary>
        public UadpDiscoveryProbeFilter? ProbeFilter { get; init; }
    }
}
