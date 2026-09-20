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
    public sealed partial class SessionContinuationOwnershipTests
    {
        [Test]
        public void SharedDependenciesSurviveRestoreResaveAndIndependentPointRelease()
        {
            var holder = new SessionContinuationPoints(() => new NodeId(1), 2, 1, null);
            var source = new Mock<IAsyncNodeManager>();
            var dependency = new Mock<IAsyncNodeManager>();
            var unrelated = new Mock<IAsyncNodeManager>();
            using ContinuationPoint first = CreatePoint(source.Object);
            using ContinuationPoint second = CreatePoint(unrelated.Object);
            first.SetDependencyOwners([dependency.Object]);
            second.SetDependencyOwners([dependency.Object]);
            int released = 0;
            holder.BrowseContinuationPointsReleased += () => released++;
            holder.SaveBrowse(first);
            holder.SaveBrowse(second);
            ByteString oldToken = first.Id.ToByteArray().ToByteString();
            Assert.That(holder.RestoreBrowse(oldToken), Is.SameAs(first));
            Assert.That(holder.HasBrowseForManager(dependency.Object), Is.True);
            Assert.That(released, Is.Zero);
            first.Id = Guid.NewGuid();
            holder.SaveBrowse(first);
            Assert.That(holder.RestoreBrowse(oldToken), Is.Null);
            Assert.That(holder.HasBrowseForManager(dependency.Object), Is.True);
            first.Dispose();
            first.Dispose();
            Assert.That(holder.HasBrowseForManager(source.Object), Is.False);
            Assert.That(holder.HasBrowseForManager(dependency.Object), Is.True);
            Assert.That(holder.HasBrowseForManager(unrelated.Object), Is.True);
            Assert.That(released, Is.EqualTo(1));
            Assert.That(holder.RestoreBrowse(second.Id.ToByteArray().ToByteString()), Is.SameAs(second));
            second.Dispose();
            Assert.That(holder.HasBrowseForManager(dependency.Object), Is.False);
            Assert.That(holder.HasBrowseForManager(unrelated.Object), Is.False);
            Assert.That(released, Is.EqualTo(2));
        }

        [Test]
        public void DependencyInvalidationDefersCheckedOutDisposalAndRejectsResave()
        {
            var holder = new SessionContinuationPoints(() => new NodeId(1), 3, 1, null);
            var source = new Mock<IAsyncNodeManager>();
            var dependency = new Mock<IAsyncNodeManager>();
            var unrelated = new Mock<IAsyncNodeManager>();
            var checkedOutData = new DisposalCounter();
            var availableData = new DisposalCounter();
            using ContinuationPoint checkedOut = CreatePoint(source.Object, checkedOutData);
            using ContinuationPoint available = CreatePoint(source.Object, availableData);
            using ContinuationPoint independent = CreatePoint(unrelated.Object);
            checkedOut.SetDependencyOwners([dependency.Object]);
            available.SetDependencyOwners([dependency.Object]);
            holder.SaveBrowse(checkedOut);
            holder.SaveBrowse(available);
            holder.SaveBrowse(independent);
            Assert.That(holder.RestoreBrowse(checkedOut.Id.ToByteArray().ToByteString()), Is.SameAs(checkedOut));
            holder.RemoveForManager(dependency.Object);
            Assert.That(availableData.Count, Is.EqualTo(1));
            Assert.That(checkedOutData.Count, Is.Zero);
            Assert.That(holder.HasBrowseForManager(dependency.Object), Is.True);
            Assert.That(holder.HasBrowseForManager(source.Object), Is.True);
            Assert.That(holder.RestoreBrowse(available.Id.ToByteArray().ToByteString()), Is.Null);
            Assert.That(() => holder.SaveBrowse(checkedOut),
                Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadContinuationPointInvalid));
            checkedOut.Dispose();
            Assert.That(checkedOutData.Count, Is.EqualTo(1));
            Assert.That(holder.HasBrowseForManager(source.Object), Is.False);
            Assert.That(holder.HasBrowseForManager(dependency.Object), Is.False);
            Assert.That(holder.RestoreBrowse(independent.Id.ToByteArray().ToByteString()), Is.SameAs(independent));
        }

        [TestCase("Eviction")]
        [TestCase("Clear")]
        [TestCase("SaveFailure")]
        [TestCase("RestoreFailure")]
        [TestCase("DataFailure")]
        public void BrowseDependencyCleanupReleasesEveryOwnerExactlyOnce(string terminal)
        {
            var store = new Mock<IContinuationPointStore>();
            var holder = new SessionContinuationPoints(() => new NodeId(1), 1, 1, store.Object);
            var source = new Mock<IAsyncNodeManager>();
            var dependency = new Mock<IAsyncNodeManager>();
            var unrelated = new Mock<IAsyncNodeManager>();
            var data = new DisposalCounter(terminal == "DataFailure");
            using ContinuationPoint point = CreatePoint(source.Object, data);
            using ContinuationPoint replacement = CreatePoint(unrelated.Object);
            point.SetDependencyOwners([dependency.Object]);
            int released = 0;
            holder.BrowseContinuationPointsReleased += () => released++;
            if (terminal == "SaveFailure")
            {
                store.Setup(value => value.StoreContinuationPoint(It.IsAny<ContinuationPointEnvelope>()))
                    .Throws<IOException>();
                Assert.That(() => holder.SaveBrowse(point), Throws.TypeOf<IOException>());
            }
            else
            {
                holder.SaveBrowse(point);
                Assert.That(holder.HasBrowseForManager(source.Object), Is.True);
                Assert.That(holder.HasBrowseForManager(dependency.Object), Is.True);
                switch (terminal)
                {
                    case "Eviction":
                        holder.SaveBrowse(replacement);
                        Assert.That(holder.HasBrowseForManager(unrelated.Object), Is.True);
                        break;
                    case "Clear":
                        holder.Clear();
                        break;
                    case "RestoreFailure":
                        store.Setup(value => value.RemoveContinuationPoint(
                            It.IsAny<NodeId>(), ContinuationPointKind.Browse, point.Id)).Throws<IOException>();
                        Assert.That(() => holder.RestoreBrowse(point.Id.ToByteArray().ToByteString()),
                            Throws.TypeOf<IOException>());
                        break;
                    case "DataFailure":
                        Assert.That(point.Dispose, Throws.TypeOf<IOException>());
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(terminal));
                }
            }
            point.Dispose();
            Assert.That(data.Count, Is.EqualTo(1));
            Assert.That(holder.HasBrowseForManager(source.Object), Is.False);
            Assert.That(holder.HasBrowseForManager(dependency.Object), Is.False);
            Assert.That(released, Is.EqualTo(1));
            holder.Clear();
        }

        [Test]
        public void BrowseDependencyIdentityRecognizesOnlyExactOwnersAndSynchronousAdapters()
        {
            var source = new Mock<IAsyncNodeManager>();
            var dependency = new Mock<IAsyncNodeManager>();
            var equivalent = new Mock<IAsyncNodeManager>();
            var unrelated = new Mock<IAsyncNodeManager>();
            var sync = new Mock<INodeManager>();
            dependency.SetupGet(value => value.SyncNodeManager).Returns(sync.Object);
            equivalent.SetupGet(value => value.SyncNodeManager).Returns(sync.Object);
            dependency.SetupGet(value => value.NamespaceUris).Returns(["urn:shared"]);
            unrelated.SetupGet(value => value.NamespaceUris).Returns(["urn:shared"]);
            using ContinuationPoint point = CreatePoint(source.Object);
            point.SetDependencyOwners([dependency.Object]);
            Assert.That(point.RequiresManager(source.Object), Is.True);
            Assert.That(point.RequiresManager(dependency.Object), Is.True);
            Assert.That(point.RequiresManager(equivalent.Object), Is.True);
            Assert.That(point.RequiresManager(unrelated.Object), Is.False);
            Assert.That(() => point.RequiresManager(null), Throws.ArgumentNullException);
            Assert.That(() => point.SetDependencyOwners([unrelated.Object]),
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(point.RequiresManager(dependency.Object), Is.True);
            Assert.That(point.RequiresManager(unrelated.Object), Is.False);
        }

        [Test]
        public async Task ConcurrentBrowseDependencySaveAndDisposalCannotRetainOwnersAsync()
        {
            var source = new Mock<IAsyncNodeManager>();
            var dependency = new Mock<IAsyncNodeManager>();
            for (int attempt = 0; attempt < 20000; attempt++)
            {
                var holder = new SessionContinuationPoints(() => new NodeId(1), 1, 1, null);
                var data = new DisposalCounter();
                using ContinuationPoint point = CreatePoint(source.Object, data);
                point.SetDependencyOwners([dependency.Object]);
                var readyToSave = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var readyToDispose = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                int released = 0;
                bool saved = false;
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
                        // Disposal may win before session ownership is acquired.
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
                bool sourceRetained = holder.HasBrowseForManager(source.Object);
                bool dependencyRetained = holder.HasBrowseForManager(dependency.Object);
                holder.Clear();
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(sourceRetained, Is.False, $"Source leaked at attempt {attempt}.");
                    Assert.That(dependencyRetained, Is.False, $"Dependency leaked at attempt {attempt}.");
                    Assert.That(data.Count, Is.EqualTo(1));
                    Assert.That(Volatile.Read(ref released), Is.EqualTo(saved ? 1 : 0));
                }
            }
        }
    }
}
