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
    /// Exercises assigned identity admission using genuine candidates and deliberately invalid provider previews.
    /// </summary>
    [TestFixture]
    public sealed class XRegistryAssignedVersionBoundaryTests
    {
        /// <summary>
        /// Ambiguous, colliding or corrupted assigned identities cannot commit or become durable correspondences.
        /// </summary>
        [TestCase("absent-metadata")]
        [TestCase("missing-id")]
        [TestCase("numeric-id")]
        [TestCase("existing-id")]
        [TestCase("mapped-id")]
        [TestCase("ancestor")]
        [TestCase("document")]
        [TestCase("missing-document")]
        public async Task InvalidAssignedPreviewCannotCommitOrRetainANewCorrespondenceAsync(string corruption)
        {
            await using var fixture = new XRegistrySyncFixture();
            using XRegistryTransactionalEndpoint source = Provider("source", fixture.Clock);
            using XRegistryTransactionalEndpoint target = Provider("target", fixture.Clock);
            if (corruption == "mapped-id")
            {
                fixture.Options = fixture.Options with
                {
                    VersionCorrespondences =
                    [
                        new XRegistryVersionCorrespondence(Resource + "/versions/reserved",
                            Resource + "/versions/native-reserved", k_second)
                    ]
                };
            }
            await CreateVersionAsync(source, "base").ConfigureAwait(false);
            await CreateVersionAsync(target, "base").ConfigureAwait(false);
            var prepared = new XRegistryPreparedBoundaryFixture(target);
            var engine = new XRegistrySynchronizer(
                source, prepared, fixture.Store, fixture.Options, fixture.Telemetry, fixture.Clock);
            XRegistrySyncReport baseline = await engine.RunOnceAsync().ConfigureAwait(false);
            Assert.That(baseline.Status, Is.EqualTo(XRegistrySyncStatus.Succeeded), Details(baseline));
            await CreateVersionAsync(source, "next", ancestor: "1").ConfigureAwait(false);
            prepared.RewritePreview = (_, response) => Corrupt(response, corruption);

            XRegistrySyncReport report = await engine.RunOnceAsync().ConfigureAwait(false);
            XRegistryResponse first = await target.ExecuteAsync(Request(XRegistryAction.Read, k_first))
                .ConfigureAwait(false);
            XRegistryResponse absent = await target.ExecuteAsync(Request(XRegistryAction.Read, k_second))
                .ConfigureAwait(false);
            XRegistrySyncState retained = new XRegistrySyncStateCodec().Decode(
                await fixture.State.ExportSnapshotAsync().ConfigureAwait(false));

            Assert.Multiple(() =>
            {
                Assert.That(report.Applied, Is.Zero, Details(report));
                Assert.That(report.Pending, Is.Zero, Details(report));
                Assert.That(report.Conflicts, Is.GreaterThan(0));
                Assert.That(prepared.Preparations, Is.EqualTo(1));
                Assert.That(prepared.Commits, Is.Zero);
                Assert.That(prepared.Disposals, Is.EqualTo(1));
                Assert.That(first.StatusCode, Is.EqualTo(200));
                Assert.That(first.Metadata.GetProperty("versionid").GetString(), Is.EqualTo("1"));
                Assert.That(first.Metadata.GetProperty("name").GetString(), Is.EqualTo("base"));
                Assert.That(absent.StatusCode, Is.EqualTo(404));
                Assert.That(retained.VersionCorrespondences.ContainsKey(k_second), Is.False);
                Assert.That(retained.VersionCorrespondences, Has.Count.EqualTo(corruption == "mapped-id" ? 1 : 0));
                Assert.That(retained.Intents.Values.Single().State, Is.EqualTo(XRegistrySyncIntentState.Conflicted));
            });
        }

        /// <summary>
        /// A dry run can plan an assigned child without acquiring a candidate or reserving its destination identity.
        /// </summary>
        [Test]
        public async Task AssignedVersionDryRunDoesNotPrepareReserveOrPublishAsync()
        {
            await using var fixture = new XRegistrySyncFixture();
            using XRegistryTransactionalEndpoint source = Provider("source", fixture.Clock);
            using XRegistryTransactionalEndpoint target = Provider("target", fixture.Clock);
            await CreateVersionAsync(source, "base").ConfigureAwait(false);
            await CreateVersionAsync(target, "base").ConfigureAwait(false);
            var prepared = new XRegistryPreparedBoundaryFixture(target);
            var engine = new XRegistrySynchronizer(
                source, prepared, fixture.Store, fixture.Options, fixture.Telemetry, fixture.Clock);
            XRegistrySyncReport baseline = await engine.RunOnceAsync().ConfigureAwait(false);
            Assert.That(baseline.Status, Is.EqualTo(XRegistrySyncStatus.Succeeded), Details(baseline));
            await CreateVersionAsync(source, "next", ancestor: "1").ConfigureAwait(false);
            ByteString before = await fixture.State.ExportSnapshotAsync().ConfigureAwait(false);

            XRegistrySyncReport preview = await engine.RunOnceAsync(dryRun: true).ConfigureAwait(false);
            ByteString after = await fixture.State.ExportSnapshotAsync().ConfigureAwait(false);
            XRegistryResponse absent = await target.ExecuteAsync(Request(XRegistryAction.Read, k_second))
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(preview.DryRun, Is.True);
                Assert.That(preview.Records.ToList().Any(record =>
                    record.Path == k_second && record.Kind == XRegistrySyncRecordKind.Planned), Is.True);
                Assert.That(preview.Applied, Is.Zero);
                Assert.That(preview.Pending, Is.Zero);
                Assert.That(prepared.Preparations, Is.Zero);
                Assert.That(prepared.Commits, Is.Zero);
                Assert.That(absent.StatusCode, Is.EqualTo(404));
                Assert.That(after.Span.SequenceEqual(before.Span), Is.True);
            });
        }

        /// <summary>
        /// Independently edited resource metadata blocks assigned creation before candidate acquisition.
        /// </summary>
        [Test]
        public async Task IndependentlyChangedMetaBlocksAssignedCreationAsync()
        {
            await using var fixture = new XRegistrySyncFixture();
            using XRegistryTransactionalEndpoint source = Provider("source", fixture.Clock);
            using XRegistryTransactionalEndpoint target = Provider("target", fixture.Clock);
            await CreateVersionAsync(source, "base").ConfigureAwait(false);
            await CreateVersionAsync(target, "base").ConfigureAwait(false);
            var prepared = new XRegistryPreparedBoundaryFixture(target);
            var engine = new XRegistrySynchronizer(
                source, prepared, fixture.Store, fixture.Options, fixture.Telemetry, fixture.Clock);
            XRegistrySyncReport baseline = await engine.RunOnceAsync().ConfigureAwait(false);
            Assert.That(baseline.Status, Is.EqualTo(XRegistrySyncStatus.Succeeded), Details(baseline));
            await CreateVersionAsync(source, "next", ancestor: "1").ConfigureAwait(false);
            XRegistryResponse edited = await target.ExecuteAsync(Request(XRegistryAction.Merge, Resource + "/meta",
                /*lang=json,strict*/ """{"name":"independent-meta"}""")).ConfigureAwait(false);
            Assert.That(edited.StatusCode, Is.EqualTo(200), edited.Error?.Detail);

            XRegistrySyncReport report = await engine.RunOnceAsync().ConfigureAwait(false);
            XRegistryResponse meta = await target.ExecuteAsync(Request(XRegistryAction.Read, Resource + "/meta"))
                .ConfigureAwait(false);
            XRegistryResponse absent = await target.ExecuteAsync(Request(XRegistryAction.Read, k_second))
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(report.Applied, Is.Zero, Details(report));
                Assert.That(report.Conflicts, Is.GreaterThan(0));
                Assert.That(prepared.Preparations, Is.Zero);
                Assert.That(prepared.Commits, Is.Zero);
                Assert.That(meta.Metadata.GetProperty("name").GetString(), Is.EqualTo("independent-meta"));
                Assert.That(absent.StatusCode, Is.EqualTo(404));
            });
        }

        /// <summary>
        /// An available ancestor and a sticky requested default survive assigned creation and the next full pass.
        /// </summary>
        [Test]
        public async Task AvailableAncestorAndStickyDefaultArePreservedThroughAssignedCreationAsync()
        {
            await using var fixture = new XRegistrySyncFixture();
            using XRegistryTransactionalEndpoint source = Provider("source", fixture.Clock);
            using XRegistryTransactionalEndpoint target = Provider("target", fixture.Clock);
            await CreateVersionAsync(source, "base").ConfigureAwait(false);
            await CreateVersionAsync(target, "base").ConfigureAwait(false);
            var prepared = new XRegistryPreparedBoundaryFixture(target);
            var engine = new XRegistrySynchronizer(
                source, prepared, fixture.Store, fixture.Options, fixture.Telemetry, fixture.Clock);
            XRegistrySyncReport baseline = await engine.RunOnceAsync().ConfigureAwait(false);
            Assert.That(baseline.Status, Is.EqualTo(XRegistrySyncStatus.Succeeded), Details(baseline));
            await CreateVersionAsync(source, "next", ancestor: "1", sticky: true).ConfigureAwait(false);

            XRegistrySyncReport report = await engine.RunOnceAsync().ConfigureAwait(false);
            XRegistrySyncReport repeated = await engine.RunOnceAsync().ConfigureAwait(false);
            XRegistryResponse version = await target.ExecuteAsync(Request(XRegistryAction.Read, k_second))
                .ConfigureAwait(false);
            XRegistryResponse meta = await target.ExecuteAsync(Request(XRegistryAction.Read, Resource + "/meta"))
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(report.Status, Is.EqualTo(XRegistrySyncStatus.Succeeded), Details(report));
                Assert.That(repeated.Status, Is.EqualTo(XRegistrySyncStatus.Succeeded), Details(repeated));
                Assert.That(repeated.Applied, Is.Zero);
                Assert.That(prepared.Commits, Is.EqualTo(1));
                Assert.That(prepared.Disposals, Is.EqualTo(1));
                Assert.That(prepared.LastPreparedRequest!.Parameters.ToList(),
                    Does.Contain(new XRegistryParameter("setdefaultversionid", "request")));
                Assert.That(version.Metadata.GetProperty("name").GetString(), Is.EqualTo("next"));
                Assert.That(version.Metadata.GetProperty("versionid").GetString(), Is.EqualTo("2"));
                Assert.That(version.Metadata.GetProperty("ancestorid").GetString(), Is.EqualTo("1"));
                Assert.That(meta.Metadata.GetProperty("defaultversionid").GetString(), Is.EqualTo("2"));
                Assert.That(meta.Metadata.GetProperty("defaultversionsticky").GetBoolean(), Is.True);
            });
        }

        /// <summary>
        /// A child cannot be assigned until its distinct source ancestor has an observable destination counterpart.
        /// </summary>
        [Test]
        public async Task MissingDestinationAncestorHoldsTheChildWhileTheAncestorIsCopiedAsync()
        {
            await using var fixture = new XRegistrySyncFixture();
            using XRegistryTransactionalEndpoint source = Provider("source", fixture.Clock);
            using XRegistryTransactionalEndpoint target = Provider("target", fixture.Clock);
            await CreateVersionAsync(source, "base").ConfigureAwait(false);
            await CreateVersionAsync(target, "base").ConfigureAwait(false);
            var prepared = new XRegistryPreparedBoundaryFixture(target);
            var engine = new XRegistrySynchronizer(
                source, prepared, fixture.Store, fixture.Options, fixture.Telemetry, fixture.Clock);
            XRegistrySyncReport baseline = await engine.RunOnceAsync().ConfigureAwait(false);
            Assert.That(baseline.Status, Is.EqualTo(XRegistrySyncStatus.Succeeded), Details(baseline));
            await CreateVersionAsync(source, "child", ancestor: "1").ConfigureAwait(false);
            await CreateVersionAsync(source, "ancestor", ancestor: "request", sticky: true).ConfigureAwait(false);
            XRegistryResponse reparented = await source.ExecuteAsync(Request(XRegistryAction.Merge, k_second,
                /*lang=json,strict*/ """{"ancestorid":"3"}""")).ConfigureAwait(false);
            Assert.That(reparented.StatusCode, Is.EqualTo(200), reparented.Error?.Detail);

            XRegistrySyncReport report = await engine.RunOnceAsync().ConfigureAwait(false);
            XRegistrySyncState state = new XRegistrySyncStateCodec().Decode(
                await fixture.State.ExportSnapshotAsync().ConfigureAwait(false));
            XRegistryResponse copied = await target.ExecuteAsync(Request(XRegistryAction.Read, k_second))
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(report.Records.ToList().Any(record =>
                    record.Path == k_second && record.Kind == XRegistrySyncRecordKind.Unsupported), Is.True);
                Assert.That(prepared.Commits, Is.EqualTo(1));
                Assert.That(copied.Metadata.GetProperty("name").GetString(), Is.EqualTo("ancestor"));
                Assert.That(copied.Metadata.GetProperty("ancestorid").GetString(), Is.EqualTo("2"));
                Assert.That(state.VersionCorrespondences.ContainsKey(k_second), Is.False);
                Assert.That(state.VersionCorrespondences[Resource + "/versions/3"].HttpPath, Is.EqualTo(k_second));
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
                case "missing-id":
                    metadata.Remove("versionid");
                    break;
                case "numeric-id":
                    metadata["versionid"] = 2;
                    break;
                case "existing-id":
                    metadata["versionid"] = "1";
                    break;
                case "ancestor":
                    metadata["ancestorid"] = "unrelated";
                    break;
                case "document":
                    metadata["schemabase64"] = "Y29ycnVwdA==";
                    break;
                case "missing-document":
                    metadata.Remove("schemabase64");
                    break;
                case "mapped-id":
                    Assert.That(metadata["versionid"]!.GetValue<string>(), Is.EqualTo("2"));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(corruption));
            }
            return response with { Metadata = Json(metadata.ToJsonString()) };
        }

        private static async Task CreateVersionAsync(
            XRegistryTransactionalEndpoint endpoint, string name, string? ancestor = null, bool sticky = false)
        {
            XRegistryResponse previous = await endpoint.ExecuteAsync(Request(XRegistryAction.Read, Resource))
                .ConfigureAwait(false);
            var metadata = new JsonObject { ["name"] = name, ["schema"] = name };
            if (ancestor is not null)
            {
                metadata["ancestorid"] = ancestor;
            }
            XRegistryResponse response = await endpoint.ExecuteAsync(Request(
                XRegistryAction.Create, Resource, metadata.ToJsonString()) with
            {
                Parameters = sticky ? [new XRegistryParameter("setdefaultversionid", "request")] : []
            }).ConfigureAwait(false);
            Assert.That(
                response.StatusCode, Is.EqualTo(previous.StatusCode == 404 ? 201 : 200), response.Error?.Detail);
        }

        private static XRegistryRequest Request(XRegistryAction action, string path, string? json = null)
        {
            return new XRegistryRequest(action, path)
            {
                Context = Writer,
                View = XRegistryView.Metadata,
                Metadata = json is null ? default : Json(json)
            };
        }

        private static XRegistryTransactionalEndpoint Provider(string id, TimeProvider clock)
        {
            return new XRegistryTransactionalEndpoint(new XRegistryTransactionalOptions
            {
                RegistryId = id,
                Model = Json(k_model)
            }, new InMemoryXRegistryTransactionStore(), clock);
        }

        private const string k_first = Resource + "/versions/1";
        private const string k_second = Resource + "/versions/2";

        private const string k_model = /*lang=json,strict*/ """
            {"groups":{"schemagroups":{"singular":"schemagroup","resources":{
              "schemas":{"singular":"schema","setversionid":false}
            }}}}
            """;
    }
}
