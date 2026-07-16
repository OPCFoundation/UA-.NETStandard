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

#if NET8_0_OR_GREATER

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SharpPcap;
using SharpPcap.LibPcap;

namespace Opc.Ua.PubSub.Eth.Channels
{
    /// <summary>
    /// SharpPcap (libpcap / Npcap) <see cref="IEthernetFrameChannel"/>.
    /// Opens a live capture device, applies an EtherType BPF filter, and
    /// bridges SharpPcap's synchronous capture callback into the async
    /// receive surface. Requires libpcap (Linux / macOS) or Npcap
    /// (Windows) and usually elevated privileges.
    /// </summary>
    /// <remarks>
    /// SharpPcap is isolated here; the AOT / trimming suppressions on the
    /// SharpPcap-touching members keep the rest of the assembly clean. The
    /// <c>Opc.Ua.Aot.Tests</c> evaluation verifies the backend runs under
    /// NativeAOT.
    /// </remarks>
    internal sealed class PcapEthernetFrameChannel : IEthernetFrameChannel
    {
        private readonly EthChannelParameters m_parameters;
        private readonly ILogger m_logger;
        private readonly string m_interfaceName;
        private readonly string m_filter;
        private readonly Lock m_sync = new();

        private LibPcapLiveDevice? m_device;
        private Channel<byte[]>? m_channel;
        private bool m_isOpen;
        private bool m_disposed;

        /// <summary>
        /// Initializes a new <see cref="PcapEthernetFrameChannel"/>.
        /// </summary>
        public PcapEthernetFrameChannel(
            EthChannelParameters parameters,
            ITelemetryContext telemetry,
            TimeProvider timeProvider)
        {
            m_parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
            _ = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
            if (telemetry is null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            m_logger = telemetry.CreateLogger<PcapEthernetFrameChannel>();
            m_interfaceName = parameters.InterfaceName
                ?? parameters.NetworkInterface?.Name
                ?? throw new ArgumentException(
                    "SharpPcap transport requires an interface name.", nameof(parameters));
            InterfaceAddress = parameters.InterfaceAddress
                ?? parameters.NetworkInterface?.GetPhysicalAddress()
                ?? PhysicalAddress.None;
            m_filter = string.Format(
                CultureInfo.InvariantCulture, "ether proto 0x{0:X4}", parameters.EtherType);
        }

        /// <inheritdoc/>
        public PhysicalAddress InterfaceAddress { get; }

        /// <inheritdoc/>
        public bool IsOpen
        {
            get
            {
                lock (m_sync)
                {
                    return m_isOpen;
                }
            }
        }

        /// <inheritdoc/>
        [UnconditionalSuppressMessage(
            "AOT",
            "IL3050:RequiresDynamicCode",
            Justification = "SharpPcap dynamically loads native libpcap/Npcap; verified by Opc.Ua.Aot.Tests.")]
        [UnconditionalSuppressMessage(
            "Trimming",
            "IL2026:RequiresUnreferencedCode",
            Justification = "SharpPcap dynamically loads native libpcap/Npcap; verified by Opc.Ua.Aot.Tests.")]
        public ValueTask OpenAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_sync)
            {
                if (m_disposed)
                {
                    throw new ObjectDisposedException(nameof(PcapEthernetFrameChannel));
                }
                if (m_isOpen)
                {
                    return default;
                }
                LibPcapLiveDevice device = SelectDevice(m_interfaceName, InterfaceAddress);
                try
                {
                    device.Open(
                        m_parameters.Promiscuous ? DeviceModes.Promiscuous : DeviceModes.None,
                        read_timeout: 1000);
                    device.Filter = m_filter;
                    m_channel = Channel.CreateBounded<byte[]>(
                        new BoundedChannelOptions(Math.Max(1, m_parameters.ReceiveQueueCapacity))
                        {
                            FullMode = BoundedChannelFullMode.DropOldest,
                            SingleReader = true,
                            SingleWriter = true
                        });
                    device.OnPacketArrival += OnPacketArrival;
#pragma warning disable CA1849 // StartCapture is SharpPcap's synchronous capture API.
                    device.StartCapture();
#pragma warning restore CA1849
                }
                catch
                {
                    device.Dispose();
                    throw;
                }
                m_device = device;
                m_isOpen = true;
            }
            m_logger.SharpPcapEthernetChannelOpened(m_interfaceName);
            return default;
        }

        /// <inheritdoc/>
        public ValueTask CloseAsync(CancellationToken cancellationToken = default)
        {
            LibPcapLiveDevice? device;
            Channel<byte[]>? channel;
            bool wasOpen;
            lock (m_sync)
            {
                device = m_device;
                channel = m_channel;
                wasOpen = m_isOpen;
                m_device = null;
                m_channel = null;
                m_isOpen = false;
            }
            if (device is not null)
            {
                CloseDevice(device);
            }
            channel?.Writer.TryComplete();
            if (wasOpen)
            {
                m_logger.SharpPcapEthernetChannelClosed(m_interfaceName);
            }
            cancellationToken.ThrowIfCancellationRequested();
            return default;
        }

        /// <inheritdoc/>
        [UnconditionalSuppressMessage(
            "AOT",
            "IL3050:RequiresDynamicCode",
            Justification = "SharpPcap dynamically loads native libpcap/Npcap; verified by Opc.Ua.Aot.Tests.")]
        [UnconditionalSuppressMessage(
            "Trimming",
            "IL2026:RequiresUnreferencedCode",
            Justification = "SharpPcap dynamically loads native libpcap/Npcap; verified by Opc.Ua.Aot.Tests.")]
        public ValueTask SendFrameAsync(
            ReadOnlyMemory<byte> frame,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LibPcapLiveDevice? device;
            lock (m_sync)
            {
                if (m_disposed)
                {
                    throw new ObjectDisposedException(nameof(PcapEthernetFrameChannel));
                }
                if (!m_isOpen || m_device is null)
                {
                    throw new InvalidOperationException("SharpPcap channel is not open.");
                }
                device = m_device;
            }
            device.SendPacket(frame.Span);
            return default;
        }

