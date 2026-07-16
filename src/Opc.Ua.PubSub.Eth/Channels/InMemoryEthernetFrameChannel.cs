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
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.PubSub.Eth.Channels
{
    /// <summary>
    /// In-memory loopback <see cref="IEthernetFrameChannel"/> produced by
    /// <see cref="InMemoryEthernetFrameChannelFactory"/>. Delivers frames
    /// through a shared in-process bus instead of a privileged socket.
    /// </summary>
    internal sealed class InMemoryEthernetFrameChannel : IEthernetFrameChannel
    {
        private readonly InMemoryEthernetFrameChannelFactory m_factory;
        private readonly string m_key;
        private readonly EthChannelParameters m_parameters;
        private readonly ILogger m_logger;
        private readonly Lock m_sync = new();

        private Channel<byte[]>? m_channel;
        private bool m_isOpen;
        private bool m_disposed;

        /// <summary>
        /// Initializes a new <see cref="InMemoryEthernetFrameChannel"/>.
        /// </summary>
        public InMemoryEthernetFrameChannel(
            InMemoryEthernetFrameChannelFactory factory,
            string key,
            EthChannelParameters parameters,
            ITelemetryContext telemetry,
            TimeProvider timeProvider)
        {
            m_factory = factory ?? throw new ArgumentNullException(nameof(factory));
            m_key = key ?? throw new ArgumentNullException(nameof(key));
            m_parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
            _ = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
            if (telemetry is null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            m_logger = telemetry.CreateLogger<InMemoryEthernetFrameChannel>();
            InterfaceAddress = ResolveInterfaceAddress(parameters);
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
        public ValueTask OpenAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_sync)
            {
                if (m_disposed)
                {
                    throw new ObjectDisposedException(nameof(InMemoryEthernetFrameChannel));
                }
                if (m_isOpen)
                {
                    return default;
                }
                m_channel = Channel.CreateBounded<byte[]>(
                    new BoundedChannelOptions(Math.Max(1, m_parameters.ReceiveQueueCapacity))
                    {
                        FullMode = BoundedChannelFullMode.DropOldest,
                        SingleReader = true,
                        SingleWriter = false
                    });
                m_isOpen = true;
            }
            m_factory.Attach(m_key, this);
            m_logger.InMemoryEthernetChannelOpened(m_key);
            return default;
        }

        /// <inheritdoc/>
        public ValueTask CloseAsync(CancellationToken cancellationToken = default)
        {
            Channel<byte[]>? channel;
            bool wasOpen;
            lock (m_sync)
            {
                channel = m_channel;
                wasOpen = m_isOpen;
                m_channel = null;
                m_isOpen = false;
            }
            if (wasOpen)
            {
                m_factory.Detach(m_key, this);
                channel?.Writer.TryComplete();
                m_logger.InMemoryEthernetChannelClosed(m_key);
            }
            cancellationToken.ThrowIfCancellationRequested();
            return default;
        }

        /// <inheritdoc/>
        public ValueTask SendFrameAsync(
            ReadOnlyMemory<byte> frame,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_sync)
            {
                if (m_disposed)
                {
                    throw new ObjectDisposedException(nameof(InMemoryEthernetFrameChannel));
                }
                if (!m_isOpen)
                {
                    throw new InvalidOperationException(
                        "In-memory Ethernet channel is not open.");
                }
            }
            m_factory.Publish(m_key, this, frame.Span);
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

        /// <summary>
        /// Delivers a frame published by a peer channel into this
        /// channel's receive queue.
        /// </summary>
        /// <param name="frame">The raw frame bytes.</param>
        internal void Deliver(ReadOnlySpan<byte> frame)
        {
            if (frame.Length > m_parameters.MaxFrameSize)
            {
                return;
            }
            Channel<byte[]>? channel;
            lock (m_sync)
            {
                channel = m_channel;
            }
            channel?.Writer.TryWrite(frame.ToArray());
        }

        private static PhysicalAddress ResolveInterfaceAddress(EthChannelParameters parameters)
        {
            if (parameters.InterfaceAddress is not null)
            {
                return parameters.InterfaceAddress;
            }
            PhysicalAddress? fromInterface = parameters.NetworkInterface?.GetPhysicalAddress();
            if (fromInterface is not null && fromInterface.GetAddressBytes().Length == 6)
            {
                return fromInterface;
            }
            return SynthesizeAddress(parameters.InterfaceName);
        }

        private static PhysicalAddress SynthesizeAddress(string? interfaceName)
        {
            byte[] bytes = new byte[6];
            // Locally administered, unicast (bit 1 set, bit 0 clear in the first octet).
            bytes[0] = 0x02;
            int hash = StringComparer.Ordinal.GetHashCode(interfaceName ?? string.Empty);
            bytes[1] = (byte)(hash >> 24);
            bytes[2] = (byte)(hash >> 16);
            bytes[3] = (byte)(hash >> 8);
            bytes[4] = (byte)hash;
            bytes[5] = 0x01;
            return new PhysicalAddress(bytes);
        }
    }

    /// <summary>
    /// Source-generated log messages for InMemoryEthernetFrameChannel.
    /// </summary>
    internal static partial class InMemoryEthernetFrameChannelLog
    {
        [LoggerMessage(EventId = PubSubEthEventIds.InMemoryEthernetFrameChannel + 0, Level = LogLevel.Debug,
            Message = "In-memory Ethernet channel opened on bus '{Bus}'.")]
        public static partial void InMemoryEthernetChannelOpened(this ILogger logger, string bus);

        [LoggerMessage(EventId = PubSubEthEventIds.InMemoryEthernetFrameChannel + 1, Level = LogLevel.Debug,
            Message = "In-memory Ethernet channel closed on bus '{Bus}'.")]
        public static partial void InMemoryEthernetChannelClosed(this ILogger logger, string bus);
    }

}
