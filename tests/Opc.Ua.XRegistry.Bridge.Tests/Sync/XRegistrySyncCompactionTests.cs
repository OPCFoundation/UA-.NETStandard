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
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.XRegistry.Bridge.Sync;
using static Opc.Ua.XRegistry.Bridge.Tests.Sync.XRegistryReconciliationTests;
using static Opc.Ua.XRegistry.Bridge.Tests.Sync.XRegistrySyncFixture;

namespace Opc.Ua.XRegistry.Bridge.Tests.Sync
{
    [TestFixture]
    public sealed class XRegistrySyncCompactionTests
    {
        [Test]
        public async Task AcknowledgedCompactionPreservesBaselinesAndMonotonicOperationIdentityAsync()
        {
            await using var fixture = new XRegistrySyncFixture();
            await fixture.SeedBothAsync(Group, /*lang=json,strict*/ """{"name":"before"}""").ConfigureAwait(false);
            await fixture.BaselineAsync().ConfigureAwait(false);
            await fixture.Native.ChangeAsync(Group, /*lang=json,strict*/ """{"name":"after"}""").ConfigureAwait(false);
            XRegistrySyncReport copied = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            Assert.That(copied.Applied, Is.EqualTo(1), Details(copied));
            var codec = new XRegistrySyncStateCodec();
            XRegistrySyncState before = codec.Decode(await ReadStateAsync(fixture.Store).ConfigureAwait(false));
            string id = before.Intents.Keys.Single();
            Assert.That(await fixture.State.CompactAsync(before.Generation, [id]).ConfigureAwait(false), Is.EqualTo(1));
            XRegistrySyncState after = codec.Decode(await ReadStateAsync(fixture.Store).ConfigureAwait(false));
            fixture.ResetCounts();
            XRegistrySyncReport restarted = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(after.Intents, Is.Empty);
                Assert.That(after.Baselines.Keys, Is.EquivalentTo(before.Baselines.Keys));
                Assert.That(after.Sequence, Is.EqualTo(before.Sequence));
                Assert.That(after.Generation, Is.GreaterThan(before.Generation));
                Assert.That(restarted.Status, Is.EqualTo(XRegistrySyncStatus.Succeeded), Details(restarted));
                Assert.That(fixture.Http.Mutations, Is.Empty);
                Assert.That(fixture.Native.Mutations, Is.Empty);
            });
        }

        [Test]
        public async Task ValidatedRestorePreservesPendingEvidenceAndCannotOverwriteAJobAsync()
        {
            await using var fixture = new XRegistrySyncFixture();
            await fixture.Native.ChangeAsync(Group, "{}").ConfigureAwait(false);
            fixture.Http.FailAfterMutation = true;
            _ = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            ByteString backup = await fixture.State.ExportSnapshotAsync().ConfigureAwait(false);
            await using var recoveredStore = new MemoryXRegistrySyncStateStore();
            var manager = new XRegistrySyncStateManager(recoveredStore, fixture.Options.JobId, fixture.Clock);
            long generation = await manager.RestoreIntoPristineAsync(backup).ConfigureAwait(false);
            XRegistrySyncStateStatus status = await manager.ReadStatusAsync().ConfigureAwait(false);
            ByteString before = await ReadStateAsync(recoveredStore).ConfigureAwait(false);
            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await manager.RestoreIntoPristineAsync(backup).ConfigureAwait(false));
            Assert.Multiple(() =>
            {
                Assert.That(status.Pending, Is.EqualTo(1));
                Assert.That(status.Generation, Is.EqualTo(generation));
            });
            Assert.That(await ReadStateAsync(recoveredStore).ConfigureAwait(false), Is.EqualTo(before));
            fixture.ResetCounts();
            XRegistrySyncReport recovered = await fixture.Engine(recoveredStore).RunOnceAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(recovered.Pending, Is.EqualTo(1), Details(recovered));
                Assert.That(fixture.Http.Mutations, Is.Empty);
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task PendingOrStaleCompactionLeavesEveryRetainedByteUnchangedAsync(bool stale)
        {
            await using var fixture = new XRegistrySyncFixture();
            await fixture.Native.ChangeAsync(Group, "{}").ConfigureAwait(false);
            fixture.Http.FailAfterMutation = !stale;
            _ = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            ByteString before = await ReadStateAsync(fixture.Store).ConfigureAwait(false);
            XRegistrySyncState state = new XRegistrySyncStateCodec().Decode(before);
            string id = state.Intents.Keys.Single();
            Assert.ThrowsAsync<InvalidOperationException>(async () => await fixture.State.CompactAsync(
                stale ? state.Generation - 1 : state.Generation, [id]).ConfigureAwait(false));
            Assert.That(await ReadStateAsync(fixture.Store).ConfigureAwait(false), Is.EqualTo(before));
        }
    }
}
