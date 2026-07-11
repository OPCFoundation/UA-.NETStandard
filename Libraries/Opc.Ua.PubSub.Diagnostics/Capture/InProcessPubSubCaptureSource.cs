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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.PubSub.Pcap
{
    /// <summary>
    /// In-process PubSub capture source. Installs itself as the active
    /// <see cref="IPubSubCaptureObserver"/> on an
    /// <see cref="IPubSubCaptureRegistry"/> and buffers every observed
    /// transport frame into a bounded channel for later replay and
    /// dissection. Key material observed via <see cref="AddKeyMaterial"/> is
    /// buffered alongside the frames so encrypted UADP messages can be
    /// decrypted offline (Part 14 §8.3).
    /// </summary>
    public sealed class InProcessPubSubCaptureSource : IPubSubCaptureSource, IPubSubCaptureObserver
    {
        /// <summary>
        /// Initializes a new <see cref="InProcessPubSubCaptureSource"/>.
        /// </summary>
        /// <param name="registry">
        /// The capture registry shared with the PubSub transports.
        /// </param>
        /// <param name="logger">Optional logger.</param>
        /// <param name="capacity">
        /// Maximum number of buffered frames before the oldest is dropped.
        /// </param>
        public InProcessPubSubCaptureSource(
            IPubSubCaptureRegistry registry,
            ILogger<InProcessPubSubCaptureSource>? logger = null,
            int capacity = DefaultCapacity)
        {
            ArgumentNullException.ThrowIfNull(registry);
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }
            m_registry = registry;
            m_logger = logger;
            m_frames = Channel.CreateBounded<PubSubCaptureFrame>(
                new BoundedChannelOptions(capacity)
                {
                    FullMode = BoundedChannelFullMode.DropOldest,
                    SingleReader = false,
                    SingleWriter = false
                });
        }

        /// <inheritdoc/>
        public long FrameCount => Interlocked.Read(ref m_frameCount);

        /// <inheritdoc/>
        public long ByteCount => Interlocked.Read(ref m_byteCount);

        /// <inheritdoc/>
        public ValueTask StartAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.Exchange(ref m_started, 1) == 1)
            {
                throw new InvalidOperationException("Capture source already started.");
            }
            m_registry.SetObserver(this);
            m_logger?.LogDebug("PubSub in-process capture started.");
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc/>
        public ValueTask StopAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.Exchange(ref m_stopped, 1) == 1)
            {
                return ValueTask.CompletedTask;
            }
            m_registry.TryClearObserver(this);
            m_frames.Writer.TryComplete();
            m_logger?.LogDebug(
                "PubSub in-process capture stopped after {FrameCount} frames.",
                FrameCount);
            return ValueTask.CompletedTask;
        }

        /// <summary>
        /// Records a key-material snapshot so encrypted frames captured in
        /// the same session can be decrypted offline. Ownership of
        /// <paramref name="keyMaterial"/> transfers to this source.
        /// </summary>
        /// <param name="keyMaterial">The key snapshot to buffer.</param>
        public void AddKeyMaterial(PubSubKeyMaterial keyMaterial)
        {
            ArgumentNullException.ThrowIfNull(keyMaterial);
            lock (m_keyLock)
            {
                m_keys.Add(keyMaterial);
            }
        }

        /// <inheritdoc/>
        void IPubSubCaptureObserver.OnFrameCaptured(
            in PubSubCaptureContext context,
            ReadOnlySpan<byte> payload)
        {
            if (Volatile.Read(ref m_stopped) == 1)
            {
                return;
            }
            byte[] copy = payload.ToArray();
            var frame = new PubSubCaptureFrame(
                context.Timestamp.ToDateTimeOffset(),
                context.Direction,
                context.TransportProfileUri,
                copy,
                context.Endpoint,
                context.Topic);
            if (m_frames.Writer.TryWrite(frame))
            {
                Interlocked.Increment(ref m_frameCount);
                Interlocked.Add(ref m_byteCount, copy.Length);
            }
        }

        /// <inheritdoc/>
        public async IAsyncEnumerable<PubSubCaptureFrame> ReadCapturedFramesAsync(
            long? maxFrames,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            long yielded = 0;
            ChannelReader<PubSubCaptureFrame> reader = m_frames.Reader;
            while (await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                while (reader.TryRead(out PubSubCaptureFrame frame))
                {
                    if (maxFrames.HasValue && yielded >= maxFrames.Value)
                    {
                        yield break;
                    }
                    yielded++;
                    yield return frame;
                }
            }
        }

        /// <inheritdoc/>
        public async IAsyncEnumerable<PubSubKeyMaterial> ReadKeyMaterialAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            PubSubKeyMaterial[] snapshot;
            lock (m_keyLock)
            {
                snapshot = [.. m_keys];
            }
            foreach (PubSubKeyMaterial key in snapshot)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return key;
            }
            await Task.CompletedTask.ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            await StopAsync().ConfigureAwait(false);
            lock (m_keyLock)
            {
                foreach (PubSubKeyMaterial key in m_keys)
                {
                    key.Dispose();
                }
                m_keys.Clear();
            }
        }

        private const int DefaultCapacity = 65536;

        private readonly IPubSubCaptureRegistry m_registry;
        private readonly ILogger<InProcessPubSubCaptureSource>? m_logger;
        private readonly Channel<PubSubCaptureFrame> m_frames;
        private readonly List<PubSubKeyMaterial> m_keys = [];
        private readonly Lock m_keyLock = new();
        private long m_frameCount;
        private long m_byteCount;
        private int m_started;
        private int m_stopped;
    }
}
