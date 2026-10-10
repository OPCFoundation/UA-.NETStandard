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

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Gds.Server;
using Opc.Ua.Gds.Server.AliasNames;

namespace Opc.Ua.Gds.Tests
{
    /// <summary>
    /// Merging rules of the GDS AliasName Server facet (OPC 10000-17
    /// Annex C.2/C.3) on <see cref="AliasNameAggregator"/>.
    /// </summary>
    [TestFixture]
    [Category("GDS")]
    [Category("AliasNames")]
    [Parallelizable]
    public class AliasNameAggregatorTests
    {
        private const string GdsUri = "urn:localhost:gds";
        private const string UriA = "urn:localhost:sourceA";
        private const string UriB = "urn:localhost:sourceB";
        private const string NsA = "urn:sourceA:ns";
        private const string NsB = "urn:sourceB:ns";
        private const ushort AggregateNs = 3;

        private static readonly NodeId s_appA = new("appA", 2);
        private static readonly NodeId s_appB = new("appB", 2);
        private static readonly string[] s_machinesAndPumps = ["Machines", "Pumps"];
        private static readonly string[] s_machineAliases = ["A_Machine", "B_Machine"];
        private static readonly string[] s_machines = ["Machines"];
        private static readonly string[] s_sourceBAliases = ["Shared", "B_Machine"];
        private static readonly string[] s_tagAlias = ["A_Tag"];
        private static readonly string[] s_allAliases =
            ["Shared", "A_Tag", "A_Pump", "A_Machine", "A_Topic", "B_Machine"];
        private static readonly string[] s_tagVariableAliases = ["A_Tag", "A_Pump"];

        private static (AliasNameAggregator Aggregator, StringTable ServerUris, TypeTable TypeTree) Create(
            params string[] extraServerUris)
        {
            var namespaces = new NamespaceTable();
            namespaces.Append("urn:gds:local");
            var serverUris = new StringTable();
            serverUris.Append(GdsUri);
            foreach (string uri in extraServerUris)
            {
                serverUris.Append(uri);
            }
            return (new AliasNameAggregator(AggregateNs, namespaces, serverUris), serverUris, new TypeTable(namespaces));
        }

        private static AliasNameSourceAlias Alias(
            string serverUri, string ns, string name, string id, params string[] path)
        {
            return new AliasNameSourceAlias(
                path, name, ReferenceTypeIds.AliasFor, new ExpandedNodeId(new NodeId(id, 0), ns, 0), serverUri);
        }

        private static AliasNameSourceSnapshot SourceA()
        {
            return new AliasNameSourceSnapshot(
                UriA,
                [
                    new AliasNameSourceCategory(["TagVariables"]),
                    new AliasNameSourceCategory(["Topics"]),
                    new AliasNameSourceCategory(["Machines"]),
                    new AliasNameSourceCategory(["TagVariables", "Pumps"])
                ],
                [
                    Alias(UriA, NsA, "Shared", "A.Shared", "TagVariables"),
                    Alias(UriA, NsA, "A_Tag", "A.Tag", "TagVariables"),
                    Alias(UriA, NsA, "A_Pump", "A.Pump", "TagVariables", "Pumps"),
                    Alias(UriA, NsA, "A_Machine", "A.Machine", "Machines"),
                    Alias(UriA, NsA, "A_Topic", "A.Topic", "Topics")
                ]);
        }

        private static AliasNameSourceSnapshot SourceB()
        {
            return new AliasNameSourceSnapshot(
                UriB,
                [new AliasNameSourceCategory(["Machines"])],
                [
                    Alias(UriB, NsB, "Shared", "B.Shared", "TagVariables"),
                    Alias(UriB, NsB, "B_Machine", "B.Machine", "Machines")
                ]);
        }

        [Test]
        public void SameAliasFromTwoServersIsMergedIntoOneNode()
        {
            (AliasNameAggregator aggregator, StringTable serverUris, _) = Create();
            aggregator.SetSource(s_appA, SourceA());
            aggregator.SetSource(s_appB, SourceB());

            AliasNameAggregateView view = aggregator.GetView();
            AliasNameAggregateAlias shared = view.Aliases.ToArray()!.Single(a => a.Name == "Shared");
            Assert.That(shared.CategoryId, Is.EqualTo(Ua.ObjectIds.TagVariables));
            Assert.That(shared.NodeId.NamespaceIndex, Is.EqualTo(AggregateNs));
            Assert.That(shared.Targets.ToArray(), Has.Length.EqualTo(2));
            Assert.That(shared.Targets.ToArray().Select(t => t.ReferenceTypeId), Is.All.EqualTo(ReferenceTypeIds.AliasFor));

            Assert.That(serverUris.ToArray(), Is.EqualTo(new[] { GdsUri, UriA, UriB }));
            ExpandedNodeId targetA = shared.Targets.ToArray().Single(t => t.Target.NamespaceUri == NsA).Target;
            ExpandedNodeId targetB = shared.Targets.ToArray().Single(t => t.Target.NamespaceUri == NsB).Target;
            Assert.That(targetA.ServerIndex, Is.EqualTo(1u));
            Assert.That(targetB.ServerIndex, Is.EqualTo(2u));
            Assert.That(targetA.IdentifierAsString, Is.EqualTo("A.Shared"));
        }

