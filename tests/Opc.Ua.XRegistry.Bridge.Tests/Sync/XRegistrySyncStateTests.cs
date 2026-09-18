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
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.XRegistry.Bridge.Sync;
using static Opc.Ua.XRegistry.Bridge.Tests.Sync.XRegistryReconciliationTests;
using static Opc.Ua.XRegistry.Bridge.Tests.Sync.XRegistrySyncFixture;

namespace Opc.Ua.XRegistry.Bridge.Tests.Sync
{
    [TestFixture]
    public sealed class XRegistrySyncStateTests
    {
        [Test]
        public async Task MemoryStoreEnforcesOwnershipReadonlyAndExactQuotaAsync()
        {
            await using var store = new MemoryXRegistrySyncStateStore(5);
            IXRegistrySyncStateSession writer = await store.OpenAsync().ConfigureAwait(false);
            ByteString pristine = await writer.ReadAsync().ConfigureAwait(false);
            Assert.That(pristine.IsNull, Is.True);
            Assert.ThrowsAsync<IOException>(async () => await store.OpenAsync().ConfigureAwait(false));
            await writer.CommitAsync(ByteString.From("12345"u8)).ConfigureAwait(false);
            Assert.ThrowsAsync<InvalidDataException>(async () =>
                await writer.CommitAsync(ByteString.From("123456"u8)).ConfigureAwait(false));
            Assert.ThrowsAsync<InvalidDataException>(async () =>
                await writer.CommitAsync(ByteString.Empty).ConfigureAwait(false));
            IXRegistrySyncStateSession reader = await store.OpenAsync(readOnly: true).ConfigureAwait(false);
            ByteString actual = await reader.ReadAsync().ConfigureAwait(false);
            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await reader.CommitAsync(ByteString.From("other"u8)).ConfigureAwait(false));
            await reader.DisposeAsync().ConfigureAwait(false);
            await writer.DisposeAsync().ConfigureAwait(false);
            IXRegistrySyncStateSession successor = await store.OpenAsync().ConfigureAwait(false);
            await successor.DisposeAsync().ConfigureAwait(false);
            Assert.That(actual.ToArray(), Is.EqualTo(Encoding.UTF8.GetBytes("12345")));
        }

