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
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Opc.Ua.Bindings;
using Opc.Ua.Bindings.Pcap.Bindings;
using Opc.Ua.Bindings.Pcap.Frame;
using Opc.Ua.Bindings.Pcap.KeyLog;
using Opc.Ua.Bindings.Pcap.Models;

namespace Opc.Ua.Bindings.Pcap.Capture.Sources
{
    /// <summary>
    /// In-process capture source that receives chunks and key material
    /// from the Pcap transport binding via
    /// <see cref="IChannelCaptureRegistry"/>. No reflection, no
    /// per-channel attach: the decorator binding already wraps every
    /// channel created by <see cref="ClientChannelManager"/>; this
    /// source simply installs itself as the active observer when it
    /// starts recording.
    /// </summary>
    /// <remarks>
    /// Capture is turned on or off via <c>StartAsync</c> / <c>StopAsync</c>.
    /// When no in-process source is recording the registry's
    /// <c>CurrentObserver</c> is <c>null</c> and the capturing socket
    /// short-circuits after a single volatile read.
    /// </remarks>
    public sealed class InProcessClientCaptureSource : InProcessCaptureSourceBase
    {
        /// <summary>
        /// Constructs a new in-process client capture source.
        /// </summary>
        public InProcessClientCaptureSource(
            IChannelCaptureRegistry registry,
            ILoggerFactory? loggerFactory = null)
            : base(registry, loggerFactory)
        {
        }
    }

    /// <summary>
    /// In-process capture source for server-hosting scenarios. Same
    /// behaviour as <see cref="InProcessClientCaptureSource"/> - the
    /// distinction is preserved only for future server-side bindings;
    /// today both sources just install an observer on the shared
    /// registry.
    /// </summary>
    public sealed class InProcessServerCaptureSource : InProcessCaptureSourceBase
    {
        /// <summary>
        /// Constructs a new in-process server capture source.
        /// </summary>
        public InProcessServerCaptureSource(
            IChannelCaptureRegistry registry,
            ILoggerFactory? loggerFactory = null)
            : base(registry, loggerFactory)
        {
        }
    }

    /// <summary>
    /// Shared base class for the two in-process capture sources. Manages
    /// the pcap + keylog file writers and the IFrameCaptureSink observer
    /// installed in the <see cref="IChannelCaptureRegistry"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Captured frames and key-material snapshots are forwarded through a
    /// bounded <see cref="System.Threading.Channels.Channel{T}"/> to a
    /// single background writer task so the
    /// <see cref="IFrameCaptureSink"/> hot path (which runs inside the
    /// channel's send / receive callback) never blocks on I/O. Overflow
    /// uses <see cref="System.Threading.Channels.BoundedChannelFullMode.DropOldest"/>
    /// so the source degrades gracefully under sustained load.
    /// </para>
    /// </remarks>
    public abstract class InProcessCaptureSourceBase : ICaptureSource, IFrameCaptureSink
    {
        private const string kPcapFileName = "capture.pcap";
        private const string kKeyLogJsonFileName = "keys.uakeys.json";
        private const string kKeyLogTextFileName = "keys.uakeys.txt";
        private const int kQueueCapacity = 4096;

        private readonly IChannelCaptureRegistry m_registry;
        private readonly ILoggerFactory m_loggerFactory;

        private PcapFileWriter? m_pcapWriter;
        private UaKeyLogJsonWriter? m_jsonKeyWriter;
        private UaKeyLogTextWriter? m_textKeyWriter;
        private string? m_sessionFolder;
        private Channel<CaptureWorkItem>? m_queue;
        private Task? m_workerTask;
        private long m_frameCount;
        private long m_byteCount;
        private long m_maxBytes;
        private long m_maxFrames;
        private DateTimeOffset m_startedAt;
        private TimeSpan m_maxDuration;
        private int m_state;

        private const int StateNew = 0;
        private const int StateRunning = 1;
        private const int StateStopped = 2;

        /// <summary>
        /// Constructs a new in-process capture source.
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="registry"/> is <c>null</c>.
        /// </exception>
        protected InProcessCaptureSourceBase(
            IChannelCaptureRegistry registry,
            ILoggerFactory? loggerFactory = null)
        {
            ArgumentNullException.ThrowIfNull(registry);
            m_registry = registry;
            m_loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
            Logger = m_loggerFactory.CreateLogger(GetType());
        }

        /// <summary>
        /// Logger for derived classes.
        /// </summary>
        protected ILogger Logger { get; }

        /// <inheritdoc/>
        public IReadOnlySet<FormatKind> SupportedFormats { get; } = new HashSet<FormatKind>
        {
            FormatKind.Pcap,
            FormatKind.PcapNg,
            FormatKind.Json,
            FormatKind.Csv,
            FormatKind.Text,
            FormatKind.ServiceTimeline
        };

        /// <inheritdoc/>
        public long FrameCount => Interlocked.Read(ref m_frameCount);

        /// <inheritdoc/>
        public long ByteCount => Interlocked.Read(ref m_byteCount);

