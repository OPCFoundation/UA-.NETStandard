// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.PubSub.Transcoding
{
    /// <summary>
    /// Frame-level transcode engine: turns a subscriber-received frame or
    /// decoded message into encoded, security-wrapped target frames,
    /// applying the configured <see cref="TranscodeSpec"/> and managing
    /// re-securing across the transcode boundary.
    /// </summary>
    public interface ITranscoder
    {
        /// <summary>
        /// Transcodes <paramref name="input"/> into target frames. Returns
        /// <see cref="TranscodeResult.Empty"/> when a transform drops the
        /// message.
        /// </summary>
        /// <param name="input">Source message and optional raw frame.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The transcode result.</returns>
        ValueTask<TranscodeResult> TranscodeAsync(
            TranscodeInput input,
            CancellationToken cancellationToken = default);
    }
}
