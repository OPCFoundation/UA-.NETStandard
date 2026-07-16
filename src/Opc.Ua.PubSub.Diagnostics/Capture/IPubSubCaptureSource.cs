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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.PubSub.Pcap
{
    /// <summary>
    /// A PubSub-aware capture source. Implementations buffer captured
    /// frames (and any associated key material) so the recording can be
    /// replayed and dissected later.
    /// </summary>
    /// <remarks>
    /// Lifecycle: construction → <see cref="StartAsync"/> → (capture work) →
    /// <see cref="StopAsync"/> → <see cref="ReadCapturedFramesAsync"/> (any
    /// number of times) → <see cref="IAsyncDisposable.DisposeAsync"/>.
    /// Implementations are sealed per repository convention; extensibility
    /// is achieved by registering an alternative source with the
    /// <see cref="PubSubCaptureSessionManager"/>.
    /// </remarks>
    public interface IPubSubCaptureSource : IAsyncDisposable
    {
        /// <summary>
        /// Number of PubSub frames captured so far.
        /// </summary>
        long FrameCount { get; }

        /// <summary>
        /// Number of payload bytes captured so far.
        /// </summary>
        long ByteCount { get; }

        /// <summary>
        /// Begins capturing.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask StartAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Stops capturing and flushes all buffers. After this returns the
        /// captured records are safe to enumerate via
        /// <see cref="ReadCapturedFramesAsync"/>.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask StopAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Replays every captured PubSub frame from buffered storage. May be
        /// called any number of times after <see cref="StopAsync"/>.
        /// </summary>
        /// <param name="maxFrames">
        /// Optional cap on the number of frames yielded.
        /// </param>
        /// <param name="cancellationToken">Cancellation token.</param>
        IAsyncEnumerable<PubSubCaptureFrame> ReadCapturedFramesAsync(
            long? maxFrames,
            CancellationToken cancellationToken);

        /// <summary>
        /// Replays every captured key-material snapshot in the order it was
        /// observed. Sources that do not capture keys yield nothing.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        IAsyncEnumerable<PubSubKeyMaterial> ReadKeyMaterialAsync(CancellationToken cancellationToken);
    }
}
