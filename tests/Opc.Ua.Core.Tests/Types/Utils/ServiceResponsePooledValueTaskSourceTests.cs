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

using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Core.Tests.Types.UtilsTests
{
    /// <summary>
    /// Tests that a pooled service response source accepts exactly one completion per rental.
    /// </summary>
    [TestFixture]
    [Category("Utils")]
    [Parallelizable]
    public sealed class ServiceResponsePooledValueTaskSourceTests
    {
        [Test]
        public async Task SecondCompletionOfSameRentalIsIgnoredAsync()
        {
            ObjectPool<ServiceResponsePooledValueTaskSource> pool =
                ServiceResponsePooledValueTaskSource.CreatePool(1);
            ServiceResponsePooledValueTaskSource source = ServiceResponsePooledValueTaskSource.Create(pool);
            int lease = source.Lease;
            ValueTask<IServiceResponse> task = source.Task;
            var first = new ReadResponse();

            Assert.That(source.TrySetResult(lease, first), Is.True);
            Assert.That(source.TrySetResult(lease, new BrowseResponse()), Is.False);

            Assert.That(await task.ConfigureAwait(false), Is.SameAs(first));
        }

        /// <summary>
        /// A completion that arrives after the source was recycled must not answer the next owner (#4729).
        /// </summary>
        [Test]
        public async Task LateCompletionDoesNotReachNextRentalAsync()
        {
            ObjectPool<ServiceResponsePooledValueTaskSource> pool =
                ServiceResponsePooledValueTaskSource.CreatePool(1);

            ServiceResponsePooledValueTaskSource first = ServiceResponsePooledValueTaskSource.Create(pool);
            int staleLease = first.Lease;
            ValueTask<IServiceResponse> firstTask = first.Task;
            Assert.That(first.TrySetResult(staleLease, new CreateSessionResponse()), Is.True);
            Assert.That(await firstTask.ConfigureAwait(false), Is.InstanceOf<CreateSessionResponse>());

            ServiceResponsePooledValueTaskSource second = ServiceResponsePooledValueTaskSource.Create(pool);
            Assert.That(second, Is.SameAs(first), "The pool should hand out the recycled source.");
            ValueTask<IServiceResponse> secondTask = second.Task;

            Assert.That(first.TrySetResult(staleLease, new CreateSessionResponse()), Is.False);
            Assert.That(secondTask.IsCompleted, Is.False);

            var expected = new ActivateSessionResponse();
            Assert.That(second.TrySetResult(second.Lease, expected), Is.True);
            Assert.That(await secondTask.ConfigureAwait(false), Is.SameAs(expected));
        }

        /// <summary>
        /// A source whose lease reached int.MaxValue is not pooled again, so leases never repeat.
        /// </summary>
        [Test]
        public async Task SourceIsRetiredBeforeLeaseWrapsAsync()
        {
            ObjectPool<ServiceResponsePooledValueTaskSource> pool =
                ServiceResponsePooledValueTaskSource.CreatePool(1);
            ServiceResponsePooledValueTaskSource source = ServiceResponsePooledValueTaskSource.Create(pool);
            ValueTask<IServiceResponse> task = source.Task;
            Assert.That(source.TrySetResult(source.Lease, new ReadResponse()), Is.True);
            await task.ConfigureAwait(false);

            typeof(ServiceResponsePooledValueTaskSource)
                .GetField("m_lease", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(source, int.MaxValue - 1);
            Assert.That(ServiceResponsePooledValueTaskSource.Create(pool), Is.SameAs(source));
            Assert.That(source.Lease, Is.EqualTo(int.MaxValue));
            task = source.Task;
            Assert.That(source.TrySetResult(source.Lease, new ReadResponse()), Is.True);
            await task.ConfigureAwait(false);

            Assert.That(ServiceResponsePooledValueTaskSource.Create(pool), Is.Not.SameAs(source));
        }
    }
}
