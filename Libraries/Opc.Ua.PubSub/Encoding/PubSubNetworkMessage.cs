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

namespace Opc.Ua.PubSub.Encoding
{
    /// <summary>
    /// Abstract container for one PubSub NetworkMessage shared between
    /// the UADP and JSON mappings. Concrete derived records add
    /// mapping-specific header fields and security envelopes.
    /// </summary>
    /// <remarks>
    /// Implements the shared NetworkMessage model of
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/5.3.4">
    /// Part 14 §5.3.4 NetworkMessage</see>. A NetworkMessage carries
    /// shared identification (<see cref="PublisherId"/>,
    /// optional <see cref="WriterGroupId"/>) and one or more
    /// <see cref="DataSetMessages"/> payloads. <see cref="MetaData"/>
    /// is populated only on metadata-announcement messages
    /// (Part 14 §7.2.4.6.4 / §7.2.5.5.2).
    /// </remarks>
    public abstract record PubSubNetworkMessage
    {
        /// <summary>
        /// Identifier of the transport profile this message is bound
        /// to. Used by the dispatcher to route messages to the matching
        /// encoder / decoder.
        /// </summary>
        public abstract string TransportProfileUri { get; }

        /// <summary>
        /// Publisher identity carried in the NetworkMessage header.
        /// </summary>
        public PublisherId PublisherId { get; init; }

        /// <summary>
        /// Optional WriterGroupId carried in the NetworkMessage
        /// GroupHeader (UADP) or in the JSON envelope. A
        /// <see langword="null"/> value means the GroupHeader is
        /// omitted or unknown.
        /// </summary>
        public ushort? WriterGroupId { get; init; }

        /// <summary>
        /// Payload DataSetMessages contained in this NetworkMessage.
        /// </summary>
        public ArrayOf<PubSubDataSetMessage> DataSetMessages { get; init; }
            = [];

        /// <summary>
        /// DataSetMetaData carried on a metadata-announcement message.
        /// <see langword="null"/> on regular data messages.
        /// </summary>
        public DataSetMetaDataType? MetaData { get; init; }
    }
}
