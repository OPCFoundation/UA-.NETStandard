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
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Opc.Ua.Pcap.Frame;
using Opc.Ua.Pcap.KeyLog;
using Opc.Ua.Pcap.Models;
using SharpPcap;
using SharpPcap.LibPcap;

namespace Opc.Ua.Pcap.Capture.Sources
{
    /// <summary>
    /// Live network-interface capture source backed by SharpPcap/libpcap.
    /// </summary>
    [RequiresDynamicCode(kSharpPcapDynamicLoadingMessage)]
    [RequiresUnreferencedCode(kSharpPcapDynamicLoadingMessage)]
    public sealed class NicCaptureSource : ICaptureSource
    {
        private const string kPcapFileName = "capture.pcap";

        private const string kSharpPcapDynamicLoadingMessage =
            "SharpPcap requires dynamic native libpcap/Npcap loading and is not NativeAOT/trimming safe.";

        private readonly ILogger m_logger;
        private readonly Lock m_lock = new();

        private LibPcapLiveDevice? m_device;
        private CaptureFileWriterDevice? m_writer;
        private string? m_filePath;
        private long m_frameCount;
        private long m_byteCount;
        private long m_maxBytes;
        private long m_maxFrames;
        private DateTimeOffset m_startedAt;
        private TimeSpan m_maxDuration;
        private volatile bool m_stopRequested;
        private int m_state;

        private const int StateNew = 0;
        private const int StateRunning = 1;
        private const int StateStopped = 2;

        /// <summary>
        /// Constructs a NIC capture source.
        /// </summary>
        public NicCaptureSource(ILoggerFactory? loggerFactory = null)
        {
            ILoggerFactory factory = loggerFactory ?? NullLoggerFactory.Instance;
            m_logger = factory.CreateLogger<NicCaptureSource>();
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
        [RequiresDynamicCode(kSharpPcapDynamicLoadingMessage)]
        [RequiresUnreferencedCode(kSharpPcapDynamicLoadingMessage)]
        public ValueTask StartAsync(StartCaptureRequest request, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(request);
            ct.ThrowIfCancellationRequested();

            if (Interlocked.CompareExchange(ref m_state, StateRunning, StateNew) != StateNew)
            {
                throw new PcapDiagnosticsException("NicCaptureSource cannot be started twice.");
            }

            if (string.IsNullOrWhiteSpace(request.InterfaceName))
            {
                throw new PcapDiagnosticsException(
                    "NIC capture requires 'interfaceName'. Use list_interfaces to discover available interfaces.");
            }

            string sessionFolder = request.SessionFolder
                ?? throw new PcapDiagnosticsException("NicCaptureSource requires a sessionFolder.");
            Directory.CreateDirectory(sessionFolder);
            m_filePath = Path.Combine(sessionFolder, kPcapFileName);
            m_maxBytes = request.MaxBytes ?? (50L * 1024 * 1024);
            m_maxFrames = request.MaxFrames ?? long.MaxValue;
            m_maxDuration = TimeSpan.FromSeconds(request.MaxDurationSeconds ?? (30 * 60));
            m_startedAt = DateTimeOffset.UtcNow;
            m_stopRequested = false;

            LibPcapLiveDevice selected = SelectDevice(request.InterfaceName);
            try
            {
                OpenDevice(selected, request.Promiscuous ?? true);
                ApplyFilter(selected, request.BpfFilter);

                var writer = new CaptureFileWriterDevice(m_filePath, FileMode.Create);
                writer.Open(new DeviceConfiguration { LinkLayerType = selected.LinkType });

                selected.OnPacketArrival += OnPacketArrival;
                m_writer = writer;
                m_device = selected;

#pragma warning disable CA1849 // StartCapture is SharpPcap's synchronous capture API.
                selected.StartCapture();
#pragma warning restore CA1849
            }
            catch (PcapDiagnosticsException)
            {
                m_writer?.Dispose();
                m_writer = null;
                selected.Dispose();
                Interlocked.Exchange(ref m_state, StateStopped);
                throw;
            }
            catch (Exception ex)
            {
                m_writer?.Dispose();
                m_writer = null;
                selected.Dispose();
                Interlocked.Exchange(ref m_state, StateStopped);
                throw new PcapDiagnosticsException(
                    $"Unable to start capture on interface '{request.InterfaceName}'.", ex);
            }

            return ValueTask.CompletedTask;
        }

        /// <inheritdoc/>
        public ValueTask StopAsync(CancellationToken ct)
        {
            if (Interlocked.Exchange(ref m_state, StateStopped) == StateStopped)
            {
                return ValueTask.CompletedTask;
            }

            ct.ThrowIfCancellationRequested();
            lock (m_lock)
            {
                if (m_device is not null)
                {
                    try
                    {
                        m_device.StopCapture();
                    }
                    catch
                    {
                        // Tolerate an already-stopped capture loop.
                    }

                    m_device.OnPacketArrival -= OnPacketArrival;
                    m_device.Dispose();
                    m_device = null;
                }

                if (m_writer is not null)
                {
                    try
                    {
                        m_writer.Close();
                    }
                    catch
                    {
                        // Tolerate an already-closed writer.
                    }

                    m_writer.Dispose();
                    m_writer = null;
                }
            }

            return ValueTask.CompletedTask;
        }

        /// <inheritdoc/>
        public string? GetRawPcapFilePath()
        {
            string? path = m_filePath;
            return path is not null && File.Exists(path) ? path : null;
        }

        /// <inheritdoc/>
        public string? GetKeyLogFilePath()
        {
            return null;
        }

        /// <inheritdoc/>
        public async IAsyncEnumerable<ChannelKeyMaterial> ReadKeyMaterialAsync(
            [EnumeratorCancellation] CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            await Task.CompletedTask.ConfigureAwait(false);
            yield break;
        }

        /// <inheritdoc/>
        public async IAsyncEnumerable<CaptureFrame> ReadCapturedFramesAsync(
            long? maxFrames,
            [EnumeratorCancellation] CancellationToken ct)
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
                // Best effort.
            }

