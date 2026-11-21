// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.PubSub.Encoding.Uadp
{
    /// <summary>
    /// UADP discovery request NetworkMessage. Carries a discovery
    /// information type and a list of DataSetWriterIds the subscriber
    /// is interested in.
    /// </summary>
    /// <remarks>
    /// Implements
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.4.6">
    /// Part 14 §7.2.4.6</see>. Requests carry no payload other than the
    /// DataSetWriterIds list; the publisher answers with one or more
    /// <see cref="UadpDiscoveryResponseMessage"/> instances.
    /// </remarks>
    public sealed record UadpDiscoveryRequestMessage : PubSubNetworkMessage
    {
        /// <summary>
        /// UADP protocol version (low nibble of header byte).
        /// </summary>
        public byte UadpVersion { get; init; } = 1;

        /// <summary>
        /// DataSetClassId carried at the NetworkMessage level (Guid).
        /// </summary>
        public Uuid DataSetClassId { get; init; }

        /// <summary>
        /// Distinguishes data messages from discovery requests/responses.
        /// </summary>
        public UadpNetworkMessageType MessageType { get; init; }
            = UadpNetworkMessageType.DiscoveryRequest;

        /// <summary>
        /// Information type the subscriber requests.
        /// </summary>
        public UadpDiscoveryType DiscoveryType { get; init; }

        /// <summary>
        /// DataSetWriterIds the subscriber is asking about. An empty
        /// list means "all writers known to the publisher".
        /// </summary>
        public ArrayOf<ushort> DataSetWriterIds { get; init; } = [];

        /// <summary>
        /// Optional filter applied when <see cref="DiscoveryType"/> is
        /// <see cref="UadpDiscoveryType.Probe"/> (Part 14 §7.2.4.6.12).
        /// <see langword="null"/> for non-probe requests.
        /// </summary>
        public UadpDiscoveryProbeFilter? ProbeFilter { get; init; }

        /// <inheritdoc/>
        public override string TransportProfileUri => Profiles.PubSubUdpUadpTransport;
    }
}