        /// <inheritdoc/>
        public ValueTask StartAsync(StartCaptureRequest request, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(request);

            if (Interlocked.CompareExchange(ref m_state, StateRunning, StateNew) != StateNew)
            {
                throw new PcapDiagnosticsException(
                    "InProcessCaptureSource cannot be started twice.");
            }

            m_sessionFolder = request.SessionFolder
                ?? throw new PcapDiagnosticsException(
                    "InProcessCaptureSource requires a sessionFolder.");
            Directory.CreateDirectory(m_sessionFolder);

            m_maxBytes = request.MaxBytes ?? (50L * 1024 * 1024);
            m_maxFrames = request.MaxFrames ?? long.MaxValue;
            m_maxDuration = TimeSpan.FromSeconds(request.MaxDurationSeconds ?? (30 * 60));
            m_startedAt = DateTimeOffset.UtcNow;

            string pcapPath = Path.Combine(m_sessionFolder, kPcapFileName);
            string jsonPath = Path.Combine(m_sessionFolder, kKeyLogJsonFileName);
            string textPath = Path.Combine(m_sessionFolder, kKeyLogTextFileName);

            m_pcapWriter = new PcapFileWriter(pcapPath, PcapFileWriter.LinkTypeNull);
            m_jsonKeyWriter = new UaKeyLogJsonWriter(jsonPath);
            m_textKeyWriter = new UaKeyLogTextWriter(textPath);

            m_queue = Channel.CreateBounded<CaptureWorkItem>(
                new BoundedChannelOptions(kQueueCapacity)
                {
                    SingleReader = true,
                    SingleWriter = false,
                    FullMode = BoundedChannelFullMode.DropOldest
                });
            m_workerTask = Task.Run(
                () => RunQueueWorkerAsync(m_queue.Reader),
                CancellationToken.None);

            // Publish ourselves as the active observer. This is the only
            // coordination point between the capture session and the
            // Pcap transport binding; from this point every chunk and
            // every token activation observed by any channel created via
            // the binding is delivered here.
            IFrameCaptureSink? previous = m_registry.SetObserver(this);
            if (previous is not null)
            {
                Logger.LogWarning(
                    "InProcessCaptureSource: an observer was already installed in the registry; it has been replaced.");
            }

            ct.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc/>
        public async ValueTask StopAsync(CancellationToken ct)
        {
            if (Interlocked.CompareExchange(ref m_state, StateStopped, StateRunning) !=
                StateRunning)
            {
                return;
            }

            // Stop receiving new events.
            m_registry.TryClearObserver(this);

            // Drain the worker.
            m_queue?.Writer.TryComplete();
            if (m_workerTask is not null)
            {
                try
                {
                    await m_workerTask.ConfigureAwait(false);
                }
                catch
                {
                    // best effort drain
                }
                m_workerTask = null;
            }

            if (m_pcapWriter is not null)
            {
                await m_pcapWriter.DisposeAsync().ConfigureAwait(false);
                m_pcapWriter = null;
            }
            if (m_jsonKeyWriter is not null)
            {
                await m_jsonKeyWriter.DisposeAsync().ConfigureAwait(false);
                m_jsonKeyWriter = null;
            }
            if (m_textKeyWriter is not null)
            {
                await m_textKeyWriter.DisposeAsync().ConfigureAwait(false);
                m_textKeyWriter = null;
            }

            ct.ThrowIfCancellationRequested();
        }

        /// <inheritdoc/>
        public string? GetRawPcapFilePath()
        {
            string? folder = m_sessionFolder;
            if (folder is null)
            {
                return null;
            }
            string path = Path.Combine(folder, kPcapFileName);
            return File.Exists(path) ? path : null;
        }

        /// <inheritdoc/>
        public string? GetKeyLogFilePath()
        {
            string? folder = m_sessionFolder;
            if (folder is null)
            {
                return null;
            }
            string path = Path.Combine(folder, kKeyLogJsonFileName);
            return File.Exists(path) ? path : null;
        }

        /// <inheritdoc/>
        public async IAsyncEnumerable<ChannelKeyMaterial> ReadKeyMaterialAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            string? path = GetKeyLogFilePath();
            if (path is null)
            {
                yield break;
            }
            var reader = new UaKeyLogJsonReader();
            await foreach (ChannelKeyMaterial material in reader.ReadAllAsync(path, ct)
                .WithCancellation(ct)
                .ConfigureAwait(false))
            {
                yield return material;
            }
        }

