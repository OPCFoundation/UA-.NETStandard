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
using Opc.Ua.Pcap.Bindings;
using Opc.Ua.Pcap.Frame;
using Opc.Ua.Pcap.KeyLog;
using Opc.Ua.Pcap.Models;

namespace Opc.Ua.Pcap.Capture.Sources
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

        /// <summary>
        /// Constructs a new in-process client capture source with internal
        /// queue controls for deterministic testing.
        /// </summary>
        internal InProcessClientCaptureSource(
            IChannelCaptureRegistry registry,
            ILoggerFactory? loggerFactory,
            InProcessCaptureSourceQueueOptions queueOptions)
            : base(registry, loggerFactory, queueOptions)
        {
        }
    }

    /// <summary>
    /// In-process capture source for server-hosting scenarios. Installs the
    /// active <see cref="IFrameCaptureSink"/> observer on the shared
    /// <see cref="IChannelCaptureRegistry"/>; the server listener binding
    /// (<see cref="PcapTransportListenerBinding"/>, installed via
    /// <c>PcapBindings.InstallServer</c> / <c>AddPcap</c>) feeds wire chunks
    /// and channel-token key material from every accepted server channel to
    /// that observer.
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

        /// <summary>
        /// Constructs a new in-process server capture source with internal
        /// queue controls for deterministic testing.
        /// </summary>
        internal InProcessServerCaptureSource(
            IChannelCaptureRegistry registry,
            ILoggerFactory? loggerFactory,
            InProcessCaptureSourceQueueOptions queueOptions)
            : base(registry, loggerFactory, queueOptions)
        {
        }
    }

    /// <summary>
    /// Internal queue configuration used to exercise the in-process capture
    /// worker deterministically without exposing test-only knobs on the
    /// public API.
    /// </summary>
    internal sealed class InProcessCaptureSourceQueueOptions
    {
        /// <summary>
        /// Gets or sets the bounded frame-queue capacity.
        /// </summary>
        public int FrameQueueCapacity { get; init; } = 4096;

        /// <summary>
        /// Gets or sets an optional callback invoked before the writer checks
        /// either queue for more work.
        /// </summary>
        public Func<ValueTask>? BeforeQueueReadAsync { get; init; }

        /// <summary>
        /// Gets or sets an optional callback invoked after a frame work item
        /// has been processed.
        /// </summary>
        public Action? AfterFrameProcessed { get; init; }

        /// <summary>
        /// Gets or sets an optional callback invoked after a key-material work
        /// item has been processed.
        /// </summary>
        public Action? AfterKeyProcessed { get; init; }
    }

    /// <summary>
    /// Shared base class for the two in-process capture sources. Manages
    /// the pcap + keylog file writers and the IFrameCaptureSink observer
    /// installed in the <see cref="IChannelCaptureRegistry"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Captured chunks and key-material snapshots are forwarded to a single
    /// background writer task so the <see cref="IFrameCaptureSink"/> hot path
    /// (which runs inside the channel's send / receive callback) never blocks
    /// on I/O.
    /// </para>
    /// <para>
    /// Chunk work uses a bounded non-blocking queue and may drop the newest
    /// chunk when the queue is full, but key-material snapshots use a separate
    /// unbounded queue so they are not displaced by bulk traffic. The writer
    /// always drains key material before frames and preserves synthetic TCP
    /// sequence gaps for rejected chunks.
    /// </para>
    /// </remarks>
    public abstract class InProcessCaptureSourceBase : ICaptureSource, IFrameCaptureSink
    {
        private const string kPcapFileName = "capture.pcap";
        private const string kKeyLogJsonFileName = "keys.uakeys.json";

        private readonly IChannelCaptureRegistry m_registry;
        private readonly ILoggerFactory m_loggerFactory;
        private readonly InProcessCaptureSourceQueueOptions m_queueOptions;
        private readonly Lock m_sequenceNumbersLock = new();
        private readonly Dictionary<ulong, uint> m_sequenceNumbers = [];

        /// <summary>
        /// CA2213: m_pcapWriter / m_jsonKeyWriter / m_textKeyWriter are owned
        /// by the StopAsync lifecycle, not by a synchronous Dispose. They are
        /// atomically swapped out and AsyncDisposed in StopAsync (see lines
        /// ~268-282); a sync Dispose would have to bridge IAsyncDisposable
        /// with .GetAwaiter().GetResult(), which the repo's no-sync-over-async
        /// rule forbids. The base type intentionally exposes only the async
        /// lifecycle (Start/StopAsync).
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Usage",
            "CA2213:Disposable fields should be disposed",
            Justification = "Owned by StopAsync (async lifecycle); see comment above.")]
        private PcapFileWriter? m_pcapWriter;

        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Usage",
            "CA2213:Disposable fields should be disposed",
            Justification = "Owned by StopAsync (async lifecycle); see comment on m_pcapWriter.")]
        private UaKeyLogJsonWriter? m_jsonKeyWriter;

        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Usage",
            "CA2213:Disposable fields should be disposed",
            Justification = "Owned by StopAsync (async lifecycle); see comment on m_pcapWriter.")]
        private UaKeyLogTextWriter? m_textKeyWriter;

        private string? m_sessionFolder;
        private string? m_resolvedPcapPath;
        private string? m_resolvedJsonKeyLogPath;
