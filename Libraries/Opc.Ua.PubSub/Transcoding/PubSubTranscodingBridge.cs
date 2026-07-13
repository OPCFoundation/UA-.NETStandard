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
using Microsoft.Extensions.Logging;
using Opc.Ua.PubSub.Connections;

namespace Opc.Ua.PubSub.Transcoding
{
    /// <summary>
    /// In-process bridge that observes NetworkMessages received on a
    /// source <see cref="IPubSubConnection"/>, transcodes them, and
    /// forwards the result to a publisher-side
    /// <see cref="IPubSubTranscodeEgress"/>. Combines the transformation
    /// primitives into an end-to-end subscriber-to-publisher pipe.
    /// </summary>
    /// <remarks>
    /// The bridge registers itself as an <see cref="IReceivedNetworkMessageSink"/>
    /// on the source connection when <see cref="Start"/> is called and
    /// removes the registration on <see cref="DisposeAsync"/>. The source
    /// connection must be receiving and the egress connection must be
    /// open for messages to flow; the bridge itself owns neither
    /// connection's lifecycle.
    /// </remarks>
    public sealed class PubSubTranscodingBridge : IReceivedNetworkMessageSink, IAsyncDisposable
    {
        private readonly IPubSubConnection m_source;
        private readonly ITranscoder m_transcoder;
        private readonly IPubSubTranscodeEgress m_egress;
        private readonly Func<ReceivedNetworkMessage, string?>? m_topicSelector;
        private readonly ILogger m_logger;
        private readonly Lock m_gate = new();
        private IDisposable? m_registration;

        /// <summary>
        /// Initializes a new <see cref="PubSubTranscodingBridge"/>.
        /// </summary>
        /// <param name="source">Source connection to observe.</param>
        /// <param name="transcoder">Transcoder applied to each message.</param>
        /// <param name="egress">Publisher-side egress for the output.</param>
        /// <param name="telemetry">Telemetry context.</param>
        /// <param name="topicSelector">
        /// Optional selector computing the target broker topic per received
        /// message. When <see langword="null"/> the egress default topic is
        /// used.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when a required dependency is <see langword="null"/>.
        /// </exception>
        public PubSubTranscodingBridge(
            IPubSubConnection source,
            ITranscoder transcoder,
            IPubSubTranscodeEgress egress,
            ITelemetryContext telemetry,
            Func<ReceivedNetworkMessage, string?>? topicSelector = null)
        {
            m_source = source ?? throw new ArgumentNullException(nameof(source));
            m_transcoder = transcoder ?? throw new ArgumentNullException(nameof(transcoder));
            m_egress = egress ?? throw new ArgumentNullException(nameof(egress));
            if (telemetry is null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            m_topicSelector = topicSelector;
            m_logger = telemetry.CreateLogger<PubSubTranscodingBridge>();
        }

        /// <summary>
        /// Name of the source connection this bridge observes.
        /// </summary>
        public string SourceConnectionName => m_source.Name;

        /// <summary>
        /// Registers the bridge on the source connection so received
        /// messages start flowing. Idempotent.
        /// </summary>
        public void Start()
        {
            lock (m_gate)
            {
                m_registration ??= m_source.RegisterReceivedNetworkMessageSink(this);
            }
        }

        /// <inheritdoc/>
        public async ValueTask OnReceivedAsync(
            ReceivedNetworkMessage received,
            CancellationToken cancellationToken = default)
        {
            if (received is null)
            {
                return;
            }

            TranscodeResult result;
            try
            {
                var input = new TranscodeInput(
                    received.Message, received.Frame, received.FrameSecured);
                result = await m_transcoder.TranscodeAsync(input, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                m_logger.LogError(ex,
                    "Transcode failed for a message from '{Connection}'.",
                    received.SourceConnectionName);
                return;
            }

            if (result.Dropped)
            {
                return;
            }

            try
            {
                string? topic = m_topicSelector?.Invoke(received);
                await m_egress.SendAsync(result, topic, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                m_logger.LogError(ex,
                    "Egress failed forwarding a transcoded message from '{Connection}'.",
                    received.SourceConnectionName);
            }
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            IDisposable? registration;
            lock (m_gate)
            {
                registration = m_registration;
                m_registration = null;
            }
            registration?.Dispose();
            return default;
        }
    }
}
