/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

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
