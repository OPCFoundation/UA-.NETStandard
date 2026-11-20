/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using Opc.Ua.Bindings;

namespace Opc.Ua.Pcap.Bindings
{
    /// <summary>
    /// A process-wide registry that publishes the currently-active
    /// <see cref="IFrameCaptureSink"/> to every
    /// <see cref="CapturingByteTransport"/> created by the Pcap binding.
    /// The registry is the single coordination point between the
    /// CaptureSessionManager (which decides when recording is on or off)
    /// and the capturing socket decorator on the hot path.
    /// </summary>
    /// <remarks>
    /// Writes go through <c>Volatile.Write</c> and reads through
    /// <c>Volatile.Read</c> so the socket can see observer changes
    /// without a lock and without paying for a barrier on every chunk.
    /// When the binding is installed but no session is recording, the
    /// hot path is a single volatile reference read that returns null.
    /// </remarks>
    public interface IChannelCaptureRegistry
    {
        /// <summary>
        /// The currently-active capture observer, or <c>null</c> when no
        /// session is recording.
        /// </summary>
        IFrameCaptureSink? CurrentObserver { get; }

        /// <summary>
        /// Atomically install <paramref name="observer"/> as the active
        /// observer. If another observer was already installed, it is
        /// returned so the caller can compare-and-restore on dispose.
        /// </summary>
        IFrameCaptureSink? SetObserver(IFrameCaptureSink? observer);

        /// <summary>
        /// Atomically clear the active observer only if it currently
        /// references <paramref name="expected"/>. Returns <c>true</c> on
        /// success.
        /// </summary>
        bool TryClearObserver(IFrameCaptureSink expected);
    }
}
