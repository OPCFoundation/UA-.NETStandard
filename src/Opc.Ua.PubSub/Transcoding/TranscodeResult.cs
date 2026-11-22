// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Transports;

namespace Opc.Ua.PubSub.Transcoding
{
    /// <summary>
    /// Result of a frame-level transcode: the encoded (and, where
    /// applicable, security-wrapped) target frames ready to hand to an
    /// egress, plus the projected target NetworkMessages for inspection
    /// and testing.
    /// </summary>
    public sealed record TranscodeResult
    {
        /// <summary>
        /// Encoded target frames, one per projected NetworkMessage. Each
        /// frame is a complete NetworkMessage ready to be sent; the egress
        /// applies transport-level chunking if required.
        /// </summary>
        public ArrayOf<ReadOnlyMemory<byte>> Frames { get; init; } = [];

        /// <summary>
        /// Projected target NetworkMessages corresponding to
        /// <see cref="Frames"/>. Useful for diagnostics and tests.
        /// </summary>
        public ArrayOf<PubSubNetworkMessage> Messages { get; init; } = [];

        /// <summary>
        /// Transport message properties promoted from the source DataSet
        /// fields, applied to every sent frame when the target transport
        /// implements <see cref="IPubSubHeaderTransport"/>. Empty when no
        /// promotion is configured or no promoted field was present.
        /// </summary>
        public ArrayOf<PubSubMessageProperty> Properties { get; init; } = [];

        /// <summary>
        /// <see langword="true"/> when the raw-frame zero-copy passthrough
        /// fast path was taken (no decode / transform / encode).
        /// </summary>
        public bool FastPath { get; init; }

        /// <summary>
        /// <see langword="true"/> when the transcode produced no output
        /// because a transform dropped (filtered) the message.
        /// </summary>
        public bool Dropped => Frames.Count == 0;

        /// <summary>
        /// A result carrying no frames (message filtered out).
        /// </summary>
        public static TranscodeResult Empty { get; } = new();
    }
}
