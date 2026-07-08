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
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Opc.Ua.Pcap.Capture;
using Opc.Ua.Pcap.DependencyInjection;
using Opc.Ua.PubSub.Pcap;

using OpcUaMcpServerOptions = Opc.Ua.Mcp.McpServerOptions;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// MCP tools for OPC UA PubSub packet capture sessions.
    /// </summary>
    [McpServerToolType]
    [SuppressMessage(
        "Performance",
        "CA1812:Avoid uninstantiated internal classes",
        Justification = "MCP discovers tool types through reflection; TODO: remove when supported.")]
    internal sealed class PubSubCaptureTools
    {
        /// <summary>
        /// Starts an in-process OPC UA PubSub capture session.
        /// </summary>
        [McpServerTool(Name = "pubsub_start_capture")]
        [Description("Starts a new in-process OPC UA PubSub capture session. The MCP server must share the " +
            "registered PubSub capture registry with the PubSub transports for live frames to appear.")]
        public static async Task<PubSubCaptureSessionInfo> StartCaptureAsync(
            PubSubCaptureSessionManager manager,
            CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(manager);

            await ClearLastSourceAsync(ct).ConfigureAwait(false);
            IPubSubCaptureSource source = await manager.StartAsync(ct).ConfigureAwait(false);
            return CreateInfo(source, isActive: true);
        }

        /// <summary>
        /// Stops the active OPC UA PubSub capture session.
        /// </summary>
        [McpServerTool(Name = "pubsub_stop_capture")]
        [Description("Stops the active in-process PubSub capture session and keeps a reusable in-memory snapshot " +
            "for pubsub_write_pcap and pubsub_dissect_capture.")]
        public static async Task<PubSubCaptureSessionInfo> StopCaptureAsync(
            PubSubCaptureSessionManager manager,
            CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(manager);

            IPubSubCaptureSource? source = await manager.StopAsync(ct).ConfigureAwait(false);
            if (source is null)
            {
                IPubSubCaptureSource? last = await GetLastSourceAsync(ct).ConfigureAwait(false);
                if (last is null)
                {
                    return new PubSubCaptureSessionInfo
                    {
                        IsActive = false,
                        FrameCount = 0,
                        ByteCount = 0,
                        State = "idle"
                    };
                }

                return CreateInfo(last, isActive: false);
            }

            SnapshotPubSubCaptureSource snapshot = await SnapshotPubSubCaptureSource.CreateAsync(source, ct)
                .ConfigureAwait(false);
            await source.DisposeAsync().ConfigureAwait(false);
            await StoreLastSourceAsync(snapshot, ct).ConfigureAwait(false);
            return CreateInfo(snapshot, isActive: false);
        }

        /// <summary>
        /// Reports the active or last OPC UA PubSub capture status.
        /// </summary>
        [McpServerTool(Name = "pubsub_capture_status")]
        [Description("Reports whether a PubSub capture is active and returns frame/byte counters for the active " +
            "capture or the last stopped capture snapshot.")]
        public static async Task<PubSubCaptureSessionInfo> CaptureStatusAsync(
            PubSubCaptureSessionManager manager,
            CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(manager);

            IPubSubCaptureSource? active = manager.ActiveSource;
            if (active is not null)
            {
                return CreateInfo(active, isActive: true);
            }

            IPubSubCaptureSource? last = await GetLastSourceAsync(ct).ConfigureAwait(false);
            return last is null
                ? new PubSubCaptureSessionInfo
                {
                    IsActive = false,
                    FrameCount = 0,
                    ByteCount = 0,
                    State = "idle"
                }
                : CreateInfo(last, isActive: false);
        }

        /// <summary>
        /// Writes the active or last OPC UA PubSub capture to a pcap file.
        /// </summary>
        [McpServerTool(Name = "pubsub_write_pcap")]
        [Description("Writes the active or last stopped PubSub capture to a .pcap or .pcapng file. If a capture is " +
            "active, it is stopped first so the buffered frames can be flushed safely. Only UDP/UADP frames are " +
            "written; MQTT payloads are skipped by the PubSub pcap writer.")]
        public static async Task<PubSubPcapWriteInfo> WritePcapAsync(
            IServiceProvider services,
            PubSubCaptureSessionManager manager,
            [Description("Destination .pcap or .pcapng path under the MCP pcap base folder. ")] string filePath,
            CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(manager);
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

            string allowedRoot = GetPcapAllowedRoot(services);
            filePath = PacketDecodeTools.ResolveAndValidateDecodePath(filePath, allowedRoot);
            string? directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            IPubSubCaptureSource source = await GetStoppedSourceAsync(manager, ct).ConfigureAwait(false);
            var writer = new PubSubPcapWriter();
            bool isPcapNg = IsPcapNgPath(filePath);
            long framesWritten = isPcapNg
                ? await writer.WritePcapNgAsync(source.ReadCapturedFramesAsync(null, ct), filePath, ct)
                    .ConfigureAwait(false)
                : await writer.WritePcapAsync(source.ReadCapturedFramesAsync(null, ct), filePath, ct)
                    .ConfigureAwait(false);
            return new PubSubPcapWriteInfo
            {
                FilePath = filePath,
                Format = isPcapNg ? "pcapng" : "pcap",
                FramesCaptured = source.FrameCount,
                BytesCaptured = source.ByteCount,
                FramesWritten = framesWritten
            };
        }

        internal static async ValueTask<IPubSubCaptureSource> GetLastStoppedSourceAsync(
            PubSubCaptureSessionManager manager,
            CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(manager);

            if (manager.ActiveSource is not null)
            {
                throw new PcapDiagnosticsException(
                    "A PubSub capture is still active; stop it with pubsub_stop_capture before dissecting it.");
            }

            IPubSubCaptureSource? source = await GetLastSourceAsync(ct).ConfigureAwait(false);
            return source ??
                throw new PcapDiagnosticsException(
                    "No stopped PubSub capture is available. Start and stop a capture first.");
        }

        private static async ValueTask<IPubSubCaptureSource> GetStoppedSourceAsync(
            PubSubCaptureSessionManager manager,
            CancellationToken ct)
        {
            IPubSubCaptureSource? active = manager.ActiveSource;
            if (active is not null)
            {
                IPubSubCaptureSource? stopped = await manager.StopAsync(ct).ConfigureAwait(false);
                if (stopped is not null)
                {
                    SnapshotPubSubCaptureSource snapshot = await SnapshotPubSubCaptureSource.CreateAsync(stopped, ct)
                        .ConfigureAwait(false);
                    await stopped.DisposeAsync().ConfigureAwait(false);
                    await StoreLastSourceAsync(snapshot, ct).ConfigureAwait(false);
                    return snapshot;
                }
            }

            return await GetLastStoppedSourceAsync(manager, ct).ConfigureAwait(false);
        }

        private static PubSubCaptureSessionInfo CreateInfo(IPubSubCaptureSource source, bool isActive)
        {
            return new PubSubCaptureSessionInfo
            {
                IsActive = isActive,
                FrameCount = source.FrameCount,
                ByteCount = source.ByteCount,
                State = isActive ? "running" : "stopped"
            };
        }

        private static async ValueTask StoreLastSourceAsync(IPubSubCaptureSource source, CancellationToken ct)
        {
            await m_lastSourceGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                IPubSubCaptureSource? previous = m_lastSource;
                m_lastSource = source;
                if (previous is not null && !ReferenceEquals(previous, source))
                {
                    await previous.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                m_lastSourceGate.Release();
            }
        }

        private static async ValueTask ClearLastSourceAsync(CancellationToken ct)
        {
            await m_lastSourceGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                IPubSubCaptureSource? previous = m_lastSource;
                m_lastSource = null;
                if (previous is not null)
                {
                    await previous.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                m_lastSourceGate.Release();
            }
        }

        private static async ValueTask<IPubSubCaptureSource?> GetLastSourceAsync(CancellationToken ct)
        {
            await m_lastSourceGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                return m_lastSource;
            }
            finally
            {
                m_lastSourceGate.Release();
            }
        }

        private static string GetPcapAllowedRoot(IServiceProvider services)
        {
            OpcUaMcpServerOptions? mcpOptions =
                services.GetService(typeof(OpcUaMcpServerOptions)) as OpcUaMcpServerOptions;
            if (mcpOptions is not null &&
                !string.IsNullOrWhiteSpace(mcpOptions.PcapBaseFolder))
            {
                return Path.GetFullPath(mcpOptions.PcapBaseFolder!);
            }

            PcapOptions? options = services.GetService(typeof(PcapOptions)) as PcapOptions;
            return options?.BaseFolder ??
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "OPCFoundation",
                    "opcua-pcap");
        }

        private static bool IsPcapNgPath(string filePath)
        {
            return string.Equals(Path.GetExtension(filePath), ".pcapng", StringComparison.OrdinalIgnoreCase);
        }

        private static readonly SemaphoreSlim m_lastSourceGate = new(1, 1);
        private static IPubSubCaptureSource? m_lastSource;

        private sealed class SnapshotPubSubCaptureSource : IPubSubCaptureSource
        {
            private SnapshotPubSubCaptureSource(
                IReadOnlyList<PubSubCaptureFrame> frames,
                IReadOnlyList<PubSubKeyMaterial> keyMaterial,
                long byteCount)
            {
                m_frames = frames;
                m_keyMaterial = keyMaterial;
                ByteCount = byteCount;
            }

            public long FrameCount => m_frames.Count;

            public long ByteCount { get; }

            public static async ValueTask<SnapshotPubSubCaptureSource> CreateAsync(
                IPubSubCaptureSource source,
                CancellationToken ct)
            {
                List<PubSubCaptureFrame> frames = [];
                await foreach (PubSubCaptureFrame frame in source.ReadCapturedFramesAsync(null, ct)
                    .WithCancellation(ct)
                    .ConfigureAwait(false))
                {
                    frames.Add(CopyFrame(in frame));
                }

                List<PubSubKeyMaterial> keys = [];
                await foreach (PubSubKeyMaterial key in source.ReadKeyMaterialAsync(ct)
                    .WithCancellation(ct)
                    .ConfigureAwait(false))
                {
                    keys.Add(CopyKeyMaterial(key));
                }

                return new SnapshotPubSubCaptureSource(frames, keys, source.ByteCount);
            }

            public ValueTask StartAsync(CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return ValueTask.CompletedTask;
            }

            public ValueTask StopAsync(CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return ValueTask.CompletedTask;
            }

            public async IAsyncEnumerable<PubSubCaptureFrame> ReadCapturedFramesAsync(
                long? maxFrames,
                [EnumeratorCancellation] CancellationToken cancellationToken)
            {
                long yielded = 0;
                foreach (PubSubCaptureFrame frame in m_frames)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (maxFrames.HasValue && yielded >= maxFrames.Value)
                    {
                        yield break;
                    }
                    yielded++;
                    yield return frame;
                }

                await Task.CompletedTask.ConfigureAwait(false);
            }

            public async IAsyncEnumerable<PubSubKeyMaterial> ReadKeyMaterialAsync(
                [EnumeratorCancellation] CancellationToken cancellationToken)
            {
                foreach (PubSubKeyMaterial key in m_keyMaterial)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    yield return CopyKeyMaterial(key);
                }

                await Task.CompletedTask.ConfigureAwait(false);
            }

            public ValueTask DisposeAsync()
            {
                foreach (PubSubKeyMaterial key in m_keyMaterial)
                {
                    key.Dispose();
                }

                return ValueTask.CompletedTask;
            }

            private static PubSubCaptureFrame CopyFrame(in PubSubCaptureFrame frame)
            {
                return new PubSubCaptureFrame(
                    frame.Timestamp,
                    frame.Direction,
                    frame.TransportProfileUri,
                    frame.Data.ToArray(),
                    frame.Endpoint,
                    frame.Topic);
            }

            private static PubSubKeyMaterial CopyKeyMaterial(PubSubKeyMaterial key)
            {
                return new PubSubKeyMaterial(
                    key.SecurityGroupId,
                    key.TokenId,
                    key.SecurityPolicyUri,
                    key.SigningKey.ToArray(),
                    key.EncryptingKey.ToArray(),
                    key.KeyNonce.ToArray());
            }

            private readonly IReadOnlyList<PubSubCaptureFrame> m_frames;
            private readonly IReadOnlyList<PubSubKeyMaterial> m_keyMaterial;
        }
    }

    /// <summary>
    /// Status and counters for a PubSub capture session.
    /// </summary>
    public sealed class PubSubCaptureSessionInfo
    {
        /// <summary>
        /// Gets whether a PubSub capture is currently active.
        /// </summary>
        public bool IsActive { get; init; }

        /// <summary>
        /// Gets the number of captured PubSub frames.
        /// </summary>
        public long FrameCount { get; init; }

        /// <summary>
        /// Gets the number of captured PubSub payload bytes.
        /// </summary>
        public long ByteCount { get; init; }

        /// <summary>
        /// Gets the capture state.
        /// </summary>
        public string State { get; init; } = string.Empty;
    }

    /// <summary>
    /// Result of writing a PubSub capture to disk.
    /// </summary>
    public sealed class PubSubPcapWriteInfo
    {
        /// <summary>
        /// Gets the destination file path.
        /// </summary>
        public string FilePath { get; init; } = string.Empty;

        /// <summary>
        /// Gets the pcap file format.
        /// </summary>
        public string Format { get; init; } = string.Empty;

        /// <summary>
        /// Gets the number of frames held by the capture.
        /// </summary>
        public long FramesCaptured { get; init; }

        /// <summary>
        /// Gets the number of payload bytes held by the capture.
        /// </summary>
        public long BytesCaptured { get; init; }

        /// <summary>
        /// Gets the number of UDP frames written to the pcap file.
        /// </summary>
        public long FramesWritten { get; init; }
    }
}
