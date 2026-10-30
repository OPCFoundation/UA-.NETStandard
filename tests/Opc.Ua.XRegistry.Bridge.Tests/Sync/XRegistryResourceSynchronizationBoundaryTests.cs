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
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.XRegistry.Bridge.Sync;
using Opc.Ua.XRegistry.Protocol;
using Opc.Ua.XRegistry.Server.Protocol;
using static Opc.Ua.XRegistry.Bridge.Tests.Sync.XRegistrySyncFixture;

namespace Opc.Ua.XRegistry.Bridge.Tests.Sync
{
    /// <summary>
    /// Exercises resource closure admission, source fidelity and operation limits with real transactional candidates.
    /// </summary>
    [TestFixture]
    public sealed class XRegistryResourceSynchronizationBoundaryTests
    {
        /// <summary>
        /// Rejects missing or additional candidate effects before commit, preserving every destination sibling.
        /// </summary>
        [TestCase("absent-metadata")]
        [TestCase("missing-meta")]
        [TestCase("changed-meta")]
        [TestCase("missing-versions")]
        [TestCase("array-versions")]
        [TestCase("extra-version")]
        [TestCase("fewer-versions")]
        [TestCase("renamed-version")]
        [TestCase("missing-document")]
        [TestCase("changed-document")]
        [TestCase("changed-sibling")]
        public async Task UnrequestedCandidateEffectsRejectTheEntireResourceClosureAsync(string corruption)
        {
            await using var fixture = new XRegistrySyncFixture();
            using XRegistryTransactionalEndpoint source = Provider("source", fixture.Clock);
            using XRegistryTransactionalEndpoint target = Provider("target", fixture.Clock);
            await ChangeAsync(source, Resource, k_versions, XRegistryAction.Replace).ConfigureAwait(false);
            await ChangeAsync(target, Resource, k_versions, XRegistryAction.Replace).ConfigureAwait(false);
            var prepared = new XRegistryPreparedBoundaryFixture(target);
            var engine = new XRegistrySynchronizer(
                source, prepared, fixture.Store, fixture.Options, fixture.Telemetry, fixture.Clock);
            XRegistrySyncReport baseline = await engine.RunOnceAsync().ConfigureAwait(false);
            Assert.That(baseline.Status, Is.EqualTo(XRegistrySyncStatus.Succeeded), Details(baseline));
            await ChangeAsync(source, k_first, /*lang=json,strict*/ """{"name":"changed-first"}""")
                .ConfigureAwait(false);
            prepared.RewritePreview = (_, response) => Corrupt(response, corruption);

            XRegistrySyncReport report = await engine.RunOnceAsync().ConfigureAwait(false);
            XRegistryResponse first = await target.ExecuteAsync(Request(XRegistryAction.Read, k_first))
                .ConfigureAwait(false);
            XRegistryResponse second = await target.ExecuteAsync(Request(XRegistryAction.Read, k_second))
                .ConfigureAwait(false);
            XRegistryResponse meta = await target.ExecuteAsync(Request(XRegistryAction.Read, Resource + "/meta"))
                .ConfigureAwait(false);
            XRegistryResponse extra = await target.ExecuteAsync(
                Request(XRegistryAction.Read, Resource + "/versions/3.0.0")).ConfigureAwait(false);
            XRegistrySyncStateStatus state = await fixture.State.ReadStatusAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(report.Applied, Is.Zero, Details(report));
                Assert.That(report.Pending, Is.Zero);
                Assert.That(report.Conflicts, Is.EqualTo(1));
                Assert.That(prepared.Preparations, Is.EqualTo(1));
                Assert.That(prepared.Commits, Is.Zero);
                Assert.That(prepared.Disposals, Is.EqualTo(1));
                Assert.That(first.Metadata.GetProperty("name").GetString(), Is.EqualTo("base-first"));
                Assert.That(second.Metadata.GetProperty("name").GetString(), Is.EqualTo("base-second"));
                Assert.That(meta.Metadata.GetProperty("defaultversionid").GetString(), Is.EqualTo("2.0.0"));
                Assert.That(meta.Metadata.TryGetProperty("name", out _), Is.False);
                Assert.That(extra.StatusCode, Is.EqualTo(404));
                Assert.That(state.Intents, Is.EqualTo(1));
                Assert.That(state.Outcomes, Is.Zero);
                Assert.That(state.Verified, Is.Zero);
            });
            XRegistryResponse document = await target.ExecuteAsync(
                Request(XRegistryAction.Read, k_first) with { View = XRegistryView.Default }).ConfigureAwait(false);
            Assert.That(document.Document.Span.SequenceEqual("a"u8), Is.True);
        }

        /// <summary>
        /// Conflicting sibling edits cannot be overwritten by selecting another version as the closure's entry point.
        /// </summary>
        [Test]
        public async Task IndependentlyChangedSiblingsBlockBothResourceClosuresBeforePreparationAsync()
        {
            await using var fixture = new XRegistrySyncFixture();
            using XRegistryTransactionalEndpoint source = Provider("source", fixture.Clock);
            using XRegistryTransactionalEndpoint target = Provider("target", fixture.Clock);
            await ChangeAsync(source, Resource, k_versions, XRegistryAction.Replace).ConfigureAwait(false);
            await ChangeAsync(target, Resource, k_versions, XRegistryAction.Replace).ConfigureAwait(false);
            var native = new XRegistryPreparedBoundaryFixture(source);
            var http = new XRegistryPreparedBoundaryFixture(target);
            var engine = new XRegistrySynchronizer(
                native, http, fixture.Store, fixture.Options, fixture.Telemetry, fixture.Clock);
            XRegistrySyncReport baseline = await engine.RunOnceAsync().ConfigureAwait(false);
            Assert.That(baseline.Status, Is.EqualTo(XRegistrySyncStatus.Succeeded), Details(baseline));
            await ChangeAsync(source, k_first, /*lang=json,strict*/ """{"name":"native-edit"}""")
                .ConfigureAwait(false);
            await ChangeAsync(target, k_second, /*lang=json,strict*/ """{"name":"http-edit"}""")
                .ConfigureAwait(false);

            XRegistrySyncReport report = await engine.RunOnceAsync().ConfigureAwait(false);
            XRegistryResponse nativeFirst = await source.ExecuteAsync(Request(XRegistryAction.Read, k_first))
                .ConfigureAwait(false);
            XRegistryResponse nativeSecond = await source.ExecuteAsync(Request(XRegistryAction.Read, k_second))
                .ConfigureAwait(false);
            XRegistryResponse httpFirst = await target.ExecuteAsync(Request(XRegistryAction.Read, k_first))
                .ConfigureAwait(false);
            XRegistryResponse httpSecond = await target.ExecuteAsync(Request(XRegistryAction.Read, k_second))
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(report.Applied, Is.Zero, Details(report));
                Assert.That(report.Conflicts, Is.GreaterThan(0));
                Assert.That(native.Preparations, Is.Zero);
                Assert.That(http.Preparations, Is.Zero);
                Assert.That(nativeFirst.Metadata.GetProperty("name").GetString(), Is.EqualTo("native-edit"));
                Assert.That(nativeSecond.Metadata.GetProperty("name").GetString(), Is.EqualTo("base-second"));
                Assert.That(httpFirst.Metadata.GetProperty("name").GetString(), Is.EqualTo("base-first"));
                Assert.That(httpSecond.Metadata.GetProperty("name").GetString(), Is.EqualTo("http-edit"));
            });
        }

        /// <summary>
        /// A destination-only version must be reconciled separately rather than disappearing in another version's copy.
        /// </summary>
        [Test]
        public async Task ExtraDestinationVersionCannotBePrunedByAnotherVersionsClosureAsync()
        {
            await using var fixture = new XRegistrySyncFixture();
            using XRegistryTransactionalEndpoint source = Provider("source", fixture.Clock);
            using XRegistryTransactionalEndpoint target = Provider("target", fixture.Clock);
            await ChangeAsync(source, Resource, k_versions, XRegistryAction.Replace).ConfigureAwait(false);
            await ChangeAsync(target, Resource, k_versions, XRegistryAction.Replace).ConfigureAwait(false);
            var native = new XRegistryPreparedBoundaryFixture(source);
            var http = new XRegistryPreparedBoundaryFixture(target);
            var engine = new XRegistrySynchronizer(
                native, http, fixture.Store, fixture.Options, fixture.Telemetry, fixture.Clock);
            XRegistrySyncReport baseline = await engine.RunOnceAsync().ConfigureAwait(false);
            Assert.That(baseline.Status, Is.EqualTo(XRegistrySyncStatus.Succeeded), Details(baseline));
            await ChangeAsync(source, k_first, /*lang=json,strict*/ """{"name":"native-edit"}""")
                .ConfigureAwait(false);
            await ChangeAsync(target, Resource + "/versions/3.0.0",
                /*lang=json,strict*/ """{"name":"destination-only","schemabase64":"Yw=="}""",
                XRegistryAction.Replace).ConfigureAwait(false);

            XRegistrySyncReport report = await engine.RunOnceAsync().ConfigureAwait(false);
            XRegistryResponse first = await target.ExecuteAsync(Request(XRegistryAction.Read, k_first))
                .ConfigureAwait(false);
            XRegistryResponse retained = await target.ExecuteAsync(
                Request(XRegistryAction.Read, Resource + "/versions/3.0.0")).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(report.Applied, Is.Zero, Details(report));
                Assert.That(report.Records.ToList().Any(record =>
                    record.Path == k_first && record.Kind == XRegistrySyncRecordKind.Conflict),
                        Is.True, Details(report));
                Assert.That(native.Preparations, Is.Zero);
                Assert.That(http.Preparations, Is.Zero);
                Assert.That(first.Metadata.GetProperty("name").GetString(), Is.EqualTo("base-first"));
                Assert.That(retained.StatusCode, Is.EqualTo(200));
                Assert.That(retained.Metadata.GetProperty("name").GetString(), Is.EqualTo("destination-only"));
            });
        }

        /// <summary>
        /// A preview plans the closure once, accounts for its siblings and never acquires a candidate.
        /// </summary>
        [Test]
        public async Task ResourceDryRunPlansOneClosureWithoutPersistingOrPreparingAsync()
        {
            await using var fixture = new XRegistrySyncFixture();
            using XRegistryTransactionalEndpoint source = Provider("source", fixture.Clock);
            using XRegistryTransactionalEndpoint target = Provider("target", fixture.Clock);
            await ChangeAsync(source, Resource, k_versions, XRegistryAction.Replace).ConfigureAwait(false);
            await ChangeAsync(target, Resource, k_versions, XRegistryAction.Replace).ConfigureAwait(false);
            var prepared = new XRegistryPreparedBoundaryFixture(target);
            var engine = new XRegistrySynchronizer(
                source, prepared, fixture.Store, fixture.Options, fixture.Telemetry, fixture.Clock);
            XRegistrySyncReport baseline = await engine.RunOnceAsync().ConfigureAwait(false);
            Assert.That(baseline.Status, Is.EqualTo(XRegistrySyncStatus.Succeeded), Details(baseline));
            await ChangeAsync(source, k_first, /*lang=json,strict*/ """{"name":"changed-first"}""")
                .ConfigureAwait(false);
            await ChangeAsync(source, k_second, /*lang=json,strict*/ """{"name":"changed-second"}""")
                .ConfigureAwait(false);
            ByteString before = await fixture.State.ExportSnapshotAsync().ConfigureAwait(false);

            XRegistrySyncReport preview = await engine.RunOnceAsync(dryRun: true).ConfigureAwait(false);
            ByteString after = await fixture.State.ExportSnapshotAsync().ConfigureAwait(false);
            XRegistrySyncRecord planned = preview.Records.ToList()
                .Single(record => record.Kind == XRegistrySyncRecordKind.Planned);

            Assert.Multiple(() =>
            {
                Assert.That(planned.Path, Is.EqualTo(k_first));
                Assert.That(preview.Applied, Is.Zero);
                Assert.That(preview.Pending, Is.Zero);
                Assert.That(prepared.Preparations, Is.Zero);
                Assert.That(prepared.Commits, Is.Zero);
                Assert.That(after.Span.SequenceEqual(before.Span), Is.True);
            });
        }

        /// <summary>
        /// The per-pass operation budget admits one complete resource, not a partial second closure.
        /// </summary>
        [Test]
        public async Task OperationBudgetLeavesTheSecondResourceEntirelyUntouchedAsync()
        {
            await using var fixture = new XRegistrySyncFixture();
            using XRegistryTransactionalEndpoint source = Provider("source", fixture.Clock);
            using XRegistryTransactionalEndpoint target = Provider("target", fixture.Clock);
            const string other = "/schemagroups/g/schemas/s";
            foreach (string resource in new[] { Resource, other })
            {
                await ChangeAsync(source, resource, k_versions, XRegistryAction.Replace).ConfigureAwait(false);
                await ChangeAsync(target, resource, k_versions, XRegistryAction.Replace).ConfigureAwait(false);
            }
            var prepared = new XRegistryPreparedBoundaryFixture(target);
            var engine = new XRegistrySynchronizer(
                source, prepared, fixture.Store, fixture.Options, fixture.Telemetry, fixture.Clock);
            XRegistrySyncReport baseline = await engine.RunOnceAsync().ConfigureAwait(false);
            Assert.That(baseline.Status, Is.EqualTo(XRegistrySyncStatus.Succeeded), Details(baseline));
            await ChangeAsync(source, k_first, /*lang=json,strict*/ """{"name":"changed-r"}""")
                .ConfigureAwait(false);
            await ChangeAsync(source, other + "/versions/1.0.0", /*lang=json,strict*/ """{"name":"changed-s"}""")
                .ConfigureAwait(false);
            var limited = new XRegistrySynchronizer(source, prepared, fixture.Store,
                fixture.Options with { MaximumOperations = 1 }, fixture.Telemetry, fixture.Clock);

            XRegistrySyncReport report = await limited.RunOnceAsync().ConfigureAwait(false);
            XRegistryResponse changed = await target.ExecuteAsync(Request(XRegistryAction.Read, k_first))
                .ConfigureAwait(false);
            XRegistryResponse untouched = await target.ExecuteAsync(
                Request(XRegistryAction.Read, other + "/versions/1.0.0")).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(report.Applied, Is.EqualTo(1), Details(report));
                Assert.That(prepared.Preparations, Is.EqualTo(1));
                Assert.That(prepared.Commits, Is.EqualTo(1));
                Assert.That(prepared.Disposals, Is.EqualTo(1));
                Assert.That(changed.Metadata.GetProperty("name").GetString(), Is.EqualTo("changed-r"));
                Assert.That(untouched.Metadata.GetProperty("name").GetString(), Is.EqualTo("base-first"));
            });
        }

        /// <summary>
        /// An ordinary endpoint cannot authorize automatic resource side effects without global preparation.
        /// </summary>
        [Test]
        public async Task AutomaticResourceClosureRequiresAPreparedDestinationAsync()
        {
            await using var fixture = new XRegistrySyncFixture(k_model);
            await fixture.SeedBothAsync(Resource, k_versions).ConfigureAwait(false);
            await fixture.BaselineAsync().ConfigureAwait(false);
            await fixture.Native.ChangeAsync(k_first, /*lang=json,strict*/ """{"name":"changed"}""",
                XRegistryAction.Merge).ConfigureAwait(false);

            XRegistrySyncReport report = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            XRegistryResponse unchanged = await fixture.Http.ReadAsync(k_first).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(report.Applied, Is.Zero);
                Assert.That(report.Records.ToList().Any(record =>
                    record.Path == k_first && record.Kind == XRegistrySyncRecordKind.Unsupported), Is.True);
                Assert.That(fixture.Http.Mutations, Is.Empty);
                Assert.That(fixture.Native.Mutations, Is.Empty);
                Assert.That(unchanged.Metadata.GetProperty("name").GetString(), Is.EqualTo("base-first"));
            });
        }

        /// <summary>
        /// Array-valued model constraints alone do not imply cross-version matching or a resource-wide write.
        /// </summary>
        [Test]
        public async Task ArrayValuedEnumWithoutMatchingRulesUsesAnOrdinaryGuardedVersionWriteAsync()
        {
            const string model = /*lang=json,strict*/ """
                {"groups":{"schemagroups":{"singular":"schemagroup","resources":{"schemas":{
                  "singular":"schema","versionmode":"manual",
                  "attributes":{"format":{"type":"string","enum":["json","xml"]}}
                }}}}}
                """;
            await using var fixture = new XRegistrySyncFixture(model);
            await fixture.SeedBothAsync(Resource, k_versions).ConfigureAwait(false);
            await fixture.BaselineAsync().ConfigureAwait(false);
            await fixture.Native.ChangeAsync(k_first, /*lang=json,strict*/ """{"format":"json"}""",
                XRegistryAction.Merge).ConfigureAwait(false);

            XRegistrySyncReport report = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            XRegistryResponse changed = await fixture.Http.ReadAsync(k_first).ConfigureAwait(false);
            XRegistryResponse sibling = await fixture.Http.ReadAsync(k_second).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(report.Status, Is.EqualTo(XRegistrySyncStatus.Succeeded), Details(report));
                Assert.That(fixture.Http.Mutations, Has.Count.EqualTo(1));
                Assert.That(fixture.Http.Mutations[0].Path, Is.EqualTo(k_first));
                Assert.That(changed.Metadata.GetProperty("format").GetString(), Is.EqualTo("json"));
                Assert.That(sibling.Metadata.TryGetProperty("format", out _), Is.False);
            });
        }

        private static XRegistryResponse Corrupt(XRegistryResponse response, string corruption)
        {
            Assert.That(response.IsSuccess, Is.True, response.Error?.Detail);
            if (corruption == "absent-metadata")
            {
                return response with { Metadata = default };
            }
            JsonObject metadata = JsonNode.Parse(response.Metadata.GetRawText())!.AsObject();
            switch (corruption)
            {
                case "missing-meta":
                    metadata.Remove("meta");
                    break;
                case "changed-meta":
                    metadata["meta"]!["name"] = "unrequested-meta";
                    break;
                case "missing-versions":
                    metadata.Remove("versions");
                    break;
                case "array-versions":
                    metadata["versions"] = new JsonArray();
                    break;
                case "extra-version":
                    metadata["versions"]!["3.0.0"] = metadata["versions"]!["2.0.0"]!.DeepClone();
                    break;
                case "fewer-versions":
                    metadata["versions"]!.AsObject().Remove("2.0.0");
                    break;
                case "renamed-version":
                    metadata["versions"]!["3.0.0"] = metadata["versions"]!["2.0.0"]!.DeepClone();
                    metadata["versions"]!.AsObject().Remove("2.0.0");
                    break;
                case "missing-document":
                    Assert.That(metadata["versions"]!["1.0.0"]!.AsObject().Remove("schemabase64"), Is.True);
                    break;
                case "changed-document":
                    metadata["versions"]!["1.0.0"]!["schemabase64"] = "Yw==";
                    break;
                case "changed-sibling":
                    metadata["versions"]!["2.0.0"]!["name"] = "unrequested-sibling";
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(corruption));
            }
            return response with { Metadata = Json(metadata.ToJsonString()) };
        }

        private static async Task ChangeAsync(
            XRegistryTransactionalEndpoint endpoint, string path, string json,
            XRegistryAction action = XRegistryAction.Merge)
        {
            XRegistryResponse response = await endpoint.ExecuteAsync(
                Request(action, path) with { Metadata = Json(json) }).ConfigureAwait(false);
            Assert.That(response.StatusCode, Is.AnyOf(200, 201), response.Error?.Detail);
        }

        private static XRegistryRequest Request(XRegistryAction action, string path)
        {
            return new XRegistryRequest(action, path) { Context = Writer, View = XRegistryView.Metadata };
        }

        private static XRegistryTransactionalEndpoint Provider(string id, TimeProvider clock)
        {
            return new XRegistryTransactionalEndpoint(new XRegistryTransactionalOptions
            {
                RegistryId = id,
                Model = Json(k_model)
            }, new InMemoryXRegistryTransactionStore(), clock);
        }

        private const string k_first = Resource + "/versions/1.0.0";
        private const string k_second = Resource + "/versions/2.0.0";

        private const string k_versions = /*lang=json,strict*/ """
            {"versions":{"1.0.0":{"name":"base-first","schemabase64":"YQ=="},
                         "2.0.0":{"name":"base-second","schemabase64":"Yg=="}}}
            """;

        private const string k_model = /*lang=json,strict*/ """
            {"groups":{"schemagroups":{"singular":"schemagroup","resources":{"schemas":{
              "singular":"schema","versionmode":"semver","maxversions":3
            }}}}}
            """;
    }
}