            GC.SuppressFinalize(this);
        }

        private static LibPcapLiveDevice SelectDevice(string interfaceName)
        {
            LibPcapLiveDeviceList devices;
            try
            {
                devices = LibPcapLiveDeviceList.New();
            }
            catch (Exception ex)
            {
                throw new PcapDiagnosticsException(
                    "Unable to enumerate devices — is libpcap / Npcap installed?", ex);
            }

            LibPcapLiveDevice? selected = null;
            foreach (LibPcapLiveDevice device in devices)
            {
                if (string.Equals(device.Name, interfaceName, StringComparison.Ordinal) ||
                    string.Equals(device.Description, interfaceName, StringComparison.Ordinal))
                {
                    selected = device;
                }
                else
                {
                    device.Dispose();
                }
            }

            return selected ??
                throw new PcapDiagnosticsException(
                    $"Interface '{interfaceName}' was not found. Use list_interfaces to discover available interfaces.");
        }

        private void OpenDevice(LibPcapLiveDevice selected, bool promiscuous)
        {
            try
            {
                selected.Open(promiscuous ? DeviceModes.Promiscuous : DeviceModes.None, read_timeout: 1000);
            }
            catch (Exception ex) when (promiscuous)
            {
                m_logger.OpenInterfacePromiscuousModeFailed(ex, selected.Name);
                try
                {
                    selected.Open(DeviceModes.None, read_timeout: 1000);
                }
                catch (Exception retryEx)
                {
                    throw new PcapDiagnosticsException(
                        $"Unable to open interface '{selected.Name}' — is libpcap / Npcap installed and permitted?",
                        retryEx);
                }

            }
            catch (Exception ex)
            {
                throw new PcapDiagnosticsException(
                    $"Unable to open interface '{selected.Name}' — is libpcap / Npcap installed and permitted?", ex);
            }
        }

        private static void ApplyFilter(LibPcapLiveDevice selected, string? bpfFilter)
        {
            if (string.IsNullOrEmpty(bpfFilter))
            {
                return;
            }

            try
            {
                selected.Filter = bpfFilter;
            }
            catch (Exception ex)
            {
                throw new PcapDiagnosticsException($"Unable to apply BPF filter '{bpfFilter}'.", ex);
            }
        }

        private void OnPacketArrival(object sender, PacketCapture e)
        {
            if (m_stopRequested)
            {
                return;
            }

            RawCapture packet = e.GetPacket();
            int length = packet.PacketLength;
            long frames = Interlocked.Increment(ref m_frameCount);
            long bytes = Interlocked.Add(ref m_byteCount, length);

            lock (m_lock)
            {
                m_writer?.Write(packet);
            }

            if (bytes > m_maxBytes || frames > m_maxFrames || DateTimeOffset.UtcNow - m_startedAt > m_maxDuration)
            {
                m_stopRequested = true;
                _ = Task.Run(StopDeviceCaptureBestEffort);
            }
        }

        private void StopDeviceCaptureBestEffort()
        {
            try
            {
                m_device?.StopCapture();
            }
            catch
            {
                // Final shutdown is handled by StopAsync.
            }
        }
    }

    /// <summary>
    /// Source-generated log messages for <see cref="NicCaptureSource"/>.
    /// </summary>
    internal static partial class NicCaptureSourceLog
    {
        [LoggerMessage(EventId = CoreDiagnosticsEventIds.NicCaptureSource + 0, Level = LogLevel.Warning,
            Message = "Unable to open interface {InterfaceName} in promiscuous mode; retrying without " +
                "promiscuous mode.")]
        public static partial void OpenInterfacePromiscuousModeFailed(
            this ILogger logger,
            Exception exception,
            string? interfaceName);
    }

}
