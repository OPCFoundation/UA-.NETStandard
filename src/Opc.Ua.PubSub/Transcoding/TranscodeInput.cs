// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.PubSub.Transcoding
{
    /// <summary>
    /// Input to a frame-level transcode. Always carries the decoded
    /// source message (the subscriber receive path has already decoded
    /// it) and, optionally, the raw wire frame as received so the
    /// transcoder can take the zero-copy passthrough fast path when the
    /// route is an identity, same-encoding transcode.
    /// </summary>
    /// <param name="Message">
    /// Decoded (plaintext) source NetworkMessage.
    /// </param>
    /// <param name="SourceFrame">
    /// Raw wire frame exactly as received. <see cref="ReadOnlyMemory{T}.IsEmpty"/>
    /// disables the fast path.
    /// </param>
    /// <param name="SourceFrameSecured">
    /// <see langword="true"/> when <paramref name="SourceFrame"/> still
    /// carries message-layer security (the plaintext lives in
    /// <paramref name="Message"/>).
    /// </param>
    public readonly record struct TranscodeInput(
        PubSubNetworkMessage Message,
        ReadOnlyMemory<byte> SourceFrame = default,
        bool SourceFrameSecured = false);
}
