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
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using NUnit.Framework;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Server.Materialization;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    [TestFixture]
    [Category("WoT")]
    public sealed class WotCanonicalViewStateTests
    {
        [Test]
        public void TwoParentsShareOneCanonicalChildWithoutCopyingSourceNodes()
        {
            var namespaces = new NamespaceTable();
            ushort ns = namespaces.GetIndexOrAppend(kNamespace);
            var member = new NodeId("Reading", ns);
            var context = new WotCanonicalViewGraphContext(
                "urn:c2:logical-server", kAllocationNamespace, namespaces,
                [new WotCanonicalViewSource(member, NodeClass.Variable)]);
            WotViewProjectionRequest child = Request(
                ns, "child", "Child", [member], []);
            WotViewProjectionRequest left = Request(
                ns, "left", "Left", [], [new WotCanonicalViewLink("child", "Group")]);
            WotViewProjectionRequest right = Request(
                ns, "right", "Right", [], [new WotCanonicalViewLink("child", "Group")]);

            WotCanonicalViewPreparation prepared = WotProjectionViewBuilder.PrepareCanonicalGraph(
                context, null, [child, left, right], []);

            WotCanonicalViewState state = prepared.State;
            WotCanonicalViewNode[] nodes = state.Nodes.ToArray()
                ?? throw new InvalidOperationException("The candidate has no node map.");
            WotCanonicalViewPublication[] views = state.Views.ToArray()
                ?? throw new InvalidOperationException("The candidate has no View map.");
            Assert.That(state.Views, Has.Count.EqualTo(3));
            Assert.That(nodes.Count(node => node.Role == WotCanonicalViewNodeRole.View), Is.EqualTo(3));
            Assert.That(nodes.Count(node => node.Role == WotCanonicalViewNodeRole.Group), Is.EqualTo(2));
            Assert.That(nodes.Count(node => node.Role == WotCanonicalViewNodeRole.ProjectionRoot), Is.EqualTo(2));
            Assert.That(nodes.Any(node => node.NodeId == new ExpandedNodeId("Reading", kNamespace)), Is.False);
            Assert.That(views.Single(view => view.ResourceXid == "child").ViewNodeId,
                Is.EqualTo(new ExpandedNodeId("Child", kNamespace)));
            Assert.That(views.All(view => view.ViewVersion == 1), Is.True);
            Assert.That(prepared.AffectedResourceXids.ToArray(), Is.EquivalentTo(new[] { "child", "left", "right" }));
            Assert.That(prepared.HasChanges, Is.True);
        }

        [Test]
        public void RetirementUsesRemainingClosureAndRetainsIdentityAndTokenHistory()
        {
            var namespaces = new NamespaceTable();
            ushort ns = namespaces.GetIndexOrAppend(kNamespace);
            var member = new NodeId("Reading", ns);
            var context = new WotCanonicalViewGraphContext(
                "urn:c2:logical-server", kAllocationNamespace, namespaces,
                [new WotCanonicalViewSource(member, NodeClass.Variable)]);
            WotViewProjectionRequest child = Request(ns, "child", "Child", [member], []);
            WotViewProjectionRequest left = Request(
                ns, "left", "Left", [], [new WotCanonicalViewLink("child", "Group")]);
            WotViewProjectionRequest right = Request(
                ns, "right", "Right", [], [new WotCanonicalViewLink("child", "Group")]);
            WotCanonicalViewState initial = WotProjectionViewBuilder.PrepareCanonicalGraph(
                context, null, [child, left, right], []).State;

            WotCanonicalViewPreparation oneRemoved = WotProjectionViewBuilder.PrepareCanonicalGraph(
                context, initial, [], ["left", "child"]);

            Assert.That(View(oneRemoved.State, "left").Active, Is.False);
            Assert.That(View(oneRemoved.State, "child").Requested, Is.False);
            Assert.That(View(oneRemoved.State, "child").Active, Is.True);
            Assert.That(View(oneRemoved.State, "right").Active, Is.True);
            Assert.That(View(oneRemoved.State, "right").ViewVersion, Is.EqualTo(View(initial, "right").ViewVersion));
            WotCanonicalViewState retired = WotProjectionViewBuilder.PrepareCanonicalGraph(
                context, oneRemoved.State, [], ["right"]).State;
            Assert.That(retired.Nodes, Is.Empty);
            Assert.That(retired.References, Is.Empty);
            Assert.That(View(retired, "child").ViewNodeId, Is.EqualTo(View(initial, "child").ViewNodeId));
            Assert.That(View(retired, "child").MembershipDigest, Is.EqualTo(View(initial, "child").MembershipDigest));
            Assert.That(View(retired, "child").ViewVersion, Is.EqualTo(View(initial, "child").ViewVersion));
            var requestWithoutIdentity = new WotViewProjectionRequest(
                "closure", "right", right.ResourceNodeId, NodeId.Null, right.Plan);

            WotCanonicalViewState reactivated = WotProjectionViewBuilder.PrepareCanonicalGraph(
                context, retired, [requestWithoutIdentity], []).State;

            Assert.That(View(reactivated, "right").ViewNodeId, Is.EqualTo(new ExpandedNodeId("Right", kNamespace)));
            Assert.That(View(reactivated, "right").ViewVersion, Is.EqualTo(1));
            Assert.That(View(reactivated, "child").Active, Is.True);
            Assert.That(View(reactivated, "child").ViewNodeId, Is.EqualTo(new ExpandedNodeId("Child", kNamespace)));
            Assert.That(View(initial, "left").Active, Is.True, "Preparation must not mutate its expected publication.");
        }

        [Test]
        public void ChildMembershipChangeIncludesAncestorsAndNoOpPreservesTokens()
        {
            var namespaces = new NamespaceTable();
            ushort ns = namespaces.GetIndexOrAppend(kNamespace);
            var firstMember = new NodeId("Reading", ns);
            var secondMember = new NodeId("OtherReading", ns);
            var context = new WotCanonicalViewGraphContext(
                "urn:c2:logical-server", kAllocationNamespace, namespaces,
                [
                    new WotCanonicalViewSource(firstMember, NodeClass.Variable),
                    new WotCanonicalViewSource(secondMember, NodeClass.Variable)
                ]);
            WotCanonicalViewState original = WotProjectionViewBuilder.PrepareCanonicalGraph(
                context, null,
                [
                    Request(ns, "child", "Child", [firstMember], []),
                    Request(ns, "left", "Left", [], [new WotCanonicalViewLink("child", "Group")]),
                    Request(ns, "right", "Right", [], [new WotCanonicalViewLink("child", "Group")]),
                    Request(ns, "unrelated", "Unrelated", [], [])
                ], []).State;
            WotViewProjectionRequest update = Request(ns, "child", "Child", [secondMember], []);

            WotCanonicalViewPreparation changed = WotProjectionViewBuilder.PrepareCanonicalGraph(
                context, original, [update], []);

            Assert.That(changed.AffectedResourceXids.ToArray(),
                Is.EquivalentTo(new[] { "child", "left", "right" }));
            foreach (string resource in new[] { "child", "left", "right" })
            {
                Assert.That(View(changed.State, resource).ViewVersion, Is.EqualTo(2));
                Assert.That(View(changed.State, resource).MembershipDigest.Length, Is.EqualTo(32));
                Assert.That(View(changed.State, resource).MembershipDigest,
                    Is.Not.EqualTo(View(original, resource).MembershipDigest));
                Assert.That(View(changed.State, resource).ViewNodeId, Is.EqualTo(View(original, resource).ViewNodeId));
            }
            Assert.That(View(changed.State, "unrelated").ViewVersion, Is.EqualTo(1));
            WotCanonicalViewPreparation noOp = WotProjectionViewBuilder.PrepareCanonicalGraph(
                context, changed.State, [update], []);
            Assert.That(noOp.HasChanges, Is.False);
            Assert.That(noOp.AffectedResourceXids, Is.Empty);
            Assert.That(View(noOp.State, "left").ViewVersion, Is.EqualTo(2));
            Assert.That(View(original, "child").ViewVersion, Is.EqualTo(1));
        }

        [Test]
        public void RetiredWrapperIdentityCannotBecomeAnotherCanonicalView()
        {
            var namespaces = new NamespaceTable();
            ushort ns = namespaces.GetIndexOrAppend(kNamespace);
            namespaces.GetIndexOrAppend(kAllocationNamespace);
            var context = new WotCanonicalViewGraphContext(
                "urn:c2:logical-server", kAllocationNamespace, namespaces, []);
            WotCanonicalViewState original = WotProjectionViewBuilder.PrepareCanonicalGraph(
                context, null,
                [
                    Request(ns, "child", "Child", [], []),
                    Request(ns, "parent", "Parent", [], [new WotCanonicalViewLink("child", "Group")])
                ], []).State;
            WotCanonicalViewNode[] nodes = original.Nodes.ToArray()
                ?? throw new InvalidOperationException("The state has no node map.");
            ExpandedNodeId wrapperId = nodes.Single(node => node.Role == WotCanonicalViewNodeRole.Group).NodeId;
            WotCanonicalViewState retired = WotProjectionViewBuilder.PrepareCanonicalGraph(
                context, original, [], ["child", "parent"]).State;
            var conflicting = new WotViewProjectionRequest(
                "closure", "other", new NodeId("Resource/other", ns),
                ExpandedNodeId.ToNodeId(wrapperId, namespaces),
                WotViewProjectionPlan.CreateCanonical("urn:c2:scenario", WotDocumentKind.ThingDescription, [], []));

            Assert.That(
                () => WotProjectionViewBuilder.PrepareCanonicalGraph(context, retired, [conflicting], []),
                Throws.ArgumentException);
            Assert.That(View(retired, "child").ViewNodeId, Is.EqualTo(new ExpandedNodeId("Child", kNamespace)));
            Assert.That(retired.Nodes, Is.Empty);
        }

        [Test]
        public void SerializedRetiredGraphRestoresHistoryWithoutAliasingCallerBytes()
        {
            var namespaces = new NamespaceTable();
            ushort ns = namespaces.GetIndexOrAppend(kNamespace);
            var context = new WotCanonicalViewGraphContext(
                "urn:c2:logical-server", kAllocationNamespace, namespaces, []);
            WotViewProjectionRequest parent = Request(
                ns, "parent", "Parent", [], [new WotCanonicalViewLink("child", "Group")]);
            WotCanonicalViewState initial = WotProjectionViewBuilder.PrepareCanonicalGraph(
                context, null, [Request(ns, "child", "Child", [], []), parent], []).State;
            WotCanonicalViewState retired = WotProjectionViewBuilder.PrepareCanonicalGraph(
                context, initial, [], ["parent", "child"]).State;

            ByteString encoded = retired.ToByteString();

            Assert.That(encoded.IsNull, Is.False);
            Assert.That(encoded.Length, Is.GreaterThan(0), "An encoded empty active image is not absent history.");
            byte[] callerBytes = encoded.Span.ToArray();
            WotCanonicalViewState restored = WotCanonicalViewState.Parse(ByteString.From(callerBytes));
            callerBytes[0] = 0;
            Assert.That(restored.ToByteString(), Is.EqualTo(encoded));
            Assert.That(restored.Nodes, Is.Empty);
            Assert.That(View(restored, "child").ViewVersion, Is.EqualTo(1));
            Assert.That(View(restored, "parent").MembershipDigest,
                Is.EqualTo(View(retired, "parent").MembershipDigest));
            var withoutIdentity = new WotViewProjectionRequest(
                "closure", "parent", parent.ResourceNodeId, NodeId.Null, parent.Plan);
            WotCanonicalViewState reactivated = WotProjectionViewBuilder.PrepareCanonicalGraph(
                context, restored, [withoutIdentity], []).State;
            Assert.That(View(reactivated, "parent").ViewNodeId, Is.EqualTo(new ExpandedNodeId("Parent", kNamespace)));
            Assert.That(View(reactivated, "child").ViewNodeId, Is.EqualTo(new ExpandedNodeId("Child", kNamespace)));
            Assert.That(View(reactivated, "parent").ViewVersion, Is.EqualTo(1));
        }

        [TestCase("definitions")]
        [TestCase("sources")]
        [TestCase("views")]
        [TestCase("nodes")]
        [TestCase("references")]
        public void MissingPublicationMapFailsRatherThanInitializingFreshState(string map)
        {
            WotCanonicalViewState state = LinkedState();
            JsonObject json = JsonNode.Parse(state.ToByteString().Span)!.AsObject();
            json.Remove(map);

            Assert.That(() => WotCanonicalViewState.Parse(JsonBytes(json)), Throws.TypeOf<FormatException>());
        }

        [TestCase("zeroVersion")]
        [TestCase("wrongDigest")]
        [TestCase("wrongPropertyType")]
        [TestCase("wrongPropertyNamespace")]
        [TestCase("wrongCanonicalTarget")]
        [TestCase("missingReference")]
        [TestCase("duplicateNode")]
        [TestCase("localNamespaceIdentity")]
        [TestCase("nullMap")]
        public void ContradictoryPublicationStateIsRejected(string change)
        {
            JsonObject json = JsonNode.Parse(LinkedState().ToByteString().Span)!.AsObject();
            JsonArray nodes = json["nodes"]!.AsArray();
            JsonObject property = nodes.Select(node => node!.AsObject()).Single(
                node => node["role"]!.GetValue<int>() == (int)WotCanonicalViewNodeRole.ProjectionRoot);
            switch (change)
            {
                case "zeroVersion":
                    json["views"]![0]!["viewVersion"] = 0;
                    break;
                case "wrongDigest":
                    json["views"]![0]!["membershipDigest"] = Convert.ToBase64String(new byte[32]);
                    break;
                case "wrongPropertyType":
                    property["dataType"] = "i=12";
                    break;
                case "wrongPropertyNamespace":
                    property["browseNamespaceUri"] = kAllocationNamespace;
                    break;
                case "wrongCanonicalTarget":
                    property["nodeIdValue"] = "nsu=urn:c2:canonical;s=Unrelated";
                    break;
                case "missingReference":
                    json["references"]!.AsArray().RemoveAt(0);
                    break;
                case "duplicateNode":
                    nodes.Add(nodes[0]!.DeepClone());
                    break;
                case "localNamespaceIdentity":
                    json["definitions"]![0]!["viewNodeId"] = "ns=1;s=Child";
                    break;
                case "nullMap":
                    json["nodes"] = null;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(change));
            }

            Assert.That(() => WotCanonicalViewState.Parse(JsonBytes(json)), Throws.TypeOf<FormatException>());
        }

        [Test]
        public void UnsupportedSchemaAndAbsentPayloadAreNotLegacyFallbacks()
        {
            JsonObject json = JsonNode.Parse(LinkedState().ToByteString().Span)!.AsObject();
            json["schemaVersion"] = 99;
            Assert.That(() => WotCanonicalViewState.Parse(JsonBytes(json)), Throws.TypeOf<NotSupportedException>());
            Assert.That(() => WotCanonicalViewState.Parse(default), Throws.TypeOf<FormatException>());
            Assert.That(() => WotCanonicalViewState.Parse(ByteString.Empty), Throws.TypeOf<FormatException>());
        }

        [TestCase("server")]
        [TestCase("allocation")]
        [TestCase("missingSource")]
        [TestCase("sourceClass")]
        public void RestoreRejectsACapturedImageThatDisagreesWithPublication(string mismatch)
        {
            var namespaces = new NamespaceTable();
            ushort ns = namespaces.GetIndexOrAppend(kNamespace);
            ArrayOf<WotCanonicalViewSource> sources = mismatch == "missingSource"
                ? []
                : [new WotCanonicalViewSource(new NodeId("Reading", ns),
                    mismatch == "sourceClass" ? NodeClass.Method : NodeClass.Variable)];
            var context = new WotCanonicalViewGraphContext(
                mismatch == "server" ? "urn:c2:other-server" : "urn:c2:logical-server",
                mismatch == "allocation" ? "urn:c2:other-allocation" : kAllocationNamespace,
                namespaces, sources);
            ByteString encoded = LinkedState().ToByteString();

            Assert.That(() => WotCanonicalViewState.Restore(encoded, context), Throws.TypeOf<FormatException>());
        }

        [Test]
        public void RestoredGraphPreparationIsNamespaceIndexIndependent()
        {
            WotCanonicalViewState initial = LinkedState();
            var namespaces = new NamespaceTable();
            namespaces.GetIndexOrAppend("urn:c2:unrelated-first-namespace");
            ushort ns = namespaces.GetIndexOrAppend(kNamespace);
            var member = new NodeId("Reading", ns);
            var context = new WotCanonicalViewGraphContext(
                "urn:c2:logical-server", kAllocationNamespace, namespaces,
                [new WotCanonicalViewSource(member, NodeClass.Variable)]);

            WotCanonicalViewState restored = WotCanonicalViewState.Restore(initial.ToByteString(), context);
            WotCanonicalViewPreparation prepared = WotProjectionViewBuilder.PrepareCanonicalGraph(
                context, restored,
                [
                    Request(ns, "parent", "Parent", [], [new WotCanonicalViewLink("child", "Group")]),
                    Request(ns, "child", "Child", [member], [])
                ], []);

            Assert.That(prepared.HasChanges, Is.False);
            Assert.That(prepared.AffectedResourceXids, Is.Empty);
            Assert.That(prepared.State.ToByteString(), Is.EqualTo(initial.ToByteString()));
        }

        [Test]
        public void MembershipTokenWrapsIndependentlyOfTruncatedDigestCollision()
        {
            var namespaces = new NamespaceTable();
            ushort ns = namespaces.GetIndexOrAppend("urn:example");
            var first = new NodeId("Member1727", ns);
            var second = new NodeId("Member33300", ns);
            var context = new WotCanonicalViewGraphContext(
                "urn:c2:logical-server", kAllocationNamespace, namespaces,
                [
                    new WotCanonicalViewSource(first, NodeClass.Variable),
                    new WotCanonicalViewSource(second, NodeClass.Variable)
                ]);
            WotCanonicalViewState original = WotProjectionViewBuilder.PrepareCanonicalGraph(
                context, null, [Request(ns, "counter", "Counter", [first], [])], []).State;
            Assert.That(WotPortableIdentity.ComputeViewVersion(["nsu=urn:example;s=Member1727"]),
                Is.EqualTo(2821918161u));
            Assert.That(WotPortableIdentity.ComputeViewVersion(["nsu=urn:example;s=Member33300"]),
                Is.EqualTo(2821918161u));
            JsonObject json = JsonNode.Parse(original.ToByteString().Span)!.AsObject();
            json["views"]![0]!["viewVersion"] = uint.MaxValue;
            JsonObject versionProperty = json["nodes"]!.AsArray().Select(node => node!.AsObject()).Single(
                node => node["role"]!.GetValue<int>() == (int)WotCanonicalViewNodeRole.ViewVersion);
            versionProperty["versionValue"] = uint.MaxValue;
            WotCanonicalViewState atMaximum = WotCanonicalViewState.Parse(JsonBytes(json));

            WotCanonicalViewState changed = WotProjectionViewBuilder.PrepareCanonicalGraph(
                context, atMaximum, [Request(ns, "counter", "Counter", [second], [])], []).State;

            Assert.That(View(changed, "counter").ViewVersion, Is.EqualTo(1));
            Assert.That(View(changed, "counter").MembershipDigest.Length, Is.EqualTo(32));
            Assert.That(View(changed, "counter").MembershipDigest,
                Is.Not.EqualTo(View(atMaximum, "counter").MembershipDigest));
            Assert.That(View(atMaximum, "counter").ViewVersion, Is.EqualTo(uint.MaxValue));
        }

        [Test]
        public void SourceRetirementUpdatesAncestorsWithoutChangingUnrelatedMembership()
        {
            var namespaces = new NamespaceTable();
            ushort ns = namespaces.GetIndexOrAppend(kNamespace);
            var reading = new NodeId("Reading", ns);
            var other = new NodeId("Other", ns);
            var context = new WotCanonicalViewGraphContext(
                "urn:c2:logical-server", kAllocationNamespace, namespaces,
                [
                    new WotCanonicalViewSource(reading, NodeClass.Variable),
                    new WotCanonicalViewSource(other, NodeClass.Variable)
                ]);
            WotCanonicalViewState initial = WotProjectionViewBuilder.PrepareCanonicalGraph(
                context, null,
                [
                    Request(ns, "child", "Child", [reading], []),
                    Request(ns, "parent", "Parent", [], [new WotCanonicalViewLink("child", "Group")]),
                    Request(ns, "other", "OtherView", [other], [])
                ], []).State;
            var retiredSourceImage = new WotCanonicalViewGraphContext(
                "urn:c2:logical-server", kAllocationNamespace, namespaces,
                [new WotCanonicalViewSource(other, NodeClass.Variable)]);

            WotCanonicalViewPreparation changed = WotProjectionViewBuilder.PrepareCanonicalGraph(
                retiredSourceImage, initial, [], []);

            Assert.That(changed.AffectedResourceXids.ToArray(), Is.EquivalentTo(new[] { "child", "parent" }));
            Assert.That(View(changed.State, "child").Membership, Is.Empty);
            Assert.That(View(changed.State, "child").ViewVersion, Is.EqualTo(2));
            Assert.That(View(changed.State, "parent").ViewVersion, Is.EqualTo(2));
            Assert.That(View(changed.State, "parent").Omissions, Has.Count.EqualTo(1));
            Assert.That(View(changed.State, "other").MembershipDigest, Is.EqualTo(View(initial, "other").MembershipDigest));
            Assert.That(View(changed.State, "other").ViewVersion, Is.EqualTo(1));
            Assert.That(View(initial, "child").Membership, Has.Count.EqualTo(1));
        }

        [Test]
        public void UnauthoredIdentitySurvivesSerializedRestartAndNamespaceRebasing()
        {
            var namespaces = new NamespaceTable();
            ushort ns = namespaces.GetIndexOrAppend(kNamespace);
            var context = new WotCanonicalViewGraphContext(
                "urn:c2:logical-server", kAllocationNamespace, namespaces, []);
            WotViewProjectionPlan plan = WotViewProjectionPlan.CreateCanonical(
                "urn:c2:scenario", WotDocumentKind.ThingDescription, [], []);
            var request = new WotViewProjectionRequest(
                "closure", "unassigned", new NodeId("Resource/unassigned", ns), NodeId.Null, plan);
            WotCanonicalViewState initial = WotProjectionViewBuilder.PrepareCanonicalGraph(
                context, null, [request], []).State;
            ExpandedNodeId identity = View(initial, "unassigned").ViewNodeId;
            Assert.That(identity.IsNull, Is.False);
            Assert.That(identity.NamespaceUri, Is.EqualTo(kAllocationNamespace));
            var nextNamespaces = new NamespaceTable();
            nextNamespaces.GetIndexOrAppend("urn:c2:other");
            ushort nextNs = nextNamespaces.GetIndexOrAppend(kNamespace);
            var nextContext = new WotCanonicalViewGraphContext(
                "urn:c2:logical-server", kAllocationNamespace, nextNamespaces, []);
            WotCanonicalViewState restored = WotCanonicalViewState.Restore(initial.ToByteString(), nextContext);
            var nextRequest = new WotViewProjectionRequest(
                "new-refresh-closure", "unassigned", new NodeId("Resource/unassigned", nextNs), NodeId.Null, plan);

            WotCanonicalViewPreparation next = WotProjectionViewBuilder.PrepareCanonicalGraph(
                nextContext, restored, [nextRequest], []);

            Assert.That(next.HasChanges, Is.False);
            Assert.That(View(next.State, "unassigned").ViewNodeId, Is.EqualTo(identity));
            Assert.That(View(next.State, "unassigned").ViewVersion, Is.EqualTo(1));
        }

        [Test]
        public void CanonicalReferencesAndProjectionRootPropertiesKeepTheirRoles()
        {
            WotCanonicalViewState state = LinkedState();
            WotCanonicalViewNode[] nodes = state.Nodes.ToArray()
                ?? throw new InvalidOperationException("The node map is missing.");
            WotCanonicalViewReference[] references = state.References.ToArray()
                ?? throw new InvalidOperationException("The reference map is missing.");
            WotCanonicalViewNode group = nodes.Single(node => node.Role == WotCanonicalViewNodeRole.Group);
            WotCanonicalViewNode property = nodes.Single(node => node.Role == WotCanonicalViewNodeRole.ProjectionRoot);
            Assert.That(group.NodeClass, Is.EqualTo(NodeClass.Object));
            Assert.That(group.TypeDefinition, Is.EqualTo(ObjectTypeIds.WoTProjectionGroupType));
            Assert.That(property.BrowseName, Is.EqualTo(new WotBrowsePathElement(Namespaces.WotCon, "ProjectionRoot")));
            Assert.That(property.DataType, Is.EqualTo(new ExpandedNodeId(Ua.DataTypeIds.NodeId)));
            Assert.That(property.ValueRank, Is.EqualTo(ValueRanks.Scalar));
            Assert.That(property.AccessLevel, Is.EqualTo(AccessLevels.CurrentRead));
            Assert.That(property.NodeIdValue, Is.EqualTo(View(state, "child").ViewNodeId));
            foreach (WotCanonicalViewPublication view in state.Views)
            {
                Assert.That(references, Has.Member(new WotCanonicalViewReference(
                    view.ResourceNodeId, ReferenceTypeIds.HasWoTProjection, view.ViewNodeId, false)));
                Assert.That(references, Has.Member(new WotCanonicalViewReference(
                    view.ViewNodeId, ReferenceTypeIds.HasWoTProjection, view.ResourceNodeId, true)));
                Assert.That(references.Where(reference => reference.SourceId == view.ViewNodeId && !reference.IsInverse)
                    .All(reference => reference.ReferenceTypeId == new ExpandedNodeId(Ua.ReferenceTypeIds.Organizes) ||
                        reference.ReferenceTypeId == new ExpandedNodeId(Ua.ReferenceTypeIds.HasProperty)), Is.True);
            }
        }

        [Test]
        public void WrapperIdentityIncludesBothCanonicalNamespaceQualifiedIdentities()
        {
            var namespaces = new NamespaceTable();
            ushort first = namespaces.GetIndexOrAppend("urn:c2:first");
            ushort second = namespaces.GetIndexOrAppend("urn:c2:second");
            var context = new WotCanonicalViewGraphContext(
                "urn:c2:logical-server", kAllocationNamespace, namespaces, []);

            WotCanonicalViewState state = WotProjectionViewBuilder.PrepareCanonicalGraph(
                context, null,
                [
                    Request(first, "childA", "Child", [], []),
                    Request(second, "childB", "Child", [], []),
                    Request(first, "parentA", "Parent", [], [new WotCanonicalViewLink("childA", "Group")]),
                    Request(second, "parentB", "Parent", [], [new WotCanonicalViewLink("childB", "Group")])
                ], []).State;

            WotCanonicalViewNode[] nodes = state.Nodes.ToArray()
                ?? throw new InvalidOperationException("The node map is missing.");
            WotCanonicalViewNode[] groups = nodes.Where(node => node.Role == WotCanonicalViewNodeRole.Group).ToArray();
            Assert.That(groups, Has.Length.EqualTo(2));
            Assert.That(groups.Select(node => node.NodeId), Is.Unique);
            Assert.That(groups.All(node => node.NodeId.NamespaceUri == kAllocationNamespace), Is.True);
        }

        [TestCase("unknownSource")]
        [TestCase("unknownChild")]
        [TestCase("cycle")]
        [TestCase("duplicateKey")]
        [TestCase("sourceRole")]
        [TestCase("resourceRole")]
        [TestCase("server")]
        public void InvalidGraphPreparationFailsWithoutChangingExpectedState(string invalid)
        {
            WotCanonicalViewState previous = LinkedState();
            ByteString before = previous.ToByteString();
            var namespaces = new NamespaceTable();
            ushort ns = namespaces.GetIndexOrAppend(kNamespace);
            var member = new NodeId("Reading", ns);
            var context = new WotCanonicalViewGraphContext(
                "urn:c2:logical-server", kAllocationNamespace, namespaces,
                [
                    new WotCanonicalViewSource(member, NodeClass.Variable),
                    new WotCanonicalViewSource(Ua.ObjectIds.Server, NodeClass.Object)
                ]);
            WotViewProjectionRequest update = invalid switch
            {
                "unknownSource" => Request(ns, "child", "Child", [new NodeId("Unknown", ns)], []),
                "unknownChild" => Request(ns, "parent", "Parent", [], [new WotCanonicalViewLink("unknown", "Group")]),
                "cycle" => Request(ns, "child", "Child", [], [new WotCanonicalViewLink("parent", "Cycle")]),
                "duplicateKey" => Request(ns, "parent", "Parent", [],
                    [new WotCanonicalViewLink("child", "Group"), new WotCanonicalViewLink("child", "Group")]),
                "sourceRole" => new WotViewProjectionRequest(
                    "closure", "new", new NodeId("Resource/new", ns), member,
                    WotViewProjectionPlan.CreateCanonical("urn:c2:scenario", WotDocumentKind.ThingDescription, [], [])),
                "resourceRole" => new WotViewProjectionRequest(
                    "closure", "new", new NodeId("Resource/new", ns), new NodeId("Resource/new", ns),
                    WotViewProjectionPlan.CreateCanonical("urn:c2:scenario", WotDocumentKind.ThingDescription, [], [])),
                "server" => Request(ns, "child", "Child", [Ua.ObjectIds.Server], []),
                _ => throw new ArgumentOutOfRangeException(nameof(invalid))
            };

            Assert.That(() => WotProjectionViewBuilder.PrepareCanonicalGraph(context, previous, [update], []),
                Throws.ArgumentException);
            Assert.That(previous.ToByteString(), Is.EqualTo(before));
        }

        [TestCase("duplicateUpdate")]
        [TestCase("duplicateRemoval")]
        [TestCase("updateAndRemoval")]
        [TestCase("unknownRemoval")]
        [TestCase("duplicateSource")]
        [TestCase("invalidSourceClass")]
        [TestCase("invalidScenario")]
        [TestCase("emptyScenario")]
        [TestCase("invalidKind")]
        public void InvalidCanonicalBatchCannotChangeTheExpectedPublication(string invalid)
        {
            WotCanonicalViewState previous = LinkedState();
            ByteString before = previous.ToByteString();
            var namespaces = new NamespaceTable();
            ushort ns = namespaces.GetIndexOrAppend(kNamespace);
            var member = new NodeId("Reading", ns);
            WotViewProjectionRequest update = Request(ns, "child", "Child", [member], []);
            ArrayOf<WotViewProjectionRequest> updates = [update];
            ArrayOf<string> removals = [];
            ArrayOf<WotCanonicalViewSource> sources = [new(member, NodeClass.Variable)];
            switch (invalid)
            {
                case "duplicateUpdate":
                    updates = [update, update];
                    break;
                case "duplicateRemoval":
                    updates = [];
                    removals = ["child", "child"];
                    break;
                case "updateAndRemoval":
                    removals = ["child"];
                    break;
                case "unknownRemoval":
                    removals = ["unknown"];
                    break;
                case "duplicateSource":
                    sources = [new(member, NodeClass.Variable), new(member, NodeClass.Variable)];
                    break;
                case "invalidSourceClass":
                    sources = [new(member, NodeClass.Unspecified)];
                    break;
                case "invalidScenario":
                case "emptyScenario":
                case "invalidKind":
                    updates =
                    [
                        new WotViewProjectionRequest(
                            "closure", "child", new NodeId("Resource/child", ns), new NodeId("Child", ns),
                            WotViewProjectionPlan.CreateCanonical(
                                invalid == "emptyScenario" ? string.Empty :
                                    invalid == "invalidScenario" ? "relative" : "urn:c2:scenario",
                                invalid == "invalidKind" ? (WotDocumentKind)int.MaxValue : WotDocumentKind.ThingDescription,
                                [member], []))
                    ];
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(invalid));
            }
            var context = new WotCanonicalViewGraphContext(
                "urn:c2:logical-server", kAllocationNamespace, namespaces, sources);

            Assert.That(() => WotProjectionViewBuilder.PrepareCanonicalGraph(context, previous, updates, removals),
                Throws.ArgumentException);
            Assert.That(previous.ToByteString(), Is.EqualTo(before));
        }

        private static ByteString JsonBytes(JsonObject json)
        {
            return ByteString.From(Encoding.UTF8.GetBytes(json.ToJsonString()));
        }

        private static WotCanonicalViewState LinkedState()
        {
            var namespaces = new NamespaceTable();
            ushort ns = namespaces.GetIndexOrAppend(kNamespace);
            var member = new NodeId("Reading", ns);
            var context = new WotCanonicalViewGraphContext(
                "urn:c2:logical-server", kAllocationNamespace, namespaces,
                [new WotCanonicalViewSource(member, NodeClass.Variable)]);
            return WotProjectionViewBuilder.PrepareCanonicalGraph(
                context, null,
                [
                    Request(ns, "child", "Child", [member], []),
                    Request(ns, "parent", "Parent", [], [new WotCanonicalViewLink("child", "Group")])
                ], []).State;
        }

        private static WotCanonicalViewPublication View(WotCanonicalViewState state, string resource)
        {
            WotCanonicalViewPublication[] views = state.Views.ToArray()
                ?? throw new InvalidOperationException("The state has no View map.");
            return views.Single(view => view.ResourceXid == resource);
        }

        private static WotViewProjectionRequest Request(
            ushort ns,
            string resource,
            string view,
            ArrayOf<NodeId> members,
            ArrayOf<WotCanonicalViewLink> links)
        {
            WotViewProjectionPlan plan = WotViewProjectionPlan.CreateCanonical(
                "urn:c2:scenario", WotDocumentKind.ThingDescription, members, links);
            return new WotViewProjectionRequest(
                "closure", resource, new NodeId("Resource/" + resource, ns), new NodeId(view, ns), plan);
        }

        private const string kNamespace = "urn:c2:canonical";
        private const string kAllocationNamespace = "urn:c2:view-allocation";
    }
}
