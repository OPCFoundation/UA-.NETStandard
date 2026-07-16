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
