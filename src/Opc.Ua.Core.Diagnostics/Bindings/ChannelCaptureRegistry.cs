/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Threading;
using Opc.Ua.Bindings;

namespace Opc.Ua.Pcap.Bindings
{
    /// <summary>
    /// Default <see cref="IChannelCaptureRegistry"/> implementation. A
    /// single instance is registered as a singleton by
    /// <c>AddPcap</c> and shared between the Pcap transport
    /// binding (which configures every channel's socket with a
    /// <see cref="CapturingByteTransport"/>) and the capture session
    /// (which writes the active observer here when it starts recording).
    /// </summary>
    public sealed class ChannelCaptureRegistry : IChannelCaptureRegistry
    {
        private IFrameCaptureSink? m_currentObserver;

        /// <inheritdoc/>
        public IFrameCaptureSink? CurrentObserver
            => Volatile.Read(ref m_currentObserver);

        /// <inheritdoc/>
        public IFrameCaptureSink? SetObserver(IFrameCaptureSink? observer)
        {
            return Interlocked.Exchange(ref m_currentObserver, observer);
        }

        /// <inheritdoc/>
        public bool TryClearObserver(IFrameCaptureSink expected)
        {
            return ReferenceEquals(
                Interlocked.CompareExchange(ref m_currentObserver, null, expected),
                expected);
        }
    }
}
