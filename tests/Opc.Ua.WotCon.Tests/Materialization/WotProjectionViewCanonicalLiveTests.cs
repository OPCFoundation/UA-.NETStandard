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
 *
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
using System.Linq;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Opc.Ua.Server;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Server;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;
using Quickstarts.ReferenceServer;
using ISession = Opc.Ua.Client.ISession;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    [TestFixture]
    [Category("WoT")]
    [Category("WotCon")]
    [Category("Integration")]
    [NonParallelizable]
    public sealed class WotProjectionViewCanonicalLiveTests
    {
        [Test]
        public async Task CanonicalViewIdentityCannotBeTakenByAnotherResourceAsync()
        {
            await using NativeHarness harness = await NativeHarness.CreateAsync().ConfigureAwait(false);
            var identity = new NodeId("canonical-owned-view", harness.NamespaceIndex);
            var first = new WotViewProjectionRequest(
                "first", "resource:first", NodeId.Null, identity, LegacyPlan(101));
            await harness.Views.ApplyAsync(first).ConfigureAwait(false);

            ServiceResultException? rejection = null;
            try
            {
                var conflicting = new WotViewProjectionRequest(
                    "second", "resource:second", NodeId.Null, identity, LegacyPlan(202));
                await harness.Views.ApplyAsync(conflicting).ConfigureAwait(false);
            }
            catch (ServiceResultException exception)
            {
                rejection = exception;
            }

            uint retainedVersion = await harness.ReadViewVersionAsync(identity).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(rejection, Is.Not.Null, "A canonical View cannot be reassigned to another Resource.");
                Assert.That(rejection?.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdExists));
                Assert.That(retainedVersion, Is.EqualTo(101u),
                    "Rejected ownership must not replace the original native publication.");
            });
        }

        [Test]
        public async Task AuthoredCanonicalViewIsBrowsableInItsOwnNamespaceAsync()
        {
            await using NativeHarness harness = await NativeHarness.CreateAsync().ConfigureAwait(false);
            NodeId identity = harness.Identity("urn:c2:authored-views", "Authored");
            await harness.Views.ApplyAsync(new WotViewProjectionRequest(
                "authored", "resource:authored", NodeId.Null, identity, LegacyPlan(1))).ConfigureAwait(false);

            ReadResponse read = await harness.Session.ReadAsync(
                null, 0, TimestampsToReturn.Neither,
                [new ReadValueId { NodeId = identity, AttributeId = Attributes.NodeClass }],
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(read.Results, Has.Count.EqualTo(1));
            Assert.That(read.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(read.Results[0].WrappedValue.TryGetValue(out int nodeClass), Is.True);
            Assert.That(nodeClass, Is.EqualTo((int)NodeClass.View));
            Assert.That(await harness.ReadViewVersionAsync(identity).ConfigureAwait(false), Is.EqualTo(1u));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CanonicalViewCannotReplaceAnExistingNodeRoleAsync(bool ownedProperty)
        {
            await using NativeHarness harness = await NativeHarness.CreateAsync().ConfigureAwait(false);
            var original = new NodeId("role-owner", harness.NamespaceIndex);
            await harness.Views.ApplyAsync(new WotViewProjectionRequest(
                "original", "resource:original", NodeId.Null, original, LegacyPlan(101))).ConfigureAwait(false);
            NodeId occupied = ownedProperty
                ? await harness.ViewVersionIdAsync(original).ConfigureAwait(false)
                : Ua.VariableIds.Server_ServerStatus_StartTime;
            DataValue before = await harness.Session.ReadValueAsync(occupied).ConfigureAwait(false);
            Assert.That(before.StatusCode, Is.EqualTo(StatusCodes.Good));
            ServiceResultException? rejection = null;
            try
            {
                await harness.Views.ApplyAsync(new WotViewProjectionRequest(
                    "collision", "resource:collision", NodeId.Null, occupied, LegacyPlan(202)))
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException exception)
            {
                rejection = exception;
            }

            DataValue after = await harness.Session.ReadValueAsync(occupied).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(rejection, Is.Not.Null, "An existing node's owner and role must not be overwritten.");
                Assert.That(rejection?.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdExists));
                Assert.That(after.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(after.WrappedValue, Is.EqualTo(before.WrappedValue));
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task NativeViewPropertyPathsResolveWithoutInventingNodesAsync(bool existing)
        {
            await using NativeHarness harness = await NativeHarness.CreateAsync().ConfigureAwait(false);
            var identity = new NodeId("translated-view", harness.NamespaceIndex);
            await harness.Views.ApplyAsync(new WotViewProjectionRequest(
                "translated", "resource:translated", NodeId.Null, identity, LegacyPlan(1))).ConfigureAwait(false);
            NodeId propertyId = await harness.ViewVersionIdAsync(identity).ConfigureAwait(false);
            TranslateBrowsePathsToNodeIdsResponse translated = await harness.Session.TranslateBrowsePathsToNodeIdsAsync(
                null,
                [
                    new BrowsePath
                    {
                        StartingNode = identity,
                        RelativePath = new RelativePath
                        {
                            Elements =
                            [
                                new RelativePathElement
                                {
                                    ReferenceTypeId = Ua.ReferenceTypeIds.HasProperty,
                                    IncludeSubtypes = false,
                                    TargetName = new QualifiedName(existing ? "ViewVersion" : "Missing")
                                }
                            ]
                        }
                    }
                ], CancellationToken.None).ConfigureAwait(false);

            Assert.That(translated.Results, Has.Count.EqualTo(1));
            BrowsePathResult result = translated.Results[0];
            if (!existing)
            {
                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNoMatch));
                Assert.That(result.Targets, Is.Empty);
                return;
            }
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(result.Targets, Has.Count.EqualTo(1));
            Assert.That(result.Targets[0].RemainingPathIndex, Is.EqualTo(uint.MaxValue));
            Assert.That(ExpandedNodeId.ToNodeId(result.Targets[0].TargetId, harness.Session.NamespaceUris),
                Is.EqualTo(propertyId));
        }

        [Test]
        public async Task CanonicalCandidateRendersSharedChildAndTypedWrappersOverTransportAsync()
        {
            await using NativeHarness harness = await NativeHarness.CreateAsync(withGraphResources: true)
                .ConfigureAwait(false);
            NodeId child = harness.Identity("urn:c2:native-views", "Child");
            NodeId left = harness.Identity("urn:c2:native-views", "Left");
            NodeId right = harness.Identity("urn:c2:native-views", "Right");
            var context = harness.GraphContext(
                [new WotCanonicalViewSource(Ua.VariableIds.Server_ServerStatus_StartTime, NodeClass.Variable)]);
            WotCanonicalViewState state = WotProjectionViewBuilder.PrepareCanonicalGraph(
                context, null,
                [
                    harness.GraphRequest("child", child, [Ua.VariableIds.Server_ServerStatus_StartTime], []),
                    harness.GraphRequest("left", left, [], [new WotCanonicalViewLink(harness.ResourceXid("child"), "Group")]),
                    harness.GraphRequest("right", right, [], [new WotCanonicalViewLink(harness.ResourceXid("child"), "Group")])
                ], []).State;

            await harness.PublishCanonicalAsync(state).ConfigureAwait(false);

            ArrayOf<ReferenceDescription> views = await harness.BrowseAsync(
                Ua.ObjectIds.ViewsFolder, Ua.ReferenceTypeIds.Organizes).ConfigureAwait(false);
            Assert.That(views.ToArray()!.Count(reference => harness.Target(reference) == child), Is.EqualTo(1));
            ReferenceDescription leftGroup = (await harness.BrowseAsync(left, Ua.ReferenceTypeIds.Organizes)
                .ConfigureAwait(false)).ToArray()!.Single();
            ReferenceDescription rightGroup = (await harness.BrowseAsync(right, Ua.ReferenceTypeIds.Organizes)
                .ConfigureAwait(false)).ToArray()!.Single();
            Assert.That(leftGroup.NodeClass, Is.EqualTo(NodeClass.Object));
            Assert.That(leftGroup.TypeDefinition,
                Is.EqualTo(new ExpandedNodeId(ExpandedNodeId.ToNodeId(
                    ObjectTypeIds.WoTProjectionGroupType, harness.NamespaceUris))));
            NodeId leftWrapper = harness.Target(leftGroup);
            NodeId rightWrapper = harness.Target(rightGroup);
            Assert.That(leftWrapper, Is.Not.EqualTo(rightWrapper));
            ReferenceDescription rootProperty = (await harness.BrowseAsync(leftWrapper, Ua.ReferenceTypeIds.HasProperty)
                .ConfigureAwait(false)).ToArray()!.Single();
            Assert.That(rootProperty.BrowseName,
                Is.EqualTo(new QualifiedName("ProjectionRoot", harness.NamespaceIndex)));
            DataValue property = await harness.Session.ReadValueAsync(harness.Target(rootProperty)).ConfigureAwait(false);
            Assert.That(property.WrappedValue.TryGetValue(out NodeId projectionRoot), Is.True);
            Assert.That(projectionRoot, Is.EqualTo(child));
            ArrayOf<ReferenceDescription> members = await harness.BrowseAsync(
                leftWrapper, Ua.ReferenceTypeIds.Organizes).ConfigureAwait(false);
            Assert.That(members.ToArray()!.Select(harness.Target),
                Is.EquivalentTo(new[] { Ua.VariableIds.Server_ServerStatus_StartTime }));
            NodeId hasProjection = ExpandedNodeId.ToNodeId(ReferenceTypeIds.HasWoTProjection, harness.NamespaceUris);
            ArrayOf<ReferenceDescription> correlation = await harness.BrowseAsync(
                child, hasProjection, BrowseDirection.Inverse).ConfigureAwait(false);
            Assert.That(correlation.ToArray()!.Select(harness.Target),
                Is.EquivalentTo(new[] { harness.ResourceNodeId("child") }));
        }

        [Test]
        public async Task CanonicalCandidateReloadUpdatesAncestorsAndRejectsTheOldTokenAsync()
        {
            await using NativeHarness harness = await NativeHarness.CreateAsync(withGraphResources: true)
                .ConfigureAwait(false);
            NodeId child = harness.Identity("urn:c2:native-views", "Child");
            NodeId left = harness.Identity("urn:c2:native-views", "Left");
            NodeId right = harness.Identity("urn:c2:native-views", "Right");
            NodeId reading = Ua.VariableIds.Server_ServerStatus_StartTime;
            NodeId other = Ua.VariableIds.Server_ServerStatus_BuildInfo_BuildNumber;
            var context = harness.GraphContext(
                [new(reading, NodeClass.Variable), new(other, NodeClass.Variable)]);
            WotCanonicalViewState initial = WotProjectionViewBuilder.PrepareCanonicalGraph(context, null,
                [
                    harness.GraphRequest("child", child, [reading], []),
                    harness.GraphRequest("left", left, [], [new(harness.ResourceXid("child"), "Group")]),
                    harness.GraphRequest("right", right, [other], [])
                ], []).State;
            NodeManagerRegistration registration = await harness.PublishCanonicalAsync(initial).ConfigureAwait(false);
            DataValue sourceBefore = await harness.Session.ReadValueAsync(reading).ConfigureAwait(false);
            WotCanonicalViewPreparation changed = WotProjectionViewBuilder.PrepareCanonicalGraph(context, initial,
                [harness.GraphRequest("child", child, [reading, other], [])], []);
            Assert.That(changed.AffectedResourceXids.ToArray(),
                Is.EquivalentTo(new[] { harness.ResourceXid("child"), harness.ResourceXid("left") }));

            NodeManagerRegistration replacement = await harness.PublishCanonicalAsync(changed.State, registration)
                .ConfigureAwait(false);

            Assert.That(replacement.Id, Is.EqualTo(registration.Id));
            Assert.That(replacement.Generation, Is.GreaterThan(registration.Generation));
            Assert.That(await harness.ReadViewVersionAsync(child).ConfigureAwait(false), Is.EqualTo(2u));
            Assert.That(await harness.ReadViewVersionAsync(left).ConfigureAwait(false), Is.EqualTo(2u));
            Assert.That(await harness.ReadViewVersionAsync(right).ConfigureAwait(false), Is.EqualTo(1u));
            BrowseResult current = await harness.BrowseInViewAsync(child, child, 2).ConfigureAwait(false);
            Assert.That(current.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(current.References.ToArray()!.Select(harness.Target),
                Is.EquivalentTo(new[] { reading, other }));
            BrowseResult stale = await harness.BrowseInViewAsync(child, child, 1).ConfigureAwait(false);
            Assert.That(stale.StatusCode, Is.EqualTo(StatusCodes.BadViewVersionInvalid));
            DataValue sourceAfter = await harness.Session.ReadValueAsync(reading).ConfigureAwait(false);
            Assert.That(sourceAfter.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(sourceAfter.WrappedValue, Is.EqualTo(sourceBefore.WrappedValue));
            NodeId hasProjection = ExpandedNodeId.ToNodeId(ReferenceTypeIds.HasWoTProjection, harness.NamespaceUris);
            ArrayOf<ReferenceDescription> correlation = await harness.BrowseAsync(
                harness.ResourceNodeId("child"), hasProjection).ConfigureAwait(false);
            Assert.That(correlation.ToArray()!.Select(harness.Target), Is.EquivalentTo(new[] { child }));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CanonicalBatchRetirementHonorsTheCapturedBrowseImageAsync(bool immediate)
        {
            await using NativeHarness harness = await NativeHarness.CreateAsync(withGraphResources: true)
                .ConfigureAwait(false);
            NodeId child = harness.Identity("urn:c2:native-views", "Child");
            NodeId reading = Ua.VariableIds.Server_ServerStatus_StartTime;
            NodeId number = Ua.VariableIds.Server_ServerStatus_BuildInfo_BuildNumber;
            NodeId product = Ua.VariableIds.Server_ServerStatus_BuildInfo_ProductName;
            var context = harness.GraphContext(
                [new(reading, NodeClass.Variable), new(number, NodeClass.Variable), new(product, NodeClass.Variable)]);
            WotCanonicalViewState initial = WotProjectionViewBuilder.PrepareCanonicalGraph(context, null,
                [harness.GraphRequest("child", child, [reading, number, product], [])], []).State;
            NodeManagerRegistration registration = await harness.PublishCanonicalAsync(initial).ConfigureAwait(false);
            BrowseResult first = await harness.BrowseInViewAsync(child, child, 1, 1).ConfigureAwait(false);
            Assert.That(first.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(first.ContinuationPoint.IsNull, Is.False);
            var retained = new List<NodeId>(first.References.ToArray()!.Select(harness.Target));
            WotCanonicalViewState changed = WotProjectionViewBuilder.PrepareCanonicalGraph(context, initial,
                [harness.GraphRequest("child", child, [number, product], [])], []).State;

            await harness.PublishCanonicalBatchAsync(changed, registration, immediate).ConfigureAwait(false);

            ByteString continuation = first.ContinuationPoint;
            do
            {
                BrowseNextResponse response = await harness.Session.BrowseNextAsync(
                    null, false, [continuation], CancellationToken.None).ConfigureAwait(false);
                BrowseResult next = response.Results[0];
                if (immediate)
                {
                    Assert.That(next.StatusCode, Is.EqualTo(StatusCodes.BadContinuationPointInvalid));
                    Assert.That(next.References.IsEmpty, Is.True);
                    break;
                }
                Assert.That(next.StatusCode, Is.EqualTo(StatusCodes.Good));
                retained.AddRange(next.References.ToArray()!.Select(harness.Target));
                continuation = next.ContinuationPoint;
            }
            while (!continuation.IsNull && continuation.Length != 0);
            if (!immediate)
            {
                Assert.That(retained, Is.EquivalentTo(new[] { reading, number, product }));
            }
            BrowseResult current = await harness.BrowseInViewAsync(child, child, 2).ConfigureAwait(false);
            Assert.That(current.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(current.References.ToArray()!.Select(harness.Target),
                Is.EquivalentTo(new[] { number, product }));
            Assert.That((await harness.BrowseInViewAsync(child, child, 1).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.BadViewVersionInvalid));
            Assert.That((await harness.Session.ReadValueAsync(reading).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.Good));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task StockViewParticipantStagesTheCompleteGraphWithoutLiveEffectsAsync(bool publish)
        {
            await using NativeHarness harness = await NativeHarness.CreateAsync(withGraphResources: true)
                .ConfigureAwait(false);
            IWotViewProjectionHost viewHost = harness.Views;
            Assert.That(viewHost, Is.AssignableTo<IWotPreparedViewProjectionHost>());
            var host = (IWotPreparedViewProjectionHost)viewHost;
            Assert.That(host.SupportsPreparedPublication, Is.True);
            NodeId child = harness.Identity("urn:c2:native-views", "Child");
            NodeId reading = Ua.VariableIds.Server_ServerStatus_StartTime;
            WotRegistrySnapshot snapshot = harness.Registry.Current;
            int namespaceCount = harness.NamespaceUris.Count;
            var originalReferences = (await harness.BrowseAsync(
                Ua.ObjectIds.ViewsFolder, Ua.ReferenceTypeIds.Organizes).ConfigureAwait(false))
                .ToArray()!.Select(reference => (reference.ReferenceTypeId, reference.IsForward, reference.NodeId,
                    reference.BrowseName, reference.NodeClass, reference.TypeDefinition)).ToArray();
            var expected = new WotCommittedPublicationState(snapshot);
            IWotPreparedViewPublication candidate = await host.PrepareAsync(
                [harness.GraphRequest("child", child, [reading], [])], [], expected).ConfigureAwait(false);
            await using (candidate.ConfigureAwait(false))
            {
                IWotPreparedProjectionPublication prepared = await harness.SourceHost.PrepareAsync([], candidate)
                    .ConfigureAwait(false);
                await using (prepared.ConfigureAwait(false))
                {
                    WotPreparedViewGraphState graph = prepared.ViewGraph!;
                    Assert.That(graph, Is.Not.Null);
                    Assert.That(graph.AffectedResourceXids.ToArray(),
                        Is.EqualTo(new[] { harness.ResourceXid("child") }));
                    Assert.That(graph.Views.ToList().Single().ViewNodeId, Is.EqualTo(child));
                    Assert.That(harness.Registry.Current, Is.SameAs(snapshot));
                    Assert.That(harness.NamespaceUris.Count, Is.EqualTo(namespaceCount));
                    Assert.That((await harness.BrowseAsync(Ua.ObjectIds.ViewsFolder, Ua.ReferenceTypeIds.Organizes)
                        .ConfigureAwait(false)).ToArray()!.Select(reference =>
                            (reference.ReferenceTypeId, reference.IsForward, reference.NodeId,
                                reference.BrowseName, reference.NodeClass, reference.TypeDefinition)),
                        Is.EqualTo(originalReferences));
                    ReadResponse hidden = await harness.Session.ReadAsync(
                        null, 0, TimestampsToReturn.Neither,
                        [new ReadValueId { NodeId = child, AttributeId = Attributes.NodeClass }],
                        CancellationToken.None).ConfigureAwait(false);
                    Assert.That(hidden.Results[0].StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                    Assert.That(() => candidate.BindPreparedRegistrations([harness.RegistryRegistration]),
                        Throws.ArgumentException);
                    if (publish)
                    {
                        WotResource resource = snapshot.FindResourceByXid(harness.ResourceXid("child"))!;
                        uint generation = snapshot.RefreshGeneration + 1;
                        var projection = new WotResourceProjection(
                            resource.GroupId, resource.ResourceId, WoTLoadStateEnum.Active,
                            resource.DefaultVersionId, generation, 1, child,
                            resource.Validation, resource.Diagnostics, DateTime.UtcNow);
                        IWotPreparedRegistryPublication metadata = await harness.Registry.PreparePublicationAsync(
                            snapshot, [projection], generation, graph.CanonicalViewGraphState).ConfigureAwait(false);
                        await using (metadata.ConfigureAwait(false))
                        {
                            var committed = new WotCommittedPublicationState(metadata.IntendedSnapshot, graph.Views);
                            await prepared.CommitAsync(metadata.DecideAsync, () =>
                            {
                                candidate.OnPublished(committed);
                                metadata.Publish();
                            }).ConfigureAwait(false);
                        }
                        Assert.That(prepared.CleanupFailure, Is.Null);
                        Assert.That(await harness.ReadViewVersionAsync(child).ConfigureAwait(false), Is.EqualTo(1u));
                        Assert.That(harness.Registry.Current.CanonicalViewGraphState,
                            Is.EqualTo(graph.CanonicalViewGraphState));
                        Assert.That((await harness.BrowseAsync(child, Ua.ReferenceTypeIds.Organizes)
                            .ConfigureAwait(false)).ToArray()!.Select(harness.Target),
                            Is.EqualTo(new[] { reading }));
                    }
                }
            }
            if (!publish)
            {
                Assert.That(harness.Registry.Current, Is.SameAs(snapshot));
                Assert.That((await harness.BrowseAsync(Ua.ObjectIds.ViewsFolder, Ua.ReferenceTypeIds.Organizes)
                    .ConfigureAwait(false)).ToArray()!.Select(reference =>
                        (reference.ReferenceTypeId, reference.IsForward, reference.NodeId,
                            reference.BrowseName, reference.NodeClass, reference.TypeDefinition)),
                    Is.EqualTo(originalReferences));
                IWotPreparedViewPublication retry = await host.PrepareAsync(
                    [harness.GraphRequest("child", child, [reading], [])], [], expected).ConfigureAwait(false);
                await retry.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task StockViewPreparationFreezesCallerPlansBeforeWaitingForAdmissionAsync()
        {
            await using NativeHarness harness = await NativeHarness.CreateAsync(withGraphResources: true)
                .ConfigureAwait(false);
            LifecycleWotViewProjectionHost host = harness.Views;
            NodeId child = harness.Identity("urn:c2:native-views", "Child");
            NodeId reading = Ua.VariableIds.Server_ServerStatus_StartTime;
            NodeId other = Ua.VariableIds.Server_ServerStatus_BuildInfo_BuildNumber;
            var expected = new WotCommittedPublicationState(harness.Registry.Current);
            IWotPreparedViewPublication first = await host.PrepareAsync(
                [harness.GraphRequest("child", child, [reading], [])], [], expected).ConfigureAwait(false);
            NodeId[] members = [reading];
            Task<IWotPreparedViewPublication> waiting;
            try
            {
                waiting = host.PrepareAsync(
                    [harness.GraphRequest("child", child, members, [])], [], expected).AsTask();
                Assert.That(waiting.IsCompleted, Is.False);
                members[0] = other;
            }
            finally
            {
                await first.DisposeAsync().ConfigureAwait(false);
            }
            IWotPreparedViewPublication candidate = await waiting.ConfigureAwait(false);
            await using (candidate.ConfigureAwait(false))
            {
                IWotPreparedProjectionPublication prepared = await harness.SourceHost.PrepareAsync([], candidate)
                    .ConfigureAwait(false);
                await using (prepared.ConfigureAwait(false))
                {
                    WotCanonicalViewState graph = WotCanonicalViewState.Restore(
                        prepared.ViewGraph!.CanonicalViewGraphState,
                        new WotCanonicalViewGraphContext(harness.LogicalServerUri, Namespaces.WotCon,
                            harness.NamespaceUris, [new(reading, NodeClass.Variable), new(other, NodeClass.Variable)]));
                    Assert.That(graph.Views.ToList().Single().Membership.ToArray(),
                        Is.EqualTo(new[] { new ExpandedNodeId(reading) }));
                }
            }
            Assert.That(harness.Registry.Current, Is.SameAs(expected.RegistrySnapshot));
        }

        [Test]
        public async Task CanonicalCandidateRetirementAndRestartRetainSharedIdentityAsync()
        {
            ByteString payload;
            ExpandedNodeId childIdentity;
            ExpandedNodeId wrapperIdentity;
            ushort originalNamespace;
            await using (NativeHarness harness = await NativeHarness.CreateAsync(withGraphResources: true)
                .ConfigureAwait(false))
            {
                NodeId child = harness.Identity("urn:c2:native-views", "Child");
                NodeId left = harness.Identity("urn:c2:native-views", "Left");
                NodeId right = harness.Identity("urn:c2:native-views", "Right");
                originalNamespace = child.NamespaceIndex;
                var context = harness.GraphContext(
                    [new(Ua.VariableIds.Server_ServerStatus_StartTime, NodeClass.Variable)]);
                WotCanonicalViewState initial = WotProjectionViewBuilder.PrepareCanonicalGraph(context, null,
                    [
                        harness.GraphRequest("child", child, [Ua.VariableIds.Server_ServerStatus_StartTime], []),
                        harness.GraphRequest("left", left, [], [new(harness.ResourceXid("child"), "Group")]),
                        harness.GraphRequest("right", right, [], [new(harness.ResourceXid("child"), "Group")])
                    ], []).State;
                childIdentity = initial.Views.ToArray()!.Single(view =>
                    view.ResourceXid == harness.ResourceXid("child")).ViewNodeId;
                wrapperIdentity = initial.Nodes.ToArray()!.Single(node =>
                    node.ResourceXid == harness.ResourceXid("right") &&
                    node.Role == WotCanonicalViewNodeRole.Group).NodeId;
                NodeManagerRegistration registration = await harness.PublishCanonicalAsync(initial).ConfigureAwait(false);
                WotCanonicalViewState remaining = WotProjectionViewBuilder.PrepareCanonicalGraph(context, initial,
                    [], [harness.ResourceXid("child"), harness.ResourceXid("left")]).State;

                registration = await harness.PublishCanonicalAsync(remaining, registration).ConfigureAwait(false);

                ArrayOf<ReferenceDescription> visible = await harness.BrowseAsync(
                    Ua.ObjectIds.ViewsFolder, Ua.ReferenceTypeIds.Organizes).ConfigureAwait(false);
                Assert.That(visible.ToArray()!.Select(harness.Target), Does.Contain(child).And.Contain(right));
                Assert.That(visible.ToArray()!.Select(harness.Target), Does.Not.Contain(left));
                Assert.That(await harness.ReadViewVersionAsync(child).ConfigureAwait(false), Is.EqualTo(1u));
                Assert.That(await harness.ReadViewVersionAsync(right).ConfigureAwait(false), Is.EqualTo(1u));
                ArrayOf<ReferenceDescription> wrappers = await harness.BrowseAsync(right, Ua.ReferenceTypeIds.Organizes)
                    .ConfigureAwait(false);
                Assert.That(wrappers.ToArray()!.Select(harness.Target),
                    Is.EquivalentTo(new[] { ExpandedNodeId.ToNodeId(wrapperIdentity, harness.NamespaceUris) }));
                WotCanonicalViewState retired = WotProjectionViewBuilder.PrepareCanonicalGraph(
                    context, remaining, [], [harness.ResourceXid("right")]).State;
                await harness.PublishCanonicalAsync(retired, registration).ConfigureAwait(false);
                visible = await harness.BrowseAsync(Ua.ObjectIds.ViewsFolder, Ua.ReferenceTypeIds.Organizes)
                    .ConfigureAwait(false);
                Assert.That(visible.ToArray()!.Select(harness.Target), Does.Not.Contain(child));
                Assert.That(visible.ToArray()!.Select(harness.Target), Does.Not.Contain(right));
                Assert.That(retired.Views, Has.Count.EqualTo(3));
                Assert.That(retired.Views.ToArray()!.All(view => !view.Active && view.ViewVersion == 1), Is.True);
                NodeId hasProjection = ExpandedNodeId.ToNodeId(ReferenceTypeIds.HasWoTProjection, harness.NamespaceUris);
                Assert.That(await harness.BrowseAsync(harness.ResourceNodeId("child"), hasProjection)
                    .ConfigureAwait(false), Is.Empty);
                DataValue source = await harness.Session.ReadValueAsync(Ua.VariableIds.Server_ServerStatus_StartTime)
                    .ConfigureAwait(false);
                Assert.That(source.StatusCode, Is.EqualTo(StatusCodes.Good));
                payload = retired.ToByteString();
            }
            await using NativeHarness restarted = await NativeHarness.CreateAsync(
                withGraphResources: true, rebaseNamespaces: true).ConfigureAwait(false);
            NodeId restartedChild = restarted.Identity("urn:c2:native-views", "Child");
            NodeId restartedRight = restarted.Identity("urn:c2:native-views", "Right");
            Assert.That(restartedChild.NamespaceIndex, Is.Not.EqualTo(originalNamespace));
            var restoredContext = restarted.GraphContext(
                [new(Ua.VariableIds.Server_ServerStatus_StartTime, NodeClass.Variable)]);
            WotCanonicalViewState restored = WotCanonicalViewState.Restore(payload, restoredContext);
            WotCanonicalViewState revived = WotProjectionViewBuilder.PrepareCanonicalGraph(restoredContext, restored,
                [restarted.GraphRequest("right", restartedRight, [], [new(restarted.ResourceXid("child"), "Group")])],
                []).State;

            await restarted.PublishCanonicalAsync(revived).ConfigureAwait(false);

            Assert.That(revived.Views.ToArray()!.Single(view =>
                view.ResourceXid == restarted.ResourceXid("child")).ViewNodeId, Is.EqualTo(childIdentity));
            Assert.That(await restarted.ReadViewVersionAsync(restartedChild).ConfigureAwait(false), Is.EqualTo(1u));
            Assert.That(await restarted.ReadViewVersionAsync(restartedRight).ConfigureAwait(false), Is.EqualTo(1u));
            ArrayOf<ReferenceDescription> restoredWrappers = await restarted.BrowseAsync(
                restartedRight, Ua.ReferenceTypeIds.Organizes).ConfigureAwait(false);
            Assert.That(restoredWrappers.ToArray()!.Select(restarted.Target),
                Is.EquivalentTo(new[] { ExpandedNodeId.ToNodeId(wrapperIdentity, restarted.NamespaceUris) }));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CanonicalCandidateRejectsSourceMismatchWithoutPublishingAsync(bool wrongClass)
        {
            await using NativeHarness harness = await NativeHarness.CreateAsync(withGraphResources: true)
                .ConfigureAwait(false);
            NodeId child = harness.Identity("urn:c2:native-views", "Child");
            NodeId reading = Ua.VariableIds.Server_ServerStatus_StartTime;
            WotViewProjectionRequest request = harness.GraphRequest("child", child, [reading], []);
            WotCanonicalViewState initial = WotProjectionViewBuilder.PrepareCanonicalGraph(
                harness.GraphContext([new(reading, NodeClass.Variable)]), null, [request], []).State;
            NodeManagerRegistration registration = await harness.PublishCanonicalAsync(initial).ConfigureAwait(false);
            WotCanonicalViewState candidate = WotProjectionViewBuilder.PrepareCanonicalGraph(
                harness.GraphContext([new(reading, wrongClass ? NodeClass.Object : NodeClass.Variable, wrongClass)]),
                initial, [], []).State;
            ServiceResultException? rejection = null;

            try
            {
                await harness.PublishCanonicalAsync(candidate, registration).ConfigureAwait(false);
            }
            catch (ServiceResultException exception)
            {
                rejection = exception;
            }

            Assert.That(rejection, Is.Not.Null);
            Assert.That(rejection!.StatusCode,
                Is.EqualTo(wrongClass ? StatusCodes.BadNodeClassInvalid : StatusCodes.BadNodeIdUnknown));
            Assert.That(harness.IsCurrent(registration), Is.True);
            Assert.That(await harness.ReadViewVersionAsync(child).ConfigureAwait(false), Is.EqualTo(1u));
            ArrayOf<ReferenceDescription> members = await harness.BrowseAsync(child, Ua.ReferenceTypeIds.Organizes)
                .ConfigureAwait(false);
            Assert.That(members.ToArray()!.Select(harness.Target), Is.EquivalentTo(new[] { reading }));
        }

        [Test]
        public async Task CanonicalFactoryRejectsAnUnrelatedPreviousRegistrationAsync()
        {
            await using NativeHarness harness = await NativeHarness.CreateAsync(withGraphResources: true)
                .ConfigureAwait(false);
            WotCanonicalViewState state = WotProjectionViewBuilder.PrepareCanonicalGraph(
                harness.GraphContext([]), null,
                [harness.GraphRequest("child", harness.ResourceNodeId("right"), [], [])], []).State;

            Assert.That(() => WotProjectionViewBuilder.CreateCanonicalNodeManagerFactory(
                state, harness.RegistryRegistration), Throws.ArgumentException);

            Assert.That(harness.IsCurrent(harness.RegistryRegistration), Is.True);
            ReadResponse read = await harness.Session.ReadAsync(
                null, 0, TimestampsToReturn.Neither,
                [new ReadValueId { NodeId = harness.ResourceNodeId("right"), AttributeId = Attributes.NodeClass }],
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(read.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(read.Results[0].WrappedValue.TryGetValue(out int nodeClass), Is.True);
            Assert.That(nodeClass, Is.EqualTo((int)NodeClass.Object));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CanonicalFactoryRejectsAnotherAllocationAuthorityAsync(bool differentServer)
        {
            await using NativeHarness harness = await NativeHarness.CreateAsync(withGraphResources: true)
                .ConfigureAwait(false);
            NodeId child = harness.Identity("urn:c2:native-views", "Child");
            WotViewProjectionRequest request = harness.GraphRequest("child", child, [], []);
            WotCanonicalViewState initial = WotProjectionViewBuilder.PrepareCanonicalGraph(
                harness.GraphContext([]), null, [request], []).State;
            NodeManagerRegistration registration = await harness.PublishCanonicalAsync(initial).ConfigureAwait(false);
            var foreignContext = new WotCanonicalViewGraphContext(
                differentServer ? "urn:c2:foreign-server" : "urn:c2:native-server",
                differentServer ? "urn:c2:native-allocation" : "urn:c2:foreign-allocation",
                harness.NamespaceUris, []);
            WotCanonicalViewState foreign = WotProjectionViewBuilder.PrepareCanonicalGraph(
                foreignContext, null, [request], []).State;
            int namespaceCount = harness.NamespaceUris.Count;

            Assert.That(() => WotProjectionViewBuilder.CreateCanonicalNodeManagerFactory(foreign, registration),
                Throws.ArgumentException);

            Assert.That(harness.IsCurrent(registration), Is.True);
            Assert.That(harness.NamespaceUris.Count, Is.EqualTo(namespaceCount));
            Assert.That(await harness.ReadViewVersionAsync(child).ConfigureAwait(false), Is.EqualTo(1u));
        }

        [Test]
        public async Task CanonicalFactoryRejectsAPreviousOwnerFromAnotherServerInstanceAsync()
        {
            await using NativeHarness original = await NativeHarness.CreateAsync(withGraphResources: true)
                .ConfigureAwait(false);
            NodeId child = original.Identity("urn:c2:native-views", "Child");
            WotCanonicalViewState state = WotProjectionViewBuilder.PrepareCanonicalGraph(
                original.GraphContext([]), null, [original.GraphRequest("child", child, [], [])], []).State;
            NodeManagerRegistration registration = await original.PublishCanonicalAsync(state).ConfigureAwait(false);
            await using NativeHarness foreign = await NativeHarness.CreateAsync(withGraphResources: true)
                .ConfigureAwait(false);
            NodeId foreignChild = foreign.Identity("urn:c2:native-views", "Child");
            int namespaceCount = foreign.NamespaceUris.Count;

            await Assert.ThatAsync(async () => await foreign.AddCanonicalAsync(
                state, registration).ConfigureAwait(false), Throws.ArgumentException).ConfigureAwait(false);

            Assert.That(original.IsCurrent(registration), Is.True);
            Assert.That(foreign.NamespaceUris.Count, Is.EqualTo(namespaceCount));
            ReadResponse read = await foreign.Session.ReadAsync(
                null, 0, TimestampsToReturn.Neither,
                [new ReadValueId { NodeId = foreignChild, AttributeId = Attributes.NodeClass }],
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(read.Results[0].StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            Assert.That(await original.ReadViewVersionAsync(child).ConfigureAwait(false), Is.EqualTo(1u));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CanonicalCandidateCannotReassignAPreviousManagersNodeAsync(bool changeRole)
        {
            await using NativeHarness harness = await NativeHarness.CreateAsync(withGraphResources: true)
                .ConfigureAwait(false);
            NodeId child = harness.Identity("urn:c2:native-views", "Child");
            NodeId left = harness.Identity("urn:c2:native-views", "Left");
            var context = harness.GraphContext([]);
            WotCanonicalViewState initial = WotProjectionViewBuilder.PrepareCanonicalGraph(context, null,
                [
                    harness.GraphRequest("child", child, [], []),
                    harness.GraphRequest("left", left, [], [new(harness.ResourceXid("child"), "Group")])
                ], []).State;
            NodeManagerRegistration registration = await harness.PublishCanonicalAsync(initial).ConfigureAwait(false);
            NodeId occupied = changeRole
                ? ExpandedNodeId.ToNodeId(initial.Nodes.ToArray()!.Single(
                    node => node.Role == WotCanonicalViewNodeRole.Group).NodeId, harness.NamespaceUris)
                : child;
            WotCanonicalViewState candidate = WotProjectionViewBuilder.PrepareCanonicalGraph(
                harness.GraphContext([]), null, [harness.GraphRequest("right", occupied, [], [])], []).State;
            ServiceResultException? rejection = null;

            try
            {
                await harness.PublishCanonicalAsync(candidate, registration).ConfigureAwait(false);
            }
            catch (ServiceResultException exception)
            {
                rejection = exception;
            }

            Assert.That(rejection, Is.Not.Null);
            Assert.That(rejection!.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdExists));
            Assert.That(harness.IsCurrent(registration), Is.True);
            Assert.That(await harness.ReadViewVersionAsync(child).ConfigureAwait(false), Is.EqualTo(1u));
            Assert.That(await harness.ReadViewVersionAsync(left).ConfigureAwait(false), Is.EqualTo(1u));
        }

        private static WotViewProjectionPlan LegacyPlan(uint version)
        {
            return new WotViewProjectionPlan(
                "urn:canonical-view-test", WotDocumentKind.ThingDescription, [], [], version, []);
        }

        private sealed class NativeHarness : IAsyncDisposable
        {
            private NativeHarness()
            {
                m_directory = Path.Combine(
                    Path.GetTempPath(), nameof(WotProjectionViewCanonicalLiveTests), Guid.NewGuid().ToString("N"));
                m_fixture = new ServerFixture<ReferenceServer>(context => new ReferenceServer(context))
                {
                    UriScheme = Utils.UriSchemeOpcTcp,
                    AutoAccept = true,
                    SecurityNone = false
                };
            }

            public ISession Session { get; private set; } = null!;

            public LifecycleWotViewProjectionHost Views { get; private set; } = null!;

            public ushort NamespaceIndex { get; private set; }

            public NamespaceTable NamespaceUris => m_server!.CurrentInstance.NamespaceUris;

            public string LogicalServerUri => m_server!.CurrentInstance.ServerUris.GetString(0)!;

            public WotRegistryService Registry => m_registry!;

            public LifecycleWotProjectionHost SourceHost => new(m_server!.NodeManagerLifecycle);

            public NodeManagerRegistration RegistryRegistration { get; private set; } = null!;

            public static async Task<NativeHarness> CreateAsync(
                bool withGraphResources = false, bool rebaseNamespaces = false)
            {
                var harness = new NativeHarness();
                try
                {
                    await harness.StartAsync(withGraphResources, rebaseNamespaces).ConfigureAwait(false);
                    return harness;
                }
                catch
                {
                    await harness.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
            }

            public NodeId Identity(string namespaceUri, string identifier)
            {
                return new NodeId(
                    identifier, m_server!.CurrentInstance.NamespaceUris.GetIndexOrAppend(namespaceUri));
            }

            public string ResourceXid(string resource)
            {
                return m_registry!.Current.FindResource(WotRegistryGroups.ThingDescriptions, resource)!.Xid;
            }

            public NodeId ResourceNodeId(string resource)
            {
                return new NodeId("WoTRegistry/groups/thingdescriptions/resources/" + resource, NamespaceIndex);
            }

            public WotCanonicalViewGraphContext GraphContext(ArrayOf<WotCanonicalViewSource> sources)
            {
                return new WotCanonicalViewGraphContext(
                    "urn:c2:native-server", "urn:c2:native-allocation", NamespaceUris, sources);
            }

            public WotViewProjectionRequest GraphRequest(
                string resource, NodeId view, ArrayOf<NodeId> members, ArrayOf<WotCanonicalViewLink> links)
            {
                return new WotViewProjectionRequest(
                    "closure", ResourceXid(resource), ResourceNodeId(resource), view,
                    WotViewProjectionPlan.CreateCanonical("urn:c2:scenario", WotDocumentKind.ThingDescription,
                        members, links));
            }

            public async Task<NodeManagerRegistration> PublishCanonicalAsync(
                WotCanonicalViewState state, NodeManagerRegistration? previous = null)
            {
                IAsyncNodeManagerFactory factory = WotProjectionViewBuilder.CreateCanonicalNodeManagerFactory(state, previous);
                return previous is null
                    ? await m_server!.NodeManagerLifecycle.AddAsync(factory, callerContext: null).ConfigureAwait(false)
                    : await m_server!.NodeManagerLifecycle.ShadowReloadAsync(previous, factory).ConfigureAwait(false);
            }

            public ValueTask<NodeManagerRegistration> AddCanonicalAsync(
                WotCanonicalViewState state, NodeManagerRegistration previous)
            {
                return m_server!.NodeManagerLifecycle.AddAsync(
                    WotProjectionViewBuilder.CreateCanonicalNodeManagerFactory(state, previous), callerContext: null);
            }

            public async Task PublishCanonicalBatchAsync(
                WotCanonicalViewState state, NodeManagerRegistration previous, bool immediate)
            {
                var lifecycle = (INodeManagerBatchLifecycle)m_server!.NodeManagerLifecycle;
                IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                    [NodeManagerBatchChange.Replace(previous,
                        WotProjectionViewBuilder.CreateCanonicalNodeManagerFactory(state, previous), immediate)])
                    .ConfigureAwait(false);
                await using (prepared.ConfigureAwait(false))
                {
                    NodeManagerBatchResult result = await prepared.CommitAsync(_ => default).ConfigureAwait(false);
                    Assert.That(result.CleanupFailure, Is.Null);
                }
            }

            public bool IsCurrent(NodeManagerRegistration registration)
            {
                return m_server!.NodeManagerLifecycle.Registrations.ToArray()!.Any(
                    current => ReferenceEquals(current, registration));
            }

            public async Task<BrowseResult> BrowseInViewAsync(
                NodeId nodeId, NodeId viewId, uint version, uint maximumReferences = 0)
            {
                BrowseResponse response = await Session.BrowseAsync(
                    null, new ViewDescription { ViewId = viewId, ViewVersion = version }, maximumReferences,
                    [
                        new BrowseDescription
                        {
                            NodeId = nodeId,
                            ReferenceTypeId = Ua.ReferenceTypeIds.Organizes,
                            BrowseDirection = BrowseDirection.Forward,
                            IncludeSubtypes = false,
                            ResultMask = (uint)BrowseResultMask.All
                        }
                    ], CancellationToken.None).ConfigureAwait(false);
                Assert.That(response.Results, Has.Count.EqualTo(1));
                return response.Results[0];
            }

            public NodeId Target(ReferenceDescription reference)
            {
                return ExpandedNodeId.ToNodeId(reference.NodeId, NamespaceUris);
            }

            public async Task<ArrayOf<ReferenceDescription>> BrowseAsync(
                NodeId nodeId, NodeId referenceType, BrowseDirection direction = BrowseDirection.Forward)
            {
                BrowseResponse response = await Session.BrowseAsync(
                    null, new ViewDescription(), 0,
                    [
                        new BrowseDescription
                        {
                            NodeId = nodeId,
                            ReferenceTypeId = referenceType,
                            IncludeSubtypes = false,
                            BrowseDirection = direction,
                            ResultMask = (uint)BrowseResultMask.All
                        }
                    ], CancellationToken.None).ConfigureAwait(false);
                Assert.That(response.Results, Has.Count.EqualTo(1));
                Assert.That(response.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                return response.Results[0].References;
            }

            public async Task<uint> ReadViewVersionAsync(NodeId viewId)
            {
                NodeId propertyId = await ViewVersionIdAsync(viewId).ConfigureAwait(false);
                DataValue value = await Session.ReadValueAsync(propertyId).ConfigureAwait(false);
                Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(value.WrappedValue.TryGetValue(out uint version), Is.True);
                return version;
            }

            public async Task<NodeId> ViewVersionIdAsync(NodeId viewId)
            {
                BrowseResponse browse = await Session.BrowseAsync(
                    null, new ViewDescription(), 0,
                    [
                        new BrowseDescription
                        {
                            NodeId = viewId,
                            BrowseDirection = BrowseDirection.Forward,
                            ReferenceTypeId = Ua.ReferenceTypeIds.HasProperty,
                            IncludeSubtypes = false,
                            ResultMask = (uint)BrowseResultMask.All
                        }
                    ], CancellationToken.None).ConfigureAwait(false);
                Assert.That(browse.Results, Has.Count.EqualTo(1));
                Assert.That(browse.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                ReferenceDescription[] references = browse.Results[0].References.ToArray()
                    ?? throw new InvalidOperationException("The native View returned no property references.");
                ReferenceDescription property = references.Single(
                    reference => reference.BrowseName.Name == "ViewVersion");
                return ExpandedNodeId.ToNodeId(property.NodeId, Session.NamespaceUris);
            }

            public async ValueTask DisposeAsync()
            {
                try
                {
                    if (Session is not null)
                    {
                        await Session.CloseAsync().ConfigureAwait(false);
                    }
                }
                finally
                {
                    try
                    {
                        Session?.Dispose();
                    }
                    finally
                    {
                        try
                        {
                            if (m_client is not null)
                            {
                                await m_client.DisposeAsync().ConfigureAwait(false);
                            }
                        }
                        finally
                        {
                            try
                            {
                                Views?.Dispose();
                            }
                            finally
                            {
                                try
                                {
                                    await m_fixture.StopAsync().ConfigureAwait(false);
                                }
                                finally
                                {
                                    try
                                    {
                                        m_coordinator?.Dispose();
                                    }
                                    finally
                                    {
                                        try
                                        {
                                            m_registry?.Dispose();
                                        }
                                        finally
                                        {
                                            m_store?.Dispose();
                                            try
                                            {
                                                m_server?.Dispose();
                                            }
                                            finally
                                            {
                                                if (Directory.Exists(m_directory))
                                                {
                                                    Directory.Delete(m_directory, recursive: true);
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            private async Task StartAsync(bool withGraphResources, bool rebaseNamespaces)
            {
                TestContext.Out.WriteLine(
                    $"C2_RUNTIME framework={RuntimeInformation.FrameworkDescription};clr={Environment.Version};" +
                    $"serverGC={GCSettings.IsServerGC};architecture={RuntimeInformation.ProcessArchitecture}");
                m_server = await m_fixture.StartAsync(m_directory).ConfigureAwait(false);
                if (rebaseNamespaces)
                {
                    m_server.CurrentInstance.NamespaceUris.GetIndexOrAppend("urn:c2:restart-padding");
                }
                m_store = new FileWotRegistryStore(Path.Combine(m_directory, "registry"));
                m_registry = new WotRegistryService(m_store);
                await m_registry.InitializeAsync().ConfigureAwait(false);
                if (withGraphResources)
                {
                    foreach (string resource in new[] { "child", "left", "right" })
                    {
                        string json = "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\"," +
                            "\"id\":\"urn:c2:resource:" + resource + "\",\"title\":\"" + resource + "\"}";
                        WotRegistryMutationResult created = await m_registry.UpsertResourceAsync(
                            new WotUpsertResourceRequest
                            {
                                GroupId = WotRegistryGroups.ThingDescriptions,
                                ResourceId = resource,
                                Content = ByteString.From(Encoding.UTF8.GetBytes(json))
                            }).ConfigureAwait(false);
                        Assert.That(created.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
                    }
                }
                m_coordinator = new WotMaterializationCoordinator(
                    m_registry, new LifecycleWotProjectionHost(m_server.NodeManagerLifecycle),
                    documentConverter: withGraphResources ? new FakeWotDocumentConverter() : null);
                var options = new WotRegistryServerOptions
                {
                    AutoRefresh = false,
                    ManagementAccess = new WotManagementAccessPolicy
                    {
                        MinimumSecurityMode = MessageSecurityMode.SignAndEncrypt,
                        AllowAnonymous = true,
                        RequiredRoleId = Ua.ObjectIds.WellKnownRole_Anonymous
                    }
                };
                RegistryRegistration = await m_server.NodeManagerLifecycle.AddAsync(
                    new WotRegistryNodeManagerFactory(options, m_registry, m_coordinator), callerContext: null)
                    .ConfigureAwait(false);
                Views = new LifecycleWotViewProjectionHost(m_server.NodeManagerLifecycle);
                m_client = new ClientFixture(false, false, NUnitTelemetryContext.Create());
                await m_client.LoadClientConfigurationAsync(m_directory).ConfigureAwait(false);
                Session = await m_client.ConnectAsync(
                    new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}"),
                    SecurityPolicies.Basic256Sha256).ConfigureAwait(false);
                Assert.That(Session.ConfiguredEndpoint.Description.SecurityMode,
                    Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
                NamespaceIndex = (ushort)Session.NamespaceUris.GetIndex(Namespaces.WotCon);
                Assert.That(NamespaceIndex, Is.GreaterThan(0));
            }

            private readonly string m_directory;
            private readonly ServerFixture<ReferenceServer> m_fixture;
            private ReferenceServer? m_server;
            private ClientFixture? m_client;
            private WotRegistryService? m_registry;
            private FileWotRegistryStore? m_store;
            private WotMaterializationCoordinator? m_coordinator;
        }
    }
}
