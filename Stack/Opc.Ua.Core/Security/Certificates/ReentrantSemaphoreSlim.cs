/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua
{
    /// <summary>
    /// Defines a reentrant SemaphoreSlim Lock.
    /// </summary>
    internal class ReentrantSemaphoreSlim
    {
        #region Constructors
        public ReentrantSemaphoreSlim()
        {
            m_semaphore = new SemaphoreSlim(1, 1);
            m_reentrantCounter = new ThreadLocal<int>(() => 0);
        }

        public ReentrantSemaphoreSlim(int initialCount, int maxCount)
        {
            m_semaphore = new SemaphoreSlim(initialCount, maxCount);
            m_reentrantCounter = new ThreadLocal<int>(() => 0);
        }
        #endregion

        #region Public Members
        /// <summary>
        /// Wait method for synchronous usage
        /// </summary>
        public void Wait()
        {
            if (m_reentrantCounter.Value == 0)
            {
                m_semaphore.Wait();
            }
            m_reentrantCounter.Value++;
        }

        /// <summary>
        /// Wait method for async usage
        /// </summary>
        public async Task WaitAsync(CancellationToken ct = default)
        {
            if (m_reentrantCounter.Value == 0)
            {
                await m_semaphore.WaitAsync(ct).ConfigureAwait(false);
            }
            m_reentrantCounter.Value++;
        }

        public void Release()
        {
            if (m_reentrantCounter.Value <= 0)
            {
                throw new InvalidOperationException("Release called without a corresponding Wait");
            }
            if (--m_reentrantCounter.Value == 0)
            {
                m_semaphore.Release();
            }
        }

        #endregion

        #region Private members
        private readonly SemaphoreSlim m_semaphore;
        private ThreadLocal<int> m_reentrantCounter;
        #endregion

    }
}
