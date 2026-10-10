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
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.XRegistry.Tests
{
    [TestFixture]
    [Category("XRegistry")]
    public sealed class RegistryStateStoreTests
    {
        [Test]
        public async Task StateStoreRegistrationRetainsAnExplicitProvider()
        {
            var services = new ServiceCollection();
            services.AddSingleton<IRegistryStateStore>(_ => new MemoryRegistryStateStore(maxStateBytes: 1));
            services.AddXRegistryStateStore();
            await using ServiceProvider provider = services.BuildServiceProvider();
            IRegistryStateStore store = provider.GetRequiredService<IRegistryStateStore>();
            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await store.CommitAsync(0, ByteString.FromHexString("0102")).ConfigureAwait(false))!;
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            Assert.That((await store.ReadAsync().ConfigureAwait(false)).Revision, Is.Zero);
        }

        [Test]
        public async Task RestartDiscardsAnUnpublishedStageWithoutChangingCommittedState()
        {
            string path = Path.Combine(TestContext.CurrentContext.WorkDirectory,
                nameof(RegistryStateStoreTests), Guid.NewGuid().ToString("N"));
            ByteString original = ByteString.FromHexString("AA01");
            try
            {
                await using (var store = new FileRegistryStateStore(path))
                {
                    await store.CommitAsync(0, original).ConfigureAwait(false);
                }
                File.WriteAllBytes(Path.Combine(path, "state.pending"), [1, 2]);
                await using var reopened = new FileRegistryStateStore(path);
                RegistryStoredState state = await reopened.ReadAsync().ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(state.Revision, Is.EqualTo(1));
                    Assert.That(state.Document, Is.EqualTo(original));
                    Assert.That(File.Exists(Path.Combine(path, "state.pending")), Is.False);
                });
            }
            finally
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CompareAndSwapCommitsOnceAndRejectsAStaleWriter(bool durable)
        {
            string path = Path.Combine(TestContext.CurrentContext.WorkDirectory,
                nameof(RegistryStateStoreTests), Guid.NewGuid().ToString("N"));
            try
            {
                await using IRegistryStateStore store = durable
                    ? new FileRegistryStateStore(path)
                    : new MemoryRegistryStateStore();
                RegistryStoredState initial = await store.ReadAsync().ConfigureAwait(false);
                Assert.That(initial.Revision, Is.Zero);
                Assert.That(initial.Document.IsNull, Is.True);
                ByteString first = ByteString.FromHexString("010203");
                RegistryStateCommit committed = await store.CommitAsync(0, first).ConfigureAwait(false);
                Assert.That(committed.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(committed.State.Revision, Is.EqualTo(1));
                RegistryStateCommit stale = await store.CommitAsync(0, ByteString.FromHexString("04"))
                    .ConfigureAwait(false);
                Assert.That(stale.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                Assert.That(stale.State.Document, Is.EqualTo(first));
                RegistryStateCommit noChange = await store.CommitAsync(1, first).ConfigureAwait(false);
                Assert.That(noChange.State.Revision, Is.EqualTo(1));
                Assert.That((await store.ReadAsync().ConfigureAwait(false)).Document, Is.EqualTo(first));
            }
            finally
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
        }

        [Test]
        public async Task CorruptRevisionCannotBeAcceptedWithAnUnchangedDocumentChecksum()
        {
            string path = Path.Combine(TestContext.CurrentContext.WorkDirectory,
                nameof(RegistryStateStoreTests), Guid.NewGuid().ToString("N"));
            try
            {
                await using (var store = new FileRegistryStateStore(path))
                {
                    await store.CommitAsync(0, ByteString.FromHexString("010203")).ConfigureAwait(false);
                }
                string statePath = Path.Combine(path, "state.bin");
                byte[] bytes = File.ReadAllBytes(statePath);
                bytes[8] ^= 2;
                File.WriteAllBytes(statePath, bytes);
                await using var restored = new FileRegistryStateStore(path);
                Assert.ThrowsAsync<InvalidDataException>(async () =>
                    await restored.ReadAsync().ConfigureAwait(false));
            }
            finally
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
        }

        [Test]
        public async Task EmptyStateQuotaCancellationAndDisposalRemainDistinct()
        {
            var store = new MemoryRegistryStateStore(maxStateBytes: 3);
            try
            {
                RegistryStateCommit initialized = await store.CommitAsync(0, ByteString.Empty).ConfigureAwait(false);
                Assert.That(initialized.State.Revision, Is.EqualTo(1));
                ByteString exactLimit = ByteString.FromHexString("010203");
                await store.CommitAsync(1, exactLimit).ConfigureAwait(false);
                ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await store.CommitAsync(2, ByteString.FromHexString("01020304")).ConfigureAwait(false))!;
                Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
                using var cancelled = new CancellationTokenSource();
                cancelled.Cancel();
                Assert.ThrowsAsync<OperationCanceledException>(async () =>
                    await store.CommitAsync(2, ByteString.Empty, cancelled.Token).ConfigureAwait(false));
                RegistryStoredState retained = await store.ReadAsync().ConfigureAwait(false);
                Assert.That(retained.Revision, Is.EqualTo(2));
                Assert.That(retained.Document, Is.EqualTo(exactLimit));
            }
            finally
            {
                await store.DisposeAsync().ConfigureAwait(false);
            }
            Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await store.ReadAsync().ConfigureAwait(false));
        }

        [Test]
        public async Task DurableStateKeepsItsRevisionAndBytesAcrossRestart()
        {
            string path = Path.Combine(TestContext.CurrentContext.WorkDirectory,
                nameof(RegistryStateStoreTests), Guid.NewGuid().ToString("N"));
            ByteString document = ByteString.FromHexString("FF000180");
            try
            {
                await using (var store = new FileRegistryStateStore(path))
                {
                    await store.CommitAsync(0, document).ConfigureAwait(false);
                    Assert.Throws<IOException>(() => _ = new FileRegistryStateStore(path));
                }
                await using var restored = new FileRegistryStateStore(path);
                RegistryStoredState state = await restored.ReadAsync().ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(state.Revision, Is.EqualTo(1));
                    Assert.That(state.Document, Is.EqualTo(document));
                });
            }
            finally
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
        }
    }
}
