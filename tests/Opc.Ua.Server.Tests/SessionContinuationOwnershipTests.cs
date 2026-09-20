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

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests
{
    [TestFixture]
    [Category("Session")]
    public sealed class SessionContinuationOwnershipTests
    {
        [Test]
        public async Task ConcurrentBrowseSaveAndDisposalCannotRetainOwnerAsync()
        {
            var manager = new Mock<IAsyncNodeManager>();
            for (int attempt = 0; attempt < 20000; attempt++)
            {
                var holder = new SessionContinuationPoints(() => new NodeId(1), 1, 1, null);
                var data = new DisposalCounter();
                using ContinuationPoint point = CreatePoint(manager.Object, data);
                var readyToSave = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var readyToDispose = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                bool saved = false;
                int released = 0;
                holder.BrowseContinuationPointsReleased += () => Interlocked.Increment(ref released);
                Task saving = Task.Run(async () =>
                {
                    readyToSave.TrySetResult(true);
                    await start.Task.ConfigureAwait(false);
                    try
                    {
                        holder.SaveBrowse(point);
                        saved = true;
                    }
                    catch (ObjectDisposedException)
                    {
                        // Disposal may win before the cache takes ownership.
                    }
                });
                Task disposing = Task.Run(async () =>
                {
                    readyToDispose.TrySetResult(true);
                    await start.Task.ConfigureAwait(false);
                    point.Dispose();
                });
                await Task.WhenAll(readyToSave.Task, readyToDispose.Task).ConfigureAwait(false);
                start.TrySetResult(true);
                await Task.WhenAll(saving, disposing).ConfigureAwait(false);
                bool retained = holder.HasBrowseForManager(manager.Object);
                holder.Clear();
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(retained, Is.False,
                        $"Concurrent SaveBrowse/Dispose retained its source at attempt {attempt}.");
                    Assert.That(data.Count, Is.EqualTo(1));
                    Assert.That(Volatile.Read(ref released), Is.EqualTo(saved ? 1 : 0));
                }
            }
        }

        [Test]
        public void DisposedBrowsePointCannotAcquireSessionOwnership()
        {
            var holder = new SessionContinuationPoints(() => new NodeId(1), 1, 1, null);
            var manager = new Mock<IAsyncNodeManager>();
            var data = new DisposalCounter();
            using ContinuationPoint point = CreatePoint(manager.Object, data);
            point.Dispose();
            Assert.That(() => holder.SaveBrowse(point), Throws.TypeOf<ObjectDisposedException>());
            Assert.That(holder.HasBrowseForManager(manager.Object), Is.False);
            Assert.That(holder.RestoreBrowse(point.Id.ToByteArray().ToByteString()), Is.Null);
            holder.Clear();
            Assert.That(data.Count, Is.EqualTo(1));
        }

        [Test]
        public void AdditionalSessionCannotReplaceTheOriginalContinuationOwner()
        {
            var first = new SessionContinuationPoints(() => new NodeId(1), 1, 1, null);
            var second = new SessionContinuationPoints(() => new NodeId(2), 1, 1, null);
            var manager = new Mock<IAsyncNodeManager>();
            var data = new DisposalCounter();
            using ContinuationPoint point = CreatePoint(manager.Object, data);
            int firstReleased = 0;
            int secondReleased = 0;
            first.BrowseContinuationPointsReleased += () => firstReleased++;
            second.BrowseContinuationPointsReleased += () => secondReleased++;
            first.SaveBrowse(point);
            Assert.That(() => second.SaveBrowse(point), Throws.TypeOf<InvalidOperationException>());
            Assert.That(first.HasBrowseForManager(manager.Object), Is.True);
            Assert.That(second.HasBrowseForManager(manager.Object), Is.False);
            point.Dispose();
            using (Assert.EnterMultipleScope())
            {
                Assert.That(first.HasBrowseForManager(manager.Object), Is.False);
                Assert.That(second.HasBrowseForManager(manager.Object), Is.False);
                Assert.That(firstReleased, Is.EqualTo(1));
                Assert.That(secondReleased, Is.Zero);
                Assert.That(data.Count, Is.EqualTo(1));
            }
        }

        [Test]
        public void InvalidReleaseCallbackDoesNotConsumeContinuationOwnership()
        {
            var manager = new Mock<IAsyncNodeManager>();
            using ContinuationPoint point = CreatePoint(manager.Object);
            Assert.That(() => point.SetOwnerRelease(null), Throws.ArgumentNullException);
            var holder = new SessionContinuationPoints(() => new NodeId(1), 1, 1, null);
            holder.SaveBrowse(point);
            Assert.That(holder.HasBrowseForManager(manager.Object), Is.True);
            point.Dispose();
            Assert.That(holder.HasBrowseForManager(manager.Object), Is.False);
        }

        [Test]
        public void RestoredBrowseRetainsExactOwnerUntilDisposal()
        {
            var holder = new SessionContinuationPoints(() => new NodeId(1), 2, 2, null);
            var manager = new Mock<IAsyncNodeManager>();
            var unrelated = new Mock<IAsyncNodeManager>();
            var data = new DisposalCounter();
            using ContinuationPoint point = CreatePoint(manager.Object, data);
            int released = 0;
            holder.BrowseContinuationPointsReleased += () => released++;
            holder.SaveBrowse(point);
            ByteString token = point.Id.ToByteArray().ToByteString();
            Assert.That(holder.RestoreBrowse(token), Is.SameAs(point));
            Assert.That(holder.RestoreBrowse(token), Is.Null);
            Assert.That(holder.HasBrowseForManager(manager.Object), Is.True);
            Assert.That(holder.HasBrowseForManager(unrelated.Object), Is.False,
                "Two pure async managers with no synchronous wrapper are still distinct owners.");
            Assert.That(released, Is.Zero);
            point.Dispose();
            point.Dispose();
            Assert.That(holder.HasBrowseForManager(manager.Object), Is.False);
            Assert.That(data.Count, Is.EqualTo(1));
            Assert.That(released, Is.EqualTo(1));
        }

        [Test]
        public void BrowseResaveKeepsOwnerAcrossTokenReplacement()
        {
            var holder = new SessionContinuationPoints(() => new NodeId(1), 1, 2, null);
            var manager = new Mock<IAsyncNodeManager>();
            using ContinuationPoint point = CreatePoint(manager.Object);
            int released = 0;
            holder.BrowseContinuationPointsReleased += () => released++;
            holder.SaveBrowse(point);
            ByteString original = point.Id.ToByteArray().ToByteString();
            Assert.That(holder.RestoreBrowse(original), Is.SameAs(point));
            point.Id = Guid.NewGuid();
            holder.SaveBrowse(point);
            Assert.That(holder.RestoreBrowse(original), Is.Null);
            Assert.That(holder.RestoreBrowse(point.Id.ToByteArray().ToByteString()), Is.SameAs(point));
            Assert.That(holder.HasBrowseForManager(manager.Object), Is.True);
            Assert.That(released, Is.Zero);
            point.Dispose();
            Assert.That(holder.HasBrowseForManager(manager.Object), Is.False);
            Assert.That(released, Is.EqualTo(1));
        }

        [Test]
        public void EvictionReleasesOnlyAvailableOwners()
        {
            var holder = new SessionContinuationPoints(() => new NodeId(1), 1, 2, null);
            var firstManager = new Mock<IAsyncNodeManager>();
            var secondManager = new Mock<IAsyncNodeManager>();
            var firstData = new DisposalCounter();
            var secondData = new DisposalCounter();
            using ContinuationPoint first = CreatePoint(firstManager.Object, firstData);
            using ContinuationPoint second = CreatePoint(secondManager.Object, secondData);
            holder.SaveBrowse(first);
            Assert.That(holder.RestoreBrowse(first.Id.ToByteArray().ToByteString()), Is.SameAs(first));
            holder.SaveBrowse(second);
            Assert.That(firstData.Count, Is.Zero, "A checked-out point is not an available cache eviction victim.");
            Assert.That(holder.HasBrowseForManager(firstManager.Object), Is.True);
            Assert.That(holder.HasBrowseForManager(secondManager.Object), Is.True);
            holder.SaveBrowse(first);
            Assert.That(secondData.Count, Is.EqualTo(1));
            Assert.That(holder.RestoreBrowse(second.Id.ToByteArray().ToByteString()), Is.Null);
            Assert.That(holder.HasBrowseForManager(secondManager.Object), Is.False);
            Assert.That(holder.HasBrowseForManager(firstManager.Object), Is.True);
            holder.Clear();
            Assert.That(firstData.Count, Is.EqualTo(1));
            Assert.That(holder.HasBrowseForManager(firstManager.Object), Is.False);
        }

        [Test]
        public void ClearLeavesCheckedOutDisposalWithTheRequestAndRejectsResave()
        {
            var holder = new SessionContinuationPoints(() => new NodeId(1), 2, 2, null);
            var manager = new Mock<IAsyncNodeManager>();
            var activeData = new DisposalCounter();
            var savedData = new DisposalCounter();
            using ContinuationPoint active = CreatePoint(manager.Object, activeData);
            using ContinuationPoint saved = CreatePoint(manager.Object, savedData);
            holder.SaveBrowse(active);
            holder.SaveBrowse(saved);
            Assert.That(holder.RestoreBrowse(active.Id.ToByteArray().ToByteString()), Is.SameAs(active));
            holder.Clear();
            Assert.That(activeData.Count, Is.Zero);
            Assert.That(savedData.Count, Is.EqualTo(1));
            Assert.That(holder.HasBrowseForManager(manager.Object), Is.True);
            Assert.That(() => holder.SaveBrowse(active), Throws.TypeOf<ObjectDisposedException>());
            active.Dispose();
            holder.Clear();
            Assert.That(activeData.Count, Is.EqualTo(1));
            Assert.That(savedData.Count, Is.EqualTo(1));
            Assert.That(holder.HasBrowseForManager(manager.Object), Is.False);
        }

        [Test]
        public void InvalidationRejectsResavingCheckedOutContinuation()
        {
            var holder = new SessionContinuationPoints(() => new NodeId(1), 1, 2, null);
            var manager = new Mock<IAsyncNodeManager>();
            var data = new DisposalCounter();
            using ContinuationPoint point = CreatePoint(manager.Object, data);
            holder.SaveBrowse(point);
            Assert.That(holder.RestoreBrowse(point.Id.ToByteArray().ToByteString()), Is.SameAs(point));
            holder.RemoveForManager(manager.Object);
            Assert.That(data.Count, Is.Zero);
            Assert.That(holder.HasBrowseForManager(manager.Object), Is.True);
            Assert.That(() => holder.SaveBrowse(point),
                Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadContinuationPointInvalid));
            point.Dispose();
            Assert.That(data.Count, Is.EqualTo(1));
            Assert.That(holder.HasBrowseForManager(manager.Object), Is.False);
        }

        [Test]
        public void OwnershipRecognizesEquivalentSynchronousAdapters()
        {
            var holder = new SessionContinuationPoints(() => new NodeId(1), 1, 2, null);
            var sync = new Mock<INodeManager>();
            var creating = new Mock<IAsyncNodeManager>();
            var lookup = new Mock<IAsyncNodeManager>();
            creating.SetupGet(manager => manager.SyncNodeManager).Returns(sync.Object);
            lookup.SetupGet(manager => manager.SyncNodeManager).Returns(sync.Object);
            using ContinuationPoint point = CreatePoint(creating.Object);
            holder.SaveBrowse(point);
            Assert.That(holder.HasBrowseForManager(lookup.Object), Is.True);
            holder.RemoveForManager(lookup.Object);
            Assert.That(holder.HasBrowseForManager(creating.Object), Is.False);
            Assert.That(holder.RestoreBrowse(point.Id.ToByteArray().ToByteString()), Is.Null);
        }

        [Test]
        public void FailedRestoreReleasesOwnerAndDisposesPoint()
        {
            var store = new Mock<IContinuationPointStore>();
            var holder = new SessionContinuationPoints(() => new NodeId(1), 1, 2, store.Object);
            var manager = new Mock<IAsyncNodeManager>();
            var data = new DisposalCounter();
            using ContinuationPoint point = CreatePoint(manager.Object, data);
            store.Setup(value => value.RemoveContinuationPoint(
                It.IsAny<NodeId>(), ContinuationPointKind.Browse, point.Id)).Throws<IOException>();
            int released = 0;
            holder.BrowseContinuationPointsReleased += () => released++;
            holder.SaveBrowse(point);
            Assert.That(() => holder.RestoreBrowse(point.Id.ToByteArray().ToByteString()),
                Throws.TypeOf<IOException>());
            Assert.That(holder.HasBrowseForManager(manager.Object), Is.False);
            Assert.That(data.Count, Is.EqualTo(1));
            Assert.That(released, Is.EqualTo(1));
        }

        [Test]
        public void FailedSaveReleasesOwnerAndDisposesPoint()
        {
            var store = new Mock<IContinuationPointStore>();
            store.Setup(value => value.StoreContinuationPoint(It.IsAny<ContinuationPointEnvelope>()))
                .Throws<IOException>();
            var holder = new SessionContinuationPoints(() => new NodeId(1), 1, 2, store.Object);
            var manager = new Mock<IAsyncNodeManager>();
            var data = new DisposalCounter();
            using ContinuationPoint point = CreatePoint(manager.Object, data);
            int released = 0;
            holder.BrowseContinuationPointsReleased += () => released++;
            Assert.That(() => holder.SaveBrowse(point), Throws.TypeOf<IOException>());
            Assert.That(holder.HasBrowseForManager(manager.Object), Is.False);
            Assert.That(data.Count, Is.EqualTo(1));
            Assert.That(released, Is.EqualTo(1));
        }

        [Test]
        public void ClearReleasesAllBrowseOwnersEvenWhenPersistenceFails()
        {
            var store = new Mock<IContinuationPointStore>();
            var holder = new SessionContinuationPoints(() => new NodeId(1), 2, 2, store.Object);
            var manager = new Mock<IAsyncNodeManager>();
            var firstData = new DisposalCounter();
            var secondData = new DisposalCounter();
            using ContinuationPoint first = CreatePoint(manager.Object, firstData);
            using ContinuationPoint second = CreatePoint(manager.Object, secondData);
            store.Setup(value => value.RemoveContinuationPoint(
                It.IsAny<NodeId>(), ContinuationPointKind.Browse, first.Id)).Throws<IOException>();
            holder.SaveBrowse(first);
            holder.SaveBrowse(second);
            int released = 0;
            holder.BrowseContinuationPointsReleased += () => released++;
            Assert.That(holder.Clear, Throws.TypeOf<AggregateException>());
            Assert.That(holder.HasBrowseForManager(manager.Object), Is.False);
            Assert.That(firstData.Count, Is.EqualTo(1));
            Assert.That(secondData.Count, Is.EqualTo(1));
            Assert.That(released, Is.EqualTo(2));
        }

        [Test]
        public void DataDisposalFailureStillReleasesOwnershipExactlyOnce()
        {
            var holder = new SessionContinuationPoints(() => new NodeId(1), 1, 2, null);
            var manager = new Mock<IAsyncNodeManager>();
            var data = new DisposalCounter(throws: true);
            using ContinuationPoint point = CreatePoint(manager.Object, data);
            int released = 0;
            holder.BrowseContinuationPointsReleased += () => released++;
            holder.SaveBrowse(point);
            Assert.That(point.Dispose, Throws.TypeOf<IOException>());
            Assert.That(holder.HasBrowseForManager(manager.Object), Is.False);
            Assert.That(point.Dispose, Throws.Nothing);
            Assert.That(data.Count, Is.EqualTo(1));
            Assert.That(released, Is.EqualTo(1));
        }

        private static ContinuationPoint CreatePoint(IAsyncNodeManager manager, IDisposable data = null)
        {
            return new ContinuationPoint { Id = Guid.NewGuid(), Manager = manager, Data = data };
        }

        private sealed class DisposalCounter(bool throws = false) : IDisposable
        {
            public int Count { get; private set; }

            public void Dispose()
            {
                Count++;
                if (throws)
                {
                    throw new IOException("The continuation data could not be disposed.");
                }
            }
        }
    }
}
