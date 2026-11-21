// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Pcap.Capture;
using Opc.Ua.Pcap.Frame;
using Opc.Ua.Pcap.KeyLog;
using Opc.Ua.Pcap.Models;

namespace Opc.Ua.Pcap.Tests
{
    /// <summary>
    /// Minimal in-memory <see cref="ICaptureSource"/> used by formatter
    /// tests. The constructor takes the desired sequence of frames and
    /// (optionally) key materials; subsequent reads enumerate the supplied
    /// values verbatim. No I/O, no threading.
    /// </summary>
    internal sealed class InMemoryCaptureSource : ICaptureSource, IDisposable
    {
        private readonly List<CaptureFrame> m_frames;
        private readonly List<ChannelKeyMaterial> m_materials;
        private readonly string? m_pcapFilePath;
        private readonly string? m_keyLogFilePath;
        private readonly HashSet<FormatKind> m_supportedFormats;

        public InMemoryCaptureSource(
            IEnumerable<CaptureFrame>? frames = null,
            IEnumerable<ChannelKeyMaterial>? materials = null,
            IEnumerable<FormatKind>? supportedFormats = null,
            string? pcapFilePath = null,
            string? keyLogFilePath = null)
        {
            m_frames = [.. frames ?? []];
            m_materials = [.. materials ?? []];
            m_supportedFormats = [.. supportedFormats ??
                [
                    FormatKind.PcapNg,
                    FormatKind.Json,
                    FormatKind.Csv,
                    FormatKind.Text,
                    FormatKind.ServiceTimeline
                    ]];
            m_pcapFilePath = pcapFilePath;
            m_keyLogFilePath = keyLogFilePath;
        }

        /// <inheritdoc/>
        public IReadOnlySet<FormatKind> SupportedFormats => m_supportedFormats;

        /// <inheritdoc/>
        public long FrameCount => m_frames.Count;

        /// <inheritdoc/>
        public long ByteCount
        {
            get
            {
                long total = 0;
                foreach (CaptureFrame frame in m_frames)
                {
                    total += frame.Data.Length;
                }
                return total;
            }
        }

        public int StartCount { get; private set; }
        public int StopCount { get; private set; }
        public int DisposeCount { get; private set; }

        /// <inheritdoc/>
        public ValueTask StartAsync(StartCaptureRequest request, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(request);
            StartCount++;
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc/>
        public ValueTask StopAsync(CancellationToken ct)
        {
            StopCount++;
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc/>
        public string? GetRawPcapFilePath()
        {
            return m_pcapFilePath;
        }

        /// <inheritdoc/>
        public string? GetKeyLogFilePath()
        {
            return m_keyLogFilePath;
        }

        /// <inheritdoc/>
#pragma warning disable CS1998 // async with no awaits — intentional: the test source is in-memory.
        public async IAsyncEnumerable<ChannelKeyMaterial> ReadKeyMaterialAsync(
            [EnumeratorCancellation] CancellationToken ct)
        {
            foreach (ChannelKeyMaterial material in m_materials)
            {
                ct.ThrowIfCancellationRequested();
                yield return material;
            }
        }

        /// <inheritdoc/>
        public async IAsyncEnumerable<CaptureFrame> ReadCapturedFramesAsync(
            long? maxFrames,
            [EnumeratorCancellation] CancellationToken ct)
        {
            long emitted = 0;
            long limit = maxFrames ?? long.MaxValue;
            foreach (CaptureFrame frame in m_frames)
            {
                if (emitted >= limit)
                {
                    yield break;
                }
                ct.ThrowIfCancellationRequested();
                yield return frame;
                emitted++;
            }
        }
#pragma warning restore CS1998

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }

        /// <summary>
        /// Synchronous disposal so that the helper can be used in
        /// non-async test methods via plain <c>using</c>.
        /// </summary>
        public void Dispose()
        {
            DisposeCount++;
        }
    }
}
