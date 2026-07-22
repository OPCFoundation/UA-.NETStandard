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
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    /// <summary>
    /// Exercises the TD/TM dependency graph: reference extraction, closure
    /// partitioning (weakly-connected components), topological ordering, and
    /// missing-dependency and cycle detection.
    /// </summary>
    [TestFixture]
    public sealed class WotDependencyGraphTests
    {
        private async Task<WotRegistrySnapshot> Snapshot(
            params (WoTDocumentKindEnum Kind, string Id, byte[] Content)[] docs)
        {
            using var service = new WotRegistryService();
            foreach ((WoTDocumentKindEnum kind, string id, byte[] content) in docs)
            {
                await service.UpsertResourceAsync(new WotUpsertResourceRequest
                {
                    GroupId = kind == WoTDocumentKindEnum.ThingModel
                        ? WotRegistryGroups.ThingModels
                        : WotRegistryGroups.ThingDescriptions,
                    ResourceId = id,
                    Kind = kind,
                    Content = content
                });
            }
            return service.Current;
        }

        [Test]
        public void ExtractReferences_FindsTmExtendsLinks()
        {
            byte[] doc = TestMaterialization.Td("urn:td", extendsHrefs: "urn:tm-1");

            IReadOnlyList<(string Href, string RefType)> references =
                WotDependencyGraph.ExtractReferences(doc, 64);

            Assert.That(references.Any(r => r.Href == "urn:tm-1" && r.RefType == "tm:extends"),
                Is.True);
        }

        [Test]
        public async Task BuildClosures_SharedModel_YieldsSingleClosure_TmFirst()
        {
            WotRegistrySnapshot snapshot = await Snapshot(
                (WoTDocumentKindEnum.ThingModel, "tm", TestMaterialization.Tm("urn:tm")),
                (WoTDocumentKindEnum.ThingDescription, "td",
                    TestMaterialization.Td("urn:td", extendsHrefs: "urn:tm")));

            ImmutableArray<WotDependencyClosure> closures =
                WotDependencyGraph.BuildClosures(snapshot, snapshot.AllResources().ToList(), 64);

            Assert.That(closures.Length, Is.EqualTo(1));
            Assert.That(closures[0].IsProjectable, Is.True);
            Assert.That(
                closures[0].OrderedResources.Select(r => r.ResourceId),
                Is.EqualTo(new[] { "tm", "td" }));
        }

        [Test]
        public async Task BuildClosures_IndependentResources_YieldSeparateClosures()
        {
            WotRegistrySnapshot snapshot = await Snapshot(
                (WoTDocumentKindEnum.ThingDescription, "a", TestMaterialization.Td("urn:a")),
                (WoTDocumentKindEnum.ThingDescription, "b", TestMaterialization.Td("urn:b")));

            ImmutableArray<WotDependencyClosure> closures =
                WotDependencyGraph.BuildClosures(snapshot, snapshot.AllResources().ToList(), 64);

            Assert.That(closures.Length, Is.EqualTo(2));
            Assert.That(closures.All(c => c.OrderedResources.Length == 1), Is.True);
        }

        [Test]
        public async Task BuildClosures_MissingDependency_IsFlagged()
        {
            WotRegistrySnapshot snapshot = await Snapshot(
                (WoTDocumentKindEnum.ThingDescription, "td",
                    TestMaterialization.Td("urn:td", extendsHrefs: "urn:missing")));

            ImmutableArray<WotDependencyClosure> closures =
                WotDependencyGraph.BuildClosures(snapshot, snapshot.AllResources().ToList(), 64);

            Assert.That(closures.Length, Is.EqualTo(1));
            Assert.That(closures[0].HasMissingDependency, Is.True);
            Assert.That(closures[0].IsProjectable, Is.False);
        }

        [Test]
        public async Task BuildClosures_Cycle_IsDetected()
        {
            WotRegistrySnapshot snapshot = await Snapshot(
                (WoTDocumentKindEnum.ThingModel, "a",
                    TestMaterialization.Tm("urn:a", extendsHrefs: "urn:b")),
                (WoTDocumentKindEnum.ThingModel, "b",
                    TestMaterialization.Tm("urn:b", extendsHrefs: "urn:a")));

            ImmutableArray<WotDependencyClosure> closures =
                WotDependencyGraph.BuildClosures(snapshot, snapshot.AllResources().ToList(), 64);

            Assert.That(closures.Length, Is.EqualTo(1));
            Assert.That(closures[0].HasCycle, Is.True);
            Assert.That(closures[0].IsProjectable, Is.False);
            Assert.That(closures[0].Members.Length, Is.EqualTo(2),
                "A cyclic closure must still report its members for diagnostics.");
        }
    }
}
