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
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Opc.Ua.Bindings.Pcap.Capture;
using Opc.Ua.Bindings.Pcap.Capture.Sources;
using Opc.Ua.Bindings.Pcap.Frame;

namespace Opc.Ua.Bindings.Pcap.Replay
{
    /// <summary>
    /// Mock-server replays raw captured server bytes to a connecting client.
    /// It does not perform OPC UA cryptography; the client must be aligned with
    /// the captured client's nonce and certificate for the replay to be
    /// meaningful. For higher-level analysis use the offline decoder pipeline
    /// instead.
    /// </summary>
    public sealed class MockServerReplay : IAsyncDisposable
    {
        /// <summary>
        /// Constructs a mock-server replay over an existing replay source.
        /// </summary>
        public MockServerReplay(
            ICaptureSource source,
            ILoggerFactory? loggerFactory = null)
        {
            ArgumentNullException.ThrowIfNull(source);

            m_source = source;
            m_loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
            m_logger = m_loggerFactory.CreateLogger<MockServerReplay>();
        }

        /// <summary>
        /// URI on which the mock server is listening after <see cref="StartAsync"/>.
        /// </summary>
        public Uri ListenUri { get; private set; } = null!;

        /// <summary>
        /// Replay speed multiplier. Values greater than one replay faster than
        /// real time; values less than one replay slower.
        /// </summary>
        public double Speed { get; set; } = 1.0;

        /// <summary>
        /// Starts listening on loopback and accepts replay clients.
        /// </summary>
        /// <exception cref="PcapDiagnosticsException"></exception>
        public async ValueTask StartAsync(string listenScheme, int? port, CancellationToken ct)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(listenScheme);
            ct.ThrowIfCancellationRequested();

            if (Speed <= 0)
            {
                throw new PcapDiagnosticsException("Replay speed must be greater than zero.");
            }

            if (m_source is not ReplayCaptureSource)
            {
                throw new PcapDiagnosticsException("Mock-server replay requires a ReplayCaptureSource.");
            }

            if (Interlocked.CompareExchange(ref m_started, 1, 0) != 0)
            {
                throw new PcapDiagnosticsException("Mock-server replay cannot be started twice.");
            }

            m_frames = await ReadReplayFramesAsync(ct).ConfigureAwait(false);
            if (m_frames.Count == 0)
            {
                throw new PcapDiagnosticsException("Mock-server replay source contains no captured frames.");
            }

            m_stopCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            m_listener = new TcpListener(IPAddress.Loopback, port ?? 0);
            m_listener.Start();

            var endpoint = (IPEndPoint)m_listener.LocalEndpoint;
            ListenUri = new UriBuilder(listenScheme, IPAddress.Loopback.ToString(), endpoint.Port).Uri;
            m_acceptTask = AcceptLoopAsync(m_stopCts.Token);
            m_logger.LogInformation("Mock-server replay listening on {ListenUri}.", ListenUri);
        }

        /// <summary>
        /// Stops the listener and any in-progress replay.
        /// </summary>
        public async ValueTask StopAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            if (Interlocked.Exchange(ref m_stopping, 1) != 0)
            {
                return;
            }

            m_stopCts?.Cancel();
            m_listener?.Stop();
            m_listener?.Dispose();

            if (m_acceptTask is not null)
            {
                try
                {
                    await m_acceptTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
                catch (SocketException)
                {
                }
                catch (ObjectDisposedException)
                {
                }
            }

            m_stopCts?.Dispose();
            m_stopCts = null;
            m_listener = null;
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
            await m_source.DisposeAsync().ConfigureAwait(false);
            GC.SuppressFinalize(this);
        }

        private async ValueTask<List<ReplayFrame>> ReadReplayFramesAsync(CancellationToken ct)
        {
            var frames = new List<ReplayFrame>();
            await foreach (CaptureFrame frame in m_source.ReadCapturedFramesAsync(null, ct)
                .WithCancellation(ct)
                .ConfigureAwait(false))
            {
                frames.Add(new ReplayFrame(frame.Timestamp, frame.Direction, frame.Data.ToArray()));
            }

            return frames;
        }

        private async Task AcceptLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client = await m_listener!.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                await ReplayConnectionAsync(client, ct).ConfigureAwait(false);
            }
        }

        private async ValueTask ReplayConnectionAsync(TcpClient client, CancellationToken ct)
        {
            using (client)
            {
                using NetworkStream stream = client.GetStream();
                int frameIndex = 0;
                int clientBytesRemaining = 0;
                DateTimeOffset? lastWrittenTimestamp = null;
                byte[] buffer = new byte[8192];

                while (!ct.IsCancellationRequested)
                {
                    int read = await stream.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    int remaining = read;
                    while (remaining > 0 && frameIndex < m_frames.Count)
                    {
                        ReplayFrame current = m_frames[frameIndex];
                        if (current.Direction == CaptureFrameDirection.ServerToClient)
                        {
                            lastWrittenTimestamp = await WriteServerFrameAsync(
                                stream,
                                current,
                                lastWrittenTimestamp,
                                ct).ConfigureAwait(false);
                            frameIndex++;
                            continue;
                        }

                        if (clientBytesRemaining == 0)
                        {
                            clientBytesRemaining = current.Data.Length;
                        }

                        int consume = Math.Min(remaining, clientBytesRemaining);
                        remaining -= consume;
                        clientBytesRemaining -= consume;
                        if (clientBytesRemaining == 0)
                        {
                            frameIndex++;
                        }
                    }
                }
            }
        }

        private async ValueTask<DateTimeOffset> WriteServerFrameAsync(
            NetworkStream stream,
            ReplayFrame frame,
            DateTimeOffset? lastWrittenTimestamp,
            CancellationToken ct)
        {
            if (lastWrittenTimestamp.HasValue)
            {
                TimeSpan delay = ScaleDelay(frame.Timestamp - lastWrittenTimestamp.Value);
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, ct).ConfigureAwait(false);
                }
            }

            await stream.WriteAsync(frame.Data.AsMemory(), ct).ConfigureAwait(false);
            return frame.Timestamp;
        }

        private TimeSpan ScaleDelay(TimeSpan delay)
        {
            if (delay <= TimeSpan.Zero)
            {
                return TimeSpan.Zero;
            }

            return TimeSpan.FromTicks(Math.Max(0, (long)(delay.Ticks / Speed)));
        }

        private readonly struct ReplayFrame
        {
            public ReplayFrame(DateTimeOffset timestamp, CaptureFrameDirection direction, byte[] data)
            {
                Timestamp = timestamp;
                Direction = direction;
                Data = data;
            }

            public DateTimeOffset Timestamp { get; }

            public CaptureFrameDirection Direction { get; }

            public byte[] Data { get; }
        }

        private readonly ICaptureSource m_source;
        private readonly ILoggerFactory m_loggerFactory;
        private readonly ILogger m_logger;
        private List<ReplayFrame> m_frames = [];
        private CancellationTokenSource? m_stopCts;
        private TcpListener? m_listener;
        private Task? m_acceptTask;
        private int m_started;
        private int m_stopping;
    }
}
