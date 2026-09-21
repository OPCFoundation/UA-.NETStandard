/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
 *
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

namespace Opc.Ua.Client.Tests.Stack.Client.Fakes
{
    internal sealed class AsyncOperationGate
    {
        public Task Entered => m_entered.Task;

        public Task Cancelled => m_cancelled.Task;

        public Task Exited => m_exited.Task;

        public bool IsReleased => m_release.Task.IsCompleted;

        public bool IgnoreCancellation { get; set; }

        public async ValueTask WaitAsync(CancellationToken ct)
        {
            m_entered.TrySetResult(true);
            using CancellationTokenRegistration registration = ct.Register(() => m_cancelled.TrySetResult(true));
            try
            {
                await m_release.Task.WaitAsync(IgnoreCancellation ? CancellationToken.None : ct).ConfigureAwait(false);
            }
            catch (System.OperationCanceledException)
            {
                m_cancelled.TrySetResult(true);
                throw;
            }
            finally
            {
                m_exited.TrySetResult(true);
            }
        }

        public void Release()
        {
            m_release.TrySetResult(true);
        }

        private readonly TaskCompletionSource<bool> m_entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> m_release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> m_cancelled = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> m_exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
