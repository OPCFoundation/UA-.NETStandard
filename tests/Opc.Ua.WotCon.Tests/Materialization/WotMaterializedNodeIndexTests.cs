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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    /// <summary>
    /// Locating the already-materialized Node behind a selected affordance. A
    /// projection only ever reaches Nodes materialized from the source it
    /// names, so an authored <c>uav:id</c> is bounded by the source root rather
    /// than trusted.
    /// </summary>
    [TestFixture]
    public sealed class WotMaterializedNodeIndexTests
    {
        [Test]
        public void ConstructorRejectsNullArguments()
        {
            var namespaces = new NamespaceTable();
            var roots = new Dictionary<string, NodeId>(StringComparer.Ordinal);

            Assert.Throws<ArgumentNullException>(
                () => new WotMaterializedNodeIndex(null!, namespaces, roots));
            Assert.Throws<ArgumentNullException>(
                () => new WotMaterializedNodeIndex(WotRegistrySnapshot.Empty, null!, roots));
            Assert.Throws<ArgumentNullException>(
                () => new WotMaterializedNodeIndex(WotRegistrySnapshot.Empty, namespaces, null!));
        }

        [Test]
        public async Task AnAffordanceOfAnUnknownSourceIsNotLocated()
        {
            WotMaterializedNodeIndex index = await IndexAsync(s_root);

            NodeId located = index.Locate(
                new WotMaterializedAffordanceRef(
                    "urn:not-registered", WotAffordanceKind.Property, "alpha",
                    ExpandedNodeId.Null));

            Assert.That(located.IsNull, Is.True);
        }

        [Test]
        public async Task AnAffordanceWhoseSourceHasNoRootIsNotLocated()
        {
            WotMaterializedNodeIndex index = await IndexAsync(NodeId.Null);

            NodeId located = index.Locate(Reference(ExpandedNodeId.Null));

            Assert.That(located.IsNull, Is.True,
                "A source that was never materialized has no Node to organize.");
        }

        [Test]
        public async Task AnAffordanceIsLocatedByItsDerivedNameWhenNoIdIsAuthored()
        {
            WotMaterializedNodeIndex index = await IndexAsync(s_root);

            NodeId located = index.Locate(Reference(ExpandedNodeId.Null));

            Assert.That(located, Is.EqualTo(new NodeId("Sources/A/alpha", 3)));
        }

        [Test]
        public async Task AnAuthoredIdBeneathTheSourceRootIsHonoured()
        {
            WotMaterializedNodeIndex index = await IndexAsync(s_root);
            var authored = new ExpandedNodeId(new NodeId("Sources/A/Custom", 3));

            NodeId located = index.Locate(Reference(authored));

            Assert.That(located, Is.EqualTo(new NodeId("Sources/A/Custom", 3)));
        }

        [Test]
        public async Task TheSourceRootItselfIsAnAcceptableAuthoredId()
        {
            WotMaterializedNodeIndex index = await IndexAsync(s_root);
            var authored = new ExpandedNodeId(s_root);

            NodeId located = index.Locate(Reference(authored));

            Assert.That(located, Is.EqualTo(s_root));
        }

        [Test]
        public async Task AnAuthoredIdOutsideTheSourceRootIsRefused()
        {
            // uav:id is authored input and a projection document may carry its
            // own; honouring it unchecked would let a View Organizes any Node.
            WotMaterializedNodeIndex index = await IndexAsync(s_root);
            var elsewhere = new ExpandedNodeId(new NodeId("Sources/B/alpha", 3));

            NodeId located = index.Locate(Reference(elsewhere));

            Assert.That(located, Is.EqualTo(new NodeId("Sources/A/alpha", 3)),
                "The out-of-bounds id is refused and the derived name is used instead.");
        }

        [Test]
        public async Task AnAuthoredIdInAnotherNamespaceIsRefused()
        {
            WotMaterializedNodeIndex index = await IndexAsync(s_root);
            var otherNamespace = new ExpandedNodeId(new NodeId("Sources/A/alpha", 4));

            NodeId located = index.Locate(Reference(otherNamespace));

            Assert.That(located, Is.EqualTo(new NodeId("Sources/A/alpha", 3)));
        }

        [Test]
        public async Task AnAuthoredNumericIdIsRefused()
        {
            // The Server object is the interesting case: a numeric id shares no
            // prefix with the source root and must never be organized.
            WotMaterializedNodeIndex index = await IndexAsync(s_root);
            var server = new ExpandedNodeId(new NodeId(2253u, 3));

            NodeId located = index.Locate(Reference(server));

            Assert.That(located, Is.EqualTo(new NodeId("Sources/A/alpha", 3)));
        }

        [Test]
        public async Task AnIdSharingOnlyAPrefixStringIsRefused()
        {
            // "Sources/AB/x" starts with "Sources/A" but is not beneath it.
            WotMaterializedNodeIndex index = await IndexAsync(s_root);
            var sibling = new ExpandedNodeId(new NodeId("Sources/AB/x", 3));

            NodeId located = index.Locate(Reference(sibling));

            Assert.That(located, Is.EqualTo(new NodeId("Sources/A/alpha", 3)));
        }

        [Test]
        public async Task AnAffordanceWithNoNameAndNoIdIsNotLocated()
        {
            WotMaterializedNodeIndex index = await IndexAsync(s_root);

            NodeId located = index.Locate(
                new WotMaterializedAffordanceRef(
                    SourceHref, WotAffordanceKind.Property, string.Empty, ExpandedNodeId.Null));

            Assert.That(located.IsNull, Is.True);
        }

        [Test]
        public async Task ANumericSourceRootCannotDeriveAName()
        {
            WotMaterializedNodeIndex index = await IndexAsync(new NodeId(7000u, 3));

            NodeId located = index.Locate(Reference(ExpandedNodeId.Null));

            Assert.That(located.IsNull, Is.True,
                "The derived form needs a string root to hang the affordance name off.");
        }

        private static WotMaterializedAffordanceRef Reference(ExpandedNodeId authoredId)
        {
            return new WotMaterializedAffordanceRef(
                SourceHref, WotAffordanceKind.Property, "alpha", authoredId);
        }

        private static async Task<WotMaterializedNodeIndex> IndexAsync(NodeId rootForSource)
        {
            using var service = new WotRegistryService();
            await service.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = WotRegistryGroups.ThingDescriptions,
                ResourceId = "a",
                Kind = WoTDocumentKindEnum.ThingDescription,
                Content = ByteString.From(TestMaterialization.Td(SourceHref))
            });
            WotRegistrySnapshot snapshot = service.Current;

            var roots = new Dictionary<string, NodeId>(StringComparer.Ordinal);
            if (!rootForSource.IsNull)
            {
                WotResource? resource = WotDependencyGraph.Resolve(snapshot, SourceHref);
                Assert.That(resource, Is.Not.Null, "The source must be registered.");
                roots[resource!.Xid] = rootForSource;
            }
            return new WotMaterializedNodeIndex(snapshot, new NamespaceTable(), roots);
        }

        private static readonly NodeId s_root = new("Sources/A", 3);
        private const string SourceHref = "urn:sourceA";
    }
}
