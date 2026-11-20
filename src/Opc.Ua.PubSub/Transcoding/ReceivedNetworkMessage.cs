// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
