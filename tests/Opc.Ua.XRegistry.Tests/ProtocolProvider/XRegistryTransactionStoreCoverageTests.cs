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
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
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
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.XRegistry.Protocol;
using Opc.Ua.XRegistry.Server.Protocol;

namespace Opc.Ua.XRegistry.Tests.ProtocolProvider
{
    [TestFixture]
    public sealed class XRegistryTransactionStoreCoverageTests
    {
        [Test]
        public async Task InMemoryStoreDistinguishesPristineEmptyAndStaleGenerationsAsync()
        {
            var store = new InMemoryXRegistryTransactionStore();
            ByteString pristine = await store.LoadAsync().ConfigureAwait(false);
            bool emptyCommit = await store.CommitAsync(default, ByteString.Empty).ConfigureAwait(false);
            ByteString empty = await store.LoadAsync().ConfigureAwait(false);
            bool staleNull = await store.CommitAsync(default, ByteString.From(new byte[] { 9 })).ConfigureAwait(false);
            bool staleValue = await store.CommitAsync(
                ByteString.From(new byte[] { 8 }), ByteString.From(new byte[] { 9 })).ConfigureAwait(false);
            bool replacement = await store.CommitAsync(
                ByteString.Empty, ByteString.From(new byte[] { 1, 2, 3 })).ConfigureAwait(false);
            Assert.ThrowsAsync<ArgumentException>(async () =>
                await store.CommitAsync(ByteString.From(new byte[] { 1, 2, 3 }), default).ConfigureAwait(false));
            ByteString actual = await store.LoadAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(store.SupportsDurableReplay, Is.False);
                Assert.That(pristine.IsNull, Is.True);
                Assert.That(emptyCommit, Is.True);
                Assert.That(empty.IsNull, Is.False);
                Assert.That(empty.Length, Is.Zero);
                Assert.That(staleNull, Is.False);
                Assert.That(staleValue, Is.False);
                Assert.That(replacement, Is.True);
                Assert.That(actual.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));
            });
        }

        [Test]
        public async Task CanceledInMemoryOperationsCannotReadOrReplaceAGenerationAsync()
        {
            var store = new InMemoryXRegistryTransactionStore();
            Assert.That(await store.CommitAsync(default, ByteString.From(new byte[] { 1 })).ConfigureAwait(false),
                Is.True);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await store.LoadAsync(cancellation.Token).ConfigureAwait(false));
            Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await store.CommitAsync(ByteString.From(new byte[] { 1 }), ByteString.From(new byte[] { 2 }),
                    cancellation.Token).ConfigureAwait(false));
            ByteString actual = await store.LoadAsync().ConfigureAwait(false);
            Assert.That(actual.ToArray(), Is.EqualTo(new byte[] { 1 }));
        }

        [Test]
        public async Task FileStoreEnforcesExactByteLimitAndPreservesBytesOnCasMismatchAsync()
        {
            string directory = NewDirectory();
            try
            {
                using var store = new FileXRegistryTransactionStore(directory, 3);
                bool created = await store.CommitAsync(default, ByteString.From(new byte[] { 1, 2, 3 }))
                    .ConfigureAwait(false);
                bool stale = await store.CommitAsync(
                    ByteString.From(new byte[] { 3, 2, 1 }), ByteString.From(new byte[] { 4 })).ConfigureAwait(false);
                bool staleNull = await store.CommitAsync(default, ByteString.From(new byte[] { 4 }))
                    .ConfigureAwait(false);
                Assert.ThrowsAsync<ArgumentException>(async () =>
                    await store.CommitAsync(ByteString.From(new byte[] { 1, 2, 3 }),
                        ByteString.From(new byte[] { 1, 2, 3, 4 })).ConfigureAwait(false));
                Assert.ThrowsAsync<ArgumentException>(async () =>
                    await store.CommitAsync(ByteString.From(new byte[] { 1, 2, 3 }), default).ConfigureAwait(false));
                ByteString actual = await store.LoadAsync().ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(store.SupportsDurableReplay, Is.True);
                    Assert.That(created, Is.True);
                    Assert.That(stale, Is.False);
                    Assert.That(staleNull, Is.False);
                    Assert.That(actual.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));
                    Assert.That(File.Exists(Path.Combine(directory, "registry.pending")), Is.False);
                });
            }
            finally
            {
                DeleteDirectory(directory);
            }
        }

        [TestCase("registry.json")]
        [TestCase("registry.initialized")]
        public async Task MissingInitializedStoreArtifactCannotResetTheRegistryAsync(string artifact)
        {
            string directory = NewDirectory();
            try
            {
                using (var store = new FileXRegistryTransactionStore(directory))
                {
                    Assert.That(await store.CommitAsync(default, ByteString.From(new byte[] { 1, 2, 3 }))
                        .ConfigureAwait(false), Is.True);
                }
                File.Delete(Path.Combine(directory, artifact));
                using var reopened = new FileXRegistryTransactionStore(directory);
                Assert.ThrowsAsync<InvalidDataException>(async () => await reopened.LoadAsync().ConfigureAwait(false));
                Assert.ThrowsAsync<InvalidDataException>(async () =>
                    await reopened.CommitAsync(default, ByteString.From(new byte[] { 4 })).ConfigureAwait(false));
                Assert.That(File.Exists(Path.Combine(directory, artifact)), Is.False);
            }
            finally
            {
                DeleteDirectory(directory);
            }
        }

        [TestCase("registry.json", "")]
        [TestCase("registry.json", "1234")]
        [TestCase("registry.pending", "pending")]
        public async Task FileStoreRejectsEmptyOversizedOrStagedStateWithoutRemovingEvidenceAsync(
            string name, string content)
        {
            string directory = NewDirectory();
            try
            {
                using var store = new FileXRegistryTransactionStore(directory, 3);
                string path = Path.Combine(directory, name);
                byte[] bytes = Encoding.UTF8.GetBytes(content);
                using (var stream = new FileStream(
                    path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
                {
#if NET
                    await stream.WriteAsync(bytes.AsMemory()).ConfigureAwait(false);
#else
                    await stream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
#endif
                }
                Assert.ThrowsAsync<InvalidDataException>(async () => await store.LoadAsync().ConfigureAwait(false));
                using var source = new FileStream(
                    path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
                using var copy = new MemoryStream();
                await source.CopyToAsync(copy).ConfigureAwait(false);
                Assert.That(copy.ToArray(), Is.EqualTo(bytes));
            }
            finally
            {
                DeleteDirectory(directory);
            }
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void FileStoreRejectsNonpositiveStorageBoundsBeforeCreatingADirectory(int maximum)
        {
            string directory = NewDirectory();
            ArgumentOutOfRangeException? error = Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                using var store = new FileXRegistryTransactionStore(directory, maximum);
            });
            Assert.Multiple(() =>
            {
                Assert.That(error?.ParamName, Is.EqualTo("maximumBytes"));
                Assert.That(Directory.Exists(directory), Is.False);
            });
        }

        [Test]
        public void FileStoreRejectsNullDirectory()
        {
            Assert.Throws<ArgumentNullException>(() =>
            {
                using var store = new FileXRegistryTransactionStore(null!);
            });
        }

        [Test]
        public async Task FileStoreCancellationAndDisposalCannotReplaceTheStoredGenerationAsync()
        {
            string directory = NewDirectory();
            try
            {
                using (var store = new FileXRegistryTransactionStore(directory))
                {
                    Assert.That(await store.CommitAsync(default, ByteString.From(new byte[] { 1 }))
                        .ConfigureAwait(false), Is.True);
                    using var cancellation = new CancellationTokenSource();
                    cancellation.Cancel();
                    Assert.CatchAsync<OperationCanceledException>(async () =>
                        await store.LoadAsync(cancellation.Token).ConfigureAwait(false));
                    Assert.CatchAsync<OperationCanceledException>(async () =>
                        await store.CommitAsync(ByteString.From(new byte[] { 1 }),
                            ByteString.From(new byte[] { 2 }), cancellation.Token).ConfigureAwait(false));
                    store.Dispose();
                    Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                        await store.LoadAsync().ConfigureAwait(false));
                    Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                        await store.CommitAsync(ByteString.From(new byte[] { 1 }), ByteString.From(new byte[] { 3 }))
                            .ConfigureAwait(false));
                }
                using var reopened = new FileXRegistryTransactionStore(directory);
                ByteString actual = await reopened.LoadAsync().ConfigureAwait(false);
                Assert.That(actual.ToArray(), Is.EqualTo(new byte[] { 1 }));
            }
            finally
            {
                DeleteDirectory(directory);
            }
        }

        [Test]
        public async Task ConcurrentEndpointWritersPublishExactlyOneWholePreparedGenerationAsync()
        {
            var store = new InMemoryXRegistryTransactionStore();
            using var firstEndpoint = new XRegistryTransactionalEndpoint(
                XRegistryProviderCoverage.Options(), store, new XRegistryProviderCoverageTimeProvider());
            using var secondEndpoint = new XRegistryTransactionalEndpoint(
                XRegistryProviderCoverage.Options(), store, new XRegistryProviderCoverageTimeProvider());
            IXRegistryPreparedOperation first = await firstEndpoint.PrepareAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Replace, "/groups/a", """{"name":"first"}""") with { OperationId = "first" })
                .ConfigureAwait(false);
            await using var firstLifetime = first.ConfigureAwait(false);
            IXRegistryPreparedOperation second = await secondEndpoint.PrepareAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Replace, "/groups/b", """{"name":"second"}""") with { OperationId = "second" })
                .ConfigureAwait(false);
            await using var secondLifetime = second.ConfigureAwait(false);
            XRegistryResponse[] results = await Task.WhenAll(
                Task.Run(async () => await first.CommitAsync().ConfigureAwait(false)),
                Task.Run(async () => await second.CommitAsync().ConfigureAwait(false))).ConfigureAwait(false);
            XRegistryResponse root = await firstEndpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Read, "/")).ConfigureAwait(false);
            XRegistryResponse groups = await secondEndpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Read, "/groups")).ConfigureAwait(false);
            XRegistryOperationOutcome firstOutcome = await firstEndpoint.GetOperationOutcomeAsync(
                "first", XRegistryProviderCoverage.Writer).ConfigureAwait(false);
            XRegistryOperationOutcome secondOutcome = await secondEndpoint.GetOperationOutcomeAsync(
                "second", XRegistryProviderCoverage.Writer).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(first.Response.StatusCode, Is.EqualTo(201));
                Assert.That(second.Response.StatusCode, Is.EqualTo(201));
                Assert.That(results.Select(response => response.StatusCode), Is.EquivalentTo(s_commitStatuses));
                Assert.That(results.Single(response => !response.IsSuccess).Error?.Code,
                    Is.EqualTo("concurrent_change"));
                Assert.That(root.Metadata.GetProperty("epoch").GetInt32(), Is.EqualTo(1));
                Assert.That(root.Metadata.GetProperty("groupscount").GetInt32(), Is.EqualTo(1));
                Assert.That(groups.Metadata.EnumerateObject().Count(), Is.EqualTo(1));
                Assert.That(new[] { firstOutcome.State, secondOutcome.State }, Is.EquivalentTo(
                    new[] { XRegistryOperationState.Committed, XRegistryOperationState.Unknown }));
                if (results[0].IsSuccess)
                {
                    Assert.That(groups.Metadata.GetProperty("a").GetProperty("name").GetString(), Is.EqualTo("first"));
                    Assert.That(groups.Metadata.TryGetProperty("b", out _), Is.False);
                    Assert.That(firstOutcome.State, Is.EqualTo(XRegistryOperationState.Committed));
                }
                else
                {
                    Assert.That(groups.Metadata.GetProperty("b").GetProperty("name").GetString(),
                        Is.EqualTo("second"));
                    Assert.That(groups.Metadata.TryGetProperty("a", out _), Is.False);
                    Assert.That(secondOutcome.State, Is.EqualTo(XRegistryOperationState.Committed));
                }
            });
        }

        private static string NewDirectory()
        {
            return Path.Combine(Path.GetTempPath(), "xregistry-coverage-" + Guid.NewGuid().ToString("N"));
        }

        private static void DeleteDirectory(string directory)
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        private static readonly int[] s_commitStatuses = [201, 409];
    }
}
