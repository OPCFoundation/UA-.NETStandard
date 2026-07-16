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
