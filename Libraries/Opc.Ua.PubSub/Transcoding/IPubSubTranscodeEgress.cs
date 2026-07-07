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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.PubSub.Transcoding
{
    /// <summary>
    /// Publisher-side sink for transcoded frames. Sends each frame in a
    /// <see cref="TranscodeResult"/> on the target transport, applying
    /// transport-level chunking when a frame exceeds the target's maximum
    /// NetworkMessage size.
    /// </summary>
    public interface IPubSubTranscodeEgress
    {
        /// <summary>
        /// Target transport profile URI the egress emits on. Used by the
        /// bridge to select the matching target encoding.
        /// </summary>
        string TransportProfileUri { get; }

        /// <summary>
        /// Sends every frame in <paramref name="result"/> on the target
        /// transport.
        /// </summary>
        /// <param name="result">Transcode output frames.</param>
        /// <param name="topic">
        /// Optional broker topic (MQTT). Ignored by datagram transports.
        /// </param>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask SendAsync(
            TranscodeResult result,
            string? topic = null,
            CancellationToken cancellationToken = default);
    }
}
