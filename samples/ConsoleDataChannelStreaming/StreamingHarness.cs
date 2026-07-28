/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using Opc.Ua;
using Opc.Ua.Bindings;

namespace ConsoleDataChannelStreaming
{
    /// <summary>
    /// Wires a source and a sink data channel together over the framing
    /// the sample was asked for.
    /// </summary>
    /// <remarks>
    /// The harness keeps both ends in one process so the sample needs no
    /// certificates, no discovery and no configuration file: what it
    /// demonstrates is the data channel layer, not how to configure a
    /// server. The application code above it is identical either way,
    /// which is exactly the point.
    /// </remarks>
    internal sealed class StreamingHarness : IAsyncDisposable
    {
        private StreamingHarness(
            DataChannelManager sourceManager,
            DataChannelManager sinkManager,
            DataChannel source,
            DataChannel sink,
            DataChannelFramingMode framingMode)
        {
            m_sourceManager = sourceManager;
            m_sinkManager = sinkManager;
            Source = source;
            Sink = sink;
            FramingMode = framingMode;
        }

        /// <summary>
        /// The sending end.
        /// </summary>
        public DataChannel Source { get; }

        /// <summary>
        /// The receiving end.
        /// </summary>
        public DataChannel Sink { get; }

        /// <summary>
        /// How frames are delimited on the wire.
        /// </summary>
        public DataChannelFramingMode FramingMode { get; }

        /// <summary>
        /// Builds the harness.
        /// </summary>
        /// <param name="options">The command line.</param>
        /// <param name="ct">Cancellation token.</param>
        public static Task<StreamingHarness> CreateAsync(
            SampleOptions options,
            CancellationToken ct)
        {
            ITelemetryContext telemetry = new ConsoleTelemetry();
            var bufferManager = new BufferManager("sample", 65536, telemetry);

            var settings = new DataChannelSettings
            {
                Direction = DataChannelDirection.SourceToSink,
                DeliveryMode = options.DeliveryMode,
                ContentType = "video/H264",
                MaxFrameSize = (uint)Math.Max(options.FrameSize, 1),
                InitialCredit = (uint)Math.Max(options.FrameSize * 16, 65536),
                FrameDeadline = options.DeliveryMode is DataChannelDeliveryMode.PartiallyReliable
                    or DataChannelDeliveryMode.Unreliable
                    ? 250
                    : 0
            };

            // Both framings are exercised through the same in-process
            // pair. Only the transport under the managers differs, which
            // is what makes the comparison honest.
            bool quic = options.Transport == SampleTransport.Quic;

            var sourceTransport = new InProcessDataChannelTransport(
                bufferManager,
                telemetry,
                quic);

            var sinkTransport = new InProcessDataChannelTransport(
                bufferManager,
                telemetry,
                quic);

            var sourceManager = new DataChannelManager(sourceTransport, true, telemetry);
            var sinkManager = new DataChannelManager(sinkTransport, false, telemetry);

            sourceTransport.Peer = sinkManager;
            sinkTransport.Peer = sourceManager;

            const uint channelId = 1;

            DataChannel source = sourceManager.Register(
                channelId,
                new NodeId("Camera1", 1),
                settings,
                isSource: true);

            DataChannel sink = sinkManager.Register(
                channelId,
                new NodeId("Camera1", 1),
                settings,
                isSource: false);

            sourceManager.MarkOpen(channelId);
            sinkManager.MarkOpen(channelId);

            return Task.FromResult(new StreamingHarness(
                sourceManager,
                sinkManager,
                source,
                sink,
                quic ? DataChannelFramingMode.Quic : DataChannelFramingMode.Inline));
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            await m_sourceManager.DisposeAsync().ConfigureAwait(false);
            await m_sinkManager.DisposeAsync().ConfigureAwait(false);
        }

        private readonly DataChannelManager m_sourceManager;
        private readonly DataChannelManager m_sinkManager;
    }

    /// <summary>
    /// Carries frames between two managers in one process, encoding and
    /// decoding each one so the sample exercises the real codec.
    /// </summary>
    internal sealed class InProcessDataChannelTransport : IDataChannelTransport
    {
        public InProcessDataChannelTransport(
            BufferManager bufferManager,
            ITelemetryContext telemetry,
            bool quic)
        {
            BufferManager = bufferManager;
            TimeProvider = TimeProvider.System;
            FramingMode = quic ? DataChannelFramingMode.Quic : DataChannelFramingMode.Inline;
            HasTransportFlowControl = quic;
            m_telemetry = telemetry;
        }

        /// <summary>
        /// The manager on the far end.
        /// </summary>
        public DataChannelManager? Peer { get; set; }

        /// <inheritdoc/>
        public DataChannelFramingMode FramingMode { get; }

        /// <inheritdoc/>
        public int MaxFrameBodySize => 16384;

        /// <inheritdoc/>
        public bool HasTransportFlowControl { get; }

        /// <inheritdoc/>
        public BufferManager BufferManager { get; }

        /// <inheritdoc/>
        public TimeProvider TimeProvider { get; }

        /// <inheritdoc/>
        public ValueTask SendFrameAsync(DataChannelFrame frame, CancellationToken ct)
        {
            byte[] encoded = new byte[frame.EncodedSize];
            DataChannelFrameCodec.Encode(encoded, frame);

            if (DataChannelFrameCodec.TryDecode(
                encoded,
                0,
                out DataChannelFrame received,
                out _))
            {
                Peer?.HandleFrame(received);
            }

            return default;
        }

        /// <inheritdoc/>
        public void OnProtocolFault(DataChannelFrameError error)
        {
            Console.Error.WriteLine($"protocol fault: {error}");
        }

        private readonly ITelemetryContext m_telemetry;
    }

    /// <summary>
    /// A telemetry context that logs to the console.
    /// </summary>
    internal sealed class ConsoleTelemetry : TelemetryContextBase
    {
        public ConsoleTelemetry()
#pragma warning disable CA2000 // The factory lives as long as the process.
            : base(Microsoft.Extensions.Logging.LoggerFactory.Create(builder => builder
                .AddConsole()
                .SetMinimumLevel(LogLevel.Warning)))
#pragma warning restore CA2000
        {
        }
    }
}
