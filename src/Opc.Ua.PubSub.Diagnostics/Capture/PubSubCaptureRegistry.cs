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
using System.Threading;

namespace Opc.Ua.PubSub.Pcap
{
    /// <summary>
    /// Default lock-free <see cref="IPubSubCaptureRegistry"/>. Reads on the
    /// transport hot path are a single <see cref="Volatile"/> read; installs
    /// / clears use <see cref="Interlocked"/> so at most one observer is ever
    /// active.
    /// </summary>
    public sealed class PubSubCaptureRegistry : IPubSubCaptureRegistry
    {
        /// <inheritdoc/>
        public IPubSubCaptureObserver? CurrentObserver => Volatile.Read(ref m_observer);

        /// <inheritdoc/>
        public void SetObserver(IPubSubCaptureObserver observer)
        {
            if (observer is null)
            {
                throw new ArgumentNullException(nameof(observer));
            }
            Volatile.Write(ref m_observer, observer);
        }

        /// <inheritdoc/>
        public bool TryClearObserver(IPubSubCaptureObserver observer)
        {
            if (observer is null)
            {
                throw new ArgumentNullException(nameof(observer));
            }
            return ReferenceEquals(
                Interlocked.CompareExchange(ref m_observer, null, observer),
                observer);
        }

        private IPubSubCaptureObserver? m_observer;
    }
}
