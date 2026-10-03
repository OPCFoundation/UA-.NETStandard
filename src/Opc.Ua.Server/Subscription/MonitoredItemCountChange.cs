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

using System.Threading;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Counts the monitored items a create or delete call actually added to or removed
    /// from a subscription. The subscription updates it under its lock in the same step
    /// that changes <see cref="ISubscription.MonitoredItemCount"/>, so a reader can tell
    /// which items of an in-flight call are already part of the subscription.
    /// </summary>
    internal sealed class MonitoredItemCountChange
    {
        /// <summary>
        /// The net number of items added (positive) or removed (negative) so far.
        /// </summary>
        public int Count => Volatile.Read(ref m_count);

        /// <summary>
        /// Records that one item was added to the subscription.
        /// </summary>
        public void Increment()
        {
            Interlocked.Increment(ref m_count);
        }

        /// <summary>
        /// Records that one item was removed from the subscription.
        /// </summary>
        public void Decrement()
        {
            Interlocked.Decrement(ref m_count);
        }

        private int m_count;
    }
}