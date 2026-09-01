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
using System.Threading;
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
        private static readonly string[] s_tmTdResourceIds = ["tm", "td"];

        /// <summary>
        /// A snapshot plus a reader for the bytes behind its versions. The
        /// snapshot carries only digests, so a caller that needs the content has
        /// to be able to fetch it; keeping the two together avoids a global
        /// registry and keeps each test self-contained.
        /// </summary>
        private sealed record SnapshotFixture(
            WotRegistrySnapshot Snapshot,
            Func<WotResourceVersion, CancellationToken, ValueTask<ByteString>> ReadContent);

        private static async Task<SnapshotFixture> Snapshot(
            params (WoTDocumentKindEnum Kind, string Id, byte[] Content)[] docs)
        {
            var byDigest = new Dictionary<string, ByteString>(StringComparer.Ordinal);
            using var service = new WotRegistryService();
            foreach ((WoTDocumentKindEnum kind, string id, byte[] content) in docs)
            {
                ByteString bytes = ByteString.From(content);
                byDigest[WotContentDigest.ToHex(WotContentDigest.Compute(bytes))] = bytes;
                await service.UpsertResourceAsync(new WotUpsertResourceRequest
                {
                    GroupId = kind == WoTDocumentKindEnum.ThingModel
                        ? WotRegistryGroups.ThingModels
                        : WotRegistryGroups.ThingDescriptions,
                    ResourceId = id,
                    Kind = kind,
                    Content = bytes
                });
            }
            return new SnapshotFixture(
                service.Current,
                (version, _) => new ValueTask<ByteString>(byDigest[version.DigestHex]));
        }

        [Test]
        public void ExtractReferencesFindsTmExtendsLinks()
        {
            byte[] doc = TestMaterialization.Td("urn:td", extendsHrefs: "urn:tm-1");

            IReadOnlyList<(string Href, string RefType)> references =
                WotDependencyGraph.ExtractReferences(doc, 64);

            Assert.That(references.Any(r => r.Href == "urn:tm-1" && r.RefType == "tm:extends"),
                Is.True);
        }

        /// <summary>
        /// An event affordance names the EventType definition its fields are
        /// selected from, and every explicit select clause names one too
        /// (WoT Binding Section 6.1). Both are dependency edges: the reference
        /// resolves against the documents a consumer holds, so the EventType
        /// Thing Model has to be a member of the same closure and has to be
        /// loaded before the document that selects from it.
        /// </summary>
        [Test]
        public void ExtractReferencesFindsEventTypeLinks()
        {
            byte[] doc = System.Text.Encoding.UTF8.GetBytes(
                "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "\"@type\":\"Thing\",\"id\":\"urn:td\",\"title\":\"Td\"," +
                "\"events\":{\"alarm\":{\"@type\":\"uav:eventType\"," +
                "\"tm:ref\":\"urn:tm-events#/events/alarm\"," +
                "\"uav:eventSelectClauses\":[" +
                "{\"tm:ref\":\"urn:tm-base\",\"uav:browsePath\":\"EventId\"}]}}}");

            IReadOnlyList<(string Href, string RefType)> references =
                WotDependencyGraph.ExtractReferences(doc, 64);

            Assert.Multiple(() =>
            {
                Assert.That(
                    references.Any(r =>
                        r.Href == "urn:tm-events#/events/alarm" &&
                        r.RefType == WotDependencyGraph.EventTypeRefType),
                    Is.True,
                    "The affordance-level fast path is an edge to the EventType definition.");
                Assert.That(
                    references.Any(r =>
                        r.Href == "urn:tm-base" &&
                        r.RefType == WotDependencyGraph.EventSelectClauseRefType),
                    Is.True,
                    "A clause names the EventType that declares the field it selects, which " +
                    "is an edge of its own.");
                Assert.That(
                    references.Count(r => r.Href == "urn:tm-events#/events/alarm"),
                    Is.EqualTo(1),
                    "The edge is stated once, under the label that says what it is.");
            });
        }

        /// <summary>
        /// The edge is what puts an EventType Thing Model in the closure of the
        /// document that selects from it, and the topological order loads it
        /// first.
        /// </summary>
        [Test]
        public async Task AnEventTypeModelIsLoadedBeforeTheDocumentThatSelectsFromIt()
        {
            SnapshotFixture fixture = await Snapshot(
                (WoTDocumentKindEnum.ThingModel, "events", TestMaterialization.Tm("urn:tm-events")),
                (WoTDocumentKindEnum.ThingDescription, "td",
                    System.Text.Encoding.UTF8.GetBytes(
                        "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\"," +
                        "\"@type\":\"Thing\",\"id\":\"urn:td\",\"title\":\"Td\"," +
                        "\"events\":{\"alarm\":{\"@type\":\"uav:eventType\"," +
                        "\"tm:ref\":\"urn:tm-events#/events/alarm\"}}}")));

            ImmutableArray<WotDependencyClosure> closures =
                await WotDependencyGraph.BuildClosuresAsync(
                    fixture.Snapshot,
                    [.. fixture.Snapshot.AllResources()],
                    64,
                    fixture.ReadContent,
                    CancellationToken.None);

            Assert.That(closures, Has.Length.EqualTo(1));
            Assert.That(
                closures[0].OrderedResources.Select(r => r.ResourceId),
                Is.EqualTo(new[] { "events", "td" }).AsCollection,
                "A consumer resolves the reference against the documents it holds, so the " +
                "definition is materialized before the document that names it.");
        }

        [Test]
        public async Task BuildClosuresSharedModelYieldsSingleClosureTmFirst()
        {
            SnapshotFixture fixture = await Snapshot(
                (WoTDocumentKindEnum.ThingModel, "tm", TestMaterialization.Tm("urn:tm")),
                (WoTDocumentKindEnum.ThingDescription, "td",
                    TestMaterialization.Td("urn:td", extendsHrefs: "urn:tm")));

            ImmutableArray<WotDependencyClosure> closures =
                await WotDependencyGraph.BuildClosuresAsync(
                    fixture.Snapshot,
                    [.. fixture.Snapshot.AllResources()],
                    64,
                    fixture.ReadContent,
                    CancellationToken.None);

            Assert.That(closures, Has.Length.EqualTo(1));
            Assert.That(closures[0].IsProjectable, Is.True);
            Assert.That(
                closures[0].OrderedResources.Select(r => r.ResourceId),
                Is.EqualTo(s_tmTdResourceIds));
        }

        [Test]
        public async Task BuildClosuresIndependentResourcesYieldSeparateClosures()
        {
            SnapshotFixture fixture = await Snapshot(
                (WoTDocumentKindEnum.ThingDescription, "a", TestMaterialization.Td("urn:a")),
                (WoTDocumentKindEnum.ThingDescription, "b", TestMaterialization.Td("urn:b")));

            ImmutableArray<WotDependencyClosure> closures =
                await WotDependencyGraph.BuildClosuresAsync(
                    fixture.Snapshot,
                    [.. fixture.Snapshot.AllResources()],
                    64,
                    fixture.ReadContent,
                    CancellationToken.None);

            Assert.That(closures, Has.Length.EqualTo(2));
            Assert.That(closures.All(c => c.OrderedResources.Length == 1), Is.True);
        }

        [Test]
        public async Task BuildClosuresMissingDependencyIsFlagged()
        {
            SnapshotFixture fixture = await Snapshot(
                (WoTDocumentKindEnum.ThingDescription, "td",
                    TestMaterialization.Td("urn:td", extendsHrefs: "urn:missing")));

            ImmutableArray<WotDependencyClosure> closures =
                await WotDependencyGraph.BuildClosuresAsync(
                    fixture.Snapshot,
                    [.. fixture.Snapshot.AllResources()],
                    64,
                    fixture.ReadContent,
                    CancellationToken.None);

            Assert.That(closures, Has.Length.EqualTo(1));
            Assert.That(closures[0].HasMissingDependency, Is.True);
            Assert.That(closures[0].IsProjectable, Is.False);
        }

        [Test]
        public async Task BuildClosuresCycleIsDetected()
        {
            SnapshotFixture fixture = await Snapshot(
                (WoTDocumentKindEnum.ThingModel, "a",
                    TestMaterialization.Tm("urn:a", extendsHrefs: "urn:b")),
                (WoTDocumentKindEnum.ThingModel, "b",
                    TestMaterialization.Tm("urn:b", extendsHrefs: "urn:a")));

            ImmutableArray<WotDependencyClosure> closures =
                await WotDependencyGraph.BuildClosuresAsync(
                    fixture.Snapshot,
                    [.. fixture.Snapshot.AllResources()],
                    64,
                    fixture.ReadContent,
                    CancellationToken.None);

            Assert.That(closures, Has.Length.EqualTo(1));
            Assert.That(closures[0].HasCycle, Is.True);
            Assert.That(closures[0].IsProjectable, Is.False);
            Assert.That(closures[0].Members, Has.Length.EqualTo(2),
                "A cyclic closure must still report its members for diagnostics.");
        }
    }
}
