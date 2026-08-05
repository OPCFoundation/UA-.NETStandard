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
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    /// <summary>
    /// Supplemental tests for <see cref="WotDependencyGraph"/> covering
    /// <c>Resolve</c>, <c>ExtractReferences</c> edge cases, closure
    /// diagnostics, and the <see cref="WotDependency"/> and
    /// <see cref="WotDependencyClosure"/> value objects.
    /// </summary>
    [TestFixture]
    [Category("WotCon")]
    [Parallelizable(ParallelScope.All)]
    public sealed class WotDependencyGraphExtendedTests
    {
        private static async Task<WotRegistrySnapshot> SnapshotAsync(
            params (WoTDocumentKindEnum Kind, string Id, string ThingId, byte[] Content)[] docs)
        {
            using var service = new WotRegistryService();
            foreach ((WoTDocumentKindEnum kind, string id, _, byte[] content) in docs)
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

        private static async Task<WotRegistrySnapshot> SnapshotAsync(
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
        public void ResolveWithNullSnapshotReturnsNull()
        {
            WotResource? result = WotDependencyGraph.Resolve(null!, "urn:some");
            Assert.That(result, Is.Null);
        }

        [Test]
        public async Task ResolveWithNullHrefReturnsNull()
        {
            WotRegistrySnapshot snapshot = await SnapshotAsync(
                (WoTDocumentKindEnum.ThingModel, "tm", TestMaterialization.Tm("urn:tm")));

            WotResource? result = WotDependencyGraph.Resolve(snapshot, null!);
            Assert.That(result, Is.Null);
        }

        [Test]
        public async Task ResolveWithEmptyHrefReturnsNull()
        {
            WotRegistrySnapshot snapshot = await SnapshotAsync(
                (WoTDocumentKindEnum.ThingModel, "tm", TestMaterialization.Tm("urn:tm")));

            WotResource? result = WotDependencyGraph.Resolve(snapshot, string.Empty);
            Assert.That(result, Is.Null);
        }

        [Test]
        public async Task ResolveWithWhitespaceHrefReturnsNull()
        {
            WotRegistrySnapshot snapshot = await SnapshotAsync(
                (WoTDocumentKindEnum.ThingModel, "tm", TestMaterialization.Tm("urn:tm")));

            WotResource? result = WotDependencyGraph.Resolve(snapshot, "   ");
            Assert.That(result, Is.Null);
        }

        [Test]
        public async Task ResolveByThingIdFindsResource()
        {
            WotRegistrySnapshot snapshot = await SnapshotAsync(
                (WoTDocumentKindEnum.ThingModel, "tm-res", TestMaterialization.Tm("urn:my-thing")));

            WotResource? result = WotDependencyGraph.Resolve(snapshot, "urn:my-thing");
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.ResourceId, Is.EqualTo("tm-res"));
        }

        [Test]
        public async Task ResolveByXidFindsResource()
        {
            WotRegistrySnapshot snapshot = await SnapshotAsync(
                (WoTDocumentKindEnum.ThingModel, "my-tm",
                    TestMaterialization.Tm("urn:other")));

            string xid = snapshot.AllResources().First().Xid;
            WotResource? result = WotDependencyGraph.Resolve(snapshot, xid);
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.ResourceId, Is.EqualTo("my-tm"));
        }

        [Test]
        public async Task ResolveByResourceIdFindsResource()
        {
            WotRegistrySnapshot snapshot = await SnapshotAsync(
                (WoTDocumentKindEnum.ThingDescription, "my-td",
                    TestMaterialization.Td("urn:td")));

            WotResource? result = WotDependencyGraph.Resolve(snapshot, "my-td");
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.ResourceId, Is.EqualTo("my-td"));
        }

        [Test]
        public async Task ResolveWithFragmentTrimsFragment()
        {
            WotRegistrySnapshot snapshot = await SnapshotAsync(
                (WoTDocumentKindEnum.ThingModel, "tm-frag",
                    TestMaterialization.Tm("urn:frag-thing")));

            WotResource? result = WotDependencyGraph.Resolve(snapshot, "urn:frag-thing#someProperty");
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.ResourceId, Is.EqualTo("tm-frag"));
        }

        [Test]
        public async Task ResolveByRegistryUriFindsResource()
        {
            WotRegistrySnapshot snapshot = await SnapshotAsync(
                (WoTDocumentKindEnum.ThingModel, "myresource",
                    TestMaterialization.Tm("urn:id")));

            string group = WotRegistryGroups.ThingModels;
            string uri = $"urn:wot:{group}/myresource";
            WotResource? result = WotDependencyGraph.Resolve(snapshot, uri);
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.ResourceId, Is.EqualTo("myresource"));
        }

        [Test]
        public async Task ResolvePrefersTmOverTd()
        {
            WotRegistrySnapshot snapshot = await SnapshotAsync(
                (WoTDocumentKindEnum.ThingModel, "tmx", TestMaterialization.Tm("urn:shared-id")),
                (WoTDocumentKindEnum.ThingDescription, "tdx",
                    TestMaterialization.Td("urn:shared-id")));

            WotResource? result = WotDependencyGraph.Resolve(snapshot, "urn:shared-id");
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Kind, Is.EqualTo(WoTDocumentKindEnum.ThingModel),
                "TM should be preferred over TD when both match the same href.");
        }

        [Test]
        public void ExtractReferencesReturnsEmptyForInvalidJson()
        {
            IReadOnlyList<(string Href, string RefType)> refs =
                WotDependencyGraph.ExtractReferences(TestMaterialization.InvalidJson(), 64);
            Assert.That(refs, Is.Empty);
        }

        [Test]
        public void ExtractReferencesReturnsEmptyForNonObjectJson()
        {
            byte[] arrayJson = Encoding.UTF8.GetBytes("[1,2,3]");
            IReadOnlyList<(string Href, string RefType)> refs =
                WotDependencyGraph.ExtractReferences(arrayJson, 64);
            Assert.That(refs, Is.Empty);
        }

        [Test]
        public void ExtractReferencesFindsLinksRelType()
        {
            byte[] doc = Encoding.UTF8.GetBytes(
                "{\"links\":[{\"rel\":\"type\",\"href\":\"urn:tm-base\"}]}");
            IReadOnlyList<(string Href, string RefType)> refs =
                WotDependencyGraph.ExtractReferences(doc, 64);
            Assert.That(refs.Any(r => r.Href == "urn:tm-base" && r.RefType == "type"), Is.True);
        }

        [Test]
        public void ExtractReferencesFindsLinksRelTmSubmodel()
        {
            byte[] doc = Encoding.UTF8.GetBytes(
                "{\"links\":[{\"rel\":\"tm:submodel\",\"href\":\"urn:sub\"}]}");
            IReadOnlyList<(string Href, string RefType)> refs =
                WotDependencyGraph.ExtractReferences(doc, 64);
            Assert.That(refs.Any(r => r.Href == "urn:sub" && r.RefType == "tm:submodel"), Is.True);
        }

        [Test]
        public void ExtractReferencesIgnoresLinksWithUnrecognizedRel()
        {
            byte[] doc = Encoding.UTF8.GetBytes(
                "{\"links\":[{\"rel\":\"related\",\"href\":\"urn:other\"}]}");
            IReadOnlyList<(string Href, string RefType)> refs =
                WotDependencyGraph.ExtractReferences(doc, 64);
            Assert.That(refs, Is.Empty);
        }

        [Test]
        public void ExtractReferencesIgnoresLinksWithoutHref()
        {
            byte[] doc = Encoding.UTF8.GetBytes(
                "{\"links\":[{\"rel\":\"tm:extends\"}]}");
            IReadOnlyList<(string Href, string RefType)> refs =
                WotDependencyGraph.ExtractReferences(doc, 64);
            Assert.That(refs, Is.Empty);
        }

        [Test]
        public void ExtractReferencesFindsTmRef()
        {
            byte[] doc = Encoding.UTF8.GetBytes(
                "{\"properties\":{\"p1\":{\"tm:ref\":\"urn:base#/properties/p1\"}}}");
            IReadOnlyList<(string Href, string RefType)> refs =
                WotDependencyGraph.ExtractReferences(doc, 64);
            Assert.That(refs.Any(r => r.RefType == "tm:ref"), Is.True);
        }

        [Test]
        public void ExtractReferencesFindsTmExtendsArray()
        {
            byte[] doc = Encoding.UTF8.GetBytes(
                "{\"tm:extends\":[\"urn:base1\",\"urn:base2\"]}");
            IReadOnlyList<(string Href, string RefType)> refs =
                WotDependencyGraph.ExtractReferences(doc, 64);
            Assert.That(refs.Count(r => r.RefType == "tm:extends"), Is.EqualTo(2));
        }

        [Test]
        public void ExtractReferencesFindsTmExtendsObjectWithHref()
        {
            byte[] doc = Encoding.UTF8.GetBytes(
                "{\"tm:extends\":[{\"href\":\"urn:obj-base\"}]}");
            IReadOnlyList<(string Href, string RefType)> refs =
                WotDependencyGraph.ExtractReferences(doc, 64);
            Assert.That(refs.Any(r => r.Href == "urn:obj-base"), Is.True);
        }

        [Test]
        public async Task BuildClosuresEmptySelectionReturnsEmpty()
        {
            WotRegistrySnapshot snapshot = await SnapshotAsync(
                (WoTDocumentKindEnum.ThingDescription, "td",
                    TestMaterialization.Td("urn:td")));

            ImmutableArray<WotDependencyClosure> closures =
                WotDependencyGraph.BuildClosures(snapshot, [], 64);
            Assert.That(closures, Is.Empty);
        }

        [Test]
        public async Task BuildClosuresDiagnosticsIncludesMissingDependencyMessage()
        {
            WotRegistrySnapshot snapshot = await SnapshotAsync(
                (WoTDocumentKindEnum.ThingDescription, "td",
                    TestMaterialization.Td("urn:td", extendsHrefs: "urn:nonexistent")));

            ImmutableArray<WotDependencyClosure> closures =
                WotDependencyGraph.BuildClosures(snapshot, [.. snapshot.AllResources()], 64);

            WotDependencyClosure closure = closures[0];
            Assert.That(closure.HasMissingDependency, Is.True);
            Assert.That(closure.Diagnostics, Has.Length.GreaterThan(0));
            Assert.That(closure.Diagnostics.Any(d => d.Contains("urn:nonexistent", StringComparison.Ordinal)), Is.True);
        }

        [Test]
        public async Task BuildClosuresDiagnosticsIncludesCycleMessage()
        {
            WotRegistrySnapshot snapshot = await SnapshotAsync(
                (WoTDocumentKindEnum.ThingModel, "a",
                    TestMaterialization.Tm("urn:a", extendsHrefs: "urn:b")),
                (WoTDocumentKindEnum.ThingModel, "b",
                    TestMaterialization.Tm("urn:b", extendsHrefs: "urn:a")));

            ImmutableArray<WotDependencyClosure> closures =
                WotDependencyGraph.BuildClosures(snapshot, [.. snapshot.AllResources()], 64);

            WotDependencyClosure closure = closures[0];
            Assert.That(closure.HasCycle, Is.True);
            Assert.That(closure.Diagnostics, Has.Length.GreaterThan(0));
            Assert.That(closure.Diagnostics.Any(d => d.Contains("cycle", StringComparison.Ordinal)), Is.True);
        }

        [Test]
        public async Task BuildClosuresClosureKeyIsJoinedSortedXids()
        {
            WotRegistrySnapshot snapshot = await SnapshotAsync(
                (WoTDocumentKindEnum.ThingModel, "tm",
                    TestMaterialization.Tm("urn:tm")),
                (WoTDocumentKindEnum.ThingDescription, "td",
                    TestMaterialization.Td("urn:td", extendsHrefs: "urn:tm")));

            ImmutableArray<WotDependencyClosure> closures =
                WotDependencyGraph.BuildClosures(snapshot, [.. snapshot.AllResources()], 64);

            WotDependencyClosure closure = closures[0];
            string expectedKey = string.Join("|",
                closure.Members.OrderBy(m => m.Xid, StringComparer.Ordinal).Select(m => m.Xid));
            Assert.That(closure.Key, Is.EqualTo(expectedKey));
        }

        [Test]
        public async Task BuildClosuresDependenciesAreRecorded()
        {
            WotRegistrySnapshot snapshot = await SnapshotAsync(
                (WoTDocumentKindEnum.ThingModel, "tm",
                    TestMaterialization.Tm("urn:tm")),
                (WoTDocumentKindEnum.ThingDescription, "td",
                    TestMaterialization.Td("urn:td", extendsHrefs: "urn:tm")));

            ImmutableArray<WotDependencyClosure> closures =
                WotDependencyGraph.BuildClosures(snapshot, [.. snapshot.AllResources()], 64);

            WotDependencyClosure closure = closures[0];
            Assert.That(closure.Dependencies, Is.Not.Empty);
            WotDependency dep = closure.Dependencies[0];
            Assert.That(dep.Resolved, Is.True);
            Assert.That(dep.TargetHref, Is.EqualTo("urn:tm"));
            Assert.That(dep.RefType, Is.EqualTo("tm:extends"));
        }

        [Test]
        public async Task BuildClosuresDiamondPatternYieldsSingleClosure()
        {
            // td → tmA and td → tmB, tmA → tmBase, tmB → tmBase
            // All four in one closure (all reachable from td).
            byte[] tmBase = TestMaterialization.Tm("urn:base");
            byte[] tmA = TestMaterialization.Tm("urn:a", extendsHrefs: "urn:base");
            byte[] tmB = TestMaterialization.Tm("urn:b", extendsHrefs: "urn:base");
            byte[] td = Encoding.UTF8.GetBytes(
                "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "\"id\":\"urn:td\"," +
                "\"title\":\"td\"," +
                "\"links\":[{\"rel\":\"tm:extends\",\"href\":\"urn:a\"}," +
                           "{\"rel\":\"tm:extends\",\"href\":\"urn:b\"}]}");

            WotRegistrySnapshot snapshot = await SnapshotAsync(
                (WoTDocumentKindEnum.ThingModel, "base", tmBase),
                (WoTDocumentKindEnum.ThingModel, "a", tmA),
                (WoTDocumentKindEnum.ThingModel, "b", tmB),
                (WoTDocumentKindEnum.ThingDescription, "td", td));

            ImmutableArray<WotDependencyClosure> closures =
                WotDependencyGraph.BuildClosures(snapshot, [.. snapshot.AllResources()], 64);

            Assert.That(closures, Has.Length.EqualTo(1),
                "Diamond dependency should group all four resources into one closure.");
            Assert.That(closures[0].IsProjectable, Is.True);
            Assert.That(closures[0].Members, Has.Length.EqualTo(4));
        }

        [Test]
        public void WotDependencyPropertiesAreCorrect()
        {
            var dep = new WotDependency(
                sourceXid: "/groups/g/resources/r1",
                targetHref: "urn:target",
                targetXid: "/groups/g/resources/r2",
                refType: "tm:extends",
                resolved: true);

            Assert.That(dep.SourceXid, Is.EqualTo("/groups/g/resources/r1"));
            Assert.That(dep.TargetHref, Is.EqualTo("urn:target"));
            Assert.That(dep.TargetXid, Is.EqualTo("/groups/g/resources/r2"));
            Assert.That(dep.RefType, Is.EqualTo("tm:extends"));
            Assert.That(dep.Resolved, Is.True);
        }

        [Test]
        public void WotDependencyUnresolvedPropertiesAreCorrect()
        {
            var dep = new WotDependency(
                sourceXid: "/groups/g/resources/r1",
                targetHref: "urn:missing",
                targetXid: null,
                refType: "tm:extends",
                resolved: false);

            Assert.That(dep.TargetXid, Is.Null);
            Assert.That(dep.Resolved, Is.False);
        }
    }
}
