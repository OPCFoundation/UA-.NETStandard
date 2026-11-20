// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
