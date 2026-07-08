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
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.PubSub.Eth.Channels
{
    /// <summary>
    /// Linux <c>AF_PACKET</c>/<c>SOCK_RAW</c> Layer-2 frame channel. Binds
    /// a raw packet socket to a specific interface and EtherType and
    /// drives a blocking <c>recvfrom</c> receive loop on a background
    /// task. Requires the <c>CAP_NET_RAW</c> capability (or root).
    /// </summary>
    /// <remarks>
    /// Uses direct libc P/Invoke (no managed dependency) so it remains
    /// NativeAOT-compatible. Only instantiated on Linux by
    /// <see cref="DefaultEthernetFrameChannelFactory"/>.
    /// </remarks>
    internal sealed class AfPacketEthernetFrameChannel : IEthernetFrameChannel
    {
        private const int AfPacket = 17;
        private const int SockRaw = 3;
        private const int SolPacket = 263;
        private const int PacketAddMembership = 1;
        private const int PacketMrMulticast = 0;
        private const int PacketMrPromisc = 1;

        private readonly EthChannelParameters m_parameters;
        private readonly ILogger m_logger;
        private readonly PhysicalAddress m_interfaceAddress;
        private readonly uint m_interfaceIndex;
        private readonly ushort m_protocol;
        private readonly Lock m_sync = new();

        private int m_socket = -1;
        private Channel<byte[]>? m_channel;
        private CancellationTokenSource? m_loopCts;
        private Task? m_loopTask;
        private bool m_isOpen;
        private bool m_disposed;

        /// <summary>
        /// Initializes a new <see cref="AfPacketEthernetFrameChannel"/>.
        /// </summary>
        public AfPacketEthernetFrameChannel(
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
            if (parameters.NetworkInterface is null)
            {
                throw new ArgumentException(
                    "AF_PACKET transport requires a resolved network interface.",
                    nameof(parameters));
            }
            m_logger = telemetry.CreateLogger<AfPacketEthernetFrameChannel>();
            m_interfaceAddress = parameters.InterfaceAddress
                ?? parameters.NetworkInterface.GetPhysicalAddress();
            m_protocol = HostToNetwork(parameters.EtherType);
            m_interfaceIndex = NativeMethods.if_nametoindex(parameters.NetworkInterface.Name);
            if (m_interfaceIndex == 0)
            {
                throw new InvalidOperationException(
                    $"Unable to resolve interface index for '{parameters.NetworkInterface.Name}'.");
            }
        }

        /// <inheritdoc/>
        public PhysicalAddress InterfaceAddress => m_interfaceAddress;

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
        public ValueTask OpenAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_sync)
            {
                if (m_disposed)
                {
                    throw new ObjectDisposedException(nameof(AfPacketEthernetFrameChannel));
                }
                if (m_isOpen)
                {
                    return default;
                }
                int fd = NativeMethods.socket(AfPacket, SockRaw, m_protocol);
                if (fd < 0)
                {
                    throw new InvalidOperationException(
                        $"AF_PACKET socket() failed (errno={Marshal.GetLastWin32Error()}). " +
                        "CAP_NET_RAW or root is required for Ethernet PubSub.");
                }
                try
                {
                    BindSocket(fd);
                    JoinMembership(fd);
                }
                catch
                {
                    _ = NativeMethods.close(fd);
                    throw;
                }
                m_socket = fd;
                m_channel = Channel.CreateBounded<byte[]>(
                    new BoundedChannelOptions(Math.Max(1, m_parameters.ReceiveQueueCapacity))
                    {
                        FullMode = BoundedChannelFullMode.DropOldest,
                        SingleReader = true,
                        SingleWriter = true
                    });
                m_loopCts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken.None);
                CancellationToken loopToken = m_loopCts.Token;
                m_loopTask = Task.Run(() => ReceiveLoop(loopToken), CancellationToken.None);
                m_isOpen = true;
            }
            m_logger.LogInformation(
                "AF_PACKET Ethernet channel opened on interface '{Interface}' (ifindex={Index}).",
                m_parameters.NetworkInterface!.Name,
                m_interfaceIndex);
            return default;
        }

        /// <inheritdoc/>
        public async ValueTask CloseAsync(CancellationToken cancellationToken = default)
        {
            int fd;
            CancellationTokenSource? loopCts;
            Task? loopTask;
            Channel<byte[]>? channel;
            lock (m_sync)
            {
                fd = m_socket;
                loopCts = m_loopCts;
                loopTask = m_loopTask;
                channel = m_channel;
                m_socket = -1;
                m_loopCts = null;
                m_loopTask = null;
                m_channel = null;
                m_isOpen = false;
            }
            loopCts?.Cancel();
            if (fd >= 0)
            {
                _ = NativeMethods.close(fd);
            }
            if (loopTask is not null)
            {
                try
                {
                    await loopTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
            }
            channel?.Writer.TryComplete();
            loopCts?.Dispose();
            cancellationToken.ThrowIfCancellationRequested();
        }

        /// <inheritdoc/>
        public ValueTask SendFrameAsync(
            ReadOnlyMemory<byte> frame,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            byte[] buffer = frame.ToArray();
            byte[] destination = BuildSockAddr(buffer);
            lock (m_sync)
            {
                if (m_disposed)
                {
                    throw new ObjectDisposedException(nameof(AfPacketEthernetFrameChannel));
                }
                if (!m_isOpen || m_socket < 0)
                {
                    throw new InvalidOperationException("AF_PACKET channel is not open.");
                }
                // Hold the lock across the syscall so CloseAsync cannot close
                // the descriptor (and let the OS reuse it) mid-send, which
                // would send on an unrelated fd (fd-reuse race, ETH-SEC-03).
                nint sent = NativeMethods.sendto(
                    m_socket, buffer, buffer.Length, 0, destination, destination.Length);
                if (sent < 0)
                {
                    throw new InvalidOperationException(
                        $"AF_PACKET sendto() failed (errno={Marshal.GetLastWin32Error()}).");
                }
            }
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

        private void ReceiveLoop(CancellationToken cancellationToken)
        {
            Channel<byte[]>? channel = m_channel;
            int fd = m_socket;
            if (channel is null || fd < 0)
            {
                return;
            }
            byte[] buffer = new byte[Math.Max(EthernetFrameCodec.MinFrameLength, m_parameters.MaxFrameSize)];
            while (!cancellationToken.IsCancellationRequested)
            {
                nint received = NativeMethods.recvfrom(
                    fd, buffer, buffer.Length, 0, IntPtr.Zero, IntPtr.Zero);
                if (received <= 0)
                {
                    // Socket closed or interrupted: terminate the loop.
                    break;
                }
                int length = (int)received;
                if (length > m_parameters.MaxFrameSize)
                {
                    continue;
                }
                byte[] frame = new byte[length];
                Buffer.BlockCopy(buffer, 0, frame, 0, length);
                if (!channel.Writer.TryWrite(frame))
                {
                    m_logger.LogTrace("AF_PACKET receive queue full; frame dropped.");
                }
            }
        }

        private void BindSocket(int fd)
        {
            byte[] address = BuildSockAddr(macAddress: null);
            if (NativeMethods.bind(fd, address, address.Length) < 0)
            {
                throw new InvalidOperationException(
                    $"AF_PACKET bind() failed (errno={Marshal.GetLastWin32Error()}).");
            }
        }

        private void JoinMembership(int fd)
        {
            if (m_parameters.Promiscuous)
            {
                AddMembership(fd, PacketMrPromisc, address: null);
            }
            if (m_parameters.MulticastGroup is not null)
            {
                AddMembership(fd, PacketMrMulticast, m_parameters.MulticastGroup.GetAddressBytes());
            }
        }

        private void AddMembership(int fd, ushort type, byte[]? address)
        {
            // struct packet_mreq { int mr_ifindex; ushort mr_type; ushort mr_alen; byte[8] mr_address; }
            byte[] mreq = new byte[16];
            BitConverter.GetBytes((int)m_interfaceIndex).CopyTo(mreq, 0);
            BitConverter.GetBytes(type).CopyTo(mreq, 4);
            if (address is not null && address.Length == EthernetFrameCodec.MacAddressLength)
            {
                BitConverter.GetBytes((ushort)address.Length).CopyTo(mreq, 6);
                address.CopyTo(mreq, 8);
            }
            if (NativeMethods.setsockopt(fd, SolPacket, PacketAddMembership, mreq, mreq.Length) < 0)
            {
                m_logger.LogWarning(
                    "AF_PACKET membership (type={Type}) failed (errno={Errno}).",
                    type,
                    Marshal.GetLastWin32Error());
            }
        }

        private byte[] BuildSockAddr(byte[]? macAddress)
        {
            // struct sockaddr_ll (20 bytes).
            byte[] address = new byte[20];
            BitConverter.GetBytes((ushort)AfPacket).CopyTo(address, 0);
            BitConverter.GetBytes(m_protocol).CopyTo(address, 2);
            BitConverter.GetBytes((int)m_interfaceIndex).CopyTo(address, 4);
            byte[] mac = macAddress is { Length: EthernetFrameCodec.MacAddressLength }
                ? macAddress
                : ExtractDestinationMac(macAddress);
            address[11] = EthernetFrameCodec.MacAddressLength;
            Array.Copy(mac, 0, address, 12, EthernetFrameCodec.MacAddressLength);
            return address;
        }

        private static byte[] ExtractDestinationMac(byte[]? frame)
        {
            byte[] mac = new byte[EthernetFrameCodec.MacAddressLength];
            if (frame is not null && frame.Length >= EthernetFrameCodec.MacAddressLength)
            {
                Array.Copy(frame, 0, mac, 0, EthernetFrameCodec.MacAddressLength);
            }
            return mac;
        }

        private static ushort HostToNetwork(ushort value)
        {
            return BitConverter.IsLittleEndian
                ? (ushort)((value << 8) | (value >> 8))
                : value;
        }

        private static class NativeMethods
        {
            [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
            [DllImport("libc", SetLastError = true)]
            internal static extern int socket(int domain, int type, int protocol);

            [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
            [DllImport("libc", SetLastError = true)]
            internal static extern int bind(int sockfd, byte[] addr, int addrlen);

            [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
            [DllImport("libc", SetLastError = true)]
            internal static extern int setsockopt(
                int sockfd, int level, int optname, byte[] optval, int optlen);

            [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
            [DllImport("libc", SetLastError = true)]
            internal static extern int close(int fd);

            [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
            [DllImport("libc", SetLastError = true, CharSet = CharSet.Ansi,
                BestFitMapping = false, ThrowOnUnmappableChar = true)]
            internal static extern uint if_nametoindex(string ifname);

            [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
            [DllImport("libc", SetLastError = true)]
            internal static extern nint sendto(
                int sockfd, byte[] buf, nint len, int flags, byte[] destAddr, int addrlen);

            [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
            [DllImport("libc", SetLastError = true)]
            internal static extern nint recvfrom(
                int sockfd, byte[] buf, nint len, int flags, IntPtr srcAddr, IntPtr addrlen);
        }
    }
}
