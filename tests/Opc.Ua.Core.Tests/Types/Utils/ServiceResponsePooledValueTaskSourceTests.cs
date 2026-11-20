// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
