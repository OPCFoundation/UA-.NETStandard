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

namespace Opc.Ua.PubSub.Transports
{
    /// <summary>
    /// Shared coordination point that holds the currently-active
    /// <see cref="IPubSubCaptureObserver"/>. PubSub transports read
    /// <see cref="CurrentObserver"/> on their hot send / receive path; a
    /// diagnostics capture session installs and removes the observer.
    /// </summary>
    /// <remarks>
    /// A single registry instance is shared (typically as a DI singleton)
    /// between the transports and the capture tooling. Reads on the
    /// transport path are lock-free; at most one observer is active at a
    /// time.
    /// </remarks>
    public interface IPubSubCaptureRegistry
    {
        /// <summary>
        /// The observer to notify of sent / received frames, or
        /// <see langword="null"/> when capture is not active. Implementations
        /// expose this as a lock-free volatile read.
        /// </summary>
        IPubSubCaptureObserver? CurrentObserver { get; }

        /// <summary>
        /// Installs <paramref name="observer"/> as the active observer,
        /// replacing any previous one.
        /// </summary>
        /// <param name="observer">The observer to install.</param>
        void SetObserver(IPubSubCaptureObserver observer);

        /// <summary>
        /// Clears the active observer if it is the same instance as
        /// <paramref name="observer"/>.
        /// </summary>
        /// <param name="observer">The observer expected to be active.</param>
        /// <returns>
        /// <see langword="true"/> if <paramref name="observer"/> was active
        /// and has been cleared; otherwise <see langword="false"/>.
        /// </returns>
        bool TryClearObserver(IPubSubCaptureObserver observer);
    }
}
