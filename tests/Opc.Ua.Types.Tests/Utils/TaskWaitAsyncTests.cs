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
using NUnit.Framework;

namespace Opc.Ua.Types.Tests.Utils
{
    /// <summary>
    /// Pins the semantics of <c>Task.WaitAsync(CancellationToken)</c> that
    /// callers rely on. On net8.0+ this is the BCL method; on the older
    /// target frameworks it is the polyfill in Opc.Ua.Types, and the two
    /// have to agree - a caller that awaits an already completed task must
    /// get its result on every target, not a result on one and a
    /// cancellation on another.
    /// </summary>
    [TestFixture]
    [Category("Utils")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class TaskWaitAsyncTests
    {
        [Test]
        public async Task CompletedTaskWinsOverAnAlreadyCancelledTokenAsync()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Task<int> completed = Task.FromResult(42);

            Assert.That(
                await completed.WaitAsync(cts.Token).ConfigureAwait(false),
                Is.EqualTo(42),
                "a completed task must be handed back rather than reported as cancelled");
        }

        [Test]
        public void CompletedNonGenericTaskWinsOverAnAlreadyCancelledToken()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.That(
                async () => await Task.CompletedTask.WaitAsync(cts.Token).ConfigureAwait(false),
                Throws.Nothing);
        }

        [Test]
        public void PendingTaskObservesTheCancellation()
        {
            using var cts = new CancellationTokenSource();
            var pending = new TaskCompletionSource<int>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            Task<int> wait = pending.Task.WaitAsync(cts.Token);
            cts.Cancel();

            Assert.That(
                () => wait,
                Throws.InstanceOf<System.OperationCanceledException>());

            // The abandoned wait must not fault the underlying task.
            pending.TrySetResult(1);
        }
    }
}