        [Test]
        public void CategoriesWithTheSameNameAreMergedAndKeptWhileReferenced()
        {
            (AliasNameAggregator aggregator, _, _) = Create();
            aggregator.SetSource(s_appA, SourceA());
            aggregator.SetSource(s_appB, SourceB());

            AliasNameAggregateView view = aggregator.GetView();
            Assert.That(view.Categories.ToArray()!.Select(c => c.Name), Is.EquivalentTo(s_machinesAndPumps),
                "Well-known categories are not created; Machines exists once.");
            AliasNameAggregateCategory machines = view.Categories.ToArray()!.Single(c => c.Name == "Machines");
            Assert.That(machines.ParentId, Is.EqualTo(Ua.ObjectIds.Aliases));
            Assert.That(view.Categories.ToArray()!.Single(c => c.Name == "Pumps").ParentId, Is.EqualTo(Ua.ObjectIds.TagVariables));
            Assert.That(view.Aliases.ToArray()!.Where(a => a.CategoryId == machines.NodeId).Select(a => a.Name),
                Is.EquivalentTo(s_machineAliases));

            // Annex C.3: a category stays while another source uses it.
            Assert.That(aggregator.RemoveSource(s_appA), Is.True);
            view = aggregator.GetView();
            Assert.That(view.Categories.ToArray()!.Select(c => c.Name), Is.EqualTo(s_machines));
            Assert.That(view.Aliases.ToArray()!.Select(a => a.Name), Is.EquivalentTo(s_sourceBAliases));
            Assert.That(view.Aliases.ToArray()!.Single(a => a.Name == "Shared").Targets.ToArray(), Has.Length.EqualTo(1),
                "The AliasFor reference to the removed Server is gone.");

            Assert.That(aggregator.RemoveSource(s_appB), Is.True);
            Assert.That(aggregator.RemoveSource(s_appB), Is.False);
            view = aggregator.GetView();
            Assert.That(view.Categories.ToArray(), Is.Empty);
            Assert.That(view.Aliases.ToArray(), Is.Empty);
        }

        [Test]
        public void RemovingASourceRemovesItsServerUriAndRenumbersTheOthers()
        {
            (AliasNameAggregator aggregator, StringTable serverUris, _) = Create();
            aggregator.SetSource(s_appA, SourceA());
            aggregator.SetSource(s_appB, SourceB());

            aggregator.RemoveSource(s_appA);

            Assert.That(serverUris.ToArray(), Is.EqualTo(new[] { GdsUri, UriB }));
            AliasNameAggregateAlias shared = aggregator.GetView().Aliases.ToArray()!.Single(a => a.Name == "Shared");
            Assert.That(shared.Targets.ToArray()!.Single().Target.ServerIndex, Is.EqualTo(1u),
                "Targets are resolved against the current ServerArray.");
        }

        [Test]
        public void ServerUrisTheAggregatorDidNotAddAreKept()
        {
            (AliasNameAggregator aggregator, StringTable serverUris, _) = Create(UriA);
            aggregator.SetSource(s_appA, SourceA());
            aggregator.RemoveSource(s_appA);

            Assert.That(serverUris.ToArray(), Is.EqualTo(new[] { GdsUri, UriA }));
        }

        [Test]
        public void ReplacingASnapshotDropsWhatTheSourceNoLongerHas()
        {
            (AliasNameAggregator aggregator, _, _) = Create();
            aggregator.SetSource(s_appA, SourceA());
            aggregator.SetSource(s_appA, new AliasNameSourceSnapshot(
                UriA, [], [Alias(UriA, NsA, "A_Tag", "A.Tag", "TagVariables")]));

            AliasNameAggregateView view = aggregator.GetView();
            Assert.That(view.Categories.ToArray(), Is.Empty);
            Assert.That(view.Aliases.ToArray()!.Select(a => a.Name), Is.EqualTo(s_tagAlias));
            Assert.That(aggregator.Sources.ToArray(), Is.EqualTo(new[] { s_appA }));
        }

