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

using Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.PubSub.Transcoding
{
    /// <summary>
    /// Re-materialises a transport-neutral <see cref="PubSubNetworkMessage"/>
    /// into a concrete target-profile record (UADP or JSON), mapping the
    /// shared identification and payload fields and translating the
    /// profile-specific header fields (content masks, message-type
    /// discriminators, DataSetMessage subtypes). This is the core of the
    /// cross-encoding transformation.
    /// </summary>
    /// <remarks>
    /// Implements the mapping between the UADP
    /// (<see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.4">
    /// Part 14 §7.2.4</see>) and JSON
    /// (<see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.5">
    /// Part 14 §7.2.5</see>) NetworkMessage mappings.
    /// </remarks>
    public interface INetworkMessageProfileProjector
    {
        /// <summary>
        /// Projects <paramref name="source"/> to a concrete record for
        /// <paramref name="targetEncoding"/>. Returns the same instance
        /// when the source is already in the requested encoding and
        /// <paramref name="options"/> requests no format change.
        /// </summary>
        /// <param name="source">Decoded (possibly already transformed) message.</param>
        /// <param name="targetEncoding">Requested target encoding.</param>
        /// <param name="options">Target format options.</param>
        /// <param name="context">Per-run transcode environment.</param>
        /// <returns>The projected concrete NetworkMessage.</returns>
        PubSubNetworkMessage Project(
            PubSubNetworkMessage source,
            TranscodeEncoding targetEncoding,
            TranscodeTargetOptions options,
            TranscodeContext context);
    }
}