        /// <inheritdoc/>
        public async IAsyncEnumerable<ReadOnlyMemory<byte>> ReceiveFramesAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Channel<byte[]>? channel;
            lock (m_sync)
            {
                channel = m_channel;
            }
            if (channel is null)
            {
                yield break;
            }
            await foreach (byte[] frame in channel.Reader
                .ReadAllAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                yield return frame;
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            await CloseAsync().ConfigureAwait(false);
            lock (m_sync)
            {
                m_disposed = true;
            }
        }

        [UnconditionalSuppressMessage(
            "AOT",
            "IL3050:RequiresDynamicCode",
            Justification = "SharpPcap dynamically loads native libpcap/Npcap; verified by Opc.Ua.Aot.Tests.")]
        [UnconditionalSuppressMessage(
            "Trimming",
            "IL2026:RequiresUnreferencedCode",
            Justification = "SharpPcap dynamically loads native libpcap/Npcap; verified by Opc.Ua.Aot.Tests.")]
        private void OnPacketArrival(object sender, PacketCapture e)
        {
            Channel<byte[]>? channel;
            lock (m_sync)
            {
                channel = m_channel;
            }
            if (channel is null)
            {
                return;
            }
            RawCapture packet = e.GetPacket();
            byte[] data = packet.Data;
            if (data.Length == 0 || data.Length > m_parameters.MaxFrameSize)
            {
                return;
            }
            if (!channel.Writer.TryWrite(data))
            {
                m_logger.SharpPcapReceiveQueueFull();
            }
        }

        [UnconditionalSuppressMessage(
            "AOT",
            "IL3050:RequiresDynamicCode",
            Justification = "SharpPcap dynamically loads native libpcap/Npcap; verified by Opc.Ua.Aot.Tests.")]
        [UnconditionalSuppressMessage(
            "Trimming",
            "IL2026:RequiresUnreferencedCode",
            Justification = "SharpPcap dynamically loads native libpcap/Npcap; verified by Opc.Ua.Aot.Tests.")]
        private void CloseDevice(LibPcapLiveDevice device)
        {
            try
            {
                if (device.Started)
                {
#pragma warning disable CA1849 // StopCapture is SharpPcap's synchronous capture API.
                    device.StopCapture();
#pragma warning restore CA1849
                }
            }
            catch (PcapException ex)
            {
                m_logger.SharpPcapStopCaptureRaisedException(ex);
            }
            device.OnPacketArrival -= OnPacketArrival;
            device.Dispose();
        }

        [UnconditionalSuppressMessage(
            "AOT",
            "IL3050:RequiresDynamicCode",
            Justification = "SharpPcap dynamically loads native libpcap/Npcap; verified by Opc.Ua.Aot.Tests.")]
        [UnconditionalSuppressMessage(
            "Trimming",
            "IL2026:RequiresUnreferencedCode",
            Justification = "SharpPcap dynamically loads native libpcap/Npcap; verified by Opc.Ua.Aot.Tests.")]
        private static LibPcapLiveDevice SelectDevice(string interfaceName, PhysicalAddress address)
        {
            var devices = LibPcapLiveDeviceList.New();
            LibPcapLiveDevice? selected = null;
            foreach (LibPcapLiveDevice device in devices)
            {
                if (selected is null && Matches(device, interfaceName, address))
                {
                    selected = device;
                }
                else
                {
                    device.Dispose();
                }
            }
            return selected ??
                throw new InvalidOperationException(
                    $"SharpPcap could not find interface '{interfaceName}'. Is libpcap / Npcap installed?");
        }

        private static bool Matches(
            LibPcapLiveDevice device,
            string interfaceName,
            PhysicalAddress address)
        {
            if (string.Equals(device.Name, interfaceName, StringComparison.Ordinal) ||
                string.Equals(device.Description, interfaceName, StringComparison.Ordinal))
            {
                return true;
            }
            return !PhysicalAddress.None.Equals(address) &&
                address.Equals(device.MacAddress);
        }
    }

    /// <summary>
    /// Source-generated log messages for PcapEthernetFrameChannel.
    /// </summary>
    internal static partial class PcapEthernetFrameChannelLog
    {
        [LoggerMessage(EventId = PubSubEthEventIds.PcapEthernetFrameChannel + 0,
            Level = LogLevel.Information, Message = "SharpPcap Ethernet channel opened on interface '{Interface}'.")]
        public static partial void SharpPcapEthernetChannelOpened(this ILogger logger, string @interface);

        [LoggerMessage(EventId = PubSubEthEventIds.PcapEthernetFrameChannel + 1,
            Level = LogLevel.Information, Message = "SharpPcap Ethernet channel closed on interface '{Interface}'.")]
        public static partial void SharpPcapEthernetChannelClosed(this ILogger logger, string @interface);

        [LoggerMessage(EventId = PubSubEthEventIds.PcapEthernetFrameChannel + 2,
            Level = LogLevel.Trace, Message = "SharpPcap receive queue full; frame dropped.")]
        public static partial void SharpPcapReceiveQueueFull(this ILogger logger);

        [LoggerMessage(EventId = PubSubEthEventIds.PcapEthernetFrameChannel + 3,
            Level = LogLevel.Debug, Message = "SharpPcap StopCapture raised an exception.")]
        public static partial void SharpPcapStopCaptureRaisedException(this ILogger logger, Exception exception);
    }

}

#endif
