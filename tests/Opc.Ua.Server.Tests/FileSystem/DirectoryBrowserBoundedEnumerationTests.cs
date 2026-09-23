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
        public void DirectoryBrowseRejectsOverLimitWithoutEnumeratingEntireProvider(int entryCount)
        {
            var entries = new CountingEntries(entryCount);
            SetEntries(entries);
            using INodeBrowser browser = CreateBrowser();

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(
                async () => await ReadProviderTargetsAsync(browser).ConfigureAwait(false))!;

            Assert.Multiple(() =>
            {
                Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
                Assert.That(entries.MoveNextCount, Is.EqualTo(1025));
                Assert.That(entries.EnumerationCount, Is.EqualTo(1));
                Assert.That(entries.DisposeCount, Is.EqualTo(1));
            });
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

        [Test]
        public async Task CancellationAfterFirstResultStopsBufferedTraversal()
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
                Assert.That(entries.MoveNextCount, Is.EqualTo(17));
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
            Assert.Multiple(() =>
            {
                Assert.That(entries.EnumerationCount, Is.EqualTo(1));
                Assert.That(entries.MoveNextCount, Is.EqualTo(17));
                Assert.That(entries.DisposeCount, Is.EqualTo(1));
            });
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

        private sealed class CountingEntries : IAsyncEnumerable<FileSystemEntry>
        {
            public CountingEntries(int entryCount)
            {
                m_entryCount = entryCount;
            }

            public int EnumerationCount { get; private set; }

            public int MoveNextCount { get; private set; }

            public int DisposeCount { get; private set; }

            public bool ObserveCancellation { get; set; } = true;

            public Action<int>? BeforeMoveNext { get; set; }

            public IAsyncEnumerator<FileSystemEntry> GetAsyncEnumerator(
                CancellationToken cancellationToken = default)
            {
                EnumerationCount++;
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

                public ValueTask<bool> MoveNextAsync()
                {
                    m_owner.MoveNextCount++;
                    m_owner.BeforeMoveNext?.Invoke(m_owner.MoveNextCount);
                    if (m_owner.ObserveCancellation)
                    {
                        m_cancellationToken.ThrowIfCancellationRequested();
                    }
                    if (m_index == m_owner.m_entryCount)
                    {
                        return new ValueTask<bool>(false);
                    }
                    string name = "entry-" + m_index.ToString("D5", CultureInfo.InvariantCulture);
                    Current = new FileSystemEntry(
                        name, name, m_index % 2 == 0, m_index, false,
                        new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), string.Empty);
                    m_index++;
                    return new ValueTask<bool>(true);
                }

                public ValueTask DisposeAsync()
                {
                    m_owner.DisposeCount++;
                    return default;
                }

                private readonly CountingEntries m_owner;
                private readonly CancellationToken m_cancellationToken;
                private int m_index;
            }

            private readonly int m_entryCount;
        }

        private MonitoredItemQueueFactory m_queueFactory = null!;
        private Mock<IFileSystemProvider> m_provider = null!;
        private FileSystemNodeManager m_manager = null!;
        private DirectoryObjectState m_directory = null!;
    }
}
