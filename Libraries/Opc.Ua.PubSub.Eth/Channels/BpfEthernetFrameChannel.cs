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
using System.Globalization;
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
    /// macOS Berkeley Packet Filter (<c>/dev/bpf*</c>) Layer-2 frame
    /// channel. Opens a BPF device, binds it to the interface, and drives
    /// a blocking <c>read</c> receive loop on a background task. Requires
    /// read/write access to the BPF device (typically root or membership
    /// of the <c>access_bpf</c> group).
    /// </summary>
    /// <remarks>
    /// Uses direct libc P/Invoke (no managed dependency) so it remains
    /// NativeAOT-compatible. Only instantiated on macOS by
    /// <see cref="DefaultEthernetFrameChannelFactory"/>. The BPF header
    /// offsets assume a 64-bit (arm64/x64) macOS runtime.
    /// </remarks>
    internal sealed class BpfEthernetFrameChannel : IEthernetFrameChannel
    {
        private const int ORdwr = 2;
        private const int IfNameSize = 16;
        private const int IfReqSize = 32;
        private const uint IocVoid = 0x20000000;
        private const uint IocOut = 0x40000000;
        private const uint IocIn = 0x80000000;
        private const uint IocParmMask = 0x1FFF;

        // bpf_hdr field offsets on a 64-bit macOS runtime (struct timeval is 16 octets).
        private const int BpfCaplenOffset = 16;
        private const int BpfHdrlenOffset = 24;

        private readonly EthChannelParameters m_parameters;
        private readonly ILogger m_logger;
        private readonly PhysicalAddress m_interfaceAddress;
        private readonly string m_interfaceName;
        private readonly System.Threading.Lock m_sync = new();

        private int m_device = -1;
        private int m_bufferLength;
        private Channel<byte[]>? m_channel;
        private CancellationTokenSource? m_loopCts;
        private Task? m_loopTask;
        private bool m_isOpen;
        private bool m_disposed;

        /// <summary>
        /// Initializes a new <see cref="BpfEthernetFrameChannel"/>.
        /// </summary>
        public BpfEthernetFrameChannel(
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
                    "BPF transport requires a resolved network interface.",
                    nameof(parameters));
            }
            m_logger = telemetry.CreateLogger<BpfEthernetFrameChannel>();
            m_interfaceName = parameters.NetworkInterface.Name;
            m_interfaceAddress = parameters.InterfaceAddress
                ?? parameters.NetworkInterface.GetPhysicalAddress();
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
                    throw new ObjectDisposedException(nameof(BpfEthernetFrameChannel));
                }
                if (m_isOpen)
                {
                    return default;
                }
                int fd = OpenDevice();
                try
                {
                    ConfigureDevice(fd);
                }
                catch
                {
                    _ = NativeMethods.close(fd);
                    throw;
                }
                m_device = fd;
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
                "BPF Ethernet channel opened on interface '{Interface}'.", m_interfaceName);
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
                fd = m_device;
                loopCts = m_loopCts;
                loopTask = m_loopTask;
                channel = m_channel;
                m_device = -1;
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
            lock (m_sync)
            {
                if (m_disposed)
                {
                    throw new ObjectDisposedException(nameof(BpfEthernetFrameChannel));
                }
                if (!m_isOpen || m_device < 0)
                {
                    throw new InvalidOperationException("BPF channel is not open.");
                }
                // Hold the lock across the syscall so CloseAsync cannot close
                // the descriptor (and let the OS reuse it) mid-send, which
                // would write on an unrelated fd (fd-reuse race, ETH-SEC-03).
                nint written = NativeMethods.write(m_device, buffer, (nint)buffer.Length);
                if (written < 0)
                {
                    throw new InvalidOperationException(
                        $"BPF write() failed (errno={Marshal.GetLastWin32Error()}).");
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

        private static int OpenDevice()
        {
            for (int i = 0; i < 256; i++)
            {
                string path = string.Concat("/dev/bpf", i.ToString(CultureInfo.InvariantCulture));
                int fd = NativeMethods.open(path, ORdwr);
                if (fd >= 0)
                {
                    return fd;
                }
            }
            throw new InvalidOperationException(
                "Unable to open a /dev/bpf* device. Root or access_bpf group membership is required.");
        }

        private void ConfigureDevice(int fd)
        {
            var enable = new byte[4];
            BitConverter.GetBytes(1).CopyTo(enable, 0);

            // BIOCSHDRCMPLT: do not let the kernel fill in the source MAC.
            Ioctl(fd, Iow('B', 117, 4), enable, "BIOCSHDRCMPLT");

            // BIOCSETIF: bind to the interface.
            var ifreq = new byte[IfReqSize];
            byte[] name = System.Text.Encoding.ASCII.GetBytes(m_interfaceName);
            Array.Copy(name, 0, ifreq, 0, Math.Min(name.Length, IfNameSize - 1));
            Ioctl(fd, Iow('B', 108, IfReqSize), ifreq, "BIOCSETIF");

            // BIOCIMMEDIATE: deliver frames as they arrive.
            Ioctl(fd, Iow('B', 112, 4), enable, "BIOCIMMEDIATE");

            // BIOCGBLEN: read the kernel buffer length.
            var blen = new byte[4];
            Ioctl(fd, Ior('B', 102, 4), blen, "BIOCGBLEN");
            m_bufferLength = Math.Max(BitConverter.ToInt32(blen, 0), m_parameters.MaxFrameSize);

            if (m_parameters.Promiscuous)
            {
                // BIOCPROMISC (_IO, no argument).
                Ioctl(fd, IocVoid | ((uint)'B' << 8) | 105, Array.Empty<byte>(), "BIOCPROMISC");
            }
        }

        private void ReceiveLoop(CancellationToken cancellationToken)
        {
            Channel<byte[]>? channel = m_channel;
            int fd = m_device;
            if (channel is null || fd < 0)
            {
                return;
            }
            var buffer = new byte[Math.Max(EthernetFrameCodec.MinFrameLength, m_bufferLength)];
            while (!cancellationToken.IsCancellationRequested)
            {
                nint read = NativeMethods.read(fd, buffer, (nint)buffer.Length);
                if (read <= 0)
                {
                    break;
                }
                ParseRecords(buffer, (int)read, channel);
            }
        }

        private void ParseRecords(byte[] buffer, int length, Channel<byte[]> channel)
        {
            int offset = 0;
            while (offset + BpfHdrlenOffset + 2 <= length)
            {
                int caplen = BitConverter.ToInt32(buffer, offset + BpfCaplenOffset);
                ushort hdrlen = BitConverter.ToUInt16(buffer, offset + BpfHdrlenOffset);
                int dataStart = offset + hdrlen;
                // caplen / hdrlen come from the (kernel-supplied) BPF buffer;
                // validate without integer overflow before using them as a
                // length and offset (defence-in-depth, ETH-SEC-04).
                if (caplen <= 0 || dataStart > length || caplen > length - dataStart)
                {
                    break;
                }
                if (caplen <= m_parameters.MaxFrameSize)
                {
                    var frame = new byte[caplen];
                    Buffer.BlockCopy(buffer, dataStart, frame, 0, caplen);
                    if (!channel.Writer.TryWrite(frame))
                    {
                        m_logger.LogTrace("BPF receive queue full; frame dropped.");
                    }
                }
                offset = WordAlign(hdrlen + caplen) + offset;
            }
        }

        private void Ioctl(int fd, uint request, byte[] argument, string name)
        {
            if (NativeMethods.ioctl(fd, request, argument) < 0)
            {
                throw new InvalidOperationException(
                    $"BPF {name} ioctl failed (errno={Marshal.GetLastWin32Error()}).");
            }
        }

        private static int WordAlign(int value)
        {
            return (value + 3) & ~3;
        }

        private static uint Iow(char group, uint number, uint length)
        {
            return IocIn | ((length & IocParmMask) << 16) | ((uint)group << 8) | number;
        }

        private static uint Ior(char group, uint number, uint length)
        {
            return IocOut | ((length & IocParmMask) << 16) | ((uint)group << 8) | number;
        }

        private static class NativeMethods
        {
                [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
                [DllImport("libc", SetLastError = true, CharSet = CharSet.Ansi,
                    BestFitMapping = false, ThrowOnUnmappableChar = true)]
                internal static extern int open(string path, int flags);

                [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
            [DllImport("libc", SetLastError = true)]
                internal static extern int close(int fd);

                [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
                [DllImport("libc", SetLastError = true)]
                internal static extern int ioctl(int fd, ulong request, byte[] argp);

                [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
                [DllImport("libc", SetLastError = true)]
                internal static extern nint read(int fd, byte[] buf, nint count);

                [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
                [DllImport("libc", SetLastError = true)]
                internal static extern nint write(int fd, byte[] buf, nint count);
        }
    }
}
