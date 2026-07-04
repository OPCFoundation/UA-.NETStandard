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
using Opc.Ua.PubSub.Connections;

namespace Opc.Ua.PubSub.Transcoding
{
    /// <summary>
    /// <see cref="IPubSubTranscodeEgress"/> that sends transcoded frames
    /// on a target <see cref="IPubSubConnection"/>'s transport, applying
    /// UADP chunking when a frame exceeds the connection's configured
    /// maximum NetworkMessage size.
    /// </summary>
    public sealed class ConnectionTranscodeEgress : IPubSubTranscodeEgress
    {
        private readonly PubSubConnection m_connection;

        /// <summary>
        /// Initializes a new <see cref="ConnectionTranscodeEgress"/>.
        /// </summary>
        /// <param name="connection">
        /// Target connection to publish transcoded frames on.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="connection"/> is
        /// <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="connection"/> is not the default
        /// <see cref="PubSubConnection"/> implementation.
        /// </exception>
        public ConnectionTranscodeEgress(IPubSubConnection connection)
        {
            if (connection is null)
            {
                throw new ArgumentNullException(nameof(connection));
            }
            m_connection = connection as PubSubConnection
                ?? throw new ArgumentException(
                    "ConnectionTranscodeEgress requires the default PubSubConnection "
                    + "implementation.",
                    nameof(connection));
        }

        /// <inheritdoc/>
        public string TransportProfileUri => m_connection.TransportProfileUri;

        /// <inheritdoc/>
        public async ValueTask SendAsync(
            TranscodeResult result,
            string? topic = null,
            CancellationToken cancellationToken = default)
        {
            if (result is null)
            {
                throw new ArgumentNullException(nameof(result));
            }
            ArrayOf<ReadOnlyMemory<byte>> frames = result.Frames;
            for (int i = 0; i < frames.Count; i++)
            {
                await m_connection
                    .SendTranscodedFrameAsync(frames[i], topic, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }
}
