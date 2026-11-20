// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using Opc.Ua.PubSub.Encoding.Uadp;

namespace Opc.Ua.PubSub.Encoding.Json
{
    /// <summary>
    /// JSON discovery NetworkMessage envelope
    /// carrying any of the discovery-response variants defined in
    /// Part 14.
    /// </summary>
    /// <remarks>
    /// Implements
    /// <see href="https://reference.opcfoundation.org/Core/Part14/v105/docs/7.2.5.5">
    /// Part 14 §7.2.5.5</see> JSON discovery mapping. The envelope
    /// carries a single body discriminated by
    /// <see cref="DiscoveryType"/>; the matching strongly-typed slot
    /// (<see cref="ApplicationInformation"/>,
    /// <see cref="Connection"/>, <see cref="MetaData"/>,
    /// <see cref="WriterConfiguration"/> /
    /// <see cref="DataSetWriterIds"/> or
    /// <see cref="PublisherEndpoints"/>) holds the payload.
    /// </remarks>
    public sealed record JsonDiscoveryMessage : PubSubNetworkMessage
    {
        /// <summary>
        /// MessageType wire literal for application discovery.
        /// </summary>
        public const string MessageTypeApplication = "ua-application";

        /// <summary>
        /// MessageType wire literal for endpoint discovery.
        /// </summary>
        public const string MessageTypeEndpoints = "ua-endpoints";

        /// <summary>
        /// MessageType wire literal for status discovery.
        /// </summary>
        public const string MessageTypeStatus = "ua-status";

        /// <summary>
        /// MessageType wire literal for connection discovery.
        /// </summary>
        public const string MessageTypeConnection = "ua-connection";

        /// <summary>
        /// MessageId per Part 14 §7.2.5.3.
        /// </summary>
        public string MessageId { get; init; } = string.Empty;

        /// <summary>
        /// Discovery-response variant carried by this envelope.
        /// </summary>
        public UadpDiscoveryType DiscoveryType { get; init; }
            = UadpDiscoveryType.None;

        /// <summary>
        /// ApplicationInformation payload when
        /// <see cref="DiscoveryType"/> is
        /// <see cref="UadpDiscoveryType.ApplicationInformation"/>
        /// (Part 14 §7.2.4.6.7).
        /// </summary>
        public UadpApplicationInformation? ApplicationInformation { get; init; }

        /// <summary>
        /// The <c>ua-application</c> Description (Part 14 §7.2.5.5.3).
        /// When not set the encoder builds it from
        /// <see cref="ApplicationInformation"/>; the decoder sets both.
        /// </summary>
        public ApplicationDescription? Description { get; init; }

        /// <summary>
        /// When the message was first sent to the middleware (Part 14
        /// §7.2.5.5). The encoder uses the current time when it is not
        /// set.
        /// </summary>
        public DateTimeUtc Timestamp { get; init; } = DateTimeUtc.MinValue;

        /// <summary>
        /// Application status payload when <see cref="DiscoveryType"/> is
        /// <see cref="UadpDiscoveryType.ApplicationInformation"/> with the
        /// status discriminator from Part 14 §7.2.4.6.7.
        /// </summary>
        public UadpApplicationStatus? ApplicationStatus { get; init; }

        /// <summary>
        /// PubSubConnection payload when <see cref="DiscoveryType"/>
        /// is <see cref="UadpDiscoveryType.PubSubConnection"/>
        /// (Part 14 §7.2.4.6.8).
        /// </summary>
        public PubSubConnectionDataType? Connection { get; init; }

        /// <summary>
        /// DataSetWriterId of the response (when applicable).
        /// </summary>
        public ushort DataSetWriterId { get; init; }

        /// <summary>
        /// DataSetWriterConfiguration payload when
        /// <see cref="DiscoveryType"/> is
        /// <see cref="UadpDiscoveryType.DataSetWriterConfiguration"/>
        /// (Part 14 §7.2.4.6.6).
        /// </summary>
        public WriterGroupDataType? WriterConfiguration { get; init; }

        /// <summary>
        /// DataSetWriterIds covered by the writer-configuration
        /// payload when applicable.
        /// </summary>
        public ushort[] DataSetWriterIds { get; init; } = [];

        /// <summary>
        /// PublisherEndpoints payload when
        /// <see cref="DiscoveryType"/> is
        /// <see cref="UadpDiscoveryType.PublisherEndpoints"/>
        /// (Part 14 §7.2.4.6.5).
        /// </summary>
        public EndpointDescription[] PublisherEndpoints { get; init; }
            = [];

        /// <summary>
        /// Status of the discovery response (Good unless the
        /// publisher signals an error). The JSON discovery messages of
        /// Part 14 §7.2.5.5 do not carry it.
        /// </summary>
        public StatusCode Status { get; init; } = StatusCodes.Good;

        /// <inheritdoc/>
        public override string TransportProfileUri
            => Profiles.PubSubMqttJsonTransport;
    }
}
