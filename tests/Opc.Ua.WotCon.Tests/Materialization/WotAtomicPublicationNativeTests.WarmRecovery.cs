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
using Moq;
using NUnit.Framework;
using Opc.Ua.Server;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;
using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    public sealed partial class WotAtomicPublicationNativeTests
    {
        [Test]
        public async Task NativeReadKeepsOneRecoveredGenerationWhileProjectionAcknowledgmentIsPending()
        {
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var resume = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            bool block = false;
            var projection = new Mock<IWotRegistryRecoveryProjection>(MockBehavior.Strict);
            projection.Setup(value => value.SynchronizeAsync(
                It.IsAny<WotRegistrySnapshot>(), It.IsAny<CancellationToken>()))
                .Returns(async (WotRegistrySnapshot _, CancellationToken _) =>
                {
                    if (block)
                    {
                        entered.TrySetResult(true);
                        await resume.Task.ConfigureAwait(false);
                    }
                });
            using IDisposable registration = m_registry.RegisterRecoveryProjection(projection.Object);
            using var views = new LifecycleWotViewProjectionHost(m_server.NodeManagerLifecycle);
            await ConfigureStockViewsAsync(views).ConfigureAwait(false);
            WotResource resource = await UpsertStockSourceAsync(false).ConfigureAwait(false);
            await AddStockViewAsync("child", false).ConfigureAwait(false);
            await m_coordinator.RefreshAsync(HandoffRequest("coherent-read-initial")).ConfigureAwait(false);
            await UpsertStockSourceAsync(true).ConfigureAwait(false);
            await AwaitStockRegistryProjectionAsync().ConfigureAwait(false);
            var properties = (await BrowseStockAsync(ResourceId(resource), Ua.ReferenceTypeIds.HasProperty)
                .ConfigureAwait(false)).ToDictionary(reference => reference.BrowseName.Name ??
                    throw new InvalidOperationException("A Resource Property has no BrowseName."),
                    reference => ExpandedNodeId.ToNodeId(reference.NodeId, m_server.CurrentInstance.NamespaceUris));
            m_indeterminateDecision = true;
            await Assert.ThatAsync(async () => await m_coordinator.RefreshAsync(
                HandoffRequest("coherent-read-unknown", 1)).ConfigureAwait(false),
                Throws.TypeOf<WotRegistryCommitIndeterminateException>()).ConfigureAwait(false);
            string directory = Path.Combine(m_root, "registry");
            File.Move(Directory.GetFiles(directory, "manifest.json.tmp-*").Single(),
                Path.Combine(directory, "manifest.json"));
            m_indeterminateDecision = false;
            block = true;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            Task<bool> recovering = m_coordinator.RecoverAsync(timeout.Token).AsTask();
            try
            {
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(recovering.IsCompleted, Is.False);
                ReadResponse read = await m_session.ReadAsync(null, 0, TimestampsToReturn.Neither,
                    [
                        new ReadValueId { NodeId = StockNode("Source/Reading"), AttributeId = Attributes.Value },
                        new ReadValueId { NodeId = properties["ActiveVersionId"], AttributeId = Attributes.Value },
                        new ReadValueId { NodeId = properties["RefreshGeneration"], AttributeId = Attributes.Value },
                        new ReadValueId { NodeId = properties["MaterializedNodeCount"], AttributeId = Attributes.Value }
                    ], timeout.Token).ConfigureAwait(false);
                Assert.That(read.Results.ToList().All(value => value.StatusCode == StatusCodes.Good), Is.True);
                Assert.That(read.Results[0].WrappedValue.TryGetValue(out int value), Is.True);
                Assert.That(value, Is.EqualTo(84));
                Assert.That(read.Results[1].WrappedValue.TryGetValue(out string? activeVersion), Is.True);
                Assert.That(activeVersion, Is.EqualTo("v2"),
                    "A single Read must not mix the recovered source with pre-recovery Resource metadata.");
                Assert.That(read.Results[2].WrappedValue.TryGetValue(out uint generation), Is.True);
                Assert.That(generation, Is.EqualTo(2u));
                Assert.That(read.Results[3].WrappedValue.TryGetValue(out uint count), Is.True);
                Assert.That(count, Is.EqualTo((uint)m_registry.Current
                    .FindResourceByXid(resource.Xid)!.MaterializedNodeCount));
            }
            finally
            {
                resume.TrySetResult(true);
            }
            Assert.That(await recovering.ConfigureAwait(false), Is.True);
        }

        [Test]
        public async Task NativeStaleRefreshStillCompletesRecoveredMetadataHandoff()
        {
            using var views = new LifecycleWotViewProjectionHost(m_server.NodeManagerLifecycle);
            await ConfigureStockViewsAsync(views, allowNativeRefresh: true).ConfigureAwait(false);
            WotResource resource = await UpsertStockSourceAsync(false).ConfigureAwait(false);
            WotResource child = await AddStockViewAsync("child", false).ConfigureAwait(false);
            await m_coordinator.RefreshAsync(HandoffRequest("native-handoff-initial")).ConfigureAwait(false);
            await UpsertStockSourceAsync(true).ConfigureAwait(false);
            await AwaitStockRegistryProjectionAsync().ConfigureAwait(false);
            m_indeterminateDecision = true;
            await Assert.ThatAsync(async () => await m_coordinator.RefreshAsync(
                HandoffRequest("native-handoff-unknown", 1)).ConfigureAwait(false),
                Throws.TypeOf<WotRegistryCommitIndeterminateException>()).ConfigureAwait(false);
            string directory = Path.Combine(m_root, "registry");
            File.Move(Directory.GetFiles(directory, "manifest.json.tmp-*").Single(),
                Path.Combine(directory, "manifest.json"));
            m_indeterminateDecision = false;
            using var observer = new FileWotRegistryStore(directory);
            WotRegistrySnapshot decided = await observer.LoadAsync().ConfigureAwait(false);
            Assert.That(decided.RefreshGeneration, Is.EqualTo(2u));
            m_events.Clear();
            await m_session.FetchNamespaceTablesAsync().ConfigureAwait(false);
            NamespaceTable namespaces = m_server.CurrentInstance.NamespaceUris;
            NodeId registryId = ExpandedNodeId.ToNodeId(ObjectIds.WoTRegistry, namespaces);
            Assert.That(registryId.IsNull, Is.False);
            NodeId refreshId = ExpandedNodeId.ToNodeId((await BrowseStockAsync(
                registryId, Ua.ReferenceTypeIds.HasComponent).ConfigureAwait(false))
                .Single(reference => reference.BrowseName.Name == "Refresh").NodeId, namespaces);

            CallResponse response = await m_session.CallAsync(null,
                [new CallMethodRequest
                {
                    ObjectId = registryId,
                    MethodId = refreshId,
                    InputArguments =
                    [
                        new Variant(ArrayOf<ExtensionObject>.Empty),
                        Variant.FromStructure(new WoTRefreshOptionsDataType { Atomicity = WoTAtomicityEnum.PerRegistry }),
                        new Variant(1u),
                        new Variant("native-stale-after-recovery")
                    ]
                }], CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results[0].StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(m_coordinator.Generation, Is.EqualTo(2u));
            Assert.That(await ReadStockValueAsync("Reading").ConfigureAwait(false), Is.EqualTo(84));
            foreach (WotResource current in new[] { resource, child })
            {
                WotResource expected = decided.FindResourceByXid(current.Xid)!;
                var properties = (await BrowseStockAsync(ResourceId(current), Ua.ReferenceTypeIds.HasProperty)
                    .ConfigureAwait(false)).ToDictionary(reference => reference.BrowseName.Name ??
                        throw new InvalidOperationException("A Resource Property has no BrowseName."),
                        reference => ExpandedNodeId.ToNodeId(reference.NodeId, namespaces));
                ReadResponse metadata = await m_session.ReadAsync(null, 0, TimestampsToReturn.Neither,
                    [
                        new ReadValueId { NodeId = properties["ActiveVersionId"], AttributeId = Attributes.Value },
                        new ReadValueId { NodeId = properties["RefreshGeneration"], AttributeId = Attributes.Value },
                        new ReadValueId { NodeId = properties["MaterializedNodeCount"], AttributeId = Attributes.Value },
                        new ReadValueId { NodeId = properties["RootNodeId"], AttributeId = Attributes.Value }
                    ], CancellationToken.None).ConfigureAwait(false);
                Assert.That(metadata.Results.ToList().All(value => value.StatusCode == StatusCodes.Good), Is.True);
                Assert.That(metadata.Results[0].WrappedValue.TryGetValue(out string? activeVersion), Is.True);
                Assert.That(activeVersion, Is.EqualTo(expected.ActiveVersionId));
                Assert.That(metadata.Results[1].WrappedValue.TryGetValue(out uint generation), Is.True);
                Assert.That(generation, Is.EqualTo(expected.RefreshGeneration));
                Assert.That(metadata.Results[2].WrappedValue.TryGetValue(out uint count), Is.True);
                Assert.That(count, Is.EqualTo((uint)expected.MaterializedNodeCount));
                Assert.That(metadata.Results[3].WrappedValue.TryGetValue(out NodeId root), Is.True);
                Assert.That(root, Is.EqualTo(m_registry.Current.FindResourceByXid(current.Xid)!.RootNodeId));
            }
            Assert.That(m_events, Is.Empty);
            Assert.That((await observer.LoadAsync().ConfigureAwait(false)).Generation, Is.EqualTo(decided.Generation));
        }

        [Test]
        public async Task UnchangedRefreshRecoversItsCommittedEvidenceWarningBeforeRetry()
        {
            bool arm = false;
            bool failCapture = false;
            int failedCaptures = 0;
            var store = new Mock<IWotRegistryRecoveryStore>(MockBehavior.Strict);
            store.SetupGet(owner => owner.SupportsPreparedCommits).Returns(() => m_store.SupportsPreparedCommits);
            store.Setup(owner => owner.LoadAsync(It.IsAny<CancellationToken>()))
                .Returns((CancellationToken token) => m_store.LoadAsync(token));
            store.Setup(owner => owner.CommitAsync(It.IsAny<WotRegistrySnapshot>(), It.IsAny<CancellationToken>()))
                .Returns((WotRegistrySnapshot snapshot, CancellationToken token) => m_store.CommitAsync(snapshot, token));
            store.Setup(owner => owner.CaptureValidatedGenerationAsync(It.IsAny<CancellationToken>()))
                .Returns((CancellationToken token) =>
                {
                    if (failCapture)
                    {
                        failCapture = false;
                        failedCaptures++;
                        throw new IOException("Transient post-commit evidence acquisition failure.");
                    }
                    return m_store.CaptureValidatedGenerationAsync(token);
                });
            store.Setup(owner => owner.ValidatePublicationAsync(
                It.IsAny<IWotRegistryValidatedGeneration>(), It.IsAny<CancellationToken>()))
                .Returns((IWotRegistryValidatedGeneration generation, CancellationToken token) =>
                    m_store.ValidatePublicationAsync(generation, token));
            store.Setup(owner => owner.PrepareCommitAsync(
                It.IsAny<WotRegistrySnapshot>(), It.IsAny<IWotRegistryValidatedGeneration>(),
                It.IsAny<WotRegistryCommitScope>(), It.IsAny<CancellationToken>()))
                .Returns(async (WotRegistrySnapshot snapshot, IWotRegistryValidatedGeneration generation,
                    WotRegistryCommitScope scope, CancellationToken token) =>
                {
                    IWotRegistryPreparedCommit prepared = await m_store.PrepareCommitAsync(
                        snapshot, generation, scope, token).ConfigureAwait(false);
                    var tracked = new Mock<IWotRegistryPreparedCommit>(MockBehavior.Strict);
                    tracked.SetupGet(value => value.IntendedSnapshot).Returns(prepared.IntendedSnapshot);
                    tracked.Setup(value => value.CommitAsync(It.IsAny<CancellationToken>()))
                        .Returns(async (CancellationToken commitToken) =>
                        {
                            await prepared.CommitAsync(commitToken).ConfigureAwait(false);
                            if (arm)
                            {
                                arm = false;
                                failCapture = true;
                            }
                        });
                    tracked.Setup(value => value.DisposeAsync()).Returns(prepared.DisposeAsync);
                    return tracked.Object;
                });
            m_coordinator.Dispose();
            m_registry.Dispose();
            m_registry = new WotRegistryService(new EvidenceStoreAdapter(store.Object, m_store.ResourceStore));
            await m_registry.InitializeAsync().ConfigureAwait(false);
            using var views = new LifecycleWotViewProjectionHost(m_server.NodeManagerLifecycle);
            HandoffProbe probe = await ConfigureStockViewsAsync(views).ConfigureAwait(false);
            WotResource resource = await UpsertStockSourceAsync(false).ConfigureAwait(false);
            await AddStockViewAsync("child", false).ConfigureAwait(false);
            await m_coordinator.RefreshAsync(HandoffRequest("metadata-initial")).ConfigureAwait(false);
            ArrayOf<NodeManagerRegistration> owners = m_server.NodeManagerLifecycle.Registrations;
            long before = m_registry.Current.Generation;
            m_events.Clear();
            arm = true;

            WotRefreshResult warning = await m_coordinator.RefreshAsync(HandoffRequest("metadata-warning", 1))
                .ConfigureAwait(false);

            Assert.That(warning.NewGeneration, Is.EqualTo(1u));
            Assert.That(warning.Summary.Outcome, Is.EqualTo(WoTOutcomeEnum.Warning));
            Assert.That(failedCaptures, Is.EqualTo(1));
            Assert.That(m_registry.Current.Generation, Is.EqualTo(before + 1));
            Assert.That(m_registry.Current.FindResourceByXid(resource.Xid)!.FindVersion("v1")!
                .LastDependencyAttempt!.RequestId, Is.EqualTo("metadata-warning"));
            await Assert.ThatAsync(async () => await m_registry.BeginPublicationAsync().ConfigureAwait(false),
                Throws.TypeOf<InvalidOperationException>().With.Message.Contains("reload")).ConfigureAwait(false);

            WotRefreshResult retry = await m_coordinator.RefreshAsync(HandoffRequest("metadata-retry", 1))
                .ConfigureAwait(false);

            Assert.That(retry.Summary.Outcome, Is.EqualTo(WoTOutcomeEnum.Unchanged));
            Assert.That(retry.NewGeneration, Is.EqualTo(1u));
            Assert.That(m_registry.Current.Generation, Is.EqualTo(before + 2));
            Assert.That(m_server.NodeManagerLifecycle.Registrations, Is.EqualTo(owners));
            Assert.That(probe.PublicationCount, Is.EqualTo(1));
            Assert.That(m_events.Any(change => change.Kind == WotMaterializationEventKind.Resource), Is.False);
            WotResourceVersion version = m_registry.Current.FindResourceByXid(resource.Xid)!.FindVersion("v1")!;
            Assert.That(version.LastDependencyAttempt!.RequestId, Is.EqualTo("metadata-retry"));
            Assert.That(version.DependencySnapshot!.RequestId, Is.EqualTo("metadata-initial"));
            Assert.That(await ReadStockValueAsync("Reading").ConfigureAwait(false), Is.EqualTo(42));
            await AssertStockMembershipAsync("child", 1, ["Reading"]).ConfigureAwait(false);
        }

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

        [Test]
        public Task FailedProjectionCompletionRetainsRecoveredOwnersAndCanRetry()
        {
            return VerifyWarmRecoveryAsync(true, false, failProjectionCompletion: true);
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

        private sealed class EvidenceStoreAdapter(
            IWotRegistryRecoveryStore inner, IXRegistryResourceStore resourceStore)
            : IWotRegistryRecoveryStore, IWotRegistryResourceStoreProvider
        {
            public IXRegistryResourceStore ResourceStore => resourceStore;
            public bool SupportsPreparedCommits => inner.SupportsPreparedCommits;

            public ValueTask<WotRegistrySnapshot> LoadAsync(CancellationToken cancellationToken = default)
            {
                return inner.LoadAsync(cancellationToken);
            }

            public ValueTask CommitAsync(
                WotRegistrySnapshot snapshot, CancellationToken cancellationToken = default)
            {
                return inner.CommitAsync(snapshot, cancellationToken);
            }

            public ValueTask<IWotRegistryValidatedGeneration> CaptureValidatedGenerationAsync(
                CancellationToken cancellationToken = default)
            {
                return inner.CaptureValidatedGenerationAsync(cancellationToken);
            }

            public ValueTask<IWotRegistryPreparedCommit> PrepareCommitAsync(
                WotRegistrySnapshot intendedSnapshot, IWotRegistryValidatedGeneration expectedGeneration,
                WotRegistryCommitScope scope, CancellationToken cancellationToken = default)
            {
                return inner.PrepareCommitAsync(intendedSnapshot, expectedGeneration, scope, cancellationToken);
            }

            public ValueTask<IWotRegistryPublicationValidation> ValidatePublicationAsync(
                IWotRegistryValidatedGeneration expectedGeneration, CancellationToken cancellationToken = default)
            {
                return inner.ValidatePublicationAsync(expectedGeneration, cancellationToken);
            }
        }

        private async Task VerifyWarmRecoveryAsync(
            bool committed, bool resolveSeparately, bool retryRefresh = false, bool cancelAfterValidation = false,
            bool failProjectionCompletion = false)
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
            ArrayOf<NodeManagerRegistration> ownersAfterFailedCompletion = default;
            var projection = new Mock<IWotRegistryRecoveryProjection>(MockBehavior.Strict);
            projection.Setup(value => value.SynchronizeAsync(
                It.IsAny<WotRegistrySnapshot>(), It.IsAny<CancellationToken>()))
                .Returns(() => throw new IOException("Recovery projection completion failed."));
            using IDisposable? projectionFailure = failProjectionCompletion
                ? m_registry.RegisterRecoveryProjection(projection.Object) : null;
            if (projectionFailure is not null)
            {
                await Assert.ThatAsync(async () => await m_coordinator.RecoverAsync().ConfigureAwait(false),
                    Throws.TypeOf<WotRegistryCommitDurabilityUncertainException>()).ConfigureAwait(false);
                ownersAfterFailedCompletion = m_server.NodeManagerLifecycle.Registrations;
                Assert.That(m_registry.Current.RefreshGeneration, Is.EqualTo(expectedGeneration));
                Assert.That(await ReadStockValueAsync("Reading").ConfigureAwait(false), Is.EqualTo(84));
                await Assert.ThatAsync(async () => await m_registry.SetEnabledAsync(
                    source.GroupId, source.ResourceId, false).ConfigureAwait(false),
                    Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
                projectionFailure.Dispose();
            }
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
            if (!ownersAfterFailedCompletion.IsNull)
            {
                Assert.That(m_server.NodeManagerLifecycle.Registrations, Is.EqualTo(ownersAfterFailedCompletion));
                projection.Verify(value => value.SynchronizeAsync(
                    It.IsAny<WotRegistrySnapshot>(), It.IsAny<CancellationToken>()), Times.Once);
            }

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
