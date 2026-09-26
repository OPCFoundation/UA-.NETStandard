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

using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NUnit.Framework;
using Opc.Ua.SpecTraceability;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Server.Materialization;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    [TestFixture]
    public sealed class WotCanonicalVectorTests
    {
        [Test]
        public void CanonicalPublicationTokensMatchTheVectors()
        {
            foreach (JsonElement test in WotSpecVectors.Cases("viewVersion"))
            {
                var namespaces = new NamespaceTable();
                ushort ns = namespaces.GetIndexOrAppend("urn:vectors:views");
                NodeId[] members = ReadMembers(test.GetProperty("members"), namespaces);
                NodeId[] previousMembers = test.TryGetProperty("previousMembers", out JsonElement old)
                    ? ReadMembers(old, namespaces) : [];
                var context = new WotCanonicalViewGraphContext("urn:vectors:server", "urn:vectors:views", namespaces,
                    members.Concat(previousMembers).Distinct()
                        .Select(member => new WotCanonicalViewSource(member, NodeClass.Object)).ToArrayOf());
                WotCanonicalViewState? previous = null;
                if (test.TryGetProperty("previousVersion", out JsonElement priorVersion))
                {
                    previous = WotProjectionViewBuilder.PrepareCanonicalGraph(
                        context, null, [Request(ns, previousMembers)], []).State;
                    JsonObject json = JsonNode.Parse(previous.ToByteString().Span)!.AsObject();
                    json["views"]![0]!["viewVersion"] = priorVersion.GetUInt32();
                    JsonObject version = json["nodes"]!.AsArray().Select(node => node!.AsObject())
                        .Single(node => node["role"]!.GetValue<int>() == (int)WotCanonicalViewNodeRole.ViewVersion);
                    version["versionValue"] = priorVersion.GetUInt32();
                    previous = WotCanonicalViewState.Parse(ByteString.From(Encoding.UTF8.GetBytes(json.ToJsonString())));
                }
                ByteString before = previous?.ToByteString() ?? default;
                WotCanonicalViewPreparation prepared = WotProjectionViewBuilder.PrepareCanonicalGraph(
                    context, previous, [Request(ns, members)], []);
                Assert.That(previous?.ToByteString() ?? default, Is.EqualTo(before),
                    "Preparation must not mutate the prior publication.");
                bool committed = !test.TryGetProperty("committed", out JsonElement decision) || decision.GetBoolean();
                WotCanonicalViewState? published = committed ? prepared.State : previous;
                uint? actual = published is null ? null : published.Views[0].ViewVersion;
                uint? expected = test.GetProperty("viewVersion").ValueKind == JsonValueKind.Null
                    ? null : test.GetProperty("viewVersion").GetUInt32();
                Assert.That(actual, Is.EqualTo(expected), test.GetProperty("id").GetString());
            }
        }

        [Test]
        public void OrganizationalRoleIdentitiesMatchTheVectors()
        {
            foreach (JsonElement test in WotSpecVectors.Cases("projectionGroups"))
            {
                var namespaces = new NamespaceTable();
                string allocation = test.GetProperty("namespaceUri").GetString()!;
                ushort ns = namespaces.GetIndexOrAppend(allocation);
                NodeId childId = ExpandedNodeId.ToNodeId(
                    ExpandedNodeId.Parse(test.GetProperty("projectionId").GetString()!), namespaces);
                NodeId parentId = ExpandedNodeId.ToNodeId(
                    ExpandedNodeId.Parse(test.GetProperty("parentId").GetString()!), namespaces);
                var context = new WotCanonicalViewGraphContext("urn:vectors:server", allocation, namespaces, []);
                WotCanonicalViewState state = WotProjectionViewBuilder.PrepareCanonicalGraph(context, null,
                    [
                        new WotViewProjectionRequest("closure", "child", new NodeId("Resource/child", ns), childId,
                            WotViewProjectionPlan.CreateCanonical(
                                "urn:vectors:scenario", WotDocumentKind.ThingDescription, [], [])),
                        new WotViewProjectionRequest("closure", "parent", new NodeId("Resource/parent", ns), parentId,
                            WotViewProjectionPlan.CreateCanonical(
                                "urn:vectors:scenario", WotDocumentKind.ThingDescription, [],
                                [new WotCanonicalViewLink("child", test.GetProperty("linkKey").GetString()!)]))
                    ], []).State;
                WotCanonicalViewNodeRole role = test.GetProperty("role").GetString() == "group"
                    ? WotCanonicalViewNodeRole.Group : WotCanonicalViewNodeRole.ProjectionRoot;
                WotCanonicalViewNode node = state.Nodes.ToList().Single(value => value.Role == role);
                Assert.That(node.NodeId, Is.EqualTo(ExpandedNodeId.Parse(test.GetProperty("nodeId").GetString()!)),
                    test.GetProperty("id").GetString());
                Assert.That(state.Views.ToList().Single(value => value.ResourceXid == "child").ViewNodeId,
                    Is.EqualTo(NodeId.ToExpandedNodeId(childId, namespaces)));
            }
        }

        private static WotViewProjectionRequest Request(ushort ns, NodeId[] members)
        {
            return new WotViewProjectionRequest("closure", "view", new NodeId("Resource/view", ns), new NodeId("View", ns),
                WotViewProjectionPlan.CreateCanonical(
                    "urn:vectors:scenario", WotDocumentKind.ThingDescription, members.ToArrayOf(), []));
        }

        private static NodeId[] ReadMembers(JsonElement members, NamespaceTable namespaces)
        {
            return members.EnumerateArray().Select(member =>
            {
                ExpandedNodeId identity = ExpandedNodeId.Parse(member.GetString()!);
                if (!string.IsNullOrEmpty(identity.NamespaceUri))
                {
                    namespaces.GetIndexOrAppend(identity.NamespaceUri);
                }
                return ExpandedNodeId.ToNodeId(identity, namespaces);
            }).ToArray();
        }
    }
}
