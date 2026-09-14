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

using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.XRegistry.Protocol;
using Opc.Ua.XRegistry.Server.Protocol;

namespace Opc.Ua.XRegistry.Tests.ProtocolProvider
{
    [TestFixture]
    public sealed class XRegistryVersionIncarnationTests
    {
        [Test]
        public async Task VersionIncarnationSurvivesEditsAndStoreReopenAsync()
        {
            var store = new InMemoryXRegistryTransactionStore();
            using var endpoint = new XRegistryTransactionalEndpoint(
                XRegistryProviderCoverage.Options(), store, new XRegistryProviderCoverageTimeProvider());
            XRegistryResponse created = await SeedAsync(endpoint).ConfigureAwait(false);
            XRegistryResponse edited = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Merge, k_version,
                    /*lang=json,strict*/ """{"epoch":0,"name":"edited"}""")
                    with
                { ExpectedVersionIncarnation = created.VersionIncarnation }).ConfigureAwait(false);
            using var reopened = new XRegistryTransactionalEndpoint(XRegistryProviderCoverage.Options(), store);
            XRegistryEndpointDescription profile = await reopened.InspectAsync(XRegistryProviderCoverage.Writer)
                .ConfigureAwait(false);
            XRegistryResponse read = await reopened.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Read, k_version) with
                {
                    ExpectedVersionIncarnation = created.VersionIncarnation
                }).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(created.StatusCode, Is.EqualTo(201));
                Assert.That(created.VersionIncarnation, Is.Not.Null.And.Not.Empty);
                Assert.That(profile.SupportsVersionIncarnationGuards, Is.True);
                Assert.That(edited.StatusCode, Is.EqualTo(200));
                Assert.That(edited.VersionIncarnation, Is.EqualTo(created.VersionIncarnation));
                Assert.That(read.StatusCode, Is.EqualTo(200));
                Assert.That(read.VersionIncarnation, Is.EqualTo(created.VersionIncarnation));
                Assert.That(read.Metadata.GetProperty("name").GetString(), Is.EqualTo("edited"));
                Assert.That(read.Metadata.GetProperty("epoch").GetInt32(), Is.EqualTo(1));
                Assert.That(read.Metadata.TryGetProperty("incarnation", out _), Is.False);
            });
        }

        [TestCase(XRegistryAction.Read, false)]
        [TestCase(XRegistryAction.Merge, false)]
        [TestCase(XRegistryAction.Delete, false)]
        [TestCase(XRegistryAction.Read, true)]
        [TestCase(XRegistryAction.Merge, true)]
        [TestCase(XRegistryAction.Delete, true)]
        public async Task OldIncarnationCannotReadOverwriteDeleteOrRecreateAVersionAsync(
            XRegistryAction action, bool recreate)
        {
            using XRegistryTransactionalEndpoint endpoint = XRegistryProviderCoverage.Create();
            XRegistryResponse original = await SeedAsync(endpoint).ConfigureAwait(false);
            XRegistryResponse deleted = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Delete, k_resource)).ConfigureAwait(false);
            Assert.That(deleted.StatusCode, Is.EqualTo(204));
            if (recreate)
            {
                XRegistryResponse replacement = await SeedAsync(endpoint, "replacement").ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(replacement.VersionIncarnation, Is.Not.EqualTo(original.VersionIncarnation));
                    Assert.That(replacement.Metadata.GetProperty("epoch").GetInt32(), Is.Zero);
                    Assert.That(replacement.Metadata.GetProperty("createdat").GetString(),
                        Is.EqualTo(original.Metadata.GetProperty("createdat").GetString()));
                });
            }
            XRegistryResponse rejected = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(action, k_version,
                    /*lang=json,strict*/ """{"name":"stale"}""") with
                {
                    ExpectedVersionIncarnation = original.VersionIncarnation
                }).ConfigureAwait(false);
            XRegistryResponse current = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Read, k_version)).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(rejected.StatusCode, Is.EqualTo(409));
                Assert.That(rejected.Error?.Code, Is.EqualTo("version_incarnation_changed"));
                Assert.That(current.StatusCode, Is.EqualTo(recreate ? 200 : 404));
                if (recreate)
                {
                    Assert.That(current.Metadata.GetProperty("name").GetString(), Is.EqualTo("replacement"));
                    Assert.That(current.Metadata.GetProperty("epoch").GetInt32(), Is.Zero);
                }
            });
        }

        [TestCase("/")]
        [TestCase("/groups")]
        [TestCase("/groups/g")]
        [TestCase(k_resource)]
        [TestCase(k_resource + "/meta")]
        [TestCase(k_resource + "/versions")]
        [TestCase("/modelsource")]
        public async Task IncarnationGuardsRequireAnExplicitVersionPathAsync(string path)
        {
            using XRegistryTransactionalEndpoint endpoint = XRegistryProviderCoverage.Create();
            XRegistryResponse rejected = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Merge, path, "{}") with
                {
                    ExpectedVersionIncarnation = "not-a-version"
                }).ConfigureAwait(false);
            await XRegistryProviderCoverage.AssertPristineAsync(endpoint, rejected, "action_not_supported", 405)
                .ConfigureAwait(false);
        }

        [Test]
        public async Task PreparedVersionUpdateCannotCrossAConcurrentRecreationAsync()
        {
            using XRegistryTransactionalEndpoint endpoint = XRegistryProviderCoverage.Create();
            XRegistryResponse original = await SeedAsync(endpoint).ConfigureAwait(false);
            IXRegistryPreparedOperation prepared = await endpoint.PrepareAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Merge, k_version,
                    /*lang=json,strict*/ """{"name":"prepared"}""") with
                {
                    ExpectedVersionIncarnation = original.VersionIncarnation
                }).ConfigureAwait(false);
            await using ConfiguredAsyncDisposable lifetime = prepared.ConfigureAwait(false);
            XRegistryResponse deleted = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Delete, k_resource)).ConfigureAwait(false);
            Assert.That(deleted.StatusCode, Is.EqualTo(204));
            XRegistryResponse replacement = await SeedAsync(endpoint, "replacement").ConfigureAwait(false);
            XRegistryResponse rejected = await prepared.CommitAsync().ConfigureAwait(false);
            XRegistryResponse current = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Read, k_version)).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(prepared.Response.Metadata.GetProperty("name").GetString(), Is.EqualTo("prepared"));
                Assert.That(prepared.Response.VersionIncarnation, Is.EqualTo(original.VersionIncarnation));
                Assert.That(rejected.StatusCode, Is.EqualTo(409));
                Assert.That(rejected.Error?.Code, Is.EqualTo("concurrent_change"));
                Assert.That(current.Metadata.GetProperty("name").GetString(), Is.EqualTo("replacement"));
                Assert.That(current.Metadata.GetProperty("epoch").GetInt32(), Is.Zero);
                Assert.That(current.VersionIncarnation, Is.EqualTo(replacement.VersionIncarnation));
            });
        }

        [Test]
        public async Task ReplayedGuardedMutationCannotAffectAReplacementVersionAsync()
        {
            using XRegistryTransactionalEndpoint endpoint = XRegistryProviderCoverage.Create();
            XRegistryResponse original = await SeedAsync(endpoint).ConfigureAwait(false);
            XRegistryRequest request = XRegistryProviderCoverage.Request(
                XRegistryAction.Merge, k_version, /*lang=json,strict*/ """{"epoch":0,"name":"once"}""") with
            {
                ExpectedVersionIncarnation = original.VersionIncarnation,
                OperationId = "guarded-once"
            };
            XRegistryResponse first = await endpoint.ExecuteAsync(request).ConfigureAwait(false);
            XRegistryResponse deleted = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Delete, k_resource)).ConfigureAwait(false);
            Assert.That(deleted.StatusCode, Is.EqualTo(204));
            XRegistryResponse replacement = await SeedAsync(endpoint, "replacement").ConfigureAwait(false);
            XRegistryResponse replay = await endpoint.ExecuteAsync(request).ConfigureAwait(false);
            XRegistryResponse current = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Read, k_version)).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(first.StatusCode, Is.EqualTo(200));
                Assert.That(replay.Metadata.GetProperty("name").GetString(), Is.EqualTo("once"));
                Assert.That(replay.VersionIncarnation, Is.EqualTo(original.VersionIncarnation));
                Assert.That(current.Metadata.GetProperty("name").GetString(), Is.EqualTo("replacement"));
                Assert.That(current.Metadata.GetProperty("epoch").GetInt32(), Is.Zero);
                Assert.That(current.VersionIncarnation, Is.EqualTo(replacement.VersionIncarnation));
            });
        }

        [Test]
        public async Task LegacyVersionGuardExpiresOnReopenAndCannotMatchAReplacementAsync()
        {
            var store = new InMemoryXRegistryTransactionStore();
            using var seed = new XRegistryTransactionalEndpoint(XRegistryProviderCoverage.Options(), store);
            await SeedAsync(seed).ConfigureAwait(false);
            ByteString prior = await store.LoadAsync().ConfigureAwait(false);
            var legacy = (JsonObject)JsonNode.Parse(prior.Span)!;
            var entry = (JsonObject)legacy["entries"]![k_version]!;
            Assert.That(entry.Remove("incarnation"), Is.True);
            Assert.That(await store.CommitAsync(prior, new XRegistryProtocolCodec().EncodeJson(legacy))
                .ConfigureAwait(false), Is.True);
            using var endpoint = new XRegistryTransactionalEndpoint(XRegistryProviderCoverage.Options(), store);
            XRegistryResponse original = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Read, k_version)).ConfigureAwait(false);
            XRegistryRequest request = XRegistryProviderCoverage.Request(
                XRegistryAction.Merge, k_version, /*lang=json,strict*/ """{"name":"legacy-update"}""") with
            {
                ExpectedVersionIncarnation = original.VersionIncarnation
            };
            using var reopened = new XRegistryTransactionalEndpoint(XRegistryProviderCoverage.Options(), store);
            XRegistryResponse expired = await reopened.ExecuteAsync(request).ConfigureAwait(false);
            XRegistryResponse edited = await endpoint.ExecuteAsync(request).ConfigureAwait(false);
            ByteString persisted = await store.LoadAsync().ConfigureAwait(false);
            using var persistedState = JsonDocument.Parse(persisted.Memory);
            Assert.That(persistedState.RootElement.GetProperty("entries").GetProperty(k_version)
                .GetProperty("incarnation").GetString(), Is.EqualTo(original.VersionIncarnation));
            XRegistryResponse deleted = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Delete, k_resource)).ConfigureAwait(false);
            Assert.That(deleted.StatusCode, Is.EqualTo(204));
            await SeedAsync(endpoint, "replacement").ConfigureAwait(false);
            XRegistryResponse rejected = await endpoint.ExecuteAsync(request).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(original.VersionIncarnation, Is.Not.Null.And.Not.Empty);
                Assert.That(edited.Metadata.GetProperty("name").GetString(), Is.EqualTo("legacy-update"));
                Assert.That(edited.VersionIncarnation, Is.EqualTo(original.VersionIncarnation));
                Assert.That(expired.Error?.Code, Is.EqualTo("version_incarnation_changed"));
                Assert.That(rejected.Error?.Code, Is.EqualTo("version_incarnation_changed"));
            });
        }

        [Test]
        public async Task ChangedUnstampedLegacyStateCannotReuseAnObservedIncarnationAsync()
        {
            var store = new InMemoryXRegistryTransactionStore();
            using var seed = new XRegistryTransactionalEndpoint(XRegistryProviderCoverage.Options(), store);
            await SeedAsync(seed).ConfigureAwait(false);
            ByteString prior = await store.LoadAsync().ConfigureAwait(false);
            var state = (JsonObject)JsonNode.Parse(prior.Span)!;
            var version = (JsonObject)state["entries"]![k_version]!;
            version.Remove("incarnation");
            var codec = new XRegistryProtocolCodec();
            ByteString legacy = codec.EncodeJson(state);
            Assert.That(await store.CommitAsync(prior, legacy).ConfigureAwait(false), Is.True);
            using var endpoint = new XRegistryTransactionalEndpoint(XRegistryProviderCoverage.Options(), store);
            XRegistryResponse original = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Read, k_version)).ConfigureAwait(false);
            Assert.That(await store.LoadAsync().ConfigureAwait(false), Is.EqualTo(legacy),
                "Reading a legacy Version must not migrate or otherwise write durable state.");
            state["generation"] = 3;
            version["metadata"]!["name"] = "external-replacement";
            ByteString external = codec.EncodeJson(state);
            Assert.That(await store.CommitAsync(legacy, external).ConfigureAwait(false), Is.True);
            XRegistryResponse rejected = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Merge, k_version,
                    /*lang=json,strict*/ """{"name":"stale"}""") with
                {
                    ExpectedVersionIncarnation = original.VersionIncarnation
                }).ConfigureAwait(false);
            XRegistryResponse current = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Read, k_version)).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(rejected.StatusCode, Is.EqualTo(409));
                Assert.That(rejected.Error?.Code, Is.EqualTo("version_incarnation_changed"));
                Assert.That(current.VersionIncarnation, Is.Not.EqualTo(original.VersionIncarnation));
                Assert.That(current.Metadata.GetProperty("name").GetString(), Is.EqualTo("external-replacement"));
                Assert.That(current.Metadata.GetProperty("epoch").GetInt32(), Is.Zero);
                Assert.That(current.Metadata.GetProperty("createdat").GetString(),
                    Is.EqualTo(original.Metadata.GetProperty("createdat").GetString()));
            });
            Assert.That(await store.LoadAsync().ConfigureAwait(false), Is.EqualTo(external));
        }

        [TestCase("null")]
        [TestCase("42")]
        [TestCase("\"\"")]
        [TestCase("\"00000000000000000000000000000000\"")]
        [TestCase("\"malformed\"")]
        public async Task CorruptPersistedIncarnationFailsClosedAsync(string invalid)
        {
            var store = new InMemoryXRegistryTransactionStore();
            using var seed = new XRegistryTransactionalEndpoint(XRegistryProviderCoverage.Options(), store);
            await SeedAsync(seed).ConfigureAwait(false);
            ByteString prior = await store.LoadAsync().ConfigureAwait(false);
            var state = (JsonObject)JsonNode.Parse(prior.Span)!;
            state["entries"]![k_version]!["incarnation"] = JsonNode.Parse(invalid);
            ByteString corrupt = new XRegistryProtocolCodec().EncodeJson(state);
            Assert.That(await store.CommitAsync(prior, corrupt).ConfigureAwait(false), Is.True);
            using var endpoint = new XRegistryTransactionalEndpoint(XRegistryProviderCoverage.Options(), store);
            for (int attempt = 0; attempt < 2; attempt++)
            {
                Assert.ThrowsAsync<InvalidDataException>(async () => await seed.ExecuteAsync(
                    XRegistryProviderCoverage.Request(XRegistryAction.Read, k_version)).ConfigureAwait(false));
                Assert.ThrowsAsync<InvalidDataException>(async () => await endpoint.ExecuteAsync(
                    XRegistryProviderCoverage.Request(XRegistryAction.Read, k_version)).ConfigureAwait(false));
            }
            Assert.That(await store.LoadAsync().ConfigureAwait(false), Is.EqualTo(corrupt));
        }

        private static async Task<XRegistryResponse> SeedAsync(
            XRegistryTransactionalEndpoint endpoint, string name = "original")
        {
            var metadata = new JsonObject { ["name"] = name };
            XRegistryResponse response = await endpoint.ExecuteAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Replace, k_version, metadata.ToJsonString())).ConfigureAwait(false);
            Assert.That(response.StatusCode, Is.EqualTo(201), response.Error?.Detail);
            return response;
        }

        private const string k_resource = "/groups/g/schemas/r";
        private const string k_version = k_resource + "/versions/v1";
    }
}