        [TestCase("not-json")]
        [TestCase(/*lang=json,strict*/ """{"format":2,"sha256":"invalid","payload":{}}""")]
        [TestCase(/*lang=json,strict*/ """{"format":1,"format":1,"sha256":"invalid","payload":{}}""")]
        [TestCase("")]
        public async Task CorruptStateNeverContactsEndpointsAsync(string corrupted)
        {
            await using var fixture = new XRegistrySyncFixture();
            if (corrupted.Length == 0)
            {
                using var fs = new VirtualFileSystem();
                string directory = VirtualDirectory();
                fs.Add(Path.Combine(directory, "sync.state"), []);
                fs.Add(Path.Combine(directory, "sync.initialized"), [1]);
                await using var fileStore = new FileXRegistrySyncStateStore(
                    fs, directory, durability: new SyncRecordingDurability());
                XRegistrySyncReport empty = await fixture.Engine(fileStore).RunOnceAsync().ConfigureAwait(false);
                Assert.That(empty.Status, Is.EqualTo(XRegistrySyncStatus.Failed), Details(empty));
            }
            else
            {
                IXRegistrySyncStateSession writer = await fixture.Store.OpenAsync().ConfigureAwait(false);
                await writer.CommitAsync(ByteString.From(Encoding.UTF8.GetBytes(corrupted))).ConfigureAwait(false);
                await writer.DisposeAsync().ConfigureAwait(false);
                XRegistrySyncReport report = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
                ByteString preserved = await ReadStateAsync(fixture.Store).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(report.Status, Is.EqualTo(XRegistrySyncStatus.Failed), Details(report));
                    Assert.That(preserved.ToArray(), Is.EqualTo(Encoding.UTF8.GetBytes(corrupted)));
                });
            }
            Assert.Multiple(() =>
            {
                Assert.That(fixture.Native.Inspections + fixture.Http.Inspections, Is.Zero);
                Assert.That(fixture.Native.Mutations, Is.Empty);
                Assert.That(fixture.Http.Mutations, Is.Empty);
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task MissingStateOrInitializationMarkerNeverMeansPristineStorageAsync(bool missingState)
        {
            using var fs = new VirtualFileSystem();
            string directory = VirtualDirectory();
            string remaining = Path.Combine(directory, missingState ? "sync.initialized" : "sync.state");
            fs.Add(remaining, Encoding.UTF8.GetBytes("existing-state-evidence"));
            await using var store = new FileXRegistrySyncStateStore(
                fs, directory, durability: new SyncRecordingDurability());
            await using var fixture = new XRegistrySyncFixture();
            Assert.ThrowsAsync<InvalidDataException>(async () => await ReadStateAsync(store).ConfigureAwait(false));
            XRegistrySyncReport report = await fixture.Engine(store).RunOnceAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(report.Status, Is.EqualTo(XRegistrySyncStatus.Failed), Details(report));
                Assert.That(fixture.Native.Inspections + fixture.Http.Inspections, Is.Zero);
                Assert.That(fixture.Native.Mutations, Is.Empty);
                Assert.That(fixture.Http.Mutations, Is.Empty);
                Assert.That(fs.Get(remaining), Is.EqualTo(Encoding.UTF8.GetBytes("existing-state-evidence")));
            });
        }

        [Test]
        public async Task ChecksumMismatchDoesNotResetBaselinesAsync()
        {
            await using var fixture = new XRegistrySyncFixture();
            await fixture.SeedBothAsync(Group, "{}").ConfigureAwait(false);
            await fixture.BaselineAsync().ConfigureAwait(false);
            ByteString original = await ReadStateAsync(fixture.Store).ConfigureAwait(false);
            JsonObject corrupt = JsonNode.Parse(original.Memory.Span)!.AsObject();
            corrupt["payload"]!["generation"] = 123456;
            IXRegistrySyncStateSession writer = await fixture.Store.OpenAsync().ConfigureAwait(false);
            await writer.CommitAsync(ByteString.From(Encoding.UTF8.GetBytes(corrupt.ToJsonString())))
                .ConfigureAwait(false);
            await writer.DisposeAsync().ConfigureAwait(false);
            XRegistrySyncReport report = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(report.Status, Is.EqualTo(XRegistrySyncStatus.Failed));
                Assert.That(report.Records.Span.ToArray().Any(record =>
                    record.Detail.Contains("checksum", StringComparison.Ordinal)), Is.True);
                Assert.That(fixture.Native.Inspections + fixture.Http.Inspections, Is.Zero);
            });
        }

        [Test]
        public async Task IntentQuotaFailureStopsEveryOutboundMutationAsync()
        {
            await using var fixture = new XRegistrySyncFixture();
            await fixture.SeedBothAsync(Group, /*lang=json,strict*/ """{"name":"base"}""").ConfigureAwait(false);
            await fixture.SeedBothAsync("/schemagroups/second",
                /*lang=json,strict*/ """{"name":"base"}""").ConfigureAwait(false);
            await fixture.BaselineAsync().ConfigureAwait(false);
            ByteString before = await ReadStateAsync(fixture.Store).ConfigureAwait(false);
            await fixture.Native.ChangeAsync(Group,
                /*lang=json,strict*/ """{"name":"first-outgoing"}""").ConfigureAwait(false);
            await fixture.Native.ChangeAsync("/schemagroups/second",
                /*lang=json,strict*/ """{"name":"second-outgoing"}""")
                .ConfigureAwait(false);
            fixture.Options = fixture.Options with { MaximumStateBytes = before.Length + 50 };
            XRegistrySyncReport failed = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            ByteString after = await ReadStateAsync(fixture.Store).ConfigureAwait(false);
            XRegistrySyncStateStatus state = await fixture.State.ReadStatusAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(failed.Status, Is.EqualTo(XRegistrySyncStatus.Failed), Details(failed));
                Assert.That(fixture.Native.Mutations, Is.Empty);
                Assert.That(fixture.Http.Mutations, Is.Empty,
                    "Do not swallow state failure and continue independent writes.");
                Assert.That(after.ToArray(), Is.EqualTo(before.ToArray()));
                Assert.That(state.Intents, Is.Zero);
            });
        }

        [Test]
        public async Task FileStateSurvivesProviderRestartAndRetainsPendingCreateAsync()
        {
            using var fs = new VirtualFileSystem();
            var durability = new SyncRecordingDurability();
            string directory = VirtualDirectory();
            await using var fixture = new XRegistrySyncFixture();
            await fixture.Native.ChangeAsync(Group,
                /*lang=json,strict*/ """{"name":"created"}""").ConfigureAwait(false);
            fixture.Http.FailAfterMutation = true;
            var first = new FileXRegistrySyncStateStore(fs, directory, durability: durability);
            XRegistrySyncReport initial = await fixture.Engine(first).RunOnceAsync().ConfigureAwait(false);
            await first.DisposeAsync().ConfigureAwait(false);
            await using var restarted = new FileXRegistrySyncStateStore(fs, directory, durability: durability);
            XRegistrySyncReport held = await fixture.Engine(restarted).RunOnceAsync().ConfigureAwait(false);
            var manager = new XRegistrySyncStateManager(restarted, fixture.Options.JobId);
            XRegistrySyncStateStatus state = await manager.ReadStatusAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(initial.Pending, Is.EqualTo(1), Details(initial));
                Assert.That(held.Pending, Is.EqualTo(1), Details(held));
                Assert.That(fixture.Http.Mutations, Has.Count.EqualTo(1));
                Assert.That(state.Intents, Is.EqualTo(1));
                Assert.That(state.Outcomes, Is.Zero);
                Assert.That(durability.FileFlushes, Is.GreaterThan(0));
                Assert.That(durability.DirectoryFlushes, Is.GreaterThan(durability.FileFlushes));
                Assert.That(fs.Exists(Path.Combine(directory, "sync.pending")), Is.False);
                Assert.That(fs.Exists(Path.Combine(directory, "sync.staged")), Is.False);
            });
        }

        [TestCase(1)]
        [TestCase(3)]
        [TestCase(4)]
        public async Task UncertainPublicationFailsClosedAcrossSessionsAsync(int barrier)
        {
            using var fs = new VirtualFileSystem();
            var durability = new SyncRecordingDurability { FailDirectoryFlush = barrier };
            string directory = VirtualDirectory();
            var store = new FileXRegistrySyncStateStore(fs, directory, durability: durability);
            IXRegistrySyncStateSession writer = await store.OpenAsync().ConfigureAwait(false);
            await writer.ReadAsync().ConfigureAwait(false);
            Assert.ThrowsAsync<IOException>(async () =>
                await writer.CommitAsync(ByteString.From("complete-state"u8)).ConfigureAwait(false));
            Assert.ThrowsAsync<IOException>(async () => await writer.ReadAsync().ConfigureAwait(false));
            await writer.DisposeAsync().ConfigureAwait(false);
            await store.DisposeAsync().ConfigureAwait(false);
            durability.FailDirectoryFlush = 0;
            await using var restarted = new FileXRegistrySyncStateStore(fs, directory, durability: durability);
            Assert.ThrowsAsync<InvalidDataException>(async () =>
                await ReadStateAsync(restarted).ConfigureAwait(false));
            Assert.That(fs.Exists(Path.Combine(directory, "sync.pending")), Is.True);
        }

        [Test]
        public async Task PartialStagingNeverPublishesAnEmptyBaselineAsync()
        {
            using var fs = new SyncFaultFileSystem();
            var durability = new SyncRecordingDurability();
            string directory = VirtualDirectory();
            var store = new FileXRegistrySyncStateStore(fs, directory, durability: durability);
            IXRegistrySyncStateSession writer = await store.OpenAsync().ConfigureAwait(false);
            await writer.ReadAsync().ConfigureAwait(false);
            await writer.CommitAsync(ByteString.From("previous"u8)).ConfigureAwait(false);
            fs.FailStagingWrite = true;
            Assert.ThrowsAsync<IOException>(async () =>
                await writer.CommitAsync(ByteString.From("replacement"u8)).ConfigureAwait(false));
            await writer.DisposeAsync().ConfigureAwait(false);
            await store.DisposeAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(fs.Bytes(Path.Combine(directory, "sync.state")),
                    Is.EqualTo(Encoding.UTF8.GetBytes("previous")));
                Assert.That(fs.Bytes(Path.Combine(directory, "sync.staged")), Has.Length.EqualTo(1));
                Assert.That(fs.Exists(Path.Combine(directory, "sync.pending")), Is.True);
            });
            await using var next = new FileXRegistrySyncStateStore(fs, directory, durability: durability);
            Assert.ThrowsAsync<InvalidDataException>(async () => await ReadStateAsync(next).ConfigureAwait(false));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task AtomicReplaceFailureNeverAcknowledgesUncertainStateAsync(bool published)
        {
            using var fs = new SyncFaultFileSystem();
            var durability = new SyncRecordingDurability();
            string directory = VirtualDirectory();
            var store = new FileXRegistrySyncStateStore(fs, directory, durability: durability);
            IXRegistrySyncStateSession writer = await store.OpenAsync().ConfigureAwait(false);
            await writer.ReadAsync().ConfigureAwait(false);
            await writer.CommitAsync(ByteString.From("previous"u8)).ConfigureAwait(false);
            fs.FailBeforeReplace = !published;
            fs.FailAfterReplace = published;
            Assert.ThrowsAsync<IOException>(async () =>
                await writer.CommitAsync(ByteString.From("replacement"u8)).ConfigureAwait(false));
            await writer.DisposeAsync().ConfigureAwait(false);
            await store.DisposeAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(fs.Bytes(Path.Combine(directory, "sync.state")),
                    Is.EqualTo(Encoding.UTF8.GetBytes(published ? "replacement" : "previous")));
                Assert.That(fs.Exists(Path.Combine(directory, "sync.pending")), Is.True);
            });
            await using var restarted = new FileXRegistrySyncStateStore(fs, directory, durability: durability);
            Assert.ThrowsAsync<InvalidDataException>(async () => await ReadStateAsync(restarted).ConfigureAwait(false));
        }

        [Test]
        public async Task SecondFileWriterIsRejectedAndReadonlyDoesNotAcquireOwnershipAsync()
        {
            using var fs = new VirtualFileSystem();
            var durability = new SyncRecordingDurability();
            string directory = VirtualDirectory();
            await using var first = new FileXRegistrySyncStateStore(fs, directory, durability: durability);
            await using var second = new FileXRegistrySyncStateStore(fs, directory, durability: durability);
            IXRegistrySyncStateSession writer = await first.OpenAsync().ConfigureAwait(false);
            Assert.ThrowsAsync<IOException>(async () => await second.OpenAsync().ConfigureAwait(false));
            ByteString empty = await ReadStateAsync(second).ConfigureAwait(false);
            Assert.That(empty.IsNull, Is.True);
            await writer.DisposeAsync().ConfigureAwait(false);
            IXRegistrySyncStateSession successor = await second.OpenAsync().ConfigureAwait(false);
            await successor.DisposeAsync().ConfigureAwait(false);
            Assert.That(durability.Writers, Is.Zero);
        }

        [Test]
        public async Task DryRunAndConflictListingDoNotCreateFileArtifactsAsync()
        {
            using var fs = new VirtualFileSystem();
            var durability = new SyncRecordingDurability();
            string directory = VirtualDirectory();
            await using var store = new FileXRegistrySyncStateStore(fs, directory, durability: durability);
            await using var fixture = new XRegistrySyncFixture();
            await fixture.Native.ChangeAsync(Group, "{}").ConfigureAwait(false);
            var manager = new XRegistrySyncStateManager(store, fixture.Options.JobId);
            ArrayOf<XRegistrySyncConflict> conflicts = await manager.ListConflictsAsync().ConfigureAwait(false);
            XRegistrySyncReport dryRun = await fixture.Engine(store).RunOnceAsync(dryRun: true).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(conflicts.Count, Is.Zero);
                Assert.That(dryRun.Planned, Is.EqualTo(1), Details(dryRun));
                Assert.That(fs.CreatedFiles, Is.Empty);
                Assert.That(durability.Acquisitions, Is.Zero);
                Assert.That(durability.FileFlushes, Is.Zero);
                Assert.That(fixture.Http.Mutations, Is.Empty);
            });
        }

        [Test]
        public void UnqualifiedFileSystemCannotClaimDurability()
        {
            using var fs = new VirtualFileSystem();
            Assert.Throws<ArgumentException>(() => new FileXRegistrySyncStateStore(fs, VirtualDirectory()));
        }

        [Test]
        public async Task LocalFileStoreCommitsAndReopensWithExclusiveOwnershipAsync()
        {
            string directory = Path.Combine(
                TestContext.CurrentContext.WorkDirectory, "sync-state-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var first = new FileXRegistrySyncStateStore(LocalFileSystem.Instance, directory);
                IXRegistrySyncStateSession writer = await first.OpenAsync().ConfigureAwait(false);
                await using var competing = new FileXRegistrySyncStateStore(LocalFileSystem.Instance, directory);
                Assert.ThrowsAsync<IOException>(async () => await competing.OpenAsync().ConfigureAwait(false));
                await writer.ReadAsync().ConfigureAwait(false);
                await writer.CommitAsync(ByteString.From("durable-state"u8)).ConfigureAwait(false);
                await writer.DisposeAsync().ConfigureAwait(false);
                await first.DisposeAsync().ConfigureAwait(false);
                await using var restarted = new FileXRegistrySyncStateStore(LocalFileSystem.Instance, directory);
                ByteString actual = await ReadStateAsync(restarted).ConfigureAwait(false);
                Assert.That(actual.ToArray(), Is.EqualTo(Encoding.UTF8.GetBytes("durable-state")));
            }
            finally
            {
                foreach (string name in new[]
                {
                    "sync.writer", "sync.state", "sync.initialized", "sync.pending", "sync.staged"
                })
                {
                    File.Delete(Path.Combine(directory, name));
                }
                Directory.Delete(directory);
            }
        }

        private static string VirtualDirectory()
        {
            return Path.Combine(
                TestContext.CurrentContext.WorkDirectory, "virtual-sync-" + Guid.NewGuid().ToString("N"));
        }
    }
}
