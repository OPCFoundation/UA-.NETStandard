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

using System.Collections.Generic;

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
        public IReadOnlyList<ushort> DataSetWriterIds { get; init; } = [];

        /// <summary>
        /// WriterGroup configuration payload for the
        /// DataSetWriterConfiguration response.
        /// </summary>
        public WriterGroupDataType? WriterConfiguration { get; init; }

        /// <summary>
        /// Publisher endpoint list for the PublisherEndpoints response.
        /// </summary>
        public IReadOnlyList<EndpointDescription> PublisherEndpoints { get; init; } = [];

        /// <inheritdoc/>
        public override string TransportProfileUri => Profiles.PubSubUdpUadpTransport;
    }
}