        /// <inheritdoc/>
        public async IAsyncEnumerable<CaptureFrame> ReadCapturedFramesAsync(
            long? maxFrames,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            string? path = GetRawPcapFilePath();
            if (path is null)
            {
                yield break;
            }
            long count = 0;
            long limit = maxFrames ?? long.MaxValue;
            await foreach (PcapRecord record in PcapFileReader.ReadAllAsync(path, ct)
                .WithCancellation(ct)
                .ConfigureAwait(false))
            {
                if (count >= limit)
                {
                    yield break;
                }
                count++;
                yield return new CaptureFrame(
                    record.Timestamp,
                    CaptureFrameDirection.Unknown,
                    string.Empty,
                    string.Empty,
                    record.Data);
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            try
            {
                await StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // best effort
            }
            GC.SuppressFinalize(this);
        }

        /// <inheritdoc/>
        void IFrameCaptureSink.OnFrameSent(uint channelId, ReadOnlySpan<byte> chunk)
        {
            EnqueueFrame(channelId, chunk, fromClient: true);
        }

        /// <inheritdoc/>
        void IFrameCaptureSink.OnFrameReceived(uint channelId, ReadOnlySpan<byte> chunk)
        {
            EnqueueFrame(channelId, chunk, fromClient: false);
        }

        /// <inheritdoc/>
        void IFrameCaptureSink.OnTokenActivated(
            uint channelId,
            ChannelToken currentToken,
            ChannelToken? previousToken)
        {
            Channel<CaptureWorkItem>? queue = m_queue;
            if (queue is null || currentToken is null)
            {
                return;
            }
            ChannelKeyMaterial material;
            try
            {
                // CA2000: ownership of the ChannelKeyMaterial transfers to
                // CaptureWorkItem.ForKey; it is disposed by the worker after the write.
#pragma warning disable CA2000
                material = ChannelKeyMaterial.From(currentToken);
#pragma warning restore CA2000
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to snapshot channel token material.");
                return;
            }
            queue.Writer.TryWrite(CaptureWorkItem.ForKey(material));
        }

        private void EnqueueFrame(uint channelId, ReadOnlySpan<byte> chunk, bool fromClient)
        {
            Channel<CaptureWorkItem>? queue = m_queue;
            if (queue is null || chunk.IsEmpty)
            {
                return;
            }
            long bytes = Interlocked.Add(ref m_byteCount, chunk.Length);
            long frames = Interlocked.Increment(ref m_frameCount);
            if (bytes > m_maxBytes ||
                frames > m_maxFrames ||
                DateTimeOffset.UtcNow - m_startedAt > m_maxDuration)
            {
                // Stop accepting more frames but keep the writers open
                // so already-buffered work flushes properly.
                if (Interlocked.CompareExchange(ref m_state, StateStopped, StateRunning) ==
                    StateRunning)
                {
                    m_registry.TryClearObserver(this);
                }
                return;
            }
            // Copy the chunk bytes; the underlying buffer is pooled and
            // only valid for the duration of this call.
            byte[] packet = LoopbackFrameBuilder.Build(
                fromClient: fromClient,
                channelId: channelId,
                chunkBytes: chunk);
            queue.Writer.TryWrite(CaptureWorkItem.ForFrame(DateTimeOffset.UtcNow, packet));
        }

        private async Task RunQueueWorkerAsync(ChannelReader<CaptureWorkItem> reader)
        {
            try
            {
                while (await reader.WaitToReadAsync().ConfigureAwait(false))
                {
                    while (reader.TryRead(out CaptureWorkItem item))
                    {
                        await ProcessWorkItemAsync(item).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "In-process capture queue worker terminated unexpectedly.");
            }
        }

        private async Task ProcessWorkItemAsync(CaptureWorkItem item)
        {
            if (item.Packet is not null)
            {
                PcapFileWriter? writer = m_pcapWriter;
                if (writer is null)
                {
                    return;
                }
                try
                {
                    await writer.WriteAsync(item.Timestamp, item.Packet, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "Failed to write captured frame to pcap.");
                }
                return;
            }
            if (item.KeyMaterial is not null)
            {
                UaKeyLogJsonWriter? jsonWriter = m_jsonKeyWriter;
                UaKeyLogTextWriter? textWriter = m_textKeyWriter;
                if (jsonWriter is null || textWriter is null)
                {
                    return;
                }
                try
                {
                    await jsonWriter.AppendAsync(item.KeyMaterial, CancellationToken.None)
                        .ConfigureAwait(false);
                    await textWriter.AppendAsync(item.KeyMaterial, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "Failed to persist key material snapshot.");
                }
            }
        }

        private readonly struct CaptureWorkItem
        {
            private CaptureWorkItem(
                DateTimeOffset timestamp,
                byte[]? packet,
                ChannelKeyMaterial? keyMaterial)
            {
                Timestamp = timestamp;
                Packet = packet;
                KeyMaterial = keyMaterial;
            }

            public DateTimeOffset Timestamp { get; }
            public byte[]? Packet { get; }
            public ChannelKeyMaterial? KeyMaterial { get; }

            public static CaptureWorkItem ForFrame(DateTimeOffset timestamp, byte[] packet)
            {
                return new CaptureWorkItem(timestamp, packet, keyMaterial: null);
            }

            public static CaptureWorkItem ForKey(ChannelKeyMaterial material)
            {
                return new CaptureWorkItem(DateTimeOffset.UtcNow, packet: null, material);
            }
        }
    }
}
