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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Opc.Ua.Bindings;
using Opc.Ua.Diagnostics.Pcap.Frame;
using Opc.Ua.Diagnostics.Pcap.KeyLog;
using Opc.Ua.Diagnostics.Pcap.Models;

namespace Opc.Ua.Diagnostics.Pcap.Capture.Sources
{
    /// <summary>
    /// In-process capture source that taps every channel registered with
    /// it through <c>AttachChannel</c>. Writes the captured chunks
    /// to a libpcap file (with synthesized BSD-loopback framing so the
    /// pcap is openable in Wireshark) and the channel key material to a
    /// JSON keylog alongside it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Consumers wire this source into their OPC UA stack manually:
    /// <list type="number">
    /// <item><description>
    /// Call <see cref="AttachClientChannel"/> for every
    /// <see cref="ITransportChannel"/> they create. The source subscribes
    /// to that channel's <see cref="ISecureChannel.OnTokenActivated"/>
    /// event and installs an <see cref="IFrameCaptureSink"/> on the
    /// underlying <see cref="UaSCUaBinaryChannel"/> via reflection on the
    /// inner channel (the source defers to a per-channel adapter so it
    /// works without subclassing).
    /// </description></item>
    /// </list>
    /// </para>
    /// </remarks>
    public sealed class InProcessClientCaptureSource : InProcessCaptureSourceBase
    {
        /// <summary>
        /// Constructs a new in-process client capture source.
        /// </summary>
        public InProcessClientCaptureSource(ILoggerFactory? loggerFactory = null)
            : base(loggerFactory)
        {
        }

        /// <summary>
        /// Subscribes to the channel's token-activated event and forwards
        /// every captured chunk + token to the pcap/keylog files. The
        /// channel's underlying <see cref="UaSCUaBinaryChannel"/> is
        /// expected to expose a <see cref="IFrameCaptureSink"/> property
        /// (see <c>FrameCaptureSink</c>) which this source replaces with
        /// its own observer.
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="channel"/> is <c>null</c>.
        /// </exception>
        public void AttachClientChannel(ITransportChannel channel)
        {
            ArgumentNullException.ThrowIfNull(channel);
            ThrowIfNotRunning();

            if (channel is not ISecureChannel secureChannel)
            {
                Logger.LogWarning(
                    "InProcessClientCaptureSource: channel of type {Type} does not " +
                    "implement ISecureChannel; cannot capture key material.",
                    channel.GetType().Name);
                return;
            }

            void OnTokenActivated(
                ITransportChannel ch,
                ChannelToken? currentToken,
                ChannelToken? previousToken)
            {
                if (currentToken is not null)
                {
                    OnTokenObserved(ChannelKeyMaterial.From(currentToken));
                }
            }

            secureChannel.OnTokenActivated += OnTokenActivated;
            RegisterDisposeAction(() => secureChannel.OnTokenActivated -= OnTokenActivated);

            // Try to install the frame sink on the wrapped UaSCUaBinaryChannel.
            UaSCUaBinaryChannel? inner = ExtractInnerBinaryChannel(channel);
            if (inner is null)
            {
                Logger.LogWarning(
                    "InProcessClientCaptureSource: could not locate inner " +
                    "UaSCUaBinaryChannel for {Type}; only key material will be captured.",
                    channel.GetType().Name);
                return;
            }
            AttachFrameSink(inner);
        }

        private static UaSCUaBinaryChannel? ExtractInnerBinaryChannel(ITransportChannel channel)
        {
            // UaSCUaBinaryTransportChannel keeps the actual binary channel in
            // a private field 'm_channel'. We walk its non-public instance
            // fields once and cache nothing - the cost only matters at
            // attach time.
            System.Reflection.FieldInfo[] fields = channel.GetType().GetFields(
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public);
            foreach (System.Reflection.FieldInfo field in fields)
            {
                object? value = field.GetValue(channel);
                if (value is UaSCUaBinaryChannel binary)
                {
                    return binary;
                }
            }
            return null;
        }
    }

    /// <summary>
    /// In-process capture source for server-side channels. Subscribes to
    /// every <see cref="TcpListenerChannel"/>'s
    /// <c>OnTokenActivated</c> event.
    /// </summary>
    public sealed class InProcessServerCaptureSource : InProcessCaptureSourceBase
    {
        /// <summary>
        /// Constructs a new in-process server capture source.
        /// </summary>
        public InProcessServerCaptureSource(ILoggerFactory? loggerFactory = null)
            : base(loggerFactory)
        {
        }

        /// <summary>
        /// Attaches the source to a server-side channel.
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="channel"/> is <c>null</c>.
        /// </exception>
        public void AttachServerChannel(TcpListenerChannel channel)
        {
            ArgumentNullException.ThrowIfNull(channel);
            ThrowIfNotRunning();

            void OnTokenActivated(
                TcpListenerChannel ch,
                ChannelToken? currentToken,
                ChannelToken? previousToken)
            {
                if (currentToken is not null)
                {
                    OnTokenObserved(ChannelKeyMaterial.From(currentToken));
                }
            }

            channel.OnTokenActivated += OnTokenActivated;
            RegisterDisposeAction(() => channel.OnTokenActivated -= OnTokenActivated);
            AttachFrameSink(channel);
        }
    }