        [Test]
        public void TargetsOnTheGdsItselfStayLocal()
        {
            (AliasNameAggregator aggregator, StringTable serverUris, _) = Create();
            aggregator.SetSource(s_appA, new AliasNameSourceSnapshot(
                GdsUri, [], [Alias(GdsUri, "urn:gds:local", "Self", "Local.Node", "TagVariables")]));

            ExpandedNodeId target = aggregator.GetView().Aliases.ToArray()!.Single().Targets.ToArray()!.Single().Target;
            Assert.That(target.ServerIndex, Is.Zero);
            Assert.That(target.IsAbsolute, Is.False);
            Assert.That(target.NamespaceIndex, Is.EqualTo(1));
            Assert.That(serverUris.ToArray(), Is.EqualTo(new[] { GdsUri }));
        }

        [Test]
        public async Task FindAliasSearchesTheCategoryAndItsDescendantsAsync()
        {
            (AliasNameAggregator aggregator, _, TypeTable typeTree) = Create();
            aggregator.SetSource(s_appA, SourceA());
            aggregator.SetSource(s_appB, SourceB());

            IReadOnlyList<AliasNameDataType> all = await aggregator
                .FindAliasAsync(Ua.ObjectIds.Aliases, "%", NodeId.Null, typeTree).ConfigureAwait(false);
            Assert.That(all.Select(a => a.AliasName.Name),
                Is.EquivalentTo(s_allAliases));
            Assert.That(all.Single(a => a.AliasName.Name == "Shared").ReferencedNodes.Count, Is.EqualTo(2));
            Assert.That(all.Select(a => a.AliasName.NamespaceIndex), Is.All.EqualTo(AggregateNs));

            IReadOnlyList<AliasNameDataType> tags = await aggregator
                .FindAliasAsync(Ua.ObjectIds.TagVariables, "A%", ReferenceTypeIds.AliasFor, typeTree)
                .ConfigureAwait(false);
            Assert.That(tags.Select(a => a.AliasName.Name), Is.EquivalentTo(s_tagVariableAliases),
                "Nested categories are searched, other categories are not.");

            NodeId machines = aggregator.GetView().Categories.ToArray()!.Single(c => c.Name == "Machines").NodeId;
            IReadOnlyList<AliasNameVerboseDataType> verbose = await aggregator
                .FindAliasVerboseAsync(machines, "B_%", NodeId.Null, typeTree).ConfigureAwait(false);
            Assert.That(verbose, Has.Count.EqualTo(1));
            Assert.That(verbose[0].ServerUris.ToArray(), Is.EqualTo(new[] { UriB }));
            Assert.That(verbose[0].AliasNameCategoryId, Is.EqualTo(machines));

            Assert.That(await aggregator.FindAliasAsync(Ua.ObjectIds.Topics, string.Empty, NodeId.Null, typeTree)
                .ConfigureAwait(false), Is.Empty);
            Assert.That(await aggregator.FindAliasAsync(Ua.ObjectIds.Topics, "%", ReferenceTypeIds.HasComponent, typeTree)
                .ConfigureAwait(false), Is.Empty, "The reference type filter applies.");
        }

        [Test]
        public void OwnsTheWellKnownCategoriesAndTheAggregatedOnes()
        {
            (AliasNameAggregator aggregator, _, _) = Create();
            Assert.That(aggregator.OwnsCategory(Ua.ObjectIds.TagVariables), Is.True);
            Assert.That(aggregator.OwnsCategory(Ua.ObjectIds.Aliases), Is.True);

            aggregator.SetSource(s_appB, SourceB());
            NodeId machines = aggregator.GetView().Categories.ToArray()!.Single().NodeId;
            Assert.That(aggregator.OwnsCategory(machines), Is.True);
            aggregator.RemoveSource(s_appB);
            Assert.That(aggregator.OwnsCategory(machines), Is.False);
            Assert.That(aggregator.RootCategories, Is.Empty);
        }

        [Test]
        public void IsAliasNameSourceRequiresServerWithAliasCapability()
        {
            var record = new ApplicationRecordDataType
            {
                ApplicationUri = UriA,
                ApplicationType = ApplicationType.Server,
                DiscoveryUrls = ["opc.tcp://localhost:4840"],
                ServerCapabilities = ["DA", "ALIAS"]
            };
            Assert.That(ApplicationsNodeManager.IsAliasNameSource(record), Is.True);

            record.ServerCapabilities = ["DA"];
            Assert.That(ApplicationsNodeManager.IsAliasNameSource(record), Is.False);

            record.ServerCapabilities = ["ALIAS"];
            record.DiscoveryUrls = default;
            Assert.That(ApplicationsNodeManager.IsAliasNameSource(record), Is.False);
            Assert.That(ApplicationsNodeManager.IsAliasNameSource(null), Is.False);
        }
    }
}
