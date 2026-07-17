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
