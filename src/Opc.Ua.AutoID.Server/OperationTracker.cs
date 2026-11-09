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
using System.Threading.Tasks;

namespace Opc.Ua.AutoID.Server
{
    /// <summary>
    /// Admits operations until it is closed and lets the closer wait until the admitted ones
    /// have finished.
    /// </summary>
    /// <remarks>
    /// Admission and the closing flag are decided under one lock, so an operation is either
    /// admitted before the close and awaited by it, or rejected: there is no window in which an
    /// operation is admitted after the close observed the count.
    /// </remarks>
    internal sealed class OperationTracker
    {
        /// <summary>
        /// Tries to admit an operation.
        /// </summary>
        /// <returns><see langword="false"/> when the tracker is closed.</returns>
        public bool TryEnter()
        {
            lock (m_lock)
            {
                if (m_closed)
                {
                    return false;
                }
                m_active++;
                return true;
            }
        }

        /// <summary>
        /// Ends an admitted operation.
        /// </summary>
        public void Exit()
        {
            TaskCompletionSource<bool>? drained = null;
            lock (m_lock)
            {
                m_active--;
                if (m_active == 0 && m_closed)
                {
                    drained = m_drained;
                }
            }
            drained?.TrySetResult(true);
        }

        /// <summary>
        /// Rejects further operations and completes when the admitted ones have ended.
        /// </summary>
        public Task CloseAsync()
        {
            lock (m_lock)
            {
                m_closed = true;
                if (m_active == 0)
                {
                    m_drained.TrySetResult(true);
                }
            }
            return m_drained.Task;
        }

        private readonly Lock m_lock = new();

        private readonly TaskCompletionSource<bool> m_drained =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int m_active;
        private bool m_closed;
    }
}
