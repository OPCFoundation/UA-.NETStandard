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
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
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
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.XRegistry.Bridge.Sync;
using Opc.Ua.XRegistry.Protocol;
using static Opc.Ua.XRegistry.Bridge.Tests.Sync.XRegistrySyncFixture;

namespace Opc.Ua.XRegistry.Bridge.Tests.Sync
{
    [TestFixture]
    public sealed class XRegistryReconciliationTests
    {
        [Test]
        public async Task EqualStatesEstablishBaselineWithoutWritesAsync()
        {
            await using var fixture = new XRegistrySyncFixture(journal: true);
            await fixture.SeedBothAsync(Group,
                /*lang=json,strict*/ """{"name":"equal","labels":{"one":"value"}}""").ConfigureAwait(false);
            fixture.Clock.Advance(TimeSpan.FromDays(2));
            await fixture.Native.ChangeAsync(Group, "{}", XRegistryAction.Merge).ConfigureAwait(false);
            await fixture.Native.ChangeAsync(Group, "{}", XRegistryAction.Merge).ConfigureAwait(false);
            fixture.Http.TransformResponse = (request, response) => request.IsMutation ? response : response with
            {
                CorrelationId = "different-on-every-read-" + fixture.Http.Reads
            };
            XRegistrySyncReport first = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            XRegistrySyncReport second = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            XRegistrySyncStateStatus state = await fixture.State.ReadStatusAsync().ConfigureAwait(false);
            XRegistryResponse native = await fixture.Native.ReadAsync(Group).ConfigureAwait(false);
            XRegistryResponse http = await fixture.Http.ReadAsync(Group).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(first.Status, Is.EqualTo(XRegistrySyncStatus.Succeeded), Details(first));
                Assert.That(second.Applied, Is.Zero);
                Assert.That(fixture.Native.Mutations, Is.Empty);
                Assert.That(fixture.Http.Mutations, Is.Empty);
                Assert.That(native.Metadata.GetProperty("epoch").GetUInt64(), Is.EqualTo(2));
                Assert.That(http.Metadata.GetProperty("epoch").GetUInt64(), Is.Zero);
                Assert.That(state.Baselines, Is.EqualTo(2));
                Assert.That(state.Intents, Is.Zero);
                Assert.That(state.Conflicts, Is.Zero);
            });
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task OneSidedEditsConvergeWithoutEchoAsync(bool nativeWins)
        {
            await using var fixture = new XRegistrySyncFixture(journal: true);
            await fixture.SeedBothAsync(Resource,
                /*lang=json,strict*/
                """
                {"versionid":"v1","name":"old","schemabase64":"AP8=","contenttype":"application/octet-stream"}
                """)
                .ConfigureAwait(false);
            await fixture.BaselineAsync().ConfigureAwait(false);
            SyncEndpoint source = nativeWins ? fixture.Native : fixture.Http;
            SyncEndpoint target = nativeWins ? fixture.Http : fixture.Native;
            await source.ChangeAsync(VersionPath,
                /*lang=json,strict*/
                """
                {"name":"new","schemabase64":"AAECA/8=","contenttype":"application/octet-stream"}
                """)
                .ConfigureAwait(false);
            XRegistrySyncReport changed = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            XRegistrySyncReport quiet = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            XRegistryResponse actual = await target.ReadAsync(VersionPath, XRegistryView.Default).ConfigureAwait(false);
            XRegistrySyncStateStatus state = await fixture.State.ReadStatusAsync().ConfigureAwait(false);
            int journal = await target.JournalCountAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(changed.Status, Is.EqualTo(XRegistrySyncStatus.Succeeded), Details(changed));
                Assert.That(changed.Applied, Is.EqualTo(1));
                Assert.That(quiet.Applied, Is.Zero, Details(quiet));
                Assert.That(source.Mutations, Is.Empty);
                Assert.That(target.Mutations, Has.Count.EqualTo(1));
                Assert.That(target.Committed, Is.EqualTo(1));
                Assert.That(actual.Metadata.GetProperty("name").GetString(), Is.EqualTo("new"));
                Assert.That(actual.Document.ToArray(), Is.EqualTo(new byte[] { 0, 1, 2, 3, 255 }));
                Assert.That(state.Intents, Is.EqualTo(1));
                Assert.That(state.Outcomes, Is.EqualTo(1));
                Assert.That(state.Verified, Is.EqualTo(1));
                Assert.That(state.Pending, Is.Zero);
                Assert.That(journal, Is.EqualTo(1));
            });
        }

        [TestCase(XRegistrySyncConflictPolicy.Manual, 0, 1)]
        [TestCase(XRegistrySyncConflictPolicy.PreferOpcUa, 1, 0)]
        [TestCase(XRegistrySyncConflictPolicy.PreferHttp, 1, 0)]
        public async Task InitialDivergenceHonorsPolicyAsync(
            XRegistrySyncConflictPolicy policy, int writes, int conflicts)
        {
            await using var fixture = new XRegistrySyncFixture();
            fixture.Options = fixture.Options with { ConflictPolicy = policy };
            await fixture.Native.ChangeAsync(Group, /*lang=json,strict*/ """{"name":"native"}""").ConfigureAwait(false);
            await fixture.Http.ChangeAsync(Group, /*lang=json,strict*/ """{"name":"http"}""").ConfigureAwait(false);
            XRegistrySyncReport report = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            XRegistryResponse native = await fixture.Native.ReadAsync(Group).ConfigureAwait(false);
            XRegistryResponse http = await fixture.Http.ReadAsync(Group).ConfigureAwait(false);
            ByteString bytes = await ReadStateAsync(fixture.Store).ConfigureAwait(false);
            using var state = JsonDocument.Parse(bytes.Memory);
            JsonElement audit = state.RootElement.GetProperty("payload").GetProperty("conflicts")[0];
            Assert.Multiple(() =>
            {
                Assert.That(report.Applied, Is.EqualTo(writes), Details(report));
                Assert.That(report.Conflicts, Is.EqualTo(conflicts));
                Assert.That(fixture.Native.Mutations.Count + fixture.Http.Mutations.Count, Is.EqualTo(writes));
                Assert.That(native.Metadata.GetProperty("name").GetString(),
                    Is.EqualTo(policy == XRegistrySyncConflictPolicy.PreferHttp ? "http" : "native"));
                Assert.That(http.Metadata.GetProperty("name").GetString(),
                    Is.EqualTo(policy == XRegistrySyncConflictPolicy.PreferOpcUa ? "native" : "http"));
                Assert.That(audit.GetProperty("opcUa").GetProperty("metadata").GetProperty("name").GetString(),
                    Is.EqualTo("native"), "The losing observation must remain in durable audit state.");
                Assert.That(audit.GetProperty("http").GetProperty("metadata").GetProperty("name").GetString(),
                    Is.EqualTo("http"));
                Assert.That(report.ExitCode, policy == XRegistrySyncConflictPolicy.Manual ? Is.Not.Zero : Is.Zero);
            });
        }

        [TestCase(XRegistrySyncConflictPolicy.Manual)]
        [TestCase(XRegistrySyncConflictPolicy.PreferOpcUa)]
        [TestCase(XRegistrySyncConflictPolicy.PreferHttp)]
        public async Task BothChangedStatesUseBaselineAndPolicyAsync(XRegistrySyncConflictPolicy policy)
        {
            await using var fixture = new XRegistrySyncFixture();
            await fixture.SeedBothAsync(Group, /*lang=json,strict*/ """{"name":"base"}""").ConfigureAwait(false);
            await fixture.BaselineAsync().ConfigureAwait(false);
            await fixture.Native.ChangeAsync(Group,
                /*lang=json,strict*/ """{"name":"native-edit"}""").ConfigureAwait(false);
            await fixture.Http.ChangeAsync(Group,
                /*lang=json,strict*/ """{"name":"http-edit"}""").ConfigureAwait(false);
            fixture.Options = fixture.Options with { ConflictPolicy = policy };
            XRegistrySyncReport report = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            XRegistryResponse native = await fixture.Native.ReadAsync(Group).ConfigureAwait(false);
            XRegistryResponse http = await fixture.Http.ReadAsync(Group).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(report.Conflicts, Is.EqualTo(policy == XRegistrySyncConflictPolicy.Manual ? 1 : 0),
                    Details(report));
                Assert.That(report.Applied, Is.EqualTo(policy == XRegistrySyncConflictPolicy.Manual ? 0 : 1));
                Assert.That(native.Metadata.GetProperty("name").GetString(),
                    Is.EqualTo(policy == XRegistrySyncConflictPolicy.PreferHttp ? "http-edit" : "native-edit"));
                Assert.That(http.Metadata.GetProperty("name").GetString(),
                    Is.EqualTo(policy == XRegistrySyncConflictPolicy.PreferOpcUa ? "native-edit" : "http-edit"));
            });
        }

        [Test]
        public async Task IndependentEntriesProceedPastConflictAsync()
        {
            await using var fixture = new XRegistrySyncFixture();
            await fixture.Native.ChangeAsync(Group, /*lang=json,strict*/ """{"name":"native"}""").ConfigureAwait(false);
            await fixture.Http.ChangeAsync(Group, /*lang=json,strict*/ """{"name":"http"}""").ConfigureAwait(false);
            await fixture.Native.ChangeAsync("/schemagroups/independent",
                /*lang=json,strict*/ """{"name":"copy"}""").ConfigureAwait(false);
            XRegistrySyncReport report = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            XRegistryResponse independent = await fixture.Http.ReadAsync("/schemagroups/independent")
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(report.Conflicts, Is.EqualTo(1), Details(report));
                Assert.That(report.Applied, Is.EqualTo(1));
                Assert.That(independent.Metadata.GetProperty("name").GetString(), Is.EqualTo("copy"));
                Assert.That(fixture.Http.Mutations, Has.Count.EqualTo(1));
                Assert.That(fixture.Native.Mutations, Is.Empty);
            });
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task ExplicitResourceAndVersionsBootstrapInBothDirectionsAsync(bool nativeSource)
        {
            await using var fixture = new XRegistrySyncFixture();
            SyncEndpoint source = nativeSource ? fixture.Native : fixture.Http;
            SyncEndpoint target = nativeSource ? fixture.Http : fixture.Native;
            await source.ChangeAsync(Resource, /*lang=json,strict*/ """
                {"meta":{"defaultversionid":"v2","defaultversionsticky":true},
                 "versions":{
                   "v1":{"name":"one","ancestorid":"v1","schemabase64":"AP8=","contenttype":"application/octet-stream"},
                   "v2":{"name":"two","ancestorid":"v1","schemabase64":"AQID","contenttype":"application/octet-stream"}
                 }}
                """).ConfigureAwait(false);
            XRegistrySyncReport report = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            XRegistrySyncReport quiet = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            XRegistryResponse first = await target.ReadAsync(VersionPath, XRegistryView.Default).ConfigureAwait(false);
            XRegistryResponse second = await target.ReadAsync(Resource + "/versions/v2", XRegistryView.Default)
                .ConfigureAwait(false);
            XRegistryResponse meta = await target.ReadAsync(Resource + "/meta").ConfigureAwait(false);
            XRegistrySyncStateStatus state = await fixture.State.ReadStatusAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(report.Status, Is.EqualTo(XRegistrySyncStatus.Succeeded), Details(report));
                Assert.That(report.Applied, Is.EqualTo(2), "One parent and one atomic resource creation are expected.");
                Assert.That(quiet.Applied, Is.Zero, Details(quiet));
                Assert.That(first.Document.ToArray(), Is.EqualTo(new byte[] { 0, 255 }));
                Assert.That(second.Document.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));
                Assert.That(meta.Metadata.GetProperty("defaultversionid").GetString(), Is.EqualTo("v2"));
                Assert.That(meta.Metadata.GetProperty("defaultversionsticky").GetBoolean(), Is.True);
                Assert.That(state.Baselines, Is.EqualTo(5));
                Assert.That(state.Intents, Is.EqualTo(2));
                Assert.That(state.Verified, Is.EqualTo(2));
            });
        }

        [Test]
        public async Task MultipleCollectionsAndVersionsRemainDistinctAsync()
        {
            await using var fixture = new XRegistrySyncFixture();
            await fixture.Native.ChangeAsync("/schemagroups/same/schemas/same",
                /*lang=json,strict*/
                """
                {"versionid":"same","schemabase64":"AQ==","contenttype":"application/octet-stream"}
                """)
                .ConfigureAwait(false);
            await fixture.Native.ChangeAsync("/schemagroups/same/events/same",
                                     /*lang=json,strict*/
                                     """{"versionid":"same","name":"metadata-only"}""").ConfigureAwait(false);
            await fixture.Native.ChangeAsync("/othergroups/same/schemas/same",
                /*lang=json,strict*/
                """
                {"versionid":"same","schemabase64":"Ag==","contenttype":"application/octet-stream"}
                """)
                .ConfigureAwait(false);
            XRegistrySyncReport report = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            XRegistryResponse schema = await fixture.Http.ReadAsync("/schemagroups/same/schemas/same/versions/same",
                XRegistryView.Default).ConfigureAwait(false);
            XRegistryResponse other = await fixture.Http.ReadAsync("/othergroups/same/schemas/same/versions/same",
                XRegistryView.Default).ConfigureAwait(false);
            XRegistryResponse metadata = await fixture.Http.ReadAsync("/schemagroups/same/events/same/versions/same",
                XRegistryView.Default).ConfigureAwait(false);
            XRegistrySyncStateStatus state = await fixture.State.ReadStatusAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(report.Status, Is.EqualTo(XRegistrySyncStatus.Succeeded), Details(report));
                Assert.That(report.Applied, Is.EqualTo(5));
                Assert.That(schema.Document.ToArray(), Is.EqualTo(new byte[] { 1 }));
                Assert.That(other.Document.ToArray(), Is.EqualTo(new byte[] { 2 }));
                Assert.That(metadata.Document.IsNull, Is.True);
                Assert.That(metadata.Metadata.GetProperty("name").GetString(), Is.EqualTo("metadata-only"));
                Assert.That(state.Baselines, Is.EqualTo(9));
            });
        }

        [TestCase("")]
        [TestCase("AP8AAf8=")]
        public async Task BinaryAndEmptyDocumentsRoundTripAsync(string content)
        {
            await using var fixture = new XRegistrySyncFixture();
            var body = new JsonObject { ["versionid"] = "v1", ["schemabase64"] = content, ["contenttype"] = null };
            await fixture.Native.ChangeAsync(Resource, body.ToJsonString()).ConfigureAwait(false);
            XRegistrySyncReport report = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            XRegistryResponse actual = await fixture.Http.ReadAsync(VersionPath, XRegistryView.Default)
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(report.Status, Is.EqualTo(XRegistrySyncStatus.Succeeded), Details(report));
                Assert.That(actual.Document.IsNull, Is.False);
                Assert.That(actual.Document.ToArray(), Is.EqualTo(Convert.FromBase64String(content)));
                Assert.That(actual.Metadata.TryGetProperty("contenttype", out _), Is.False);
            });
        }

        [Test]
        public async Task CanonicalMetadataPreservesNestedValuesAsync()
        {
            await using var fixture = new XRegistrySyncFixture();
            await fixture.Native.ChangeAsync(Group,
                /*lang=json,strict*/
                """
                {"extension":{"self":"user","epoch":1,"modifiedat":"user-time","values":[1,2]}}
                """)
                .ConfigureAwait(false);
            await fixture.Http.ChangeAsync(Group,
                /*lang=json,strict*/
                """
                {"extension":{"values":[1.0,2e0],"modifiedat":"user-time","epoch":1.0,"self":"user"}}
                """)
                .ConfigureAwait(false);
            await fixture.BaselineAsync().ConfigureAwait(false);
            await fixture.Native.ChangeAsync(Group,
                /*lang=json,strict*/
                """
                {"extension":{"self":"user","epoch":2,"modifiedat":"new-user-time","values":[1,2]}}
                """)
                .ConfigureAwait(false);
            XRegistrySyncReport report = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            XRegistryResponse actual = await fixture.Http.ReadAsync(Group).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(report.Applied, Is.EqualTo(1), Details(report));
                Assert.That(actual.Metadata.GetProperty("extension").GetProperty("epoch").GetInt32(), Is.EqualTo(2));
                Assert.That(actual.Metadata.GetProperty("extension").GetProperty("modifiedat").GetString(),
                    Is.EqualTo("new-user-time"), "Only top-level generated fields may be ignored.");
            });
        }

        [Test]
        public async Task DefaultSelectionUsesMetaEpochAsync()
        {
            await using var fixture = new XRegistrySyncFixture();
            await fixture.SeedBothAsync(Resource, /*lang=json,strict*/ """
                {"meta":{"defaultversionid":"v1","defaultversionsticky":true},
                 "versions":{"v1":{"ancestorid":"v1","schemabase64":""},
                             "v2":{"ancestorid":"v1","schemabase64":""}}}
                """).ConfigureAwait(false);
            await fixture.BaselineAsync().ConfigureAwait(false);
            await fixture.Http.ChangeAsync(VersionPath, "{}", XRegistryAction.Merge).ConfigureAwait(false);
            await fixture.Http.ChangeAsync(VersionPath, "{}", XRegistryAction.Merge).ConfigureAwait(false);
            await fixture.Native.ChangeAsync(Resource + "/meta",
                                     /*lang=json,strict*/
                                     """{"defaultversionid":"v2","defaultversionsticky":true}""").ConfigureAwait(false);
            XRegistrySyncReport report = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            XRegistryResponse actual = await fixture.Http.ReadAsync(Resource + "/meta").ConfigureAwait(false);
            XRegistryResponse version = await fixture.Http.ReadAsync(VersionPath).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(report.Applied, Is.EqualTo(1), Details(report));
                Assert.That(fixture.Http.Mutations.Single().Path, Is.EqualTo(Resource + "/meta"));
                Assert.That(fixture.Http.Mutations.Single().Metadata.GetProperty("epoch").GetUInt64(), Is.Zero);
                Assert.That(actual.Metadata.GetProperty("defaultversionid").GetString(), Is.EqualTo("v2"));
                Assert.That(actual.Metadata.GetProperty("defaultversionsticky").GetBoolean(), Is.True);
                Assert.That(version.Metadata.GetProperty("epoch").GetUInt64(), Is.EqualTo(2));
            });
        }

        [Test]
        public async Task ConcurrentDestinationEditIsNeverRetriedWithFreshEpochAsync()
        {
            await using var fixture = new XRegistrySyncFixture();
            await fixture.SeedBothAsync(Group, /*lang=json,strict*/ """{"name":"baseline"}""").ConfigureAwait(false);
            await fixture.BaselineAsync().ConfigureAwait(false);
            await fixture.Native.ChangeAsync(Group,
                /*lang=json,strict*/ """{"name":"outgoing"}""").ConfigureAwait(false);
            fixture.Options = fixture.Options with { ConflictPolicy = XRegistrySyncConflictPolicy.PreferOpcUa };
            fixture.Http.BeforeExecuteAsync = async (request, _) =>
            {
                if (request.IsMutation)
                {
                    fixture.Http.BeforeExecuteAsync = null;
                    await fixture.Http.ChangeAsync(Group,
                        /*lang=json,strict*/ """{"name":"concurrent"}""").ConfigureAwait(false);
                }
            };
            XRegistrySyncReport first = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            XRegistrySyncReport second = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            XRegistryResponse actual = await fixture.Http.ReadAsync(Group).ConfigureAwait(false);
            XRegistrySyncStateStatus state = await fixture.State.ReadStatusAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(first.Applied, Is.Zero, Details(first));
                Assert.That(second.Applied, Is.Zero, Details(second));
                Assert.That(fixture.Http.Mutations, Has.Count.EqualTo(1));
                Assert.That(fixture.Http.Committed, Is.Zero);
                Assert.That(actual.Metadata.GetProperty("name").GetString(), Is.EqualTo("concurrent"));
                Assert.That(state.Intents, Is.EqualTo(1));
                Assert.That(state.Outcomes, Is.EqualTo(1));
                Assert.That(state.Pending, Is.Zero);
                Assert.That(state.Conflicts, Is.EqualTo(1));
            });
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task CreatingVersionCannotOverrideConcurrentDefaultUpdateAsync(bool nativeSource)
        {
            await using var fixture = new XRegistrySyncFixture();
            await fixture.SeedBothAsync(Resource,
                                     /*lang=json,strict*/
                                     """{"versionid":"v1","schemabase64":"","meta":{"defaultversionsticky":true}}""")
                                         .ConfigureAwait(false);
            await fixture.BaselineAsync().ConfigureAwait(false);
            SyncEndpoint source = nativeSource ? fixture.Native : fixture.Http;
            SyncEndpoint target = nativeSource ? fixture.Http : fixture.Native;
            await source.ChangeAsync(Resource,
                                     /*lang=json,strict*/
                                     """{"versionid":"v2","schemabase64":"AQ=="}""",
                                         XRegistryAction.Create).ConfigureAwait(false);
            target.BeforeExecuteAsync = async (request, _) =>
            {
                if (request.Action == XRegistryAction.Create && request.Path == Resource)
                {
                    target.BeforeExecuteAsync = null;
                    await target.ChangeAsync(Resource + "/meta",
                        /*lang=json,strict*/
                        """
                        {"defaultversionid":"v1","defaultversionsticky":true,"name":"operator-default"}
                        """)
                        .ConfigureAwait(false);
                }
            };
            XRegistrySyncReport report = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            XRegistryResponse missing = await target.ReadAsync(Resource + "/versions/v2").ConfigureAwait(false);
            XRegistryResponse meta = await target.ReadAsync(Resource + "/meta").ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(report.Applied, Is.Zero, Details(report));
                Assert.That(missing.StatusCode, Is.EqualTo(404), "The compound failure must not leave a new Version.");
                Assert.That(meta.Metadata.GetProperty("defaultversionid").GetString(), Is.EqualTo("v1"));
                Assert.That(meta.Metadata.GetProperty("name").GetString(), Is.EqualTo("operator-default"));
                Assert.That(target.Mutations.Count(request => request.Action == XRegistryAction.Create), Is.EqualTo(1));
            });
        }

        [Test]
        public async Task UnqualifiedEndpointReceivesNoMutationOrInventedReplayFlagAsync()
        {
            await using var fixture = new XRegistrySyncFixture();
            await fixture.Native.ChangeAsync(Group, /*lang=json,strict*/ """{"name":"source"}""").ConfigureAwait(false);
            fixture.Http.Qualified = false;
            fixture.Http.RejectOperationId = true;
            XRegistrySyncReport blocked = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            fixture.Http.Qualified = true;
            XRegistrySyncReport allowed = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(blocked.Applied, Is.Zero);
                Assert.That(blocked.Records.Span.ToArray()
                    .Any(record => record.Kind == XRegistrySyncRecordKind.Unsupported),
                    Is.True);
                Assert.That(allowed.Applied, Is.EqualTo(1), Details(allowed));
                Assert.That(fixture.Http.Mutations.Single().OperationId, Is.Null);
                Assert.That(fixture.Http.JournalQueries, Is.Zero);
            });
        }

        [TestCase("createdat", 0, true)]
        [TestCase("manual", 2, true)]
        [TestCase("manual", 0, false)]
        public async Task UnsupportedModelWritesAreReportedWithoutSideEffectsAsync(
            string versionMode, int maxVersions, bool setVersionId)
        {
            JsonObject model = JsonNode.Parse(DefaultModel)!.AsObject();
            JsonObject resource = model["groups"]!["schemagroups"]!["resources"]!["schemas"]!.AsObject();
            resource["versionmode"] = versionMode;
            resource["maxversions"] = maxVersions;
            resource["setversionid"] = setVersionId;
            await using var fixture = new XRegistrySyncFixture(model.ToJsonString());
            string json =
                setVersionId ? /*lang=json,strict*/ """{"versionid":"v1","schemabase64":""}""" : /*lang=json,strict*/
                """{"schemabase64":""}""";
            await fixture.Native.ChangeAsync(Resource, json).ConfigureAwait(false);
            XRegistrySyncReport report = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            XRegistryResponse absent = await fixture.Http.ReadAsync(Resource).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(absent.StatusCode, Is.EqualTo(404), Details(report));
                Assert.That(report.Conflicts, Is.GreaterThan(0));
                Assert.That(fixture.Http.Mutations.All(request => request.Path == "/"), Is.True,
                    "Only the independent group may be bootstrapped, not an unsafe resource/version.");
            });
        }

        internal static async Task<ByteString> ReadStateAsync(IXRegistrySyncStateStore store)
        {
            IXRegistrySyncStateSession session = await store.OpenAsync(readOnly: true).ConfigureAwait(false);
            await using ConfiguredAsyncDisposable lifetime = session.ConfigureAwait(false);
            return await session.ReadAsync().ConfigureAwait(false);
        }
    }
}
