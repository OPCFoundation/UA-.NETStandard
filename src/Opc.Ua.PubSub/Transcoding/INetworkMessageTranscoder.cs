// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
