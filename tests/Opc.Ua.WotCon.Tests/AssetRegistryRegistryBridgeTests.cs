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
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server;
using Opc.Ua.WotCon.Server;
using Opc.Ua.WotCon.Server.Assets;
using Opc.Ua.WotCon.Server.Registry;
using Opc.Ua.WotCon.Server.ThingDescriptions;
using Opc.Ua.WotCon.Tests.Providers;

namespace Opc.Ua.WotCon.Tests
{
    /// <summary>
    /// Verifies the optional WoT Connectivity asset-to-xRegistry bridge.
    /// </summary>
    [TestFixture]
    [Category("WotCon")]
    public sealed class AssetRegistryRegistryBridgeTests
    {
        [SetUp]
        public void SetUp()
        {
            m_tempFolder = Path.Combine(
                Path.GetTempPath(),
                "wotcon-registry-bridge-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_tempFolder);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(m_tempFolder))
            {
                try
                {
                    Directory.Delete(m_tempFolder, recursive: true);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        [Test]
        public async Task RebuildMirrorsCreatedThingDescriptionToRegistry()
        {
            using var registry = new WotRegistryService();
            var requests = new List<WotUpsertResourceRequest>();
            Mock<IWotRegistryService> bridge = CreateRecordingBridge(registry, requests);
            using var harness = new ManagerHarness(m_tempFolder, bridge.Object);
            await harness.StartAsync().ConfigureAwait(false);
            AssetEntry entry = await CreateAssetEntryAsync(harness, "asset-001").ConfigureAwait(false);
            ThingDescription td = CreateThingDescription("asset-001", "sim://opcua.test/wot/asset-001");

            ServiceResult status = await harness.Registry
                .RebuildAsync(entry, td, persistOnSuccess: true, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(status), Is.True);
            Assert.That(requests, Has.Count.EqualTo(1));
            AssertUpsertRequest(requests[0], WotRegistryGroups.ThingDescriptions, "asset-001", td);
        }

        [Test]
        public async Task RebuildMirrorsUpdatedThingDescriptionToRegistry()
        {
            using var registry = new WotRegistryService();
            var requests = new List<WotUpsertResourceRequest>();
            Mock<IWotRegistryService> bridge = CreateRecordingBridge(registry, requests);
            using var harness = new ManagerHarness(m_tempFolder, bridge.Object);
            await harness.StartAsync().ConfigureAwait(false);
            AssetEntry entry = await CreateAssetEntryAsync(harness, "asset-001").ConfigureAwait(false);
            ThingDescription original = CreateThingDescription("asset-001", "sim://opcua.test/wot/asset-001");
            ThingDescription updated = CreateThingDescription("asset-001", "sim://opcua.test/wot/asset-001-updated");

            await harness.Registry.RebuildAsync(
                entry,
                original,
                persistOnSuccess: true,
                CancellationToken.None).ConfigureAwait(false);
            ServiceResult status = await harness.Registry.RebuildAsync(
                entry,
                updated,
                persistOnSuccess: true,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(status), Is.True);
            Assert.That(requests, Has.Count.EqualTo(2));
            AssertUpsertRequest(requests[1], WotRegistryGroups.ThingDescriptions, "asset-001", updated);
        }

        [Test]
        public async Task DeleteRemovesMirroredThingDescriptionFromRegistry()
        {
            using var registry = new WotRegistryService();
            var requests = new List<WotUpsertResourceRequest>();
            var deletes = new List<(string GroupId, string ResourceId)>();
            Mock<IWotRegistryService> bridge = CreateRecordingBridge(registry, requests, deletes);
            using var harness = new ManagerHarness(m_tempFolder, bridge.Object);
            await harness.StartAsync().ConfigureAwait(false);
            AssetEntry entry = await CreateAssetEntryAsync(harness, "asset-001").ConfigureAwait(false);
            ThingDescription td = CreateThingDescription("asset-001", "sim://opcua.test/wot/asset-001");
            await harness.Registry.RebuildAsync(
                entry,
                td,
                persistOnSuccess: true,
                CancellationToken.None).ConfigureAwait(false);

            ServiceResult status = await harness.Registry
                .DeleteAssetAsync(entry.Asset.NodeId, CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(status), Is.True);
            Assert.That(deletes, Has.Count.EqualTo(1));
            Assert.That(deletes[0].GroupId, Is.EqualTo(WotRegistryGroups.ThingDescriptions));
            Assert.That(deletes[0].ResourceId, Is.EqualTo("asset-001"));
            Assert.That(registry.Current.FindResource(WotRegistryGroups.ThingDescriptions, "asset-001"), Is.Null);
        }

        [Test]
        public async Task RejectedBackingDeleteKeepsTheLegacyAssetAndReportsFailure()
        {
            await VerifyRefusedDeleteAsync(WoTOutcomeEnum.Rejected).ConfigureAwait(false);
        }

        [Test]
        public async Task FailedBackingDeleteKeepsTheLegacyAssetAndReportsFailure()
        {
            await VerifyRefusedDeleteAsync(WoTOutcomeEnum.Failed).ConfigureAwait(false);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task NoncommittedBackingFailurePreservesTheAssetAndPermitsRetry(bool explicitNoncommit)
        {
            var store = new ControlledRegistryStore();
            using var registry = new WotRegistryService(store);
            using var harness = new ManagerHarness(m_tempFolder, registry);
            await harness.StartAsync().ConfigureAwait(false);
            Mock<IWotAssetProvider> provider = TrackProvider(harness);
            AssetEntry entry = await CreatePopulatedAssetAsync(harness).ConfigureAwait(false);
            WotRegistrySnapshot previous = registry.Current;
            byte[] persisted = File.ReadAllBytes(Path.Combine(m_tempFolder, "Pump01.jsonld"));
            store.BeforeCommit = (snapshot, _) =>
            {
                if (explicitNoncommit)
                {
                    throw new WotRegistryCommitNotCommittedException(snapshot, new IOException("private-store-location"));
                }
                throw new IOException("private-store-location");
            };

            ServiceResult result = await harness.Registry.DeleteAssetAsync(entry.Asset.NodeId, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadResourceUnavailable));
            Assert.That(result.ToString(), Does.Not.Contain("private-store-location"));
            AssertRetainedAsset(harness, entry, provider, persisted);
            Assert.That(registry.Current, Is.SameAs(previous));
            Assert.That(store.Current, Is.SameAs(previous));

            store.BeforeCommit = null;
            ServiceResult retried = await harness.Registry.DeleteAssetAsync(entry.Asset.NodeId, CancellationToken.None)
                .ConfigureAwait(false);
            AssertDeletedAsset(harness, entry, provider, registry, retried);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CommittedBackingWarningFinishesDeletionAndReportsTheWarning(bool durabilityFailure)
        {
            var store = new ControlledRegistryStore();
            using var registry = new WotRegistryService(store);
            using var harness = new ManagerHarness(m_tempFolder, registry);
            await harness.StartAsync().ConfigureAwait(false);
            Mock<IWotAssetProvider> provider = TrackProvider(harness);
            AssetEntry entry = await CreatePopulatedAssetAsync(harness).ConfigureAwait(false);
            if (durabilityFailure)
            {
                store.AfterCommit = (snapshot, _) => throw new WotRegistryCommitDurabilityUncertainException(
                    snapshot, new IOException("private-store-location"));
            }
            else
            {
                registry.Changed += (_, _) => throw new InvalidOperationException("private-observer-detail");
            }

            ServiceResult result = await harness.Registry.DeleteAssetAsync(entry.Asset.NodeId, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.GoodResultsMayBeIncomplete));
            Assert.That(result.ToString(), Does.Not.Contain("private-"));
            AssertDeletedAsset(harness, entry, provider, registry, result);
            using var restarted = new WotRegistryService(store);
            await restarted.InitializeAsync().ConfigureAwait(false);
            Assert.That(restarted.Current.AllResources(), Is.Empty);
            using var restartedAssets = new ManagerHarness(m_tempFolder, restarted);
            await restartedAssets.StartAsync().ConfigureAwait(false);
            Assert.That(restartedAssets.Registry.AssetNames, Is.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CancellationAtTheBackingDecisionPreservesOrCompletesDeletion(bool committed)
        {
            var store = new ControlledRegistryStore();
            using var registry = new WotRegistryService(store);
            using var harness = new ManagerHarness(m_tempFolder, registry);
            await harness.StartAsync().ConfigureAwait(false);
            Mock<IWotAssetProvider> provider = TrackProvider(harness);
            AssetEntry entry = await CreatePopulatedAssetAsync(harness).ConfigureAwait(false);
            byte[] persisted = File.ReadAllBytes(Path.Combine(m_tempFolder, "Pump01.jsonld"));
            using var cancellation = new CancellationTokenSource();
            if (committed)
            {
                store.AfterCommit = (_, _) => cancellation.Cancel();
                ServiceResult result = await harness.Registry.DeleteAssetAsync(entry.Asset.NodeId, cancellation.Token)
                    .ConfigureAwait(false);
                AssertDeletedAsset(harness, entry, provider, registry, result);
            }
            else
            {
                store.BeforeCommit = (_, token) =>
                {
                    cancellation.Cancel();
                    token.ThrowIfCancellationRequested();
                };
                await Assert.ThatAsync(async () => await harness.Registry.DeleteAssetAsync(
                    entry.Asset.NodeId, cancellation.Token).ConfigureAwait(false),
                    Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
                AssertRetainedAsset(harness, entry, provider, persisted);
                Assert.That(registry.Current.AllResources().Count(), Is.EqualTo(1));
                store.BeforeCommit = null;
                ServiceResult retried = await harness.Registry.DeleteAssetAsync(entry.Asset.NodeId, CancellationToken.None)
                    .ConfigureAwait(false);
                AssertDeletedAsset(harness, entry, provider, registry, retried);
            }
            Assert.That(cancellation.IsCancellationRequested, Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task IndeterminateBackingDeleteWaitsForAuthoritativeRecovery(bool committed)
        {
            var store = new ControlledRegistryStore();
            using var registry = new WotRegistryService(store);
            using var harness = new ManagerHarness(m_tempFolder, registry);
            await harness.StartAsync().ConfigureAwait(false);
            Mock<IWotAssetProvider> provider = TrackProvider(harness);
            AssetEntry entry = await CreatePopulatedAssetAsync(harness).ConfigureAwait(false);
            WotRegistrySnapshot previous = registry.Current;
            byte[] persisted = File.ReadAllBytes(Path.Combine(m_tempFolder, "Pump01.jsonld"));
            static void Fail(WotRegistrySnapshot snapshot, CancellationToken _)
            {
                throw new WotRegistryCommitIndeterminateException(snapshot,
                    new IOException("private-store-location"), new IOException("private-validation-detail"));
            }
            if (committed)
            {
                store.AfterCommit = Fail;
            }
            else
            {
                store.BeforeCommit = Fail;
            }

            ServiceResult result = await harness.Registry.DeleteAssetAsync(entry.Asset.NodeId, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(result.ToString(), Does.Not.Contain("private-"));
            AssertRetainedAsset(harness, entry, provider, persisted);
            Assert.That(registry.Current, Is.SameAs(previous));
            ServiceResult replacement = await harness.Registry.RebuildAsync(
                entry, CreateThingDescription("Pump01", "sim://opcua.test/replacement"),
                persistOnSuccess: true, CancellationToken.None).ConfigureAwait(false);
            Assert.That(replacement.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            AssertRetainedAsset(harness, entry, provider, persisted);
            int commits = store.CommitCount;
            ServiceResult blocked = await harness.Registry.DeleteAssetAsync(entry.Asset.NodeId, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(ServiceResult.IsBad(blocked), Is.True);
            Assert.That(store.CommitCount, Is.EqualTo(commits));
            AssertRetainedAsset(harness, entry, provider, persisted);

            store.BeforeCommit = null;
            store.AfterCommit = null;
            await registry.InitializeAsync().ConfigureAwait(false);
            ServiceResult retried = await harness.Registry.DeleteAssetAsync(entry.Asset.NodeId, CancellationToken.None)
                .ConfigureAwait(false);
            AssertDeletedAsset(harness, entry, provider, registry, retried);
            Assert.That(store.CommitCount, Is.EqualTo(commits + (committed ? 0 : 1)));
        }

        [Test]
        public async Task ACommittedWarningResultDoesNotBecomePlainSuccess()
        {
            using var registry = new WotRegistryService();
            var requests = new List<WotUpsertResourceRequest>();
            Mock<IWotRegistryService> bridge = CreateRecordingBridge(registry, requests);
            using var harness = new ManagerHarness(m_tempFolder, bridge.Object);
            await harness.StartAsync().ConfigureAwait(false);
            Mock<IWotAssetProvider> provider = TrackProvider(harness);
            AssetEntry entry = await CreatePopulatedAssetAsync(harness).ConfigureAwait(false);
            bridge.Setup(value => value.DeleteResourceAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long?>(), It.IsAny<CancellationToken>()))
                .Returns(async (string groupId, string resourceId, long? epoch, CancellationToken token) =>
                {
                    WotRegistryMutationResult deleted = await registry.DeleteResourceAsync(
                        groupId, resourceId, epoch, token).ConfigureAwait(false);
                    return new WotRegistryMutationResult(
                        WoTOutcomeEnum.Warning, deleted.Resource, deleted.Generation, [], "Committed with a warning.");
                });

            ServiceResult result = await harness.Registry.DeleteAssetAsync(entry.Asset.NodeId, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.GoodResultsMayBeIncomplete));
            AssertDeletedAsset(harness, entry, provider, registry, result);
        }

        [Test]
        [Platform("Win")]
        public async Task LocalDocumentCleanupRetryDoesNotRepeatTheCommittedBackingDelete()
        {
            var store = new ControlledRegistryStore();
            using var registry = new WotRegistryService(store);
            using var harness = new ManagerHarness(m_tempFolder, registry);
            await harness.StartAsync().ConfigureAwait(false);
            Mock<IWotAssetProvider> provider = TrackProvider(harness);
            AssetEntry entry = await CreatePopulatedAssetAsync(harness).ConfigureAwait(false);
            int commits = store.CommitCount;
            using (var retained = new FileStream(
                Path.Combine(m_tempFolder, "Pump01.jsonld"), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                ServiceResult incomplete = await harness.Registry.DeleteAssetAsync(entry.Asset.NodeId, CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(incomplete.StatusCode, Is.EqualTo(StatusCodes.BadResourceUnavailable));
                Assert.That(harness.Registry.FindByNodeId(entry.Asset.NodeId), Is.SameAs(entry));
                Assert.That(registry.Current.AllResources(), Is.Empty);
                Assert.That(store.CommitCount, Is.EqualTo(commits + 1));
                provider.Verify(value => value.DisposeAsync(), Times.Never);
                ServiceResult replacement = await harness.Registry.RebuildAsync(
                    entry, CreateThingDescription("Pump01", "sim://opcua.test/replacement"),
                    persistOnSuccess: true, CancellationToken.None).ConfigureAwait(false);
                Assert.That(replacement.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                Assert.That(retained.Length, Is.GreaterThan(0));
            }

            ServiceResult retried = await harness.Registry.DeleteAssetAsync(entry.Asset.NodeId, CancellationToken.None)
                .ConfigureAwait(false);

            AssertDeletedAsset(harness, entry, provider, registry, retried);
            Assert.That(store.CommitCount, Is.EqualTo(commits + 1));
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task RestartedIndeterminateDeletionNeverRemirrorsOrReactivatesTheAsset(
            bool committed, bool initializeBeforeStart)
        {
            var store = new ControlledRegistryStore();
            NodeId assetId;
            byte[] content;
            using (var registry = new WotRegistryService(store))
            {
                using var harness = new ManagerHarness(m_tempFolder, registry);
                await harness.StartAsync().ConfigureAwait(false);
                TrackProvider(harness);
                AssetEntry entry = await CreatePopulatedAssetAsync(harness).ConfigureAwait(false);
                assetId = entry.Asset.NodeId;
                content = File.ReadAllBytes(Path.Combine(m_tempFolder, "Pump01.jsonld"));
                static void Fail(WotRegistrySnapshot snapshot, CancellationToken _)
                {
                    throw new WotRegistryCommitIndeterminateException(
                        snapshot, new IOException("decision interrupted"), new IOException("decision not yet validated"));
                }
                if (committed)
                {
                    store.AfterCommit = Fail;
                }
                else
                {
                    store.BeforeCommit = Fail;
                }
                ServiceResult result = await harness.Registry.DeleteAssetAsync(assetId, CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            }
            store.BeforeCommit = null;
            store.AfterCommit = null;
            WotRegistrySnapshot durable = store.Current;
            int commits = store.CommitCount;
            using var reopened = new WotRegistryService(store);
            if (initializeBeforeStart)
            {
                await reopened.InitializeAsync().ConfigureAwait(false);
            }
            using var restarted = new ManagerHarness(m_tempFolder, reopened);
            Mock<IWotAssetProvider> unusedProvider = TrackProvider(restarted);

            await restarted.StartAsync().ConfigureAwait(false);

            Assert.That(store.Current, Is.SameAs(durable), "Restart must not remirror an unresolved deletion.");
            Assert.That(store.CommitCount, Is.EqualTo(commits));
            AssetEntry? pending = restarted.Registry.FindByNodeId(assetId);
            Assert.That(pending, Is.Not.Null);
            Assert.That(pending!.Provider, Is.Null, "Unresolved deletion must not reconnect the old provider.");
            Assert.That(pending.Properties, Is.Empty);
            Assert.That(pending.FileManager!.CurrentContent, Is.EqualTo(content));
            if (!initializeBeforeStart)
            {
                ServiceResult blocked = await restarted.Registry.DeleteAssetAsync(assetId, CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(ServiceResult.IsBad(blocked), Is.True,
                    "An uninitialized empty registry is not authoritative evidence of committed deletion.");
                Assert.That(restarted.Registry.FindByNodeId(assetId), Is.SameAs(pending));
                await reopened.InitializeAsync().ConfigureAwait(false);
            }
            ServiceResult completed = await restarted.Registry.DeleteAssetAsync(assetId, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(completed), Is.True);
            Assert.That(store.CommitCount, Is.EqualTo(commits + (committed ? 0 : 1)));
            Assert.That(reopened.Current.AllResources(), Is.Empty);
            Assert.That(restarted.Registry.FindByNodeId(assetId), Is.Null);
            Assert.That(File.Exists(Path.Combine(m_tempFolder, "Pump01.jsonld")), Is.False);
            unusedProvider.Verify(value => value.DisposeAsync(), Times.Never);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task InvalidPendingDeletionRecordsFailStartupWithoutActivatingAnAsset(bool oversized)
        {
            using var registry = new WotRegistryService();
            using var harness = new ManagerHarness(m_tempFolder, registry);
            string path = Path.Combine(m_tempFolder, "Pump01.jsonld.delete-pending");
            string content = oversized ? new string(' ', 16385) : "{";
            File.WriteAllText(path, content);

            await Assert.ThatAsync(harness.StartAsync,
                Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadConfigurationError)).ConfigureAwait(false);

            Assert.That(harness.Registry.AssetNames, Is.Empty);
            Assert.That(registry.Current.AllResources(), Is.Empty);
            Assert.That(File.ReadAllText(path), Is.EqualTo(content));
        }

        [Test]
        public async Task PendingDeletionCannotFallBackToAnUnbridgedAssetAfterRestart()
        {
            using var harness = new ManagerHarness(m_tempFolder, registryBridge: null);
            string path = Path.Combine(m_tempFolder, "Pump01.jsonld.delete-pending");
            File.WriteAllText(path, """
                {"groupId":"thingdescriptions","resourceId":"pump01","generation":1}
                """);

            await Assert.ThatAsync(harness.StartAsync,
                Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadConfigurationError)).ConfigureAwait(false);

            Assert.That(harness.Registry.AssetNames, Is.Empty);
            Assert.That(File.Exists(path), Is.True);
        }

        [Test]
        [Platform("Win")]
        public async Task FailedDeletionIntentPersistenceDoesNotReachTheBackingDecision()
        {
            var store = new ControlledRegistryStore();
            using var registry = new WotRegistryService(store);
            using var harness = new ManagerHarness(m_tempFolder, registry);
            await harness.StartAsync().ConfigureAwait(false);
            Mock<IWotAssetProvider> provider = TrackProvider(harness);
            AssetEntry entry = await CreatePopulatedAssetAsync(harness).ConfigureAwait(false);
            int commits = store.CommitCount;
            byte[] content = File.ReadAllBytes(Path.Combine(m_tempFolder, "Pump01.jsonld"));
            using (var retained = new FileStream(Path.Combine(m_tempFolder, "Pump01.jsonld.delete-pending"),
                FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read))
            {
                ServiceResult refused = await harness.Registry.DeleteAssetAsync(entry.Asset.NodeId, CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(ServiceResult.IsBad(refused), Is.True);
                Assert.That(store.CommitCount, Is.EqualTo(commits));
                AssertRetainedAsset(harness, entry, provider, content);
                Assert.That(retained.Length, Is.Zero);
            }

            ServiceResult retried = await harness.Registry.DeleteAssetAsync(entry.Asset.NodeId, CancellationToken.None)
                .ConfigureAwait(false);
            AssertDeletedAsset(harness, entry, provider, registry, retried);
            Assert.That(store.CommitCount, Is.EqualTo(commits + 1));
        }

        [Test]
        public async Task NonPersistedRebuildMirrorsThingDescriptionToRegistry()
        {
            using var registry = new WotRegistryService();
            var requests = new List<WotUpsertResourceRequest>();
            Mock<IWotRegistryService> bridge = CreateRecordingBridge(registry, requests);
            using var harness = new ManagerHarness(m_tempFolder, bridge.Object);
            await harness.StartAsync().ConfigureAwait(false);
            AssetEntry entry = await CreateAssetEntryAsync(harness, "asset-001").ConfigureAwait(false);
            ThingDescription td = CreateThingDescription("asset-001", "sim://opcua.test/wot/asset-001");

            ServiceResult status = await harness.Registry
                .RebuildAsync(entry, td, persistOnSuccess: false, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(status), Is.True);
            Assert.That(requests, Has.Count.EqualTo(1));
            AssertUpsertRequest(requests[0], WotRegistryGroups.ThingDescriptions, "asset-001", td);
        }

        [Test]
        public async Task NullRegistryBridgePerformsNoRegistryCalls()
        {
            var bridge = new Mock<IWotRegistryService>(MockBehavior.Strict);
            using var harness = new ManagerHarness(m_tempFolder, registryBridge: null);
            await harness.StartAsync().ConfigureAwait(false);
            AssetEntry entry = await CreateAssetEntryAsync(harness, "asset-001").ConfigureAwait(false);
            ThingDescription td = CreateThingDescription("asset-001", "sim://opcua.test/wot/asset-001");

            ServiceResult rebuild = await harness.Registry
                .RebuildAsync(entry, td, persistOnSuccess: false, CancellationToken.None)
                .ConfigureAwait(false);
            ServiceResult delete = await harness.Registry
                .DeleteAssetAsync(entry.Asset.NodeId, CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(rebuild), Is.True);
            Assert.That(ServiceResult.IsGood(delete), Is.True);
            bridge.VerifyNoOtherCalls();
        }

        [Test]
        public async Task RegistryRejectionDoesNotFailAssetRebuild()
        {
            var bridge = new Mock<IWotRegistryService>(MockBehavior.Strict);
            bridge.Setup(r => r.UpsertResourceAsync(
                    It.IsAny<WotUpsertResourceRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<WotRegistryMutationResult>(
                    new WotRegistryMutationResult(
                        WoTOutcomeEnum.Rejected,
                        resource: null,
                        generation: 0,
                        diagnostics: ImmutableArray.Create("invalid"),
                        message: "rejected")));
            using var harness = new ManagerHarness(m_tempFolder, bridge.Object);
            await harness.StartAsync().ConfigureAwait(false);
            AssetEntry entry = await CreateAssetEntryAsync(harness, "asset-001").ConfigureAwait(false);
            ThingDescription td = CreateThingDescription("asset-001", "sim://opcua.test/wot/asset-001");

            ServiceResult status = await harness.Registry
                .RebuildAsync(entry, td, persistOnSuccess: true, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(status), Is.True);
            Assert.That(harness.Registry.AssetNames, Has.Member("asset-001"));
            bridge.Verify(r => r.UpsertResourceAsync(
                It.IsAny<WotUpsertResourceRequest>(),
                It.IsAny<CancellationToken>()), Times.Once);
            bridge.VerifyNoOtherCalls();
        }

        [Test]
        public async Task RegistryExceptionDoesNotFailAssetRebuild()
        {
            var bridge = new Mock<IWotRegistryService>(MockBehavior.Strict);
            bridge.Setup(r => r.UpsertResourceAsync(
                    It.IsAny<WotUpsertResourceRequest>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new IOException("registry unavailable"));
            using var harness = new ManagerHarness(m_tempFolder, bridge.Object);
            await harness.StartAsync().ConfigureAwait(false);
            AssetEntry entry = await CreateAssetEntryAsync(harness, "asset-001").ConfigureAwait(false);
            ThingDescription td = CreateThingDescription("asset-001", "sim://opcua.test/wot/asset-001");

            ServiceResult status = await harness.Registry
                .RebuildAsync(entry, td, persistOnSuccess: true, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(status), Is.True);
            Assert.That(harness.Registry.AssetNames, Has.Member("asset-001"));
            bridge.Verify(r => r.UpsertResourceAsync(
                It.IsAny<WotUpsertResourceRequest>(),
                It.IsAny<CancellationToken>()), Times.Once);
            bridge.VerifyNoOtherCalls();
        }

        private async Task VerifyRefusedDeleteAsync(WoTOutcomeEnum outcome)
        {
            using var registry = new WotRegistryService();
            var requests = new List<WotUpsertResourceRequest>();
            Mock<IWotRegistryService> bridge = CreateRecordingBridge(registry, requests);
            using var harness = new ManagerHarness(m_tempFolder, bridge.Object);
            await harness.StartAsync().ConfigureAwait(false);
            Mock<IWotAssetProvider> provider = TrackProvider(harness);
            AssetEntry entry = await CreatePopulatedAssetAsync(harness).ConfigureAwait(false);
            WotResource assigned = registry.Current.AllResources().Single();
            WotAssetFileManager file = entry.FileManager!;
            BaseDataVariableState property = entry.Properties.Single().Value.Variable;
            byte[] persisted = File.ReadAllBytes(Path.Combine(m_tempFolder, "Pump01.jsonld"));
            bridge.Setup(value => value.DeleteResourceAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long?>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<WotRegistryMutationResult>(new WotRegistryMutationResult(
                    outcome, null, registry.Current.Generation, [], "The backing delete was refused.")
                {
                    StatusCode = StatusCodes.BadUserAccessDenied
                }));

            ServiceResult result = await harness.Registry.DeleteAssetAsync(entry.Asset.NodeId, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(ServiceResult.IsBad(result), Is.True,
                "A rejected backing mutation must not be reported as a successful legacy deletion.");
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            AssertRetainedAsset(harness, entry, provider, persisted);
            Assert.That(entry.FileManager, Is.SameAs(file));
            Assert.That(entry.Properties.Single().Value.Variable, Is.SameAs(property));
            Assert.That(registry.Current.AllResources().Single(), Is.SameAs(assigned));
            bridge.Verify(value => value.DeleteResourceAsync(
                assigned.GroupId, assigned.ResourceId, null, It.IsAny<CancellationToken>()), Times.Once);

            bridge.Setup(value => value.DeleteResourceAsync(
                assigned.GroupId, assigned.ResourceId, It.IsAny<long?>(), It.IsAny<CancellationToken>()))
                .Returns((string group, string resource, long? epoch, CancellationToken token) =>
                    registry.DeleteResourceAsync(group, resource, epoch, token));
            ServiceResult retried = await harness.Registry.DeleteAssetAsync(entry.Asset.NodeId, CancellationToken.None)
                .ConfigureAwait(false);
            AssertDeletedAsset(harness, entry, provider, registry, retried);
            Assert.That(requests, Has.Count.EqualTo(1));
        }

        private static async Task<AssetEntry> CreateAssetEntryAsync(ManagerHarness harness, string assetName)
        {
            (ServiceResult status, NodeId assetId) = await harness.Registry
                .CreateAssetAsync(assetName, CancellationToken.None).ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(status), Is.True);
            AssetEntry? entry = harness.Registry.FindByNodeId(assetId);
            Assert.That(entry, Is.Not.Null);
            return entry!;
        }

        private static Mock<IWotAssetProvider> TrackProvider(ManagerHarness harness)
        {
            var provider = new Mock<IWotAssetProvider>(MockBehavior.Strict);
            provider.Setup(value => value.DisposeAsync()).Returns(default(ValueTask));
            var factory = new Mock<IWotAssetProviderFactory>(MockBehavior.Strict);
            factory.SetupGet(value => value.SupportedBindings).Returns(Array.Empty<string>());
            factory.Setup(value => value.CanHandle(It.IsAny<ThingDescription>())).Returns(true);
            factory.Setup(value => value.ConnectAsync(It.IsAny<ThingDescription>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IWotAssetProvider>(provider.Object));
            harness.Options.Bindings.Clear();
            harness.Options.Bindings.Add(factory.Object);
            return provider;
        }

        private static async Task<AssetEntry> CreatePopulatedAssetAsync(ManagerHarness harness)
        {
            AssetEntry entry = await CreateAssetEntryAsync(harness, "Pump01").ConfigureAwait(false);
            ThingDescription description = CreateThingDescription("Pump01", "sim://opcua.test/wot/Pump01");
            description.Properties = new Dictionary<string, WotProperty>
            {
                ["Speed"] = new WotProperty { Type = "number" }
            };
            ServiceResult result = await harness.Registry.RebuildAsync(
                entry, description, persistOnSuccess: true, CancellationToken.None).ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(entry.Properties, Has.Count.EqualTo(1));
            Assert.That(entry.RegistryMirror, Is.Not.Null);
            return entry;
        }

        private void AssertRetainedAsset(
            ManagerHarness harness, AssetEntry entry, Mock<IWotAssetProvider> provider, byte[] persisted)
        {
            Assert.That(harness.Registry.FindByNodeId(entry.Asset.NodeId), Is.SameAs(entry));
            Assert.That(harness.Registry.AssetNames, Has.Member("Pump01"));
            Assert.That(entry.Provider, Is.SameAs(provider.Object));
            provider.Verify(value => value.DisposeAsync(), Times.Never);
            Assert.That(entry.Properties, Has.Count.EqualTo(1));
            Assert.That(harness.Manager.FindPredefinedNode<NodeState>(entry.Asset.NodeId), Is.SameAs(entry.Asset));
            Assert.That(File.ReadAllBytes(Path.Combine(m_tempFolder, "Pump01.jsonld")), Is.EqualTo(persisted));
        }

        private void AssertDeletedAsset(
            ManagerHarness harness, AssetEntry entry, Mock<IWotAssetProvider> provider,
            WotRegistryService registry, ServiceResult result)
        {
            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(harness.Registry.FindByNodeId(entry.Asset.NodeId), Is.Null);
            Assert.That(harness.Registry.AssetNames, Is.Empty);
            Assert.That(harness.Manager.FindPredefinedNode<NodeState>(entry.Asset.NodeId), Is.Null);
            Assert.That(File.Exists(Path.Combine(m_tempFolder, "Pump01.jsonld")), Is.False);
            Assert.That(registry.Current.AllResources(), Is.Empty);
            provider.Verify(value => value.DisposeAsync(), Times.Once);
        }

        private static Mock<IWotRegistryService> CreateRecordingBridge(
            WotRegistryService registry,
            List<WotUpsertResourceRequest> requests,
            List<(string GroupId, string ResourceId)>? deletes = null)
        {
            var bridge = new Mock<IWotRegistryService>(MockBehavior.Strict);
            bridge.SetupGet(value => value.Current).Returns(() => registry.Current);
            bridge.Setup(r => r.UpsertResourceAsync(
                    It.IsAny<WotUpsertResourceRequest>(),
                    It.IsAny<CancellationToken>()))
                .Callback<WotUpsertResourceRequest, CancellationToken>((request, _) => requests.Add(request))
                .Returns((WotUpsertResourceRequest request, CancellationToken ct) =>
                    registry.UpsertResourceAsync(request, ct));
            bridge.Setup(r => r.DeleteResourceAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<long?>(),
                    It.IsAny<CancellationToken>()))
                .Callback<string, string, long?, CancellationToken>((groupId, resourceId, _, _) =>
                    deletes?.Add((groupId, resourceId)))
                .Returns((string groupId, string resourceId, long? expectedEpoch, CancellationToken ct) =>
                    registry.DeleteResourceAsync(groupId, resourceId, expectedEpoch, ct));
            return bridge;
        }

        private static ThingDescription CreateThingDescription(string name, string endpoint)
        {
            return new ThingDescription
            {
                Name = name,
                Base = endpoint
            };
        }

        private static void AssertUpsertRequest(
            WotUpsertResourceRequest request,
            string groupId,
            string resourceId,
            ThingDescription expected)
        {
            Assert.That(request.GroupId, Is.EqualTo(groupId));
            Assert.That(request.ResourceId, Is.EqualTo(resourceId));
            Assert.That(request.ContentType, Is.EqualTo("application/td+json"));
            Assert.That(request.Format, Is.EqualTo("WoT-TD/1.1"));
            ThingDescription? roundtrip = JsonSerializer.Deserialize(
                request.Content.Span,
                ThingDescriptionJsonContext.Default.ThingDescription);
            Assert.That(roundtrip, Is.Not.Null);
            Assert.That(roundtrip!.Name, Is.EqualTo(expected.Name));
            Assert.That(roundtrip.Base, Is.EqualTo(expected.Base));
            Assert.That(roundtrip.Properties, Is.EqualTo(expected.Properties));
        }

        private string m_tempFolder = null!;

        private sealed class ManagerHarness : IDisposable
        {
            private const string AssetNamespace = "http://opcfoundation.org/UA/WoT-Con/Assets/";

            public ManagerHarness(string thingDescriptionFolder, IWotRegistryService? registryBridge)
            {
                MockServer = new Mock<IServerInternal>();

                var namespaceTable = new NamespaceTable();
                namespaceTable.Append(Namespaces.WotCon);
                namespaceTable.Append(AssetNamespace);

                MockServer.Setup(s => s.NamespaceUris).Returns(namespaceTable);
                MockServer.Setup(s => s.ServerUris).Returns(new StringTable());
                var typeTable = new TypeTable(namespaceTable);
                SeedStandardTypeTree(typeTable);
                MockServer.Setup(s => s.TypeTree).Returns(typeTable);
                MockServer.Setup(s => s.Factory).Returns(EncodeableFactory.Create());

                var mockMaster = new Mock<IMasterNodeManager>();
                var mockConfig = new Mock<IConfigurationNodeManager>();
                mockMaster.Setup(m => m.ConfigurationNodeManager).Returns(mockConfig.Object);
                MockServer.Setup(s => s.NodeManager).Returns(mockMaster.Object);

                var mockTelemetry = new Mock<ITelemetryContext>();
                MockServer.Setup(s => s.Telemetry).Returns(mockTelemetry.Object);

                m_monitoredItemQueueFactory = new MonitoredItemQueueFactory(mockTelemetry.Object);
                MockServer.Setup(s => s.MonitoredItemQueueFactory).Returns(m_monitoredItemQueueFactory);

                m_serverSystemContext = new ServerSystemContext(MockServer.Object);
                MockServer.Setup(s => s.DefaultSystemContext).Returns(m_serverSystemContext);

                m_configuration = new ApplicationConfiguration
                {
                    ServerConfiguration = new ServerConfiguration
                    {
                        MaxNotificationQueueSize = 100,
                        MaxDurableNotificationQueueSize = 200
                    }
                };

                Options = new WotConnectivityServerOptions
                {
                    AssetNamespaceUri = AssetNamespace,
                    ThingDescriptionStorageFolder = thingDescriptionFolder,
                    RegistryBridge = registryBridge
                };
                Options.AssetEndpointPolicy.AllowedSchemes.Add("sim");
                Options.Bindings.Add(new SimulatedWotAssetProviderFactory());

                Manager = new WotConnectivityNodeManager(
                    MockServer.Object,
                    m_configuration,
                    Options);
            }

            public Mock<IServerInternal> MockServer { get; }
            public WotConnectivityServerOptions Options { get; }
            public WotConnectivityNodeManager Manager { get; }

            public AssetRegistry Registry
                => (AssetRegistry)typeof(WotConnectivityNodeManager)
                    .GetField(
                        "m_registry",
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.NonPublic)!
                    .GetValue(Manager)!;

            public async Task StartAsync()
            {
                IDictionary<NodeId, IList<IReference>> externalReferences =
                    new Dictionary<NodeId, IList<IReference>>();
                await Manager.CreateAddressSpaceAsync(externalReferences).ConfigureAwait(false);
            }

            public void Dispose()
            {
                Manager.Dispose();
                m_monitoredItemQueueFactory.Dispose();
            }

            private static void SeedStandardTypeTree(TypeTable typeTable)
            {
                NodeId baseObject = Ua.ObjectTypeIds.BaseObjectType;
                NodeId baseVariable = VariableTypeIds.BaseVariableType;
                NodeId baseDataVariable = VariableTypeIds.BaseDataVariableType;
                NodeId propertyType = VariableTypeIds.PropertyType;
                NodeId fileType = Ua.ObjectTypeIds.FileType;
                NodeId namespaceMetadataType = Ua.ObjectTypeIds.NamespaceMetadataType;
                NodeId baseInterfaceType = Ua.ObjectTypeIds.BaseInterfaceType;

                typeTable.AddSubtype(baseObject, NodeId.Null);
                typeTable.AddSubtype(fileType, baseObject);
                typeTable.AddSubtype(namespaceMetadataType, baseObject);
                typeTable.AddSubtype(baseInterfaceType, baseObject);

                typeTable.AddSubtype(baseVariable, NodeId.Null);
                typeTable.AddSubtype(baseDataVariable, baseVariable);
                typeTable.AddSubtype(propertyType, baseVariable);

                typeTable.AddReferenceSubtype(
                    Ua.ReferenceTypeIds.References,
                    NodeId.Null,
                    new QualifiedName("References"));
                typeTable.AddReferenceSubtype(
                    Ua.ReferenceTypeIds.HierarchicalReferences,
                    Ua.ReferenceTypeIds.References,
                    new QualifiedName("HierarchicalReferences"));
                typeTable.AddReferenceSubtype(
                    Ua.ReferenceTypeIds.HasChild,
                    Ua.ReferenceTypeIds.HierarchicalReferences,
                    new QualifiedName("HasChild"));
                typeTable.AddReferenceSubtype(
                    Ua.ReferenceTypeIds.Aggregates,
                    Ua.ReferenceTypeIds.HasChild,
                    new QualifiedName("Aggregates"));
                typeTable.AddReferenceSubtype(
                    Ua.ReferenceTypeIds.HasComponent,
                    Ua.ReferenceTypeIds.Aggregates,
                    new QualifiedName("HasComponent"));
                typeTable.AddReferenceSubtype(
                    Ua.ReferenceTypeIds.HasProperty,
                    Ua.ReferenceTypeIds.Aggregates,
                    new QualifiedName("HasProperty"));
                typeTable.AddReferenceSubtype(
                    Ua.ReferenceTypeIds.Organizes,
                    Ua.ReferenceTypeIds.HierarchicalReferences,
                    new QualifiedName("Organizes"));
                typeTable.AddReferenceSubtype(
                    Ua.ReferenceTypeIds.NonHierarchicalReferences,
                    Ua.ReferenceTypeIds.References,
                    new QualifiedName("NonHierarchicalReferences"));
                typeTable.AddReferenceSubtype(
                    Ua.ReferenceTypeIds.HasInterface,
                    Ua.ReferenceTypeIds.NonHierarchicalReferences,
                    new QualifiedName("HasInterface"));
            }

            private readonly ApplicationConfiguration m_configuration;
            private readonly ServerSystemContext m_serverSystemContext;
            private readonly MonitoredItemQueueFactory m_monitoredItemQueueFactory;
        }

        private sealed class ControlledRegistryStore : IWotRegistryStore
        {
            public WotRegistrySnapshot Current { get; private set; } = WotRegistrySnapshot.Empty;
            public Action<WotRegistrySnapshot, CancellationToken>? BeforeCommit { get; set; }
            public Action<WotRegistrySnapshot, CancellationToken>? AfterCommit { get; set; }
            public int CommitCount { get; private set; }

            public ValueTask<WotRegistrySnapshot> LoadAsync(CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new ValueTask<WotRegistrySnapshot>(Current);
            }

            public ValueTask CommitAsync(
                WotRegistrySnapshot snapshot, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CommitCount++;
                BeforeCommit?.Invoke(snapshot, cancellationToken);
                Current = snapshot;
                AfterCommit?.Invoke(snapshot, cancellationToken);
                return default;
            }
        }
    }
}
