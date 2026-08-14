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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Server.Materialization;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    /// <summary>
    /// Exercises <see cref="WotProjectionViewBuilder"/> against the WoT Binding
    /// Section 12 materialization rules: a projection document materializes as a
    /// single View that Organizes the Nodes already materialized from its
    /// sources, creates no affordance Node of its own, grows organizational
    /// Objects from its <c>ua:Organizes</c> links, omits sources that are not in
    /// this address space, rejects an organizing cycle, and carries a
    /// deterministic ViewVersion.
    /// </summary>
    [TestFixture]
    [Category("WoT")]
    public sealed class WotProjectionViewBuilderTests
    {
        private const string TestNamespaceUri = "urn:test:pump";
        private static readonly NodeId s_alphaNode = new("SourceA/Alpha", 5);
        private static readonly NodeId s_betaNode = new("SourceA/Beta", 5);
        private static readonly NodeId s_gammaNode = new("SourceB/Gamma", 5);
        private static readonly NodeId s_deltaNode = new("SourceC/Delta", 5);

        [Test]
        public async Task ViewOrganizesTheSourceNodesAndDefinesNoAffordanceNode()
        {
            var index = new MapNodeIndex(new Dictionary<string, NodeId>(StringComparer.Ordinal)
            {
                ["urn:sourceA#alpha"] = s_alphaNode,
                ["urn:sourceA#beta"] = s_betaNode
            });
            WotProjectionViewBuilder builder = Builder(index,
                ("urn:sourceA", SourceA));

            WotViewProjectionResult result = await Build(builder, SimpleProjection);

            Assert.That(result.Success, Is.True);
            WotViewProjectionPlan plan = result.Plan!;
            Assert.That(plan.OrganizedNodeIds.ToArray(),
                Is.EquivalentTo(new[] { s_alphaNode, s_betaNode }),
                "The View must Organize the Nodes already materialized from the source.");
            Assert.That(plan.Groups.Count, Is.Zero);
            Assert.That(plan.Omissions.Count, Is.Zero);
            Assert.That(plan.Scenario, Is.EqualTo("http://example.com/scenario/Simple"));
            // The plan holds only located, already-materialized NodeIds; it never
            // fabricates an affordance Node, so the materializer count is the
            // single View.
            Assert.That(plan.MaterializedNodeCount, Is.EqualTo(1));
        }

        [Test]
        public async Task ProjectionOfAProjectionOrganizesTheNodesTheUltimateSourcesMaterialized()
        {
            // The index knows only the real source: an intermediate projection
            // materializes a View, never Nodes, so it is absent by construction.
            var index = new MapNodeIndex(new Dictionary<string, NodeId>(StringComparer.Ordinal)
            {
                ["urn:sourceA#alpha"] = s_alphaNode,
                ["urn:sourceA#beta"] = s_betaNode
            });
            WotProjectionViewBuilder builder = Builder(index,
                ("urn:sourceA", SourceA),
                ("urn:view:simple", SimpleProjection));

            WotViewProjectionResult result = await Build(builder, ProjectionOverProjection);

            Assert.That(result.Success, Is.True);
            WotViewProjectionPlan plan = result.Plan!;
            Assert.That(plan.OrganizedNodeIds.ToArray(),
                Is.EquivalentTo(new[] { s_alphaNode, s_betaNode }),
                "A projection selecting from a projection must organize the Nodes the " +
                "ultimate sources materialized.");
            Assert.That(plan.Omissions.Count, Is.Zero);
        }

        [Test]
        public async Task ProjectionOfAProjectionOmitsAndNamesTheUltimateSourceWhenItIsNotMaterialized()
        {
            // Nothing is materialized anywhere, so the walk runs to the end of
            // the chain and the omission must name where it stopped.
            var index = new MapNodeIndex(new Dictionary<string, NodeId>(StringComparer.Ordinal));
            WotProjectionViewBuilder builder = Builder(index,
                ("urn:sourceA", SourceA),
                ("urn:view:simple", SimpleProjection));

            WotViewProjectionResult result = await Build(builder, ProjectionOverProjection);

            Assert.That(result.Success, Is.True);
            WotViewProjectionPlan plan = result.Plan!;
            Assert.That(plan.OrganizedNodeIds.Count, Is.Zero);
            Assert.That(plan.Omissions.Count, Is.EqualTo(2));
            for (int i = 0; i < plan.Omissions.Count; i++)
            {
                Assert.That(plan.Omissions[i], Does.Contain("urn:sourceA"),
                    "The omission must name the ultimate source the walk reached, not the " +
                    "intermediate projection, because the intermediate never materializes Nodes.");
            }
        }

        [Test]
        public async Task MaterializedNodeCountCountsOnlyViewPlusOrganizationalObjects()
        {
            var index = new MapNodeIndex(new Dictionary<string, NodeId>(StringComparer.Ordinal)
            {
                ["urn:sourceA#alpha"] = s_alphaNode,
                ["urn:sourceB#gamma"] = s_gammaNode,
                ["urn:sourceC#delta"] = s_deltaNode
            });
            WotProjectionViewBuilder builder = Builder(index,
                ("urn:sourceA", SourceA),
                ("urn:sourceB", SourceB),
                ("urn:sourceC", SourceC),
                ("urn:view:inner", InnerGroupProjection),
                ("urn:view:outer", OuterGroupProjection));

            WotViewProjectionResult result = await Build(builder, NestedGroupProjection);

            Assert.That(result.Success, Is.True);
            WotViewProjectionPlan plan = result.Plan!;
            // View + outer Object + inner Object = 3; the organized member Nodes
            // (alpha/gamma/delta) are materialized from their own sources and are
            // never counted here.
            Assert.That(plan.MaterializedNodeCount, Is.EqualTo(3));
        }

        [Test]
        public async Task OrganizingLinksProduceObjectsWithOnlyTheOutermostAView()
        {
            var index = new MapNodeIndex(new Dictionary<string, NodeId>(StringComparer.Ordinal)
            {
                ["urn:sourceB#gamma"] = s_gammaNode,
                ["urn:sourceC#delta"] = s_deltaNode
            });
            WotProjectionViewBuilder builder = Builder(index,
                ("urn:sourceB", SourceB),
                ("urn:sourceC", SourceC),
                ("urn:view:inner", InnerGroupProjection));

            WotViewProjectionResult result = await Build(builder, OuterGroupProjection);

            Assert.That(result.Success, Is.True);
            WotViewProjectionPlan plan = result.Plan!;
            Assert.That(plan.OrganizedNodeIds.ToArray(), Is.EquivalentTo(new[] { s_gammaNode }),
                "The outermost View directly Organizes only its own selected Nodes.");
            Assert.That(plan.Groups.Count, Is.EqualTo(1),
                "The ua:Organizes link becomes one organizational Object, not a View.");
            WotOrganizationalGroup group = plan.Groups[0];
            Assert.That(group.RefName, Is.EqualTo("inner"));
            Assert.That(group.OrganizedNodeIds.ToArray(), Is.EquivalentTo(new[] { s_deltaNode }),
                "The organizing document does not absorb the organized affordances.");
            Assert.That(plan.MaterializedNodeCount, Is.EqualTo(2));
        }

        [Test]
        public async Task OutOfAddressSpaceSourceIsOmittedButBuildStillSucceeds()
        {
            // The index knows alpha's Node but not beta's, standing in for a
            // source served by another Server that is not in this address space.
            var index = new MapNodeIndex(new Dictionary<string, NodeId>(StringComparer.Ordinal)
            {
                ["urn:sourceA#alpha"] = s_alphaNode
            });
            WotProjectionViewBuilder builder = Builder(index,
                ("urn:sourceA", SourceA));

            WotViewProjectionResult result = await Build(builder, SimpleProjection);

            Assert.That(result.Success, Is.True,
                "Omitting an out-of-address-space source is not a failure.");
            WotViewProjectionPlan plan = result.Plan!;
            Assert.That(plan.OrganizedNodeIds.ToArray(), Is.EquivalentTo(new[] { s_alphaNode }));
            Assert.That(plan.Omissions.Count, Is.EqualTo(1));
            Assert.That(plan.Omissions[0], Does.Contain("beta"),
                "The omitted affordance must be reported.");
        }

        [Test]
        public async Task OrganizingCycleIsRejected()
        {
            var index = new MapNodeIndex(new Dictionary<string, NodeId>(StringComparer.Ordinal));
            WotProjectionViewBuilder builder = Builder(index,
                ("urn:sourceA", SourceA),
                ("urn:sourceB", SourceB),
                ("urn:view:cycleA", CycleProjectionA),
                ("urn:view:cycleB", CycleProjectionB));

            WotViewProjectionResult result = await Build(builder, CycleProjectionA);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Plan, Is.Null);
            Assert.That(
                HasCode(result.Diagnostics, WotDiagnosticCode.ProjectionCycle),
                Is.True, "An organizing cycle must be reported as ProjectionCycle.");
        }

        [Test]
        public async Task ViewVersionIsStableAcrossAnUnchangedBuildAndChangesOnMembershipChange()
        {
            var full = new MapNodeIndex(new Dictionary<string, NodeId>(StringComparer.Ordinal)
            {
                ["urn:sourceA#alpha"] = s_alphaNode,
                ["urn:sourceA#beta"] = s_betaNode
            });
            WotViewProjectionResult first =
                await Build(Builder(full, ("urn:sourceA", SourceA)), SimpleProjection);
            WotViewProjectionResult second =
                await Build(Builder(full, ("urn:sourceA", SourceA)), SimpleProjection);

            Assert.That(second.Plan!.ViewVersion, Is.EqualTo(first.Plan!.ViewVersion),
                "An unchanged resolved membership must yield the same ViewVersion.");

            var reduced = new MapNodeIndex(new Dictionary<string, NodeId>(StringComparer.Ordinal)
            {
                ["urn:sourceA#alpha"] = s_alphaNode
            });
            WotViewProjectionResult changed =
                await Build(Builder(reduced, ("urn:sourceA", SourceA)), SimpleProjection);

            Assert.That(changed.Plan!.ViewVersion, Is.Not.EqualTo(first.Plan!.ViewVersion),
                "A changed resolved membership must change the ViewVersion.");
        }

        /// <summary>
        /// <i>OPC UA — WoT Binding</i> §12.6 makes ViewVersion a function of the
        /// resolved membership alone, taken in a canonical order, so it records
        /// what a View contains rather than how it is arranged. Selecting the
        /// same members through a source that declares them in the opposite
        /// order must therefore leave it untouched.
        /// </summary>
        [Test]
        public async Task ViewVersionIsUnchangedWhenOnlyTheOrderOfTheMembershipChanges()
        {
            var full = new MapNodeIndex(new Dictionary<string, NodeId>(StringComparer.Ordinal)
            {
                ["urn:sourceA#alpha"] = s_alphaNode,
                ["urn:sourceA#beta"] = s_betaNode
            });

            WotViewProjectionResult inOrder =
                await Build(Builder(full, ("urn:sourceA", SourceA)), SimpleProjection);
            WotViewProjectionResult reordered =
                await Build(Builder(full, ("urn:sourceA", SourceAReordered)), SimpleProjection);

            Assert.That(inOrder.Success, Is.True);
            Assert.That(reordered.Success, Is.True);
            Assert.That(reordered.Plan!.OrganizedNodeIds, Has.Count.EqualTo(
                inOrder.Plan!.OrganizedNodeIds.Count),
                "Both builds must select the same number of members.");
            Assert.That(reordered.Plan!.ViewVersion, Is.EqualTo(inOrder.Plan!.ViewVersion),
                "Reordering the membership alone must not change the ViewVersion.");
        }

        /// <summary>
        /// A NodeId string identifier may contain U+000A, the separator the
        /// <c>ViewVersion</c> encoding of <i>OPC UA — WoT Binding</i> §12.6
        /// writes after each member, and nothing escapes it. The length prefix
        /// the clause requires is what keeps the encoding injective: without it
        /// a single member embedding a newline would serialize byte-for-byte as
        /// the two members it imitates, and the two memberships would share a
        /// <c>ViewVersion</c>.
        /// </summary>
        /// <remarks>
        /// This is a structural collision an author can construct deliberately,
        /// which is a different thing from the 32-bit collision the clause
        /// knowingly accepts.
        /// </remarks>
        [Test]
        public async Task ViewVersionDoesNotCollideWhenAMemberIdentifierEmbedsTheJoinSeparator()
        {
            var twoMembers = new MapNodeIndex(new Dictionary<string, NodeId>(StringComparer.Ordinal)
            {
                ["urn:sourceA#alpha"] = new NodeId("A", 5),
                ["urn:sourceA#beta"] = new NodeId("B", 5)
            });
            // Without the length prefix the two members and this single member
            // both encode to "nsu=urn:test:pump;s=A\nnsu=urn:test:pump;s=B\n".
            var collidingNode = new NodeId("A\nnsu=" + TestNamespaceUri + ";s=B", 5);
            var oneMember = new MapNodeIndex(new Dictionary<string, NodeId>(StringComparer.Ordinal)
            {
                ["urn:sourceA#alpha"] = collidingNode
            });

            WotViewProjectionResult two =
                await Build(Builder(twoMembers, ("urn:sourceA", SourceA)), SimpleProjection);
            WotViewProjectionResult one =
                await Build(Builder(oneMember, ("urn:sourceA", SourceA)), SimpleProjection);

            Assert.That(two.Success, Is.True);
            Assert.That(one.Success, Is.True);
            Assert.That(two.Plan!.OrganizedNodeIds, Has.Count.EqualTo(2));
            Assert.That(one.Plan!.OrganizedNodeIds, Has.Count.EqualTo(1));
            Assert.That(one.Plan!.ViewVersion, Is.Not.EqualTo(two.Plan!.ViewVersion),
                "The length prefix must keep the encoding injective, so a member embedding " +
                "the U+000A separator cannot imitate the members it splits into.");
        }

        /// <summary>
        /// Two groups that carry no <c>uav:refName</c> tie on it, and
        /// <c>List&lt;T&gt;.Sort</c> is not stable, so ordering on the name
        /// alone would let the authoring order of the links leak into the
        /// ViewVersion. The same two groups authored either way are the same
        /// membership.
        /// </summary>
        [Test]
        public async Task ViewVersionIsUnchangedWhenOnlyTheOrderOfTwoUnnamedGroupsChanges()
        {
            var index = new MapNodeIndex(new Dictionary<string, NodeId>(StringComparer.Ordinal)
            {
                ["urn:sourceA#alpha"] = s_alphaNode,
                ["urn:sourceB#gamma"] = new NodeId("SourceB/Gamma", 5),
                ["urn:sourceC#delta"] = new NodeId("SourceC/Delta", 5)
            });
            (string, string)[] documents =
            [
                ("urn:sourceA", SourceA),
                ("urn:sourceB", SourceB),
                ("urn:sourceC", SourceC),
                ("urn:view:inner", InnerGroupProjection),
                ("urn:view:inner2", SecondInnerGroupProjection)
            ];

            WotViewProjectionResult first = await Build(
                Builder(index, documents), TwoUnnamedGroupsProjection);
            WotViewProjectionResult swapped = await Build(
                Builder(index, documents), TwoUnnamedGroupsReorderedProjection);

            Assert.That(first.Success, Is.True);
            Assert.That(swapped.Success, Is.True);
            Assert.That(first.Plan!.Groups, Has.Count.EqualTo(2));
            Assert.That(swapped.Plan!.ViewVersion, Is.EqualTo(first.Plan!.ViewVersion),
                "Two groups tying on an absent uav:refName must be ordered deterministically.");
        }

        /// <summary>
        /// Pins <c>ViewVersion</c> to the algorithm <i>OPC UA — WoT Binding</i>
        /// §12.6 specifies, not to whatever this code happens to produce. The
        /// expected value is computed independently of the implementation: the
        /// two members in portable form are
        /// <c>nsu=urn:test:pump;i=1001</c> and <c>nsu=urn:test:pump;s=Alpha</c>;
        /// sorted ascending by code point and each written as its UTF-8 octet
        /// length, a colon, the string and U+000A, that is
        /// <c>"24:nsu=urn:test:pump;i=1001\n25:nsu=urn:test:pump;s=Alpha\n"</c>,
        /// whose SHA-256 digest begins 87 C4 C4 9C — 2277819548 big-endian.
        /// </summary>
        [Test]
        public async Task ViewVersionMatchesTheSpecifiedAlgorithm()
        {
            const uint expected = 2277819548u;
            var index = new MapNodeIndex(new Dictionary<string, NodeId>(StringComparer.Ordinal)
            {
                ["urn:sourceA#alpha"] = new NodeId(1001u, 5),
                ["urn:sourceA#beta"] = new NodeId("Alpha", 5)
            });

            WotViewProjectionResult result =
                await Build(Builder(index, ("urn:sourceA", SourceA)), SimpleProjection);

            Assert.That(result.Success, Is.True);
            Assert.That(result.Plan!.OrganizedNodeIds, Has.Count.EqualTo(2));
            Assert.That(result.Plan!.ViewVersion, Is.EqualTo(expected),
                "ViewVersion must be the first four octets of the SHA-256 digest of the " +
                "code-point-sorted portable member identities joined by U+000A.");
        }

        [Test]
        public async Task NonProjectionDocumentDoesNotMaterializeAView()
        {
            var index = new MapNodeIndex(new Dictionary<string, NodeId>(StringComparer.Ordinal));
            WotProjectionViewBuilder builder = Builder(index);

            WotViewProjectionResult result = await Build(builder, SourceA);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Plan, Is.Null);
        }

        [Test]
        public async Task ThingModelProjectionAlsoMaterializesAView()
        {
            var index = new MapNodeIndex(new Dictionary<string, NodeId>(StringComparer.Ordinal)
            {
                ["urn:sourceA#alpha"] = s_alphaNode,
                ["urn:sourceA#beta"] = s_betaNode
            });
            WotProjectionViewBuilder builder = Builder(index, ("urn:sourceA", SourceA));

            WotViewProjectionResult result = await Build(builder, ThingModelProjection);

            Assert.That(result.Success, Is.True);
            Assert.That(result.Plan!.DocumentKind, Is.EqualTo(WotDocumentKind.ThingModel),
                "A type-level projection over Thing Models still materializes a View.");
            Assert.That(result.Plan!.OrganizedNodeIds.Count, Is.EqualTo(2));
        }

        /// <summary>
        /// The namespace table the builder writes portable member identities
        /// against. Index 5 is the namespace the test NodeIds live in.
        /// </summary>
        private static NamespaceTable TestNamespaces()
        {
            var namespaces = new NamespaceTable();
            namespaces.Append("urn:test:one");
            namespaces.Append("urn:test:two");
            namespaces.Append("urn:test:three");
            namespaces.Append("urn:test:four");
            namespaces.Append(TestNamespaceUri);
            return namespaces;
        }

        private static WotProjectionViewBuilder Builder(
            IWotMaterializedNodeIndex index,
            params (string Href, string Json)[] documents)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < documents.Length; i++)
            {
                map[documents[i].Href] = documents[i].Json;
            }
            return new WotProjectionViewBuilder(
                new MapThingResolver(map), index, null, TestNamespaces());
        }

        private static async Task<WotViewProjectionResult> Build(
            WotProjectionViewBuilder builder, string json)
        {
            using WotDocument document = WotDocument.Parse(Encoding.UTF8.GetBytes(json));
            return await builder.BuildAsync(document);
        }

        private static bool HasCode(ArrayOf<WotDiagnostic> diagnostics, WotDiagnosticCode code)
        {
            for (int i = 0; i < diagnostics.Count; i++)
            {
                if (diagnostics[i].Code == code)
                {
                    return true;
                }
            }
            return false;
        }

        private sealed class MapThingResolver : IWotThingResolver
        {
            public MapThingResolver(Dictionary<string, string> map)
            {
                m_map = map;
            }

            public ValueTask<WotResolverResult> ResolveThingAsync(
                string reference,
                WotResolutionContext context,
                CancellationToken cancellationToken)
            {
                WotResolverResult result = m_map.TryGetValue(reference, out string? json)
                    ? WotResolverResult.FromBytes(Encoding.UTF8.GetBytes(json))
                    : WotResolverResult.NotFound;
                return new ValueTask<WotResolverResult>(result);
            }

            private readonly Dictionary<string, string> m_map;
        }

        private sealed class MapNodeIndex : IWotMaterializedNodeIndex
        {
            public MapNodeIndex(Dictionary<string, NodeId> map)
            {
                m_map = map;
            }

            public NodeId Locate(in WotMaterializedAffordanceRef affordance)
            {
                string key = affordance.SourceHref + "#" + affordance.AffordanceName;
                return m_map.TryGetValue(key, out NodeId nodeId) ? nodeId : NodeId.Null;
            }

            private readonly Dictionary<string, NodeId> m_map;
        }

        private const string SourceA = """
        {
          "@context": [
            "https://www.w3.org/2022/wot/td/v1.1",
            { "uav": "http://opcfoundation.org/UA/WoT-Binding/" }
          ],
          "@type": "uav:object",
          "id": "urn:sourceA",
          "title": "Source A",
          "securityDefinitions": { "nosec_sc": { "scheme": "nosec" } },
          "security": "nosec_sc",
          "base": "opc.tcp://demo:4840",
          "properties": {
            "alpha": {
              "@type": "uav:variable",
              "title": "Alpha",
              "uav:browseName": "s:Alpha",
              "type": "number",
              "forms": [{ "href": "/?id=nsu=urn:sourceA;s=Alpha", "op": ["readproperty"] }]
            },
            "beta": {
              "@type": "uav:variable",
              "title": "Beta",
              "uav:browseName": "s:Beta",
              "type": "number",
              "forms": [{ "href": "/?id=nsu=urn:sourceA;s=Beta", "op": ["readproperty"] }]
            }
          }
        }
        """;

        /// <summary>
        /// <see cref="SourceA"/> with its two affordances declared in the opposite
        /// order. The resolved membership is the same set, reached in a different
        /// order, which is what the ViewVersion canonicalization has to absorb.
        /// </summary>
        private const string SourceAReordered = """
        {
          "@context": [
            "https://www.w3.org/2022/wot/td/v1.1",
            { "uav": "http://opcfoundation.org/UA/WoT-Binding/" }
          ],
          "@type": "uav:object",
          "id": "urn:sourceA",
          "title": "Source A",
          "securityDefinitions": { "nosec_sc": { "scheme": "nosec" } },
          "security": "nosec_sc",
          "base": "opc.tcp://demo:4840",
          "properties": {
            "beta": {
              "@type": "uav:variable",
              "title": "Beta",
              "uav:browseName": "s:Beta",
              "type": "number",
              "forms": [{ "href": "/?id=nsu=urn:sourceA;s=Beta", "op": ["readproperty"] }]
            },
            "alpha": {
              "@type": "uav:variable",
              "title": "Alpha",
              "uav:browseName": "s:Alpha",
              "type": "number",
              "forms": [{ "href": "/?id=nsu=urn:sourceA;s=Alpha", "op": ["readproperty"] }]
            }
          }
        }
        """;

        private const string SourceB = """
        {
          "@context": [
            "https://www.w3.org/2022/wot/td/v1.1",
            { "uav": "http://opcfoundation.org/UA/WoT-Binding/" }
          ],
          "@type": "uav:object",
          "id": "urn:sourceB",
          "title": "Source B",
          "securityDefinitions": { "nosec_sc": { "scheme": "nosec" } },
          "security": "nosec_sc",
          "base": "opc.tcp://demo:4840",
          "properties": {
            "gamma": {
              "@type": "uav:variable",
              "title": "Gamma",
              "uav:browseName": "s:Gamma",
              "type": "number",
              "forms": [{ "href": "/?id=nsu=urn:sourceB;s=Gamma", "op": ["readproperty"] }]
            }
          }
        }
        """;

        private const string SourceC = """
        {
          "@context": [
            "https://www.w3.org/2022/wot/td/v1.1",
            { "uav": "http://opcfoundation.org/UA/WoT-Binding/" }
          ],
          "@type": "uav:object",
          "id": "urn:sourceC",
          "title": "Source C",
          "securityDefinitions": { "nosec_sc": { "scheme": "nosec" } },
          "security": "nosec_sc",
          "base": "opc.tcp://demo:4840",
          "properties": {
            "delta": {
              "@type": "uav:variable",
              "title": "Delta",
              "uav:browseName": "s:Delta",
              "type": "number",
              "forms": [{ "href": "/?id=nsu=urn:sourceC;s=Delta", "op": ["readproperty"] }]
            }
          }
        }
        """;

        private const string SimpleProjection = """
        {
          "@context": [
            "https://www.w3.org/2022/wot/td/v1.1",
            { "uav": "http://opcfoundation.org/UA/WoT-Binding/", "tm": "https://www.w3.org/2019/wot/tm#" }
          ],
          "@type": ["Thing", "uav:projection"],
          "id": "urn:view:simple",
          "title": "Simple view",
          "uav:scenario": "http://example.com/scenario/Simple",
          "securityDefinitions": { "nosec_sc": { "scheme": "nosec" } },
          "security": "nosec_sc",
          "uav:projects": [
            {
              "uav:sourceName": "a",
              "href": "urn:sourceA",
              "type": "application/td+json",
              "uav:routing": "source",
              "uav:selectAll": true
            }
          ]
        }
        """;

        private const string ThingModelProjection = """
        {
          "@context": [
            "https://www.w3.org/2022/wot/td/v1.1",
            { "uav": "http://opcfoundation.org/UA/WoT-Binding/", "tm": "https://www.w3.org/2019/wot/tm#" }
          ],
          "@type": ["tm:ThingModel", "uav:projection"],
          "id": "urn:view:type",
          "title": "Type view",
          "uav:scenario": "http://example.com/scenario/Type",
          "securityDefinitions": { "nosec_sc": { "scheme": "nosec" } },
          "security": "nosec_sc",
          "uav:projects": [
            {
              "uav:sourceName": "a",
              "href": "urn:sourceA",
              "type": "application/td+json",
              "uav:routing": "source",
              "uav:selectAll": true
            }
          ]
        }
        """;

        /// <summary>
        /// A projection whose own source is a projection. Its selections name
        /// <c>urn:view:simple</c>, which materializes a View and not Nodes, so
        /// reaching the organized Nodes requires walking through to
        /// <c>urn:sourceA</c>.
        /// </summary>
        private const string ProjectionOverProjection = """
        {
          "@context": [
            "https://www.w3.org/2022/wot/td/v1.1",
            { "uav": "http://opcfoundation.org/UA/WoT-Binding/", "tm": "https://www.w3.org/2019/wot/tm#" }
          ],
          "@type": ["Thing", "uav:projection"],
          "id": "urn:view:overview",
          "title": "Over view",
          "uav:scenario": "http://example.com/scenario/Over",
          "securityDefinitions": { "nosec_sc": { "scheme": "nosec" } },
          "security": "nosec_sc",
          "uav:projects": [
            {
              "uav:sourceName": "s",
              "href": "urn:view:simple",
              "type": "application/td+json",
              "uav:routing": "source",
              "uav:selectAll": true
            }
          ]
        }
        """;

        private const string OuterGroupProjection = """
        {
          "@context": [
            "https://www.w3.org/2022/wot/td/v1.1",
            { "uav": "http://opcfoundation.org/UA/WoT-Binding/", "tm": "https://www.w3.org/2019/wot/tm#" }
          ],
          "@type": ["Thing", "uav:projection"],
          "id": "urn:view:outer",
          "title": "Outer view",
          "uav:scenario": "http://example.com/scenario/Outer",
          "securityDefinitions": { "nosec_sc": { "scheme": "nosec" } },
          "security": "nosec_sc",
          "uav:projects": [
            {
              "uav:sourceName": "b",
              "href": "urn:sourceB",
              "type": "application/td+json",
              "uav:routing": "source",
              "uav:selectAll": true
            }
          ],
          "links": [
            { "rel": "ua:Organizes", "uav:refName": "inner", "href": "urn:view:inner", "type": "application/td+json" }
          ]
        }
        """;

        private const string InnerGroupProjection = """
        {
          "@context": [
            "https://www.w3.org/2022/wot/td/v1.1",
            { "uav": "http://opcfoundation.org/UA/WoT-Binding/", "tm": "https://www.w3.org/2019/wot/tm#" }
          ],
          "@type": ["Thing", "uav:projection"],
          "id": "urn:view:inner",
          "title": "Inner view",
          "uav:scenario": "http://example.com/scenario/Inner",
          "securityDefinitions": { "nosec_sc": { "scheme": "nosec" } },
          "security": "nosec_sc",
          "uav:projects": [
            {
              "uav:sourceName": "c",
              "href": "urn:sourceC",
              "type": "application/td+json",
              "uav:routing": "source",
              "uav:selectAll": true
            }
          ]
        }
        """;

        /// <summary>
        /// A second group document, so a projection can organize two groups that
        /// carry no uav:refName and therefore tie on it.
        /// </summary>
        private const string SecondInnerGroupProjection = """
        {
          "@context": [
            "https://www.w3.org/2022/wot/td/v1.1",
            { "uav": "http://opcfoundation.org/UA/WoT-Binding/", "tm": "https://www.w3.org/2019/wot/tm#" }
          ],
          "@type": ["Thing", "uav:projection"],
          "id": "urn:view:inner2",
          "title": "Second inner view",
          "uav:scenario": "http://example.com/scenario/Inner2",
          "securityDefinitions": { "nosec_sc": { "scheme": "nosec" } },
          "security": "nosec_sc",
          "uav:projects": [
            {
              "uav:sourceName": "b",
              "href": "urn:sourceB",
              "type": "application/td+json",
              "uav:routing": "source",
              "uav:selectAll": true
            }
          ]
        }
        """;

        private const string NestedGroupProjection = """
        {
          "@context": [
            "https://www.w3.org/2022/wot/td/v1.1",
            { "uav": "http://opcfoundation.org/UA/WoT-Binding/", "tm": "https://www.w3.org/2019/wot/tm#" }
          ],
          "@type": ["Thing", "uav:projection"],
          "id": "urn:view:nested",
          "title": "Nested view",
          "uav:scenario": "http://example.com/scenario/Nested",
          "securityDefinitions": { "nosec_sc": { "scheme": "nosec" } },
          "security": "nosec_sc",
          "uav:projects": [
            {
              "uav:sourceName": "a",
              "href": "urn:sourceA",
              "type": "application/td+json",
              "uav:routing": "source",
              "uav:selectAll": true
            }
          ],
          "links": [
            { "rel": "ua:Organizes", "uav:refName": "outer", "href": "urn:view:outer", "type": "application/td+json" }
          ]
        }
        """;

        /// <summary>
        /// A projection organizing two groups, neither carrying uav:refName.
        /// </summary>
        private const string TwoUnnamedGroupsProjection = """
        {
          "@context": [
            "https://www.w3.org/2022/wot/td/v1.1",
            { "uav": "http://opcfoundation.org/UA/WoT-Binding/", "tm": "https://www.w3.org/2019/wot/tm#" }
          ],
          "@type": ["Thing", "uav:projection"],
          "id": "urn:view:two",
          "title": "Two group view",
          "uav:scenario": "http://example.com/scenario/Two",
          "securityDefinitions": { "nosec_sc": { "scheme": "nosec" } },
          "security": "nosec_sc",
          "uav:projects": [
            {
              "uav:sourceName": "a",
              "href": "urn:sourceA",
              "type": "application/td+json",
              "uav:routing": "source",
              "uav:selectAll": true
            }
          ],
          "links": [
            { "rel": "ua:Organizes", "href": "urn:view:inner", "type": "application/td+json" },
            { "rel": "ua:Organizes", "href": "urn:view:inner2", "type": "application/td+json" }
          ]
        }
        """;

        /// <summary>
        /// <see cref="TwoUnnamedGroupsProjection"/> with the two organizing
        /// links authored in the opposite order.
        /// </summary>
        private const string TwoUnnamedGroupsReorderedProjection = """
        {
          "@context": [
            "https://www.w3.org/2022/wot/td/v1.1",
            { "uav": "http://opcfoundation.org/UA/WoT-Binding/", "tm": "https://www.w3.org/2019/wot/tm#" }
          ],
          "@type": ["Thing", "uav:projection"],
          "id": "urn:view:two",
          "title": "Two group view",
          "uav:scenario": "http://example.com/scenario/Two",
          "securityDefinitions": { "nosec_sc": { "scheme": "nosec" } },
          "security": "nosec_sc",
          "uav:projects": [
            {
              "uav:sourceName": "a",
              "href": "urn:sourceA",
              "type": "application/td+json",
              "uav:routing": "source",
              "uav:selectAll": true
            }
          ],
          "links": [
            { "rel": "ua:Organizes", "href": "urn:view:inner2", "type": "application/td+json" },
            { "rel": "ua:Organizes", "href": "urn:view:inner", "type": "application/td+json" }
          ]
        }
        """;

        private const string CycleProjectionA = """
        {
          "@context": [
            "https://www.w3.org/2022/wot/td/v1.1",
            { "uav": "http://opcfoundation.org/UA/WoT-Binding/", "tm": "https://www.w3.org/2019/wot/tm#" }
          ],
          "@type": ["Thing", "uav:projection"],
          "id": "urn:view:cycleA",
          "title": "Cycle A",
          "uav:scenario": "http://example.com/scenario/CycleA",
          "securityDefinitions": { "nosec_sc": { "scheme": "nosec" } },
          "security": "nosec_sc",
          "uav:projects": [
            {
              "uav:sourceName": "a",
              "href": "urn:sourceA",
              "type": "application/td+json",
              "uav:routing": "source",
              "uav:selectAll": true
            }
          ],
          "links": [
            { "rel": "ua:Organizes", "uav:refName": "toB", "href": "urn:view:cycleB", "type": "application/td+json" }
          ]
        }
        """;

        private const string CycleProjectionB = """
        {
          "@context": [
            "https://www.w3.org/2022/wot/td/v1.1",
            { "uav": "http://opcfoundation.org/UA/WoT-Binding/", "tm": "https://www.w3.org/2019/wot/tm#" }
          ],
          "@type": ["Thing", "uav:projection"],
          "id": "urn:view:cycleB",
          "title": "Cycle B",
          "uav:scenario": "http://example.com/scenario/CycleB",
          "securityDefinitions": { "nosec_sc": { "scheme": "nosec" } },
          "security": "nosec_sc",
          "uav:projects": [
            {
              "uav:sourceName": "b",
              "href": "urn:sourceB",
              "type": "application/td+json",
              "uav:routing": "source",
              "uav:selectAll": true
            }
          ],
          "links": [
            { "rel": "ua:Organizes", "uav:refName": "toA", "href": "urn:view:cycleA", "type": "application/td+json" }
          ]
        }
        """;
    }
}
