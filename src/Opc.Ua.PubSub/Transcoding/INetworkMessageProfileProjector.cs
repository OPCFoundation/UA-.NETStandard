// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
