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
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.WotCon.Server;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    public sealed partial class WotAtomicPublicationNativeTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public Task ColdRecoveryKeepsCommittedResolutionInputWithoutActivatingIt(bool overwriteVersion)
        {
            return VerifyCommittedResolutionInputAsync(overwriteVersion);
        }

        [TestCase(false)]
        [TestCase(true)]
        public Task ColdRecoveryRetainsEvictedResolutionVersionWithoutApplyingPendingEnable(bool enableInput)
        {
            return VerifyCommittedResolutionInputAsync(false, evictVersion: true, enableInput: enableInput);
        }

        [TestCase(false)]
        [TestCase(true)]
        public Task ColdRecoveryRejectsMissingOrContradictoryResolutionInputEvidence(bool contradictory)
        {
            return VerifyCommittedResolutionInputAsync(
                false, damage: contradictory ? RetainedInputDamage.Contradictory : RetainedInputDamage.Missing);
        }

        [Test]
        public Task DirectPublicationRetainsResolutionInputsWithoutDependencyObservationOrigin()
        {
            return VerifyCommittedResolutionInputAsync(true, nativeOrigin: false);
        }

        [Test]
        public Task ColdRecoverySharesOneRetainedInputAcrossCommittedOwners()
        {
            return VerifyCommittedResolutionInputAsync(false, sharedInput: true);
        }

        [TestCase(false)]
        [TestCase(true)]
        public Task ColdRecoveryPreservesAnAlreadyActiveResolutionInputOwner(bool overwriteVersion)
        {
            return VerifyCommittedResolutionInputAsync(overwriteVersion, activeInput: true);
        }

        [Test]
        public Task ColdRecoveryRejectsAResolutionInputThatConflictsWithItsActiveOwner()
        {
            return VerifyCommittedResolutionInputAsync(
                false, damage: RetainedInputDamage.ActiveVersionMismatch, activeInput: true);
        }

        private async Task VerifyCommittedResolutionInputAsync(
            bool overwriteVersion,
            bool evictVersion = false,
            bool enableInput = false,
            RetainedInputDamage damage = RetainedInputDamage.None,
            bool nativeOrigin = true,
            bool sharedInput = false,
            bool activeInput = false)
        {
            var consumed = new List<ByteString>();
            var converter = new Mock<IWotDocumentConverter>(MockBehavior.Strict);
            converter.Setup(value => value.ConvertAsync(
                It.IsAny<WotResource>(), It.IsAny<ByteString>(), It.IsAny<WotRegistrySnapshot>(),
                It.IsAny<IReadOnlyDictionary<string, ByteString>>(), It.IsAny<CancellationToken>()))
                .Returns((WotResource resource, ByteString content, WotRegistrySnapshot snapshot,
                    IReadOnlyDictionary<string, ByteString> contents, CancellationToken token) =>
                {
                    if (resource.ResourceId == "retained-dependent")
                    {
                        WotResourceVersion dependency = snapshot.FindResource(
                            WotRegistryGroups.ThingModels, "retained-input")!.DefaultVersion!;
                        ByteString input = contents[dependency.DigestHex];
                        consumed.Add(input);
                        using JsonDocument document = JsonDocument.Parse(input.Memory);
                        bool original = document.RootElement.GetProperty("title").GetString() ==
                            "urn:retained-input-1";
                        m_converter.SetNodeCount(resource.ResourceId, original ? 2 : 3);
                    }
                    return m_converter.ConvertAsync(resource, content, snapshot, contents, token);
                });
            m_coordinator.Dispose();
            m_coordinator = new WotMaterializationCoordinator(
                m_registry, new LifecycleWotProjectionHost(m_server.NodeManagerLifecycle),
                documentConverter: converter.Object)
            {
                ServerNamespaceUris = m_server.CurrentInstance.NamespaceUris
            };
            m_coordinator.Event += (_, change) => m_events.Add(change);
                if (nativeOrigin)
                {
                    await m_server.NodeManagerLifecycle.AddAsync(new WotRegistryNodeManagerFactory(
                        new WotRegistryServerOptions { AutoRefresh = false }, m_registry, m_coordinator),
                        callerContext: null).ConfigureAwait(false);
                }
            WotResource model = await AddUnitModelAsync("retained-input").ConfigureAwait(false);
            WotResource source = await AddAsync("retained-dependent").ConfigureAwait(false);
            await SetUnitDependencyAsync(source, model.ResourceId).ConfigureAwait(false);
            if (activeInput)
            {
                WotRefreshResult activated = await m_coordinator.RefreshAsync(new WotRefreshRequest
                {
                    Selection = [UnitSelector(model)],
                    RequestId = "retained-input-preexisting",
                    Options = new WoTRefreshOptionsDataType { Atomicity = WoTAtomicityEnum.PerRegistry }
                }).ConfigureAwait(false);
                Assert.That(activated.NewGeneration, Is.EqualTo(1u));
            }
            await m_registry.SetEnabledAsync(model.GroupId, model.ResourceId, false).ConfigureAwait(false);
            ArrayOf<WoTResourceSelectorDataType> selection = [UnitSelector(source)];
            if (sharedInput)
            {
                WotResource peer = await AddAsync("retained-peer").ConfigureAwait(false);
                await SetUnitDependencyAsync(peer, model.ResourceId).ConfigureAwait(false);
                selection = [UnitSelector(source), UnitSelector(peer)];
            }
            ByteString committedInput = ByteString.From(TestMaterialization.Tm("urn:retained-input"));
            WotRefreshResult initial = await m_coordinator.RefreshAsync(new WotRefreshRequest
            {
                Selection = [.. selection],
                RequestId = "retained-input-initial",
                Options = new WoTRefreshOptionsDataType { Atomicity = WoTAtomicityEnum.PerRegistry }
            }).ConfigureAwait(false);
            Assert.That(initial.NewGeneration, Is.EqualTo(activeInput ? 2u : 1u));
            Assert.That(consumed, Is.EqualTo(new[] { committedInput }));
            Assert.That((await ReadNodeClassAsync(Root(model)).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(activeInput ? StatusCodes.Good : StatusCodes.BadNodeIdUnknown));
            WotResource active = m_registry.Current.FindResourceByXid(source.Xid)!;
            WotDependencySnapshot? observation = active.ActiveVersion!.DependencySnapshot;
            if (nativeOrigin)
            {
                Assert.That(observation, Is.Not.Null);
                Assert.That(observation!.Targets.Count, Is.EqualTo(sharedInput ? 2 : 1));
                Assert.That(observation.Targets[0].ContentDigest, Is.EqualTo(model.DefaultVersion!.Digest));
            }
            else
            {
                Assert.That(observation, Is.Null);
            }
            Assert.That(m_registry.Current.FindResourceByXid(model.Xid)!.ActiveVersionId,
                activeInput ? Is.EqualTo("v1") : Is.Null);

            WotRegistryMutationResult edited = await m_registry.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = model.GroupId,
                ResourceId = model.ResourceId,
                Kind = WoTDocumentKindEnum.ThingModel,
                VersionId = overwriteVersion ? "v1" : "v2",
                Content = ByteString.From(TestMaterialization.Tm("urn:retained-input", "2"))
            }).ConfigureAwait(false);
            Assert.That(edited.Changed, Is.True, edited.Message);
            Assert.That(edited.Resource!.Enabled, Is.False);
            if (evictVersion)
            {
                WotRegistryMutationResult removed = await m_registry.DeleteVersionAsync(
                    model.GroupId, model.ResourceId, "v1").ConfigureAwait(false);
                Assert.That(removed.Changed, Is.True, removed.Message);
                Assert.That(m_registry.Current.FindResourceByXid(model.Xid)!.FindVersion("v1"), Is.Null);
            }
            if (enableInput)
            {
                await m_registry.SetEnabledAsync(model.GroupId, model.ResourceId, true).ConfigureAwait(false);
            }
            WotRegistrySnapshot decided = m_registry.Current;
            if (damage == RetainedInputDamage.ActiveVersionMismatch)
            {
                WotResource owner = decided.FindResourceByXid(model.Xid)!;
                WotResourceVersion version = owner.DefaultVersion!;
                WotResource changed = owner.With(activeVersionId: version.VersionId).WithCommittedVersion(version);
                WotResourceGroup group = decided.FindGroup(model.GroupId)!;
                decided = decided.WithGroup(group.WithResources(
                    group.Resources.SetItem(model.ResourceId, changed), group.Epoch), decided.Generation + 1);
                await m_store.CommitAsync(decided).ConfigureAwait(false);
            }
            else if (damage != RetainedInputDamage.None)
            {
                WotResource owner = decided.FindResourceByXid(source.Xid)!;
                ArrayOf<WotResource> retained = [];
                if (damage == RetainedInputDamage.Contradictory)
                {
                    WotResource changed = decided.FindResourceByXid(model.Xid)!;
                    WotResourceVersion version = changed.DefaultVersion!;
                    retained =
                    [
                        changed.With(versions: [version],
                            defaultVersionId: version.VersionId, desiredVersionId: version.VersionId,
                            enabled: false, clearActiveVersion: true, clearRootNodeId: true)
                    ];
                }
                WotResourceGroup group = decided.FindGroup(source.GroupId)!;
                decided = decided.WithGroup(group.WithResources(
                    group.Resources.SetItem(source.ResourceId, owner.WithCommittedInputs(retained)), group.Epoch),
                    decided.Generation + 1);
                await m_store.CommitAsync(decided).ConfigureAwait(false);
            }
            consumed.Clear();

            await using PreparedWotTestRuntime restarted = await PreparedWotTestRuntime.StartAsync()
                .ConfigureAwait(false);
            restarted.Namespaces.GetIndexOrAppend("urn:c1:retained-input-padding");
            using var store = new FileWotRegistryStore(Path.Combine(m_root, "registry"));
            using var registry = new WotRegistryService(store);
            using var recovered = new WotMaterializationCoordinator(
                registry, restarted.Host, documentConverter: converter.Object);
            var events = new List<WotMaterializationEventArgs>();
            recovered.Event += (_, change) => events.Add(change);
            if (damage != RetainedInputDamage.None)
            {
                int before = restarted.Lifecycle.Registrations.Count;
                await Assert.ThatAsync(async () => await restarted.Lifecycle.AddAsync(new WotRegistryNodeManagerFactory(
                    new WotRegistryServerOptions { AutoRefresh = false }, registry, recovered),
                    callerContext: null).ConfigureAwait(false), Throws.TypeOf<InvalidOperationException>())
                    .ConfigureAwait(false);
                Assert.That(recovered.Generation, Is.Zero);
                Assert.That(restarted.Lifecycle.Registrations.Count, Is.EqualTo(before + 1));
                Assert.That(consumed, Is.Empty);
                Assert.That(events, Is.Empty);
                Assert.That((await store.LoadAsync().ConfigureAwait(false)).Generation, Is.EqualTo(decided.Generation));
                return;
            }
            await restarted.Lifecycle.AddAsync(new WotRegistryNodeManagerFactory(
                new WotRegistryServerOptions { AutoRefresh = false }, registry, recovered),
                callerContext: null).ConfigureAwait(false);
            using ISession session = await m_client.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{restarted.Port}"), SecurityPolicies.None)
                .ConfigureAwait(false);
            try
            {
                NodeId extra = ExpandedNodeId.ToNodeId(
                    new ExpandedNodeId(5002u, ModelUri(source)), restarted.Namespaces);
                ReadResponse read = await session.ReadAsync(null, 0, TimestampsToReturn.Neither,
                    [
                        new ReadValueId
                        {
                            NodeId = ExpandedNodeId.ToNodeId(
                                new ExpandedNodeId(5001u, ModelUri(source)), restarted.Namespaces),
                            AttributeId = Attributes.NodeClass
                        },
                        new ReadValueId { NodeId = extra, AttributeId = Attributes.NodeClass }
                    ], CancellationToken.None).ConfigureAwait(false);
                Assert.That(read.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(read.Results[1].StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown),
                    "Recovery must not rebuild the committed source using newer resolution-only input bytes.");
                Assert.That(consumed, Is.EqualTo(new[] { committedInput }));
                Assert.That(registry.Current.FindResourceByXid(model.Xid)!.ActiveVersionId,
                    activeInput ? Is.EqualTo("v1") : Is.Null);
                Assert.That(registry.Current.FindResourceByXid(model.Xid)!.Enabled, Is.EqualTo(enableInput));
                Assert.That(registry.Current.FindResourceByXid(model.Xid)!.DefaultVersionId,
                    Is.EqualTo(overwriteVersion ? "v1" : "v2"));
                Assert.That(registry.Current.FindResourceByXid(model.Xid)!.RefreshGeneration,
                    Is.EqualTo(activeInput ? 1u : 0u));
                Assert.That(registry.Current.Generation, Is.EqualTo(decided.Generation));
                Assert.That(recovered.Generation, Is.EqualTo(activeInput ? 2u : 1u));
                Assert.That(events, Is.Empty);
                if (activeInput)
                {
                    NodeId root = ExpandedNodeId.ToNodeId(
                        new ExpandedNodeId(5000u, ModelUri(model)), restarted.Namespaces);
                    ReadResponse existing = await session.ReadAsync(null, 0, TimestampsToReturn.Neither,
                        [new ReadValueId { NodeId = root, AttributeId = Attributes.NodeClass }],
                        CancellationToken.None).ConfigureAwait(false);
                    Assert.That(existing.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(registry.Current.FindResourceByXid(model.Xid)!.RootNodeId, Is.EqualTo(root));
                }
                WotDependencySnapshot? restoredObservation =
                    registry.Current.FindResourceByXid(source.Xid)!.ActiveVersion!.DependencySnapshot;
                if (observation is null)
                {
                    Assert.That(restoredObservation, Is.Null);
                }
                else
                {
                    Assert.That(restoredObservation, Is.Not.Null);
                    Assert.That(restoredObservation!.EffectiveInputDigest,
                        Is.EqualTo(observation.EffectiveInputDigest));
                }
            }
            finally
            {
                await session.CloseAsync().ConfigureAwait(false);
            }
        }

        private enum RetainedInputDamage
        {
            None,
            Missing,
            Contradictory,
            ActiveVersionMismatch
        }
    }
}
