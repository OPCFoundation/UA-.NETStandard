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
