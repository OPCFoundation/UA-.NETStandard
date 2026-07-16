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

using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.PubSub.Transcoding
{
    /// <summary>
    /// Structured transcode primitive: applies a
    /// <see cref="TranscodeSpec"/> (transform pipeline plus profile
    /// projection) to a decoded <see cref="PubSubNetworkMessage"/> and
    /// returns zero or more target messages. This is the pure,
    /// transport- and security-free stage that higher layers (the
    /// frame-level <see cref="ITranscoder"/> and the bridge) build on.
    /// </summary>
    public interface INetworkMessageTranscoder
    {
        /// <summary>
        /// Transcodes <paramref name="source"/> into the target
        /// representation. Returns an empty result when a transform drops
        /// the message.
        /// </summary>
        /// <param name="source">Decoded source NetworkMessage.</param>
        /// <param name="context">Per-run transcode environment.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The projected target NetworkMessages (0..n).</returns>
        ValueTask<ArrayOf<PubSubNetworkMessage>> TranscodeAsync(
            PubSubNetworkMessage source,
            TranscodeContext context,
            CancellationToken cancellationToken = default);
    }
}
