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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Server;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    public sealed partial class WotAtomicPublicationNativeTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public Task WarmRecoveryWaitsForTheDecidingStoreAndRestoresItsActualOutcome(bool committed)
        {
            return VerifyWarmRecoveryAsync(committed, false);
        }

        [TestCase(false)]
        [TestCase(true)]
        public Task ResolvingTheStoreDoesNotAdmitMutationBeforeRuntimeRecovery(bool committed)
        {
            return VerifyWarmRecoveryAsync(committed, true);
        }

        [TestCase(false)]
        [TestCase(true)]
        public Task RefreshRecoversItsOwnInterruptedPublicationBeforeAdmittingTheRetry(bool committed)
        {
            return VerifyWarmRecoveryAsync(committed, false, retryRefresh: true);
        }

        [Test]
        public Task CancellationAfterAuthoritativeValidationCannotAbandonRecoveredRuntime()
        {
            return VerifyWarmRecoveryAsync(true, false, cancelAfterValidation: true);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task WarmRecoveryResolvesTheFirstPublicationWithoutInventingAPriorImage(bool committed)
        {
            using var views = new LifecycleWotViewProjectionHost(m_server.NodeManagerLifecycle);
            await ConfigureStockViewsAsync(views).ConfigureAwait(false);
            WotResource source = await UpsertStockSourceAsync(false).ConfigureAwait(false);
            await AddStockViewAsync("child", false).ConfigureAwait(false);
            m_indeterminateDecision = true;
            ArrayOf<NodeManagerRegistration> before = m_server.NodeManagerLifecycle.Registrations;

            await Assert.ThatAsync(async () => await m_coordinator.RefreshAsync(HandoffRequest("first-unknown"))
                .ConfigureAwait(false), Throws.TypeOf<WotRegistryCommitIndeterminateException>()).ConfigureAwait(false);

            await Assert.ThatAsync(async () => await m_coordinator.RecoverAsync().ConfigureAwait(false),
                Throws.TypeOf<InvalidDataException>()).ConfigureAwait(false);
            string directory = Path.Combine(m_root, "registry");
            string decision = Directory.GetFiles(directory,
                committed ? "manifest.json.tmp-*" : "manifest.json.replace-backup-*").Single();
            File.Move(decision, Path.Combine(directory, "manifest.json"));
            m_indeterminateDecision = false;
            m_events.Clear();

            Assert.That(await m_coordinator.RecoverAsync().ConfigureAwait(false), Is.EqualTo(committed));

            Assert.That(m_coordinator.Generation, Is.EqualTo(committed ? 1u : 0u));
            Assert.That(m_events, Is.Empty);
            await m_registry.SetEnabledAsync(source.GroupId, source.ResourceId, true).ConfigureAwait(false);
            if (!committed)
            {
                Assert.That(m_server.NodeManagerLifecycle.Registrations, Is.EqualTo(before));
                Assert.That((await ReadNodeClassAsync(StockNode("Source")).ConfigureAwait(false)).StatusCode,
                    Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            }
            WotRefreshResult next = await m_coordinator.RefreshAsync(HandoffRequest("first-next"))
                .ConfigureAwait(false);
            Assert.That(next.NewGeneration, Is.EqualTo(1u));
            Assert.That(await ReadStockValueAsync("Reading").ConfigureAwait(false), Is.EqualTo(42));
            await AssertStockMembershipAsync("child", 1, ["Reading"]).ConfigureAwait(false);
        }

        private async Task VerifyWarmRecoveryAsync(
            bool committed, bool resolveSeparately, bool retryRefresh = false, bool cancelAfterValidation = false)
        {
            using var views = new LifecycleWotViewProjectionHost(m_server.NodeManagerLifecycle);
            HandoffProbe probe = await ConfigureStockViewsAsync(views).ConfigureAwait(false);
            WotResource source = await UpsertStockSourceAsync(false).ConfigureAwait(false);
            await AddStockViewAsync("child", false).ConfigureAwait(false);
            await m_coordinator.RefreshAsync(HandoffRequest("warm-initial")).ConfigureAwait(false);
            await UpsertStockSourceAsync(true).ConfigureAwait(false);
            WotRegistrySnapshot before = m_registry.Current;
            ArrayOf<NodeManagerRegistration> oldOwners = m_server.NodeManagerLifecycle.Registrations;
            m_indeterminateDecision = true;
            m_events.Clear();

            await Assert.ThatAsync(async () => await m_coordinator.RefreshAsync(
                HandoffRequest("warm-unknown", 1)).ConfigureAwait(false),
                Throws.TypeOf<WotRegistryCommitIndeterminateException>()).ConfigureAwait(false);

            Assert.That(m_registry.Current, Is.SameAs(before));
            Assert.That(m_server.NodeManagerLifecycle.Registrations, Is.EqualTo(oldOwners));
            await Assert.ThatAsync(async () => await m_coordinator.RecoverAsync().ConfigureAwait(false),
                Throws.TypeOf<InvalidDataException>()).ConfigureAwait(false);
            Assert.That(m_registry.Current, Is.SameAs(before));
            Assert.That(m_coordinator.Generation, Is.EqualTo(1u));
            Assert.That(await ReadStockValueAsync("Reading").ConfigureAwait(false), Is.EqualTo(42));
            await Assert.ThatAsync(async () => await m_registry.SetEnabledAsync(
                source.GroupId, source.ResourceId, false).ConfigureAwait(false),
                Throws.TypeOf<InvalidOperationException>().With.Message.Contains("reload")).ConfigureAwait(false);
            if (retryRefresh)
            {
                await Assert.ThatAsync(async () => await m_coordinator.RefreshAsync(HandoffRequest("still-unknown"))
                    .ConfigureAwait(false),
                    Throws.TypeOf<InvalidOperationException>().With.Message.Contains("reload")).ConfigureAwait(false);
            }

            string directory = Path.Combine(m_root, "registry");
            string[] decisions = Directory.GetFiles(directory,
                committed ? "manifest.json.tmp-*" : "manifest.json.replace-backup-*");
            Assert.That(decisions, Has.Length.EqualTo(1));
            string manifest = Path.Combine(directory, "manifest.json");
            Assert.That(File.Exists(manifest), Is.False);
            File.Move(decisions.Single(), manifest);
            m_indeterminateDecision = false;
            using var authoritativeStore = new FileWotRegistryStore(directory);
            WotRegistrySnapshot decided = await authoritativeStore.LoadAsync().ConfigureAwait(false);
            uint expectedGeneration = committed ? 2u : 1u;
            Assert.That(decided.RefreshGeneration, Is.EqualTo(expectedGeneration));
            if (retryRefresh)
            {
                WotRefreshResult retry = await m_coordinator.RefreshAsync(HandoffRequest("automatic-recovery"))
                    .ConfigureAwait(false);
                Assert.That(retry.NewGeneration, Is.EqualTo(2u));
                Assert.That(m_registry.Current.FindResourceByXid(source.Xid)!.ActiveVersionId, Is.EqualTo("v2"));
                Assert.That(await ReadStockValueAsync("Reading").ConfigureAwait(false), Is.EqualTo(84));
                await AssertStockMembershipAsync("child", 2, ["Reading", "Other"]).ConfigureAwait(false);
                WotRegistrySnapshot actual = await authoritativeStore.LoadAsync().ConfigureAwait(false);
                Assert.That(actual.Generation, Is.EqualTo(decided.Generation + 1));
                WotResourceVersion active = actual.FindResourceByXid(source.Xid)!.FindVersion("v2")!;
                Assert.That(active.LastDependencyAttempt!.RequestId, Is.EqualTo("automatic-recovery"));
                Assert.That(active.DependencySnapshot!.RequestId,
                    Is.EqualTo(committed ? "warm-unknown" : "automatic-recovery"));
                Assert.That(m_events.Count(change => change.Kind == WotMaterializationEventKind.Resource),
                    Is.EqualTo(committed ? 0 : 2));
                return;
            }
            if (resolveSeparately)
            {
                Assert.That(await m_registry.ResolveRecoveryAsync().ConfigureAwait(false), Is.True);
                await Assert.ThatAsync(async () => await m_registry.SetEnabledAsync(
                    source.GroupId, source.ResourceId, false).ConfigureAwait(false),
                    Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
                using var cancelled = new CancellationTokenSource();
                cancelled.Cancel();
                await Assert.ThatAsync(async () => await m_coordinator.RecoverAsync(cancelled.Token)
                    .ConfigureAwait(false), Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
                await Assert.ThatAsync(async () => await m_registry.SetEnabledAsync(
                    source.GroupId, source.ResourceId, false).ConfigureAwait(false),
                    Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
                Assert.That(await ReadStockValueAsync("Reading").ConfigureAwait(false), Is.EqualTo(42));
                Assert.That(m_server.NodeManagerLifecycle.Registrations, Is.EqualTo(oldOwners));
            }

            using var recoveryCancellation = new CancellationTokenSource();
            if (cancelAfterValidation)
            {
                probe.AfterDecisionAsync = _ =>
                {
                    recoveryCancellation.Cancel();
                    return default;
                };
            }
            Assert.That(await m_coordinator.RecoverAsync(recoveryCancellation.Token).ConfigureAwait(false), Is.True);
            Assert.That(recoveryCancellation.IsCancellationRequested, Is.EqualTo(cancelAfterValidation));
            probe.AfterDecisionAsync = null;

            Assert.That(m_registry.Current.RefreshGeneration, Is.EqualTo(expectedGeneration));
            Assert.That(m_coordinator.Generation, Is.EqualTo(expectedGeneration));
            Assert.That(m_registry.Current.FindResourceByXid(source.Xid)!.ActiveVersionId,
                Is.EqualTo(committed ? "v2" : "v1"));
            Assert.That(await ReadStockValueAsync("Reading").ConfigureAwait(false), Is.EqualTo(committed ? 84 : 42));
            await AssertStockMembershipAsync("child", expectedGeneration,
                committed ? ["Reading", "Other"] : ["Reading"]).ConfigureAwait(false);
            Assert.That(m_events, Is.Empty);
            WotRegistrySnapshot durable = await authoritativeStore.LoadAsync().ConfigureAwait(false);
            Assert.That(durable.Generation, Is.EqualTo(decided.Generation));
            Assert.That(durable.CanonicalViewGraphState, Is.EqualTo(decided.CanonicalViewGraphState));
            WotRegistrySnapshot recovered = m_registry.Current;
            ArrayOf<NodeManagerRegistration> recoveredOwners = m_server.NodeManagerLifecycle.Registrations;
            Assert.That(await m_coordinator.RecoverAsync().ConfigureAwait(false), Is.True);
            Assert.That(m_registry.Current, Is.SameAs(recovered));
            Assert.That(m_server.NodeManagerLifecycle.Registrations, Is.EqualTo(recoveredOwners));
            if (!committed)
            {
                Assert.That(recoveredOwners, Is.EqualTo(oldOwners));
            }
            WotRefreshResult next = await m_coordinator.RefreshAsync(
                HandoffRequest("warm-next", expectedGeneration)).ConfigureAwait(false);
            Assert.That(next.NewGeneration, Is.EqualTo(2u));
            Assert.That(await ReadStockValueAsync("Reading").ConfigureAwait(false), Is.EqualTo(84));
        }
    }
}
