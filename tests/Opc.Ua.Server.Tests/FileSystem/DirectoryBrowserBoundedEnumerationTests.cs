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

#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.FileSystem;
using Opc.Ua.Server.Tests.NodeManager;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.FileSystem
{
    [TestFixture]
    [Category("FileSystem")]
    public sealed class DirectoryBrowserBoundedEnumerationTests
    {
        [SetUp]
        public void SetUp()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out m_queueFactory);
            server.SetupGet(value => value.Telemetry).Returns(NUnitTelemetryContext.Create());
            m_provider = new Mock<IFileSystemProvider>(MockBehavior.Strict);
            m_provider.SetupGet(value => value.MountName).Returns("BoundedDirectory");
            m_manager = new FileSystemNodeManager(
                server.Object, new ApplicationConfiguration(), m_provider.Object);
            m_directory = new DirectoryObjectState(
                m_manager.SystemContext,
                FileSystemNodeId.BuildRoot(m_manager.NamespaceIndex),
                string.Empty,
                "BoundedDirectory",
                isRoot: true);
        }

        [TearDown]
        public void TearDown()
        {
            m_manager?.Dispose();
            m_queueFactory?.Dispose();
        }

        [TestCase(1025)]
        [TestCase(100000)]
        public async Task DirectoryBrowsePagesBeyondFormerLimitWithoutEagerEnumerationAsync(int entryCount)
        {
            var entries = new CountingEntries(entryCount);
            SetEntries(entries);
            m_provider.Setup(value => value.GetEntryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<FileSystemEntry?>((FileSystemEntry?)null));
            using var context = new OperationContext(
                new RequestHeader(), null, RequestType.Browse, RequestLifetime.None);
            using var point = new ContinuationPoint
            {
                Manager = m_manager,
                NodeToBrowse = new NodeHandle { NodeId = m_directory.NodeId, Node = m_directory, Validated = true },
                MaxResultsToReturn = 17,
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = ReferenceTypeIds.HasComponent
            };
            ContinuationPoint? continuation = point;
            int returned = 0;
            while (continuation != null)
            {
                var page = new List<ReferenceDescription>();
                continuation = await m_manager.BrowseAsync(context, continuation, page).ConfigureAwait(false);
                Assert.That(page, Has.Count.LessThanOrEqualTo(17));
                foreach (ReferenceDescription reference in page)
                {
                    string name = "entry-" + returned.ToString("D5", CultureInfo.InvariantCulture);
                    NodeId expected = returned % 2 == 0
                        ? FileSystemNodeId.BuildDirectory(name, m_manager.NamespaceIndex)
                        : FileSystemNodeId.BuildFile(name, m_manager.NamespaceIndex);
                    Assert.That(reference.NodeId, Is.EqualTo(new ExpandedNodeId(expected)));
                    returned++;
                }
                Assert.That(entries.MoveNextCount, Is.LessThanOrEqualTo(returned + 1),
                    "A continuation may retain one lookahead reference, not a directory snapshot.");
                Assert.That(entries.EnumerationCount, Is.EqualTo(1));
                Assert.That(entries.DisposeCount, Is.EqualTo(continuation == null ? 1 : 0));
            }
            Assert.That(returned, Is.EqualTo(entryCount));
            Assert.That(entries.MoveNextCount, Is.EqualTo(entryCount + 1));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(1023)]
        [TestCase(1024)]
        public async Task DirectoryBrowseReturnsEveryInLimitEntryInOrderWithoutReenumeration(int entryCount)
        {
            var entries = new CountingEntries(entryCount);
            SetEntries(entries);
            using INodeBrowser browser = CreateBrowser();

            List<ExpandedNodeId> targets = await ReadProviderTargetsAsync(browser).ConfigureAwait(false);

            Assert.That(targets, Has.Count.EqualTo(entryCount));
            for (int index = 0; index < entryCount; index++)
            {
                string name = "entry-" + index.ToString("D5", CultureInfo.InvariantCulture);
                NodeId expected = index % 2 == 0
                    ? FileSystemNodeId.BuildDirectory(name, m_manager.NamespaceIndex)
                    : FileSystemNodeId.BuildFile(name, m_manager.NamespaceIndex);
                Assert.That(targets[index], Is.EqualTo(new ExpandedNodeId(expected)));
            }
            Assert.That(await browser.NextAsync().ConfigureAwait(false), Is.Null);
            Assert.Multiple(() =>
            {
                Assert.That(entries.MoveNextCount, Is.EqualTo(entryCount + 1));
                Assert.That(entries.EnumerationCount, Is.EqualTo(1));
                Assert.That(entries.DisposeCount, Is.EqualTo(1));
            });
        }

        [TestCase(3)]
        [TestCase(5000)]
        public async Task NamedChildBrowseStopsAtMatchWithoutBufferingDirectory(int matchingIndex)
        {
            var entries = new CountingEntries(100000);
            SetEntries(entries);
            string name = "entry-" + matchingIndex.ToString("D5", CultureInfo.InvariantCulture);
            using INodeBrowser browser = CreateBrowser(new QualifiedName(name, m_manager.NamespaceIndex));

            List<ExpandedNodeId> targets = await ReadProviderTargetsAsync(browser).ConfigureAwait(false);

            NodeId expected = matchingIndex % 2 == 0
                ? FileSystemNodeId.BuildDirectory(name, m_manager.NamespaceIndex)
                : FileSystemNodeId.BuildFile(name, m_manager.NamespaceIndex);
            Assert.That(targets, Is.EqualTo([new ExpandedNodeId(expected)]));
            Assert.Multiple(() =>
            {
                Assert.That(entries.MoveNextCount, Is.EqualTo(matchingIndex + 1));
                Assert.That(entries.EnumerationCount, Is.EqualTo(1));
                Assert.That(entries.DisposeCount, Is.EqualTo(1));
            });
        }

        [TestCase(0)]
        [TestCase(2)]
        public async Task NamedDirectoryPushPreservesFinalMatchAfterProviderCompletionAsync(int matchingIndex)
        {
            var entries = new CountingEntries(3);
            SetEntries(entries);
            string name = "entry-" + matchingIndex.ToString("D5", CultureInfo.InvariantCulture);
            using INodeBrowser browser = CreateBrowser(new QualifiedName(name, m_manager.NamespaceIndex));

            IReference? match = await ReadNextProviderTargetAsync(browser).ConfigureAwait(false);

            Assert.That(match, Is.Not.Null);
            Assert.That(match!.TargetId, Is.EqualTo(new ExpandedNodeId(
                FileSystemNodeId.BuildDirectory(name, m_manager.NamespaceIndex))));
            Assert.That(entries.Disposed.Task.IsCompleted, Is.True);
            Assert.That(entries.DisposeCount, Is.EqualTo(1));

            browser.Push(match);
            Assert.That(await browser.NextAsync().ConfigureAwait(false), Is.SameAs(match));
            Assert.That(await browser.NextAsync().ConfigureAwait(false), Is.Null);
            browser.Push(match);
            Assert.That(await browser.NextAsync().ConfigureAwait(false), Is.SameAs(match));
            Assert.That(await browser.NextAsync().ConfigureAwait(false), Is.Null);

            Assert.That(entries.EnumerationCount, Is.EqualTo(1));
            Assert.That(entries.MoveNextCount, Is.EqualTo(matchingIndex + 1));
            Assert.That(entries.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public async Task NamedChildNamespaceMismatchDoesNotEnumerateProvider()
        {
            var entries = new CountingEntries(100000);
            SetEntries(entries);
            using INodeBrowser browser = CreateBrowser(
                new QualifiedName("entry-00003", (ushort)(m_manager.NamespaceIndex + 1)));

            Assert.That(await ReadProviderTargetsAsync(browser).ConfigureAwait(false), Is.Empty);
            Assert.Multiple(() =>
            {
                Assert.That(entries.EnumerationCount, Is.Zero);
                Assert.That(entries.MoveNextCount, Is.Zero);
                Assert.That(entries.DisposeCount, Is.Zero);
            });
        }

        [Test]
        public async Task MissingNamedChildUsesOneEnumerationWithoutApplyingSnapshotLimit()
        {
            var entries = new CountingEntries(2000);
            SetEntries(entries);
            using INodeBrowser browser = CreateBrowser(new QualifiedName("missing", m_manager.NamespaceIndex));

            Assert.That(await ReadProviderTargetsAsync(browser).ConfigureAwait(false), Is.Empty);
            Assert.That(await browser.NextAsync().ConfigureAwait(false), Is.Null);
            Assert.Multiple(() =>
            {
                Assert.That(entries.MoveNextCount, Is.EqualTo(2001));
                Assert.That(entries.EnumerationCount, Is.EqualTo(1));
                Assert.That(entries.DisposeCount, Is.EqualTo(1));
            });
        }

        [TestCase(true)]
        [TestCase(false)]
        public void CancelledEnumerationIsDisposedAndDoesNotReturnPartialSuccess(bool providerObservesCancellation)
        {
            using var cancellation = new CancellationTokenSource();
            var entries = new CountingEntries(100000)
            {
                ObserveCancellation = providerObservesCancellation,
                BeforeMoveNext = count =>
                {
                    if (count == 5)
                    {
                        cancellation.Cancel();
                    }
                }
            };
            SetEntries(entries);
            using INodeBrowser browser = CreateBrowser();

            Assert.ThrowsAsync<OperationCanceledException>(
                async () => await ReadProviderTargetsAsync(browser, cancellation.Token).ConfigureAwait(false));
            Assert.Multiple(() =>
            {
                Assert.That(entries.MoveNextCount, Is.EqualTo(5));
                Assert.That(entries.DisposeCount, Is.EqualTo(1));
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ReleasingDirectoryContinuationDisposesOnlyItsLiveCursorAsync(bool cancelNextPage)
        {
            var entries = new CountingEntries(100000);
            SetEntries(entries);
            m_provider.Setup(value => value.GetEntryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<FileSystemEntry?>((FileSystemEntry?)null));
            using var context = new OperationContext(
                new RequestHeader(), null, RequestType.Browse, RequestLifetime.None);
            using var cancellation = new CancellationTokenSource();
            using var point = new ContinuationPoint
            {
                Manager = m_manager,
                NodeToBrowse = new NodeHandle { NodeId = m_directory.NodeId, Node = m_directory, Validated = true },
                MaxResultsToReturn = 1,
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = ReferenceTypeIds.HasComponent
            };
            var page = new List<ReferenceDescription>();
            ContinuationPoint? continuation = await m_manager.BrowseAsync(context, point, page).ConfigureAwait(false);
            Assert.That(continuation, Is.SameAs(point));
            Assert.That(page, Has.Count.EqualTo(1));
            NodeId expected = FileSystemNodeId.BuildDirectory("entry-00000", m_manager.NamespaceIndex);
            Assert.That(page[0].NodeId, Is.EqualTo(new ExpandedNodeId(expected)));
            Assert.That(entries.MoveNextCount, Is.EqualTo(2));
            Assert.That(entries.DisposeCount, Is.Zero);
            if (cancelNextPage)
            {
                cancellation.Cancel();
                var cancelledPage = new List<ReferenceDescription>();
                OperationCanceledException exception = Assert.CatchAsync<OperationCanceledException>(async () =>
                    await m_manager.BrowseAsync(context, point, cancelledPage, cancellation.Token)
                        .ConfigureAwait(false))!;
                Assert.That(exception.CancellationToken, Is.EqualTo(cancellation.Token));
                Assert.That(cancelledPage, Is.Empty);
            }
            point.Dispose();
            point.Dispose();
            await entries.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.That(entries.EnumerationCount, Is.EqualTo(1));
            Assert.That(entries.MoveNextCount, Is.EqualTo(2));
            Assert.That(entries.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public async Task DirectoryContinuationDoesNotRetainPreviouslyReturnedProviderMetadataAsync()
        {
            var entries = new CountingEntries(1024) { TrackMetadataLifetime = true };
            SetEntries(entries);
            m_provider.Setup(value => value.GetEntryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<FileSystemEntry?>((FileSystemEntry?)null));
            using var context = new OperationContext(
                new RequestHeader(), null, RequestType.Browse, RequestLifetime.None);
            using var point = new ContinuationPoint
            {
                Manager = m_manager,
                NodeToBrowse = new NodeHandle { NodeId = m_directory.NodeId, Node = m_directory, Validated = true },
                MaxResultsToReturn = 8,
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = ReferenceTypeIds.HasComponent
            };
            for (int pageIndex = 0; pageIndex < 64; pageIndex++)
            {
                var page = new List<ReferenceDescription>();
                ContinuationPoint? continuation = await m_manager.BrowseAsync(context, point, page)
                    .ConfigureAwait(false);
                Assert.That(continuation, Is.SameAs(point));
                Assert.That(page, Has.Count.EqualTo(8));
            }

            Assert.That(entries.CountRetainedMetadata(), Is.LessThanOrEqualTo(4),
                "The live cursor may retain its current entry, not all 512 previously returned entries.");
            GC.KeepAlive(point);
            Assert.That(entries.MoveNextCount, Is.EqualTo(513));
            Assert.That(entries.EnumerationCount, Is.EqualTo(1));
            Assert.That(entries.DisposeCount, Is.Zero);
            point.Dispose();
            await entries.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.That(entries.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public async Task CancellationAfterFirstResultStopsCursorTraversal()
        {
            var entries = new CountingEntries(16);
            SetEntries(entries);
            using INodeBrowser browser = CreateBrowser();
            IReference? reference;
            do
            {
                reference = await browser.NextAsync().ConfigureAwait(false);
            }
            while (reference != null && reference.TargetId.NamespaceIndex != m_manager.NamespaceIndex);
            Assert.That(reference, Is.Not.Null);
            Assert.That(reference!.TargetId, Is.EqualTo(new ExpandedNodeId(
                FileSystemNodeId.BuildDirectory("entry-00000", m_manager.NamespaceIndex))));
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            Assert.ThrowsAsync<OperationCanceledException>(
                async () => await browser.NextAsync(cancellation.Token).ConfigureAwait(false));
            Assert.Multiple(() =>
            {
                Assert.That(entries.MoveNextCount, Is.EqualTo(1));
                Assert.That(entries.DisposeCount, Is.EqualTo(1));
            });
        }

        [Test]
        public async Task DisposedBrowserDropsPendingChildrenWithoutReenumeratingProvider()
        {
            var entries = new CountingEntries(16);
            SetEntries(entries);
            using INodeBrowser browser = CreateBrowser();
            IReference? reference;
            do
            {
                reference = await browser.NextAsync().ConfigureAwait(false);
            }
            while (reference != null && reference.TargetId.NamespaceIndex != m_manager.NamespaceIndex);
            Assert.That(reference, Is.Not.Null);

            browser.Dispose();

            Assert.That(await browser.NextAsync().ConfigureAwait(false), Is.Null);
            await entries.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(entries.EnumerationCount, Is.EqualTo(1));
                Assert.That(entries.MoveNextCount, Is.EqualTo(1));
                Assert.That(entries.DisposeCount, Is.EqualTo(1));
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task DirectoryContinuationScopesCancellationToItsActivePageAsync(bool cancelNextPage)
        {
            var entries = new CountingEntries(4);
            SetEntries(entries);
            m_provider.Setup(value => value.GetEntryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<FileSystemEntry?>((FileSystemEntry?)null));
            using var context = new OperationContext(
                new RequestHeader(), null, RequestType.Browse, RequestLifetime.None);
            using var firstPageCancellation = new CancellationTokenSource();
            using var nextPageCancellation = new CancellationTokenSource();
            using var point = new ContinuationPoint
            {
                Manager = m_manager,
                NodeToBrowse = new NodeHandle { NodeId = m_directory.NodeId, Node = m_directory, Validated = true },
                MaxResultsToReturn = 1,
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = ReferenceTypeIds.HasComponent
            };
            var firstPage = new List<ReferenceDescription>();
            ContinuationPoint? continuation = await m_manager.BrowseAsync(
                context, point, firstPage, firstPageCancellation.Token).ConfigureAwait(false);
            Assert.That(continuation, Is.SameAs(point));
            Assert.That(firstPage, Has.Count.EqualTo(1));
            Assert.That(firstPage[0].NodeId, Is.EqualTo(new ExpandedNodeId(
                FileSystemNodeId.BuildDirectory("entry-00000", m_manager.NamespaceIndex))));
            firstPageCancellation.Cancel();
            Assert.That(entries.EnumerationToken.CanBeCanceled, Is.True);
            Assert.That(entries.EnumerationToken.IsCancellationRequested, Is.False,
                "A completed Browse call must not leave its caller token linked to the retained cursor.");
            Assert.That(entries.DisposeCount, Is.Zero);

            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            entries.BeforeMoveNextAsync = async ct =>
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(ct).ConfigureAwait(false);
            };
            var nextPage = new List<ReferenceDescription>();
            Task<ContinuationPoint?> pending = m_manager.BrowseAsync(
                context, point, nextPage, nextPageCancellation.Token).AsTask();
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                Assert.That(pending.IsCompleted, Is.False);
                if (cancelNextPage)
                {
                    nextPageCancellation.Cancel();
                    await Assert.ThatAsync(
                        () => pending.WaitAsync(TimeSpan.FromSeconds(5)),
                        Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
                    Assert.That(entries.EnumerationToken.IsCancellationRequested, Is.True);
                    Assert.That(entries.Disposed.Task.IsCompleted, Is.True);
                }
                else
                {
                    release.TrySetResult(true);
                    Assert.That(await pending.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false),
                        Is.SameAs(point));
                    Assert.That(nextPage, Has.Count.EqualTo(1));
                    Assert.That(nextPage[0].NodeId, Is.EqualTo(new ExpandedNodeId(
                        FileSystemNodeId.BuildFile("entry-00001", m_manager.NamespaceIndex))));
                    Assert.That(entries.EnumerationToken.IsCancellationRequested, Is.False);
                }
                Assert.That(entries.EnumerationCount, Is.EqualTo(1));
                Assert.That(entries.MoveNextCount, Is.EqualTo(3));
            }
            finally
            {
                release.TrySetResult(true);
                try
                {
                    await pending.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (nextPageCancellation.IsCancellationRequested)
                {
                }
                point.Dispose();
                await entries.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            Assert.That(entries.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public async Task DirectoryBrowserDisposeDoesNotBlockOnAsynchronousCursorCleanupAsync()
        {
            var entries = new CountingEntries(4);
            SetEntries(entries);
            using INodeBrowser browser = CreateBrowser();
            IReference? first = await ReadNextProviderTargetAsync(browser).ConfigureAwait(false);
            Assert.That(first, Is.Not.Null);
            Assert.That(first!.TargetId, Is.EqualTo(new ExpandedNodeId(
                FileSystemNodeId.BuildDirectory("entry-00000", m_manager.NamespaceIndex))));
            Assert.That(entries.DisposeCount, Is.Zero);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            entries.OnDisposeAsync = async () =>
            {
                entered.TrySetResult(entries.EnumerationToken.IsCancellationRequested);
                await release.Task.ConfigureAwait(false);
            };
            Task disposal = Task.CompletedTask;
            try
            {
                disposal = Task.Run(browser.Dispose);
                await disposal.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                bool cancellationDeliveredAtCleanupEntry = await entered.Task.WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false);

                Assert.That(entries.Disposed.Task.IsCompleted, Is.False);
                Assert.That(cancellationDeliveredAtCleanupEntry, Is.True);
                Assert.That(await browser.NextAsync().ConfigureAwait(false), Is.Null);
                Assert.That(entries.MoveNextCount, Is.EqualTo(1));
                browser.Dispose();
                Assert.That(entries.DisposeCount, Is.EqualTo(1));
            }
            finally
            {
                release.TrySetResult(true);
                await disposal.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                await entries.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            Assert.That(entries.EnumerationCount, Is.EqualTo(1));
            Assert.That(entries.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public async Task LegacyNextContinuesAnAsyncDirectoryCursorWithoutReenumerationAsync()
        {
            var entries = new CountingEntries(4);
            SetEntries(entries);
            using INodeBrowser browser = CreateBrowser();
            IReference? first = await ReadNextProviderTargetAsync(browser).ConfigureAwait(false);
            Assert.That(first, Is.Not.Null);
            var targets = new List<ExpandedNodeId> { first!.TargetId };
            while (browser.Next() is { } reference)
            {
                if (reference.TargetId.NamespaceIndex == m_manager.NamespaceIndex)
                {
                    targets.Add(reference.TargetId);
                }
            }

            Assert.That(targets, Is.EqualTo(new ExpandedNodeId[]
            {
                FileSystemNodeId.BuildDirectory("entry-00000", m_manager.NamespaceIndex),
                FileSystemNodeId.BuildFile("entry-00001", m_manager.NamespaceIndex),
                FileSystemNodeId.BuildDirectory("entry-00002", m_manager.NamespaceIndex),
                FileSystemNodeId.BuildFile("entry-00003", m_manager.NamespaceIndex)
            }));
            Assert.That(entries.MoveNextCount, Is.EqualTo(5));
            Assert.That(entries.EnumerationCount, Is.EqualTo(1));
            await entries.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.That(entries.DisposeCount, Is.EqualTo(1));
        }

        private void SetEntries(CountingEntries entries)
        {
            m_provider.Setup(value => value.EnumerateAsync(string.Empty, It.IsAny<CancellationToken>()))
                .Returns(entries);
        }

        private INodeBrowser CreateBrowser(QualifiedName browseName = default)
        {
            return m_directory.CreateBrowser(
                m_manager.SystemContext,
                null,
                ReferenceTypeIds.HasComponent,
                true,
                BrowseDirection.Forward,
                browseName,
                null,
                false);
        }

        private async Task<List<ExpandedNodeId>> ReadProviderTargetsAsync(
            INodeBrowser browser,
            CancellationToken cancellationToken = default)
        {
            var targets = new List<ExpandedNodeId>();
            while (await browser.NextAsync(cancellationToken).ConfigureAwait(false) is { } reference)
            {
                if (reference.TargetId.NamespaceIndex == m_manager.NamespaceIndex)
                {
                    targets.Add(reference.TargetId);
                }
            }
            return targets;
        }

        private async Task<IReference?> ReadNextProviderTargetAsync(INodeBrowser browser)
        {
            while (await browser.NextAsync().ConfigureAwait(false) is { } reference)
            {
                if (reference.TargetId.NamespaceIndex == m_manager.NamespaceIndex)
                {
                    return reference;
                }
            }
            return null;
        }

        private sealed class CountingEntries : IAsyncEnumerable<FileSystemEntry>
        {
            public CountingEntries(int entryCount)
            {
                m_entryCount = entryCount;
            }

            public int EnumerationCount { get; private set; }

            public int MoveNextCount { get; private set; }

            public int DisposeCount { get; private set; }

            public CancellationToken EnumerationToken { get; private set; }

            public TaskCompletionSource<bool> Disposed { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public bool ObserveCancellation { get; set; } = true;

            public bool TrackMetadataLifetime { get; set; }

            public Action<int>? BeforeMoveNext { get; set; }

            public Func<CancellationToken, Task>? BeforeMoveNextAsync { get; set; }

            public Func<Task>? OnDisposeAsync { get; set; }

            public int CountRetainedMetadata()
            {
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);
                int retained = 0;
                foreach (WeakReference<string> metadata in m_metadata)
                {
                    if (metadata.TryGetTarget(out _))
                    {
                        retained++;
                    }
                }
                return retained;
            }

            public IAsyncEnumerator<FileSystemEntry> GetAsyncEnumerator(
                CancellationToken cancellationToken = default)
            {
                EnumerationCount++;
                EnumerationToken = cancellationToken;
                return new Enumerator(this, cancellationToken);
            }

            private sealed class Enumerator : IAsyncEnumerator<FileSystemEntry>
            {
                public Enumerator(CountingEntries owner, CancellationToken cancellationToken)
                {
                    m_owner = owner;
                    m_cancellationToken = cancellationToken;
                }

                public FileSystemEntry Current { get; private set; }

                public async ValueTask<bool> MoveNextAsync()
                {
                    m_owner.MoveNextCount++;
                    m_owner.BeforeMoveNext?.Invoke(m_owner.MoveNextCount);
                    if (m_owner.BeforeMoveNextAsync is { } beforeMove)
                    {
                        await beforeMove(m_cancellationToken).ConfigureAwait(false);
                    }
                    if (m_owner.ObserveCancellation)
                    {
                        m_cancellationToken.ThrowIfCancellationRequested();
                    }
                    if (m_index == m_owner.m_entryCount)
                    {
                        return false;
                    }
                    string name = "entry-" + m_index.ToString("D5", CultureInfo.InvariantCulture);
                    string mimeType = string.Empty;
                    if (m_owner.TrackMetadataLifetime)
                    {
                        mimeType = "application/" + name;
                        m_owner.m_metadata.Add(new WeakReference<string>(mimeType));
                    }
                    Current = new FileSystemEntry(
                        name, name, m_index % 2 == 0, m_index, false,
                        new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), mimeType);
                    m_index++;
                    return true;
                }

                public async ValueTask DisposeAsync()
                {
                    m_owner.DisposeCount++;
                    if (m_owner.OnDisposeAsync is { } onDispose)
                    {
                        await onDispose().ConfigureAwait(false);
                    }
                    m_owner.Disposed.TrySetResult(true);
                }

                private readonly CountingEntries m_owner;
                private readonly CancellationToken m_cancellationToken;
                private int m_index;
            }

            private readonly int m_entryCount;
            private readonly List<WeakReference<string>> m_metadata = [];
        }

        private MonitoredItemQueueFactory m_queueFactory = null!;
        private Mock<IFileSystemProvider> m_provider = null!;
        private FileSystemNodeManager m_manager = null!;
        private DirectoryObjectState m_directory = null!;
    }
}
