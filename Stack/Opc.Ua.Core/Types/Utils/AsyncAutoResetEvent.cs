/* ========================================================================
 * Copyright (c) 2005-2023 The OPC Foundation, Inc. All rights reserved.
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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Types.Utils
{
    /// <summary>
    /// An async version of <see cref="AutoResetEvent"/> based on
    /// https://devblogs.microsoft.com/pfxteam/building-async-coordination-primitives-part-2-asyncautoresetevent/.
    /// </summary>
    public class AsyncAutoResetEvent
    {
        private static readonly Task s_completed = Task.FromResult(true);
        private readonly Queue<TaskCompletionSource<bool>> m_waits = new Queue<TaskCompletionSource<bool>>();
        private bool m_signaled;

        /// <summary>
        /// Task can wait for next event.
        /// </summary>
        public Task WaitAsync()
        {
            lock (m_waits)
            {
                if (m_signaled)
                {
                    m_signaled = false;
                    return s_completed;
                }
                else
                {
                    // TaskCreationOptions.RunContinuationsAsynchronously is needed
                    // to decouple the reader thread from the processing in the subscriptions.
                    var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    m_waits.Enqueue(tcs);
                    return tcs.Task;
                }
            }
        }

        /// <summary>
        /// Set next waiting task.
        /// </summary>
        public void Set()
        {
            TaskCompletionSource<bool> toRelease;
            lock (m_waits)
            {
                if (m_waits.Count > 0)
                {
                    toRelease = m_waits.Dequeue();
                }
                else
                {
                    m_signaled = true;
                    return;
                }
            }
            toRelease.SetResult(true);
        }

        /// <summary>
        /// Set all waiting tasks.
        /// </summary>
        public void SetAll()
        {
            lock (m_waits)
            {
                TaskCompletionSource<bool> toRelease;
                while (m_waits.Count > 0)
                {
                    toRelease = m_waits.Dequeue();
                    toRelease.SetResult(true);
                }
                m_signaled = true;
            }
        }
    }
}
