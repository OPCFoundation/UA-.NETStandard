// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.PubSub.Connections;
using Opc.Ua.PubSub.Transports;

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
                    "ConnectionTranscodeEgress requires the default PubSubConnection " +
                    "implementation.",
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
            ArrayOf<PubSubMessageProperty> properties = result.Properties;
            for (int i = 0; i < frames.Count; i++)
            {
                await m_connection.ValidateTranscodedFrameSecurityAsync(frames[i], cancellationToken)
                    .ConfigureAwait(false);
            }
            for (int i = 0; i < frames.Count; i++)
            {
                await m_connection
                    .SendValidatedTranscodedFrameAsync(frames[i], topic, properties, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }
}
