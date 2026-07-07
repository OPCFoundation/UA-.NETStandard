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

using System;
using Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.PubSub.Transcoding
{
    /// <summary>
    /// A NetworkMessage observed on the subscriber receive path, together
    /// with the raw wire frame it was decoded from. Supplied to an
    /// <see cref="IReceivedNetworkMessageSink"/> so a transcoding bridge
    /// can forward it to a publisher side.
    /// </summary>
    public sealed record ReceivedNetworkMessage
    {
        /// <summary>
        /// The decoded (and, if the connection was secured, already
        /// unwrapped / plaintext) NetworkMessage.
        /// </summary>
        public PubSubNetworkMessage Message { get; init; } = default!;

        /// <summary>
        /// The raw wire frame the message was decoded from, exactly as
        /// received. May be <see cref="ReadOnlyMemory{T}.IsEmpty"/> when
        /// the source frame is not available (e.g. after chunk
        /// reassembly), which disables the raw-frame fast path.
        /// </summary>
        public ReadOnlyMemory<byte> Frame { get; init; }

        /// <summary>
        /// <see langword="true"/> when <see cref="Frame"/> still carries
        /// message-layer security.
        /// </summary>
        public bool FrameSecured { get; init; }

        /// <summary>
        /// Transport profile URI of the source connection the frame
        /// arrived on.
        /// </summary>
        public string SourceTransportProfileUri { get; init; } = string.Empty;

        /// <summary>
        /// Name of the source connection the frame arrived on.
        /// </summary>
        public string SourceConnectionName { get; init; } = string.Empty;
    }
}
