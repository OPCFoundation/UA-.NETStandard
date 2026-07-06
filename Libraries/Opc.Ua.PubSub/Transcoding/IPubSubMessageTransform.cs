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
    /// A single, composable transformation step applied to a decoded
    /// <see cref="PubSubNetworkMessage"/> during transcoding. Transforms
    /// run in order on the transport-neutral message tree before the
    /// profile projection re-materialises the concrete target record.
    /// </summary>
    /// <remarks>
    /// Transforms operate on immutable records and return a new (or the
    /// same) message; they must not mutate the input. Returning
    /// <see langword="null"/> drops the message from the output (a
    /// filtering transform). Implementations should be allocation-light —
    /// prefer record <c>with</c> copies over rebuilding whole trees.
    /// </remarks>
    public interface IPubSubMessageTransform
    {
        /// <summary>
        /// Transforms <paramref name="message"/>, returning the new
        /// message, the same instance when unchanged, or
        /// <see langword="null"/> to drop it.
        /// </summary>
        /// <param name="message">Decoded source message.</param>
        /// <param name="context">Per-run transcode environment.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>
        /// The transformed message, or <see langword="null"/> to filter
        /// it out.
        /// </returns>
        ValueTask<PubSubNetworkMessage?> TransformAsync(
            PubSubNetworkMessage message,
            TranscodeContext context,
            CancellationToken cancellationToken = default);
    }
}
