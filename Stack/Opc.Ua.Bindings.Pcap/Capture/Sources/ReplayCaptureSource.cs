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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Opc.Ua.Bindings.Pcap.Frame;
using Opc.Ua.Bindings.Pcap.KeyLog;
using Opc.Ua.Bindings.Pcap.Models;

namespace Opc.Ua.Bindings.Pcap.Capture.Sources
{
    /// <summary>
    /// Replay capture source that projects an existing pcap and optional keylog through ICaptureSource.
    /// </summary>
    public sealed class ReplayCaptureSource : ICaptureSource
    {
        private readonly ILogger m_logger;
        private string? m_pcapFilePath;
        private string? m_keyLogFilePath;
        private long m_frameCount;
        private long m_byteCount;
        private int m_state;

        private const int StateNew = 0;
        private const int StateRunning = 1;
        private const int StateStopped = 2;

        /// <summary>
        /// Constructs a replay capture source.
        /// </summary>
        public ReplayCaptureSource(ILoggerFactory? loggerFactory = null)
        {
            ILoggerFactory factory = loggerFactory ?? NullLoggerFactory.Instance;
            m_logger = factory.CreateLogger<ReplayCaptureSource>();
        }

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
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(request.PcapFilePath))
            {
                throw new PcapDiagnosticsException("Replay capture requires 'pcapFilePath'.");
            }

            if (!File.Exists(request.PcapFilePath))
            {
                throw new PcapDiagnosticsException($"Replay pcap file '{request.PcapFilePath}' does not exist.");
            }

            if (Interlocked.CompareExchange(ref m_state, StateRunning, StateNew) != StateNew)
            {
                throw new PcapDiagnosticsException("ReplayCaptureSource cannot be started twice.");
            }

            m_pcapFilePath = request.PcapFilePath;
            if (!string.IsNullOrWhiteSpace(request.KeyLogFilePath) && File.Exists(request.KeyLogFilePath))
            {
                m_keyLogFilePath = request.KeyLogFilePath;
            }

            return ValueTask.CompletedTask;
        }

        /// <inheritdoc/>
        public ValueTask StopAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Interlocked.Exchange(ref m_state, StateStopped);
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
        public async IAsyncEnumerable<ChannelKeyMaterial> ReadKeyMaterialAsync(
            [EnumeratorCancellation] CancellationToken ct)
        {
            string? path = m_keyLogFilePath;
            if (path is null)
            {
                yield break;
            }

            IKeyLogReader reader = CreateKeyLogReader(path);
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
            [EnumeratorCancellation] CancellationToken ct)
        {
            string? path = m_pcapFilePath;
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
                Interlocked.Increment(ref m_frameCount);
                Interlocked.Add(ref m_byteCount, record.Data.Length);
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
                // Best effort.
            }

            GC.SuppressFinalize(this);
        }

        private IKeyLogReader CreateKeyLogReader(string path)
        {
            string fileName = Path.GetFileName(path);
            if (fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith(".uakeys.json", StringComparison.OrdinalIgnoreCase))
            {
                return new UaKeyLogJsonReader();
            }

            if (fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith(".uakeys.txt", StringComparison.OrdinalIgnoreCase))
            {
                return new UaKeyLogTextReader();
            }

            return new FallbackKeyLogReader(m_logger);
        }

        private sealed class FallbackKeyLogReader : IKeyLogReader
        {
            private readonly ILogger m_logger;

            public FallbackKeyLogReader(ILogger logger)
            {
                m_logger = logger;
            }

            public async IAsyncEnumerable<ChannelKeyMaterial> ReadAllAsync(
                string filePath,
                [EnumeratorCancellation] CancellationToken ct)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

                List<ChannelKeyMaterial>? jsonMaterials = await TryReadJsonKeyLogAsync(filePath, ct)
                    .ConfigureAwait(false);
                if (jsonMaterials is not null)
                {
                    foreach (ChannelKeyMaterial material in jsonMaterials)
                    {
                        yield return material;
                    }

                    yield break;
                }

                await foreach (ChannelKeyMaterial material in new UaKeyLogTextReader().ReadAllAsync(filePath, ct)
                    .WithCancellation(ct)
                    .ConfigureAwait(false))
                {
                    yield return material;
                }
            }

            public IAsyncEnumerable<ChannelKeyMaterial> ReadAllAsync(Stream stream, CancellationToken ct)
            {
                ArgumentNullException.ThrowIfNull(stream);
                return new UaKeyLogJsonReader().ReadAllAsync(stream, ct);
            }

            private async ValueTask<List<ChannelKeyMaterial>?> TryReadJsonKeyLogAsync(
                string filePath,
                CancellationToken ct)
            {
                var materials = new List<ChannelKeyMaterial>();
                try
                {
                    await foreach (ChannelKeyMaterial material in new UaKeyLogJsonReader().ReadAllAsync(filePath, ct)
                        .WithCancellation(ct)
                        .ConfigureAwait(false))
                    {
                        materials.Add(material);
                    }

                    m_logger.LogTrace("Read keylog {KeyLogFilePath} as JSON.", filePath);
                    return materials;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    m_logger.LogTrace(ex, "Unable to read keylog {KeyLogFilePath} as JSON; trying text.", filePath);
                    return null;
                }
            }
        }
    }
}