    /// <summary>
    /// Shared base class for the two in-process capture sources. Manages
    /// the pcap + keylog file writers and the IFrameCaptureSink observer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Captured frames and key-material snapshots are forwarded through a
    /// bounded <see cref="System.Threading.Channels.Channel{T}"/> to a
    /// single background writer task so the event handlers (which run on
    /// the OPC UA send / receive path) never block on I/O. Overflow uses
    /// <see cref="System.Threading.Channels.BoundedChannelFullMode.DropOldest"/>
    /// so we degrade gracefully under sustained load.
    /// </para>
    /// </remarks>
    public abstract class InProcessCaptureSourceBase : ICaptureSource
    {
        private const string kPcapFileName = "capture.pcap";
        private const string kKeyLogJsonFileName = "keys.uakeys.json";
        private const string kKeyLogTextFileName = "keys.uakeys.txt";
        private const int kQueueCapacity = 4096;

        private readonly ILoggerFactory m_loggerFactory;
        private readonly ConcurrentBag<Action> m_disposeActions = [];

        private PcapFileWriter? m_pcapWriter;
        private UaKeyLogJsonWriter? m_jsonKeyWriter;
        private UaKeyLogTextWriter? m_textKeyWriter;
        private string? m_sessionFolder;
        private CaptureFrameSinkObserver? m_sink;
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
        protected InProcessCaptureSourceBase(ILoggerFactory? loggerFactory = null)
        {
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
            m_sink = new CaptureFrameSinkObserver(this);

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

            ct.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc/>
        public async ValueTask StopAsync(CancellationToken ct)
        {
            if (Interlocked.CompareExchange(ref m_state, StateStopped, StateRunning)
                != StateRunning)
            {
                return;
            }

            foreach (Action action in m_disposeActions)
            {
                try { action(); } catch { /* best effort */ }
            }
            m_disposeActions.Clear();

            // Signal the worker to drain and exit.
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

        /// <summary>
        /// Called by the derived in-process source whenever a token is
        /// activated on one of its tracked channels. The snapshot is
        /// enqueued for asynchronous persistence; this method never
        /// blocks on I/O.
        /// </summary>
        protected void OnTokenObserved(ChannelKeyMaterial material)
        {
            ArgumentNullException.ThrowIfNull(material);
            Channel<CaptureWorkItem>? queue = m_queue;
            if (queue is null)
            {
                return;
            }
            queue.Writer.TryWrite(CaptureWorkItem.ForKey(material));
        }

        /// <summary>
        /// Attaches the frame-capture sink to the supplied binary channel.
        /// </summary>
        protected void AttachFrameSink(UaSCUaBinaryChannel channel)
        {
            ArgumentNullException.ThrowIfNull(channel);
            CaptureFrameSinkObserver? sink = m_sink;
            if (sink is null)
            {
                throw new InvalidOperationException(
                    "Capture source has not been started.");
            }
            channel.FrameCaptureSink = sink;
            RegisterDisposeAction(() =>
            {
                if (ReferenceEquals(channel.FrameCaptureSink, sink))
                {
                    channel.FrameCaptureSink = null;
                }
            });
        }

        /// <summary>
        /// Registers an action to invoke during <see cref="StopAsync"/>.
        /// </summary>
        protected void RegisterDisposeAction(Action action)
        {
            ArgumentNullException.ThrowIfNull(action);
            m_disposeActions.Add(action);
        }

        /// <summary>
        /// Throws if the source has not been started.
        /// </summary>
        protected void ThrowIfNotRunning()
        {
            if (m_state != StateRunning)
            {
                throw new InvalidOperationException(
                    "Capture source must be started before channels can be attached.");
            }
        }

        private void OnFrameCaptured(uint channelId, ReadOnlySpan<byte> chunk, bool sent)
        {
            Channel<CaptureWorkItem>? queue = m_queue;
            if (queue is null || chunk.IsEmpty)
            {
                return;
            }
            long bytes = Interlocked.Add(ref m_byteCount, chunk.Length);
            long frames = Interlocked.Increment(ref m_frameCount);
            if (bytes > m_maxBytes || frames > m_maxFrames ||
                DateTimeOffset.UtcNow - m_startedAt > m_maxDuration)
            {
                // Stop accepting more frames but keep the writers open
                // so already-buffered work flushes properly.
                Interlocked.CompareExchange(ref m_state, StateStopped, StateRunning);
                return;
            }
            // Copy the chunk bytes; the underlying buffer is pooled and
            // only valid for the duration of this call.
            byte[] packet = LoopbackFrameBuilder.Build(
                fromClient: sent,
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

        private sealed class CaptureFrameSinkObserver : IFrameCaptureSink
        {
            private readonly InProcessCaptureSourceBase m_owner;
            public CaptureFrameSinkObserver(InProcessCaptureSourceBase owner)
            {
                m_owner = owner;
            }

            public void OnFrameSent(uint channelId, ReadOnlySpan<byte> chunk)
            {
                m_owner.OnFrameCaptured(channelId, chunk, sent: true);
            }

            public void OnFrameReceived(uint channelId, ReadOnlySpan<byte> chunk)
            {
                m_owner.OnFrameCaptured(channelId, chunk, sent: false);
            }
        }
    }
}
