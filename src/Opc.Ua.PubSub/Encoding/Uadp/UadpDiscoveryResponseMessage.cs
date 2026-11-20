/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.PubSub.Encoding.Uadp
{
    /// <summary>
    /// UADP discovery response NetworkMessage. Carries one of three
    /// information payloads (DataSetMetaData, DataSetWriterConfiguration,
    /// PublisherEndpoints) as selected by <see cref="DiscoveryType"/>.
    /// </summary>
    /// <remarks>
    /// Implements
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.4.6">
    /// Part 14 §7.2.4.6</see>. Only the payload fields matching
    /// <see cref="DiscoveryType"/> are honoured by the encoder.
    /// </remarks>
    public sealed record UadpDiscoveryResponseMessage : PubSubNetworkMessage
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
            = UadpNetworkMessageType.DiscoveryResponse;

        /// <summary>
        /// Per-publisher monotonically increasing sequence number for
        /// discovery responses.
        /// </summary>
        public ushort SequenceNumber { get; init; }

        /// <summary>
        /// Information type carried by this response.
        /// </summary>
        public UadpDiscoveryType DiscoveryType { get; init; }

        /// <summary>
        /// Operation status code reported by the publisher.
        /// </summary>
        public StatusCode StatusCode { get; init; }

        /// <summary>
        /// DataSetWriterId for the DataSetMetaData response.
        /// </summary>
        public ushort DataSetWriterId { get; init; }

        /// <summary>
        /// DataSetMetaData payload for the DataSetMetaData response.
        /// </summary>
        public DataSetMetaDataType? DataSetMetaData { get; init; }

        /// <summary>
        /// DataSetWriterIds for the DataSetWriterConfiguration
        /// response.
        /// </summary>
        public ArrayOf<ushort> DataSetWriterIds { get; init; } = [];

        /// <summary>
        /// WriterGroup configuration payload for the
        /// DataSetWriterConfiguration response.
        /// </summary>
        public WriterGroupDataType? WriterConfiguration { get; init; }

        /// <summary>
        /// Publisher endpoint list for the PublisherEndpoints response.
        /// </summary>
        public ArrayOf<EndpointDescription> PublisherEndpoints { get; init; } = [];

        /// <summary>
        /// ApplicationInformation payload for the ApplicationInformation
        /// response (Part 14 §7.2.4.6.7). Set only when
        /// <see cref="DiscoveryType"/> is
        /// <see cref="UadpDiscoveryType.ApplicationInformation"/>.
        /// </summary>
        public UadpApplicationInformation? ApplicationInformation { get; init; }

        /// <summary>
        /// Publisher status payload for ApplicationInformationType 2.
        /// </summary>
        public UadpApplicationStatus? ApplicationStatus { get; init; }

        /// <summary>
        /// PubSubConnection announcement payload (Part 14 §7.2.4.6.8).
        /// Set only when <see cref="DiscoveryType"/> is
        /// <see cref="UadpDiscoveryType.PubSubConnection"/>.
        /// </summary>
        public PubSubConnectionDataType? Connection { get; init; }

        /// <inheritdoc/>
        public override string TransportProfileUri => Profiles.PubSubUdpUadpTransport;
    }
}
