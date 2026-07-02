/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

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
        /// publisher signals an error).
        /// </summary>
        public StatusCode Status { get; init; } = StatusCodes.Good;

        /// <inheritdoc/>
        public override string TransportProfileUri
            => Profiles.PubSubMqttJsonTransport;
    }
}