#pragma warning disable IDE0052 // Text key-log path is resolved for paired JSON/text key-log capture diagnostics.
        private string? m_resolvedTextKeyLogPath;
#pragma warning restore IDE0052
        private Channel<CaptureWorkItem>? m_frameQueue;
        private Channel<CaptureWorkItem>? m_keyQueue;
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
            : this(
                registry,
                loggerFactory,
                new InProcessCaptureSourceQueueOptions())
        {
        }

        /// <summary>
        /// Constructs a new in-process capture source with internal queue
        /// controls for deterministic testing.
        /// </summary>
        private protected InProcessCaptureSourceBase(
            IChannelCaptureRegistry registry,
            ILoggerFactory? loggerFactory,
            InProcessCaptureSourceQueueOptions queueOptions)
        {
            ArgumentNullException.ThrowIfNull(registry);
            if (queueOptions.FrameQueueCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(queueOptions),
                    "queueOptions.FrameQueueCapacity must be greater than zero.");
            }
            m_registry = registry;
            m_loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
            m_queueOptions = queueOptions;
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

            string pcapPath = ResolveArtifactPath(
                request.PcapFilePath,
                m_sessionFolder,
                kPcapFileName);
            string jsonPath = ResolveArtifactPath(
                request.KeyLogFilePath,
                m_sessionFolder,
                kKeyLogJsonFileName);
            string textPath = ResolveTextKeyLogPath(jsonPath, m_sessionFolder);

            EnsureParentDirectoryExists(pcapPath);
            EnsureParentDirectoryExists(jsonPath);
            EnsureParentDirectoryExists(textPath);

            m_resolvedPcapPath = pcapPath;
            m_resolvedJsonKeyLogPath = jsonPath;
            m_resolvedTextKeyLogPath = textPath;

            m_pcapWriter = new PcapFileWriter(pcapPath, PcapFileWriter.LinkTypeNull);
            m_jsonKeyWriter = new UaKeyLogJsonWriter(jsonPath);
            m_textKeyWriter = new UaKeyLogTextWriter(textPath);

            m_frameQueue = Channel.CreateBounded<CaptureWorkItem>(
                new BoundedChannelOptions(m_queueOptions.FrameQueueCapacity)
                {
                    SingleReader = true,
                    SingleWriter = false,
                    FullMode = BoundedChannelFullMode.Wait
                });
            m_keyQueue = Channel.CreateUnbounded<CaptureWorkItem>(
                new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = false
                });
            m_workerTask = Task.Run(
                () => RunQueueWorkerAsync(
                    m_keyQueue.Reader,
                    m_frameQueue.Reader),
                CancellationToken.None);

            // Publish ourselves as the active observer. This is the only
            // coordination point between the capture session and the
            // Pcap transport binding; from this point every chunk and
            // every token activation observed by any channel created via
            // the binding is delivered here.
            IFrameCaptureSink? previous = m_registry.SetObserver(this);
            if (previous is not null)
            {
                Logger.ObserverAlreadyInstalled();
            }

            ct.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc/>
        public async ValueTask StopAsync(CancellationToken ct)
        {
            // The state may already be StateStopped if EnqueueFrame's
            // byte/frame/duration cap self-stop path transitioned it;
            // we MUST still drain the worker and dispose the writers
            // in that case so any frames queued before the cap was hit
            // are flushed to disk. Without this, a race where the cap
            // is exceeded before the explicit StopAsync call leaves
            // the pcap file empty (observed in macOS net10.0 CI:
            // MaxBytesCapStopsAcceptingFramesAfterLimit fails with
            // 0 records when the worker hasn't yet dequeued the
            // pre-cap frame). Use m_workerTask itself as the
            // single-shot drain guard: Interlocked.Exchange ensures
            // exactly one StopAsync call performs the teardown.
            Interlocked.Exchange(ref m_state, StateStopped);

            Task? workerTask = Interlocked.Exchange(ref m_workerTask, null);
            if (workerTask is null)
            {
                return;
            }

            // Stop receiving new events.
            m_registry.TryClearObserver(this);

            // Drain the worker.
            CompleteWriter(m_keyQueue);
            CompleteWriter(m_frameQueue);
            try
            {
                await workerTask.ConfigureAwait(false);
            }
            catch
            {
                // best effort drain
            }

            PcapFileWriter? pcapWriter = Interlocked.Exchange(ref m_pcapWriter, null);
            if (pcapWriter is not null)
            {
                await pcapWriter.DisposeAsync().ConfigureAwait(false);
            }
            UaKeyLogJsonWriter? jsonKeyWriter = Interlocked.Exchange(ref m_jsonKeyWriter, null);
            if (jsonKeyWriter is not null)
            {
                await jsonKeyWriter.DisposeAsync().ConfigureAwait(false);
            }
            UaKeyLogTextWriter? textKeyWriter = Interlocked.Exchange(ref m_textKeyWriter, null);
            if (textKeyWriter is not null)
            {
                await textKeyWriter.DisposeAsync().ConfigureAwait(false);
            }

            m_keyQueue = null;
            m_frameQueue = null;

            ct.ThrowIfCancellationRequested();
        }

        /// <inheritdoc/>
        public string? GetRawPcapFilePath()
        {
            string? path = m_resolvedPcapPath;
            return path is not null && File.Exists(path) ? path : null;
        }

        /// <inheritdoc/>
        public string? GetKeyLogFilePath()
        {
            string? path = m_resolvedJsonKeyLogPath;
            return path is not null && File.Exists(path) ? path : null;
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
            _ = channelId;
            _ = previousToken;

            Channel<CaptureWorkItem>? keyQueue = m_keyQueue;
            if (keyQueue is null || currentToken is null)
            {
                return;
            }
            ChannelKeyMaterial material;
            try
            {
                // CA2000: ownership of the ChannelKeyMaterial transfers to
                // CaptureWorkItem.ForKey; it is disposed by the worker after processing.
#pragma warning disable CA2000
                material = ChannelKeyMaterial.From(currentToken);
#pragma warning restore CA2000
            }
            catch (Exception ex)
            {
                Logger.SnapshotChannelTokenMaterialFailed(ex);
                return;
            }
            if (!keyQueue.Writer.TryWrite(CaptureWorkItem.ForKey(material)))
            {
                material.Dispose();
                Logger.DroppedCapturedKeyMaterial();
            }
        }

        private void EnqueueFrame(uint channelId, ReadOnlySpan<byte> chunk, bool fromClient)
        {
            Channel<CaptureWorkItem>? frameQueue = m_frameQueue;
            if (frameQueue is null || chunk.IsEmpty)
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

            uint sequenceNumber = ReserveSequenceNumber(channelId, fromClient, chunk.Length);
            frameQueue.Writer.TryWrite(
                CaptureWorkItem.ForFrame(
                    DateTimeOffset.UtcNow,
                    channelId,
                    fromClient,
                    sequenceNumber,
                    chunk.ToArray()));
        }

        private uint ReserveSequenceNumber(uint channelId, bool fromClient, int length)
        {
            ulong key = ((ulong)channelId << 1) | (fromClient ? 1UL : 0UL);
            uint increment = (uint)length;
            lock (m_sequenceNumbersLock)
            {
                if (!m_sequenceNumbers.TryGetValue(key, out uint sequenceNumber))
                {
                    m_sequenceNumbers.Add(key, increment);
                    return 0;
                }
                m_sequenceNumbers[key] = unchecked(sequenceNumber + increment);
                return sequenceNumber;
            }
        }

        private async Task RunQueueWorkerAsync(
            ChannelReader<CaptureWorkItem> keyReader,
            ChannelReader<CaptureWorkItem> frameReader)
        {
            try
            {
                while (true)
                {
                    await WaitForQueueTurnAsync().ConfigureAwait(false);
                    if (TryReadNextWorkItem(keyReader, frameReader, out CaptureWorkItem item))
                    {
                        await ProcessWorkItemAsync(item).ConfigureAwait(false);
                        continue;
                    }

                    if (!await WaitForWorkAsync(keyReader, frameReader).ConfigureAwait(false))
                    {
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.QueueWorkerTerminatedUnexpectedly(ex);
            }
        }

        private async Task ProcessWorkItemAsync(CaptureWorkItem item)
        {
            if (item.Chunk is not null)
            {
                PcapFileWriter? writer = m_pcapWriter;
                if (writer is null)
                {
                    m_queueOptions.AfterFrameProcessed?.Invoke();
                    return;
                }
                try
                {
                    byte[][] packets = LoopbackFrameBuilder.BuildPackets(
                        item.FromClient,
                        item.ChannelId,
                        item.SequenceNumberStart,
                        item.Chunk);
                    foreach (byte[] packet in packets)
                    {
                        await writer.WriteAsync(item.Timestamp, packet, CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    Logger.WriteCapturedFrameFailed(ex);
                }
                m_queueOptions.AfterFrameProcessed?.Invoke();
                return;
            }
            ChannelKeyMaterial? keyMaterial = item.KeyMaterial;
            if (keyMaterial is not null)
            {
                UaKeyLogJsonWriter? jsonWriter = m_jsonKeyWriter;
                UaKeyLogTextWriter? textWriter = m_textKeyWriter;
                try
                {
                    if (jsonWriter is not null && textWriter is not null)
                    {
                        await jsonWriter.AppendAsync(keyMaterial, CancellationToken.None)
                            .ConfigureAwait(false);
                        await textWriter.AppendAsync(keyMaterial, CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    Logger.PersistKeyMaterialSnapshotFailed(ex);
                }
                finally
                {
                    keyMaterial.Dispose();
                }

                m_queueOptions.AfterKeyProcessed?.Invoke();
            }
        }

        private async ValueTask WaitForQueueTurnAsync()
        {
            Func<ValueTask>? beforeQueueReadAsync = m_queueOptions.BeforeQueueReadAsync;
            if (beforeQueueReadAsync is not null)
            {
                await beforeQueueReadAsync().ConfigureAwait(false);
            }
        }

        private static bool TryReadNextWorkItem(
            ChannelReader<CaptureWorkItem> keyReader,
            ChannelReader<CaptureWorkItem> frameReader,
            out CaptureWorkItem item)
        {
            if (keyReader.TryRead(out item))
            {
                return true;
            }

            return frameReader.TryRead(out item);
        }

        private static async ValueTask<bool> WaitForWorkAsync(
            ChannelReader<CaptureWorkItem> keyReader,
            ChannelReader<CaptureWorkItem> frameReader)
        {
            using var waitCancellation = new CancellationTokenSource();
            Task<bool> keyWaitTask = keyReader
                .WaitToReadAsync(waitCancellation.Token)
                .AsTask();
            Task<bool> frameWaitTask = frameReader
                .WaitToReadAsync(waitCancellation.Token)
                .AsTask();
            Task<bool> completedTask = await Task.WhenAny(keyWaitTask, frameWaitTask)
                .ConfigureAwait(false);
            Task<bool> otherTask = ReferenceEquals(completedTask, keyWaitTask)
                ? frameWaitTask
                : keyWaitTask;
            if (!await completedTask.ConfigureAwait(false))
            {
                return await otherTask.ConfigureAwait(false);
            }

            waitCancellation.Cancel();
            try
            {
                await otherTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (waitCancellation.IsCancellationRequested)
            {
                // The other queue wait was canceled after work became available.
            }
            return true;
        }

        private static void CompleteWriter(Channel<CaptureWorkItem>? queue)
        {
            queue?.Writer.TryComplete();
        }

        private readonly struct CaptureWorkItem
        {
            private CaptureWorkItem(
                DateTimeOffset timestamp,
                uint channelId,
                bool fromClient,
                uint sequenceNumberStart,
                byte[]? chunk,
                ChannelKeyMaterial? keyMaterial)
            {
                Timestamp = timestamp;
                ChannelId = channelId;
                FromClient = fromClient;
                SequenceNumberStart = sequenceNumberStart;
                Chunk = chunk;
                KeyMaterial = keyMaterial;
            }

            public DateTimeOffset Timestamp { get; }
            public uint ChannelId { get; }
            public bool FromClient { get; }
            public uint SequenceNumberStart { get; }
            public byte[]? Chunk { get; }
            public ChannelKeyMaterial? KeyMaterial { get; }

            public static CaptureWorkItem ForFrame(
                DateTimeOffset timestamp,
                uint channelId,
                bool fromClient,
                uint sequenceNumberStart,
                byte[] chunk)
            {
                return new CaptureWorkItem(
                    timestamp,
                    channelId,
                    fromClient,
                    sequenceNumberStart,
                    chunk,
                    keyMaterial: null);
            }

            public static CaptureWorkItem ForKey(ChannelKeyMaterial material)
            {
                return new CaptureWorkItem(
                    DateTimeOffset.UtcNow,
                    channelId: 0,
                    fromClient: false,
                    sequenceNumberStart: 0,
                    chunk: null,
                    material);
            }
        }

        private static string ResolveArtifactPath(
            string? requestedPath,
            string sessionFolder,
            string defaultFileName)
        {
            if (string.IsNullOrWhiteSpace(requestedPath))
            {
                return Path.Combine(sessionFolder, defaultFileName);
            }
            return Path.IsPathRooted(requestedPath)
                ? requestedPath
                : Path.Combine(sessionFolder, requestedPath);
        }

        private static string ResolveTextKeyLogPath(string jsonKeyLogPath, string sessionFolder)
        {
            string? directory = Path.GetDirectoryName(jsonKeyLogPath);
            string baseName = Path.GetFileNameWithoutExtension(jsonKeyLogPath);
            // Strip a trailing ".uakeys" segment so e.g. "keys.uakeys.json"
            // produces a "keys.uakeys.txt" sibling rather than "keys.txt".
            if (baseName.EndsWith(".uakeys", StringComparison.OrdinalIgnoreCase))
            {
                baseName = baseName[..^".uakeys".Length];
            }
            string textName = baseName + ".uakeys.txt";
            return string.IsNullOrEmpty(directory)
                ? Path.Combine(sessionFolder, textName)
                : Path.Combine(directory, textName);
        }

        private static void EnsureParentDirectoryExists(string filePath)
        {
            string? directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

    }

    /// <summary>
    /// Source-generated log messages for InProcessCaptureSource.
    /// </summary>
    internal static partial class InProcessCaptureSourceLog
    {
        [LoggerMessage(EventId = CoreDiagnosticsEventIds.InProcessCaptureSource + 0, Level = LogLevel.Warning,
            Message = "InProcessCaptureSource: an observer was already installed in the registry; it has been " +
                "replaced.")]
        public static partial void ObserverAlreadyInstalled(this ILogger logger);

        [LoggerMessage(EventId = CoreDiagnosticsEventIds.InProcessCaptureSource + 1, Level = LogLevel.Warning,
            Message = "Failed to snapshot channel token material.")]
        public static partial void SnapshotChannelTokenMaterialFailed(this ILogger logger, Exception exception);

        [LoggerMessage(EventId = CoreDiagnosticsEventIds.InProcessCaptureSource + 2, Level = LogLevel.Error,
            Message = "In-process capture queue worker terminated unexpectedly.")]
        public static partial void QueueWorkerTerminatedUnexpectedly(this ILogger logger, Exception exception);

        [LoggerMessage(EventId = CoreDiagnosticsEventIds.InProcessCaptureSource + 3, Level = LogLevel.Warning,
            Message = "Failed to write captured frame to pcap.")]
        public static partial void WriteCapturedFrameFailed(this ILogger logger, Exception exception);

        [LoggerMessage(EventId = CoreDiagnosticsEventIds.InProcessCaptureSource + 4, Level = LogLevel.Warning,
            Message = "Failed to persist key material snapshot.")]
        public static partial void PersistKeyMaterialSnapshotFailed(this ILogger logger, Exception exception);

        [LoggerMessage(EventId = CoreDiagnosticsEventIds.InProcessCaptureSource + 5, Level = LogLevel.Warning,
            Message = "Dropped captured key material because the capture session is stopping.")]
        public static partial void DroppedCapturedKeyMaterial(this ILogger logger);
    }

}
