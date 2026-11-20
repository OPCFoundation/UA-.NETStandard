/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
