/* ========================================================================
 * Copyright (c) 2005-2021 The OPC Foundation, Inc. All rights reserved.
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

// Copyright Stephen Cleary Nito.AsyncEx
// Original idea by Stephen Toub:
// http://blogs.msdn.com/b/pfxteam/archive/2012/02/11/10266920.aspx

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client
{
    /// <summary>
    /// An async-compatible manual-reset event.
    /// </summary>
    public sealed class AsyncManualResetEvent
    {
        /// <summary>
        /// Creates an async-compatible manual-reset event.
        /// </summary>
        /// <param name="set">Whether the manual-reset event is
        /// initially set or unset.</param>
        public AsyncManualResetEvent(bool set)
        {
            m_tcs = new TaskCompletionSource<object>(
                 TaskCreationOptions.RunContinuationsAsynchronously);
            if (set)
            {
                m_tcs.TrySetResult(null);
            }
        }

        /// <summary>
        /// Creates an async-compatible manual-reset event
        /// that is initially unset.
        /// </summary>
        public AsyncManualResetEvent()
            : this(false)
        {
        }

        /// <summary>
        /// Whether this event is currently set. This member is seldom
        /// used; code using this member has a high possibility of race
        /// conditions.
        /// </summary>
        public bool IsSet
        {
            get
            {
                lock (m_lock)
                {
                    return m_tcs.Task.IsCompleted;
                }
            }
        }

        /// <summary>
        /// Asynchronously waits for this event to be set.
        /// </summary>
        public Task WaitAsync()
        {
            lock (m_lock)
            {
                return m_tcs.Task;
            }
        }

        /// <summary>
        /// Asynchronously waits for this event to be set or for the wait
        /// to be canceled.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token used
        /// to cancel the wait. If this token is already canceled,
        /// this method will first check whether the event is set.</param>
        public Task WaitAsync(CancellationToken cancellationToken)
        {
            Task waitTask = WaitAsync();
            if (waitTask.IsCompleted)
            {
                return waitTask;
            }
            return waitTask.WaitAsync(cancellationToken);
        }

        /// <summary>
        /// Sets the event, atomically completing every task returned
        /// WaitAsync. If the event is already set, this method does
        /// nothing.
        /// </summary>
        public void Set()
        {
            lock (m_lock)
            {
                m_tcs.TrySetResult(null);
            }
        }

        /// <summary>
        /// Resets the event. If the event is already reset,
        /// this method does nothing.
        /// </summary>
        public void Reset()
        {
            lock (m_lock)
            {
                if (m_tcs.Task.IsCompleted)
                {
                    m_tcs = new TaskCompletionSource<object>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                }
            }
        }

        private readonly Lock m_lock = new();
        private TaskCompletionSource<object> m_tcs;
    }
}
