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
#if mist
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Types.Utils
{
    /// <summary>
    /// An async version of <see cref="ManualResetEvent"/> based on
    /// https://devblogs.microsoft.com/pfxteam/building-async-coordination-primitives-part-1-asyncmanualresetevent/.
    /// </summary>
    public class AsyncManualResetEvent
    {
        private volatile TaskCompletionSource<bool> m_tcs = new TaskCompletionSource<bool>();

        /// <summary>
        /// 
        /// </summary>
        public Task WaitAsync() { return m_tcs.Task; }

        /// <summary>
        /// 
        /// </summary>
        public void Set() { m_tcs.TrySetResult(true); }

        /// <summary>
        /// 
        /// </summary>
        public void Reset()
        {
            while (true)
            {
                var tcs = m_tcs;
                if (!tcs.Task.IsCompleted ||
                    Interlocked.CompareExchange(ref m_tcs, new TaskCompletionSource<bool>(), tcs) == tcs)
                    return;
            }
        }
        /// <summary>
        /// 
        /// </summary>
        public void Set()
        {
            var tcs = m_tcs;
            Task.Factory.StartNew(s => ((TaskCompletionSource<bool>)s).TrySetResult(true),
                tcs, CancellationToken.None, TaskCreationOptions.PreferFairness, TaskScheduler.Default);
            tcs.Task.Wait();
        }
    }
}
#endif
