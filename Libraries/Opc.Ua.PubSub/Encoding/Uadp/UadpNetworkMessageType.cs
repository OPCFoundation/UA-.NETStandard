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
    /// NetworkMessage subtype indicator (UADP). Stored as the high
    /// nibble of the legacy UADPNetworkMessageType byte; mirrors the
    /// values previously surfaced in the v1.5 stack so
    /// downstream code paths remain comparable.
    /// </summary>
    /// <remarks>
    /// Implements
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/A.2.2.4">
    /// Part 14 §A.2.2.4 — UADP NetworkMessage Header Layout</see>
    /// Table 160. Discovery-* values are tag bits in the
    /// <see cref="ExtendedFlags2EncodingMask"/> byte.
    /// </remarks>
#pragma warning disable CA1027 // not a flags enum: values are discrete tag codes from Part 14 Table 160
    public enum UadpNetworkMessageType
    {
        /// <summary>
        /// A regular data NetworkMessage carrying one or more
        /// <see cref="UadpDataSetMessage"/> payloads.
        /// </summary>
        DataSetMessage = 0,

        /// <summary>
        /// A discovery request NetworkMessage. The
        /// <see cref="ExtendedFlags2EncodingMask.NetworkMessageWithDiscoveryRequest"/>
        /// bit is set; the payload identifies the request type and the
        /// addressed DataSetWriterId list.
        /// </summary>
        DiscoveryRequest = 4,

        /// <summary>
        /// A discovery response NetworkMessage. The
        /// <see cref="ExtendedFlags2EncodingMask.NetworkMessageWithDiscoveryResponse"/>
        /// bit is set; the payload carries metadata, configuration, or
        /// endpoint descriptions.
        /// </summary>
        DiscoveryResponse = 8
    }
#pragma warning restore CA1027
}
