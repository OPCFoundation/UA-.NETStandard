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
using System.IO;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.XRegistry.Protocol;
using Opc.Ua.XRegistry.Server.Protocol;
using static Opc.Ua.XRegistry.Tests.ProtocolProvider.XRegistryProviderCoverage;

namespace Opc.Ua.XRegistry.Tests.ProtocolProvider
{
    [TestFixture]
    public sealed class XRegistryShortLinkTests
    {
        [Test]
        public async Task InitializationIsExplicitAndReadDoesNotPublishAsync()
        {
            var store = new InMemoryXRegistryTransactionStore();
            using var endpoint = new XRegistryTransactionalEndpoint(AliasOptions(), store);
            XRegistryResponse before = await endpoint.ExecuteAsync(Request(XRegistryAction.Read, "/"))
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(before.StatusCode, Is.EqualTo(503));
                Assert.That(before.Error?.Code, Is.EqualTo("shortlinks_not_initialized"));
            });
            Assert.That((await store.LoadAsync().ConfigureAwait(false)).IsNull, Is.True);
            Assert.That(await endpoint.InitializeShortLinksAsync(Writer).ConfigureAwait(false), Is.EqualTo(1));
            ByteString committed = await store.LoadAsync().ConfigureAwait(false);
            XRegistryResponse root = await endpoint.ExecuteAsync(Request(XRegistryAction.Read, "/"))
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(root.Metadata.GetProperty("shortself").GetString(),
                    Is.EqualTo("https://aliases.example/_s/1"));
                Assert.That(root.Metadata.GetProperty("epoch").GetInt32(), Is.Zero);
            });
            Assert.That(await endpoint.InitializeShortLinksAsync(Writer).ConfigureAwait(false), Is.Zero);
            Assert.That(await store.LoadAsync().ConfigureAwait(false), Is.EqualTo(committed));
        }

        [Test]
        public async Task AliasLifetimesSurviveRestartAndDisableReenableWithoutChangingTargetsAsync()
        {
            var store = new InMemoryXRegistryTransactionStore();
            XRegistryTransactionalOptions options = AliasOptions();
            string alias;
            using (var endpoint = new XRegistryTransactionalEndpoint(options, store))
            {
                _ = await endpoint.InitializeShortLinksAsync(Writer).ConfigureAwait(false);
                XRegistryResponse created = await endpoint.ExecuteAsync(Request(
                    XRegistryAction.Replace,
                    "/groups/g/schemas/r/versions/v1",
                    /*lang=json,strict*/ """{"name":"persisted","schemabase64":"AQI="}"""))
                    .ConfigureAwait(false);
                Assert.That(created.StatusCode, Is.EqualTo(201), created.Error?.Detail);
                alias = Alias(created);
            }
            using (var disabled = new XRegistryTransactionalEndpoint(options with { ShortLinksEnabled = false }, store))
            {
                XRegistryResponse read = await disabled.ExecuteAsync(Request(XRegistryAction.Read, alias))
                    .ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(read.StatusCode, Is.EqualTo(200), read.Error?.Detail);
                    Assert.That(read.Metadata.TryGetProperty("shortself", out _), Is.False);
                    Assert.That(read.Metadata.GetProperty("name").GetString(), Is.EqualTo("persisted"));
                });
            }
            using var reopened = new XRegistryTransactionalEndpoint(options, store);
            XRegistryResponse restored = await reopened.ExecuteAsync(Request(XRegistryAction.Read, alias))
                .ConfigureAwait(false);
            Assert.That(Alias(restored), Is.EqualTo(alias));
            Assert.That((await reopened.InspectAsync(Writer).ConfigureAwait(false)).Capabilities
                .GetProperty("shortself").GetBoolean(), Is.True);
        }

        [Test]
        public async Task LogicalResourceMetaAndExactVersionHaveIndependentAliasesAsync()
        {
            using var endpoint = new XRegistryTransactionalEndpoint(
                AliasOptions(), new InMemoryXRegistryTransactionStore());
            _ = await endpoint.InitializeShortLinksAsync(Writer).ConfigureAwait(false);
            XRegistryResponse created = await endpoint.ExecuteAsync(Request(XRegistryAction.Replace,
                "/groups/g/schemas/r", /*lang=json,strict*/ """
                {"meta":{"defaultversionid":"v1","defaultversionsticky":true},
                 "versions":{"v1":{"schemabase64":"AQI="},"v2":{"schemabase64":"AwQ="}}}
                """)).ConfigureAwait(false);
            Assert.That(created.StatusCode, Is.EqualTo(201), created.Error?.Detail);
            string logical = Alias(created);
            string meta = Alias(await endpoint.ExecuteAsync(Request(XRegistryAction.Read, "/groups/g/schemas/r/meta"))
                .ConfigureAwait(false));
            string version = Alias(await endpoint.ExecuteAsync(
                Request(XRegistryAction.Read, "/groups/g/schemas/r/versions/v1"))
                .ConfigureAwait(false));
            Assert.That(new[] { logical, meta, version }, Is.Unique);
            XRegistryResponse selected = await endpoint.ExecuteAsync(Request(XRegistryAction.Merge, meta,
                /*lang=json,strict*/ """{"defaultversionid":"v2"}""")).ConfigureAwait(false);
            Assert.That(selected.StatusCode, Is.EqualTo(200), selected.Error?.Detail);
            XRegistryResponse current = await endpoint.ExecuteAsync(Request(XRegistryAction.Read, logical) with
            { View = XRegistryView.Default }).ConfigureAwait(false);
            XRegistryResponse exact = await endpoint.ExecuteAsync(Request(XRegistryAction.Read, version) with
            { View = XRegistryView.Default }).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(Alias(current), Is.EqualTo(logical));
                Assert.That(current.Document, Is.EqualTo(ByteString.From(new byte[] { 3, 4 })));
                Assert.That(exact.Document, Is.EqualTo(ByteString.From(new byte[] { 1, 2 })));
            });
        }

        [Test]
        public async Task RecreatedEntityCannotReuseItsRetiredAliasOrResolvedRequestAsync()
        {
            using var endpoint = new XRegistryTransactionalEndpoint(
                AliasOptions(), new InMemoryXRegistryTransactionStore());
            _ = await endpoint.InitializeShortLinksAsync(Writer).ConfigureAwait(false);
            XRegistryResponse first = await endpoint.ExecuteAsync(Request(XRegistryAction.Replace, "/groups/g", "{}"))
                .ConfigureAwait(false);
            string alias = Alias(first);
            XRegistryRequest change = Request(XRegistryAction.Merge, alias,
                /*lang=json,strict*/ """{"name":"old"}""") with
            { OperationId = "once" };
            XRegistryAddressResolution resolution = await endpoint.ResolveAddressAsync(change).ConfigureAwait(false);
            Assert.That(resolution.Rejection, Is.Null);
            var codec = new XRegistryProtocolCodec();
            Assert.That(codec.ComputeRequestDigest(resolution.Request),
                Is.EqualTo(codec.ComputeRequestDigest(change)));
            XRegistryResponse original = await endpoint.ExecuteAsync(resolution.Request).ConfigureAwait(false);
            Assert.That(original.StatusCode, Is.EqualTo(200));
            _ = await endpoint.ExecuteAsync(Request(XRegistryAction.Delete, alias)).ConfigureAwait(false);
            XRegistryResponse second = await endpoint.ExecuteAsync(Request(XRegistryAction.Replace, "/groups/g",
                /*lang=json,strict*/ """{"name":"new"}""")).ConfigureAwait(false);
            XRegistryResponse stale = await endpoint.ExecuteAsync(resolution.Request with { OperationId = null })
                .ConfigureAwait(false);
            XRegistryResponse replay = await endpoint.ExecuteAsync(change).ConfigureAwait(false);
            XRegistryResponse current = await endpoint.ExecuteAsync(Request(XRegistryAction.Read, "/groups/g"))
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(Alias(second), Is.Not.EqualTo(alias));
                Assert.That(stale.StatusCode, Is.EqualTo(404));
                Assert.That(replay.Metadata.GetRawText(), Is.EqualTo(original.Metadata.GetRawText()));
                Assert.That(current.Metadata.GetProperty("name").GetString(), Is.EqualTo("new"));
                Assert.That(current.Metadata.GetProperty("epoch").GetInt32(), Is.Zero);
            });
        }

        [Test]
        public async Task ForgedCanonicalPathForLiveAliasRejectsWithoutMutationAsync()
        {
            var store = new InMemoryXRegistryTransactionStore();
            using var endpoint = new XRegistryTransactionalEndpoint(AliasOptions(), store);
            _ = await endpoint.InitializeShortLinksAsync(Writer).ConfigureAwait(false);
            XRegistryResponse first = await endpoint.ExecuteAsync(Request(
                XRegistryAction.Replace, "/groups/a", """{"name":"first"}""")).ConfigureAwait(false);
            XRegistryResponse second = await endpoint.ExecuteAsync(Request(
                XRegistryAction.Replace, "/groups/b", """{"name":"second"}""")).ConfigureAwait(false);
            Assert.That(second.StatusCode, Is.EqualTo(201));
            ByteString before = await store.LoadAsync().ConfigureAwait(false);
            XRegistryRequest forged = Request(XRegistryAction.Merge, Alias(first), """{"name":"wrong"}""")
                .AtResolvedPath("/groups/b");
            XRegistryResponse rejected = await endpoint.ExecuteAsync(forged).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(rejected.StatusCode, Is.EqualTo(409));
                Assert.That(rejected.Error?.Code, Is.EqualTo("address_changed"));
            });
            Assert.That(await store.LoadAsync().ConfigureAwait(false), Is.EqualTo(before));
        }

        [Test]
        public async Task FailedPreparationDoesNotAllocateOrRetireCommittedAliasesAsync()
        {
            var store = new InMemoryXRegistryTransactionStore();
            using var endpoint = new XRegistryTransactionalEndpoint(AliasOptions() with { MaxShortLinks = 2 }, store);
            _ = await endpoint.InitializeShortLinksAsync(Writer).ConfigureAwait(false);
            ByteString before = await store.LoadAsync().ConfigureAwait(false);
            XRegistryResponse rejected = await endpoint.ExecuteAsync(Request(XRegistryAction.Merge, "/",
                /*lang=json,strict*/ """{"groups":{"a":{},"b":{}}}""")).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(rejected.StatusCode, Is.EqualTo(413));
                Assert.That(rejected.Error?.Code, Is.EqualTo("too_large"));
            });
            Assert.That(await store.LoadAsync().ConfigureAwait(false), Is.EqualTo(before));
            IXRegistryPreparedOperation pending = await endpoint.PrepareAsync(
                Request(XRegistryAction.Replace, "/groups/a", "{}"))
                .ConfigureAwait(false);
            await using (pending.ConfigureAwait(false))
            {
                Assert.That(pending.Response.StatusCode, Is.EqualTo(201), pending.Response.Error?.Detail);
                Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await endpoint.InitializeShortLinksAsync(Writer).ConfigureAwait(false));
                var candidate = (IXRegistryPreparedSnapshot)pending;
                XRegistryResponse view = await candidate.ReadCandidateAsync(Request(
                    XRegistryAction.Read, Alias(pending.Response))).ConfigureAwait(false);
                Assert.That(view.Metadata.GetProperty("groupid").GetString(), Is.EqualTo("a"));
            }
            Assert.That(await store.LoadAsync().ConfigureAwait(false), Is.EqualTo(before));
        }

        [TestCase("/model")]
        [TestCase("/a/b")]
        [TestCase("/s?query")]
        [TestCase("/s$details")]
        public void InvalidAliasMountsRejectAtConstruction(string prefix)
        {
            Assert.Throws<ArgumentException>(() =>
            {
                using var endpoint = new XRegistryTransactionalEndpoint(
                    AliasOptions() with { ShortLinkPrefix = prefix },
                    new InMemoryXRegistryTransactionStore());
            });
        }

        [Test]
        public async Task StoredAliasCorruptionOrOriginChangeFailsClosedAsync()
        {
            var store = new InMemoryXRegistryTransactionStore();
            using (var endpoint = new XRegistryTransactionalEndpoint(AliasOptions(), store))
            {
                _ = await endpoint.InitializeShortLinksAsync(Writer).ConfigureAwait(false);
            }
            using (var changedRoot = new XRegistryTransactionalEndpoint(
                AliasOptions() with { PublicRoot = new Uri("https://different.example/") }, store))
            {
                Assert.ThrowsAsync<InvalidDataException>(async () =>
                    await changedRoot.InspectAsync(Writer).ConfigureAwait(false));
            }
            ByteString saved = await store.LoadAsync().ConfigureAwait(false);
            JsonObject state = JsonNode.Parse(saved.Span)!.AsObject();
            state["shortlinks"]!["next"] = 0;
            Assert.That(await store.CommitAsync(saved, new XRegistryProtocolCodec().EncodeJson(state))
                .ConfigureAwait(false),
                Is.True);
            using var corrupted = new XRegistryTransactionalEndpoint(AliasOptions(), store);
            Assert.ThrowsAsync<InvalidDataException>(async () =>
                await corrupted.InspectAsync(Writer).ConfigureAwait(false));
        }

        [Test]
        public async Task ReferencedVersionAliasesDoNotSurviveTargetRecreationAsync()
        {
            using var endpoint = new XRegistryTransactionalEndpoint(
                AliasOptions(), new InMemoryXRegistryTransactionStore());
            _ = await endpoint.InitializeShortLinksAsync(Writer).ConfigureAwait(false);
            _ = await endpoint.ExecuteAsync(Request(XRegistryAction.Replace, "/groups/target/schemas/r",
                """{"versions":{"v1":{"schemabase64":"AQI="},"v2":{"schemabase64":"AwQ="}}}""")).ConfigureAwait(false);
            XRegistryResponse alias = await endpoint.ExecuteAsync(Request(
                XRegistryAction.Replace, "/groups/source/schemas/r",
                """{"meta":{"xref":"/groups/target/schemas/r"}}""")).ConfigureAwait(false);
            Assert.That(alias.IsSuccess, Is.True, alias.Error?.Detail);
            string resourceAlias = Alias(alias);
            XRegistryResponse first = await endpoint.ExecuteAsync(
                Request(XRegistryAction.Read, "/groups/source/schemas/r/versions/v1")).ConfigureAwait(false);
            string versionAlias = Alias(first);
            XRegistryResponse deleted = await endpoint.ExecuteAsync(
                Request(XRegistryAction.Delete, "/groups/target/schemas/r/versions/v1")).ConfigureAwait(false);
            Assert.That(deleted.StatusCode, Is.EqualTo(204), deleted.Error?.Detail);
            XRegistryResponse recreated = await endpoint.ExecuteAsync(Request(XRegistryAction.Replace,
                "/groups/target/schemas/r/versions/v1", """{"schemabase64":"BQY="}""")).ConfigureAwait(false);
            Assert.That(recreated.IsSuccess, Is.True, recreated.Error?.Detail);
            XRegistryResponse stale = await endpoint.ExecuteAsync(Request(XRegistryAction.Read, versionAlias))
                .ConfigureAwait(false);
            XRegistryResponse current = await endpoint.ExecuteAsync(
                Request(XRegistryAction.Read, "/groups/source/schemas/r/versions/v1")).ConfigureAwait(false);
            XRegistryResponse source = await endpoint.ExecuteAsync(Request(XRegistryAction.Read, resourceAlias))
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(stale.StatusCode, Is.EqualTo(404));
                Assert.That(Alias(current), Is.Not.EqualTo(versionAlias));
                Assert.That(Alias(source), Is.EqualTo(resourceAlias));
            });
        }

        [Test]
        public async Task ExpandedReferencesRespectTheEntityBudgetBeforePublishingAliasesAsync()
        {
            var store = new InMemoryXRegistryTransactionStore();
            using var endpoint = new XRegistryTransactionalEndpoint(AliasOptions() with { MaxEntities = 8 }, store);
            _ = await endpoint.InitializeShortLinksAsync(Writer).ConfigureAwait(false);
            XRegistryResponse target = await endpoint.ExecuteAsync(Request(
                XRegistryAction.Replace, "/groups/target/schemas/r",
                """{"versions":{"v1":{"schemabase64":"AQI="},"v2":{"schemabase64":"AwQ="}}}""")).ConfigureAwait(false);
            Assert.That(target.IsSuccess, Is.True, target.Error?.Detail);
            ByteString before = await store.LoadAsync().ConfigureAwait(false);
            XRegistryResponse rejected = await endpoint.ExecuteAsync(Request(
                XRegistryAction.Replace, "/groups/source/schemas/r",
                """{"meta":{"xref":"/groups/target/schemas/r"}}""")).ConfigureAwait(false);
            Assert.That(rejected.StatusCode, Is.EqualTo(413));
            Assert.That(await store.LoadAsync().ConfigureAwait(false), Is.EqualTo(before));
        }

        private static XRegistryTransactionalOptions AliasOptions()
        {
            return Options() with { ShortLinksEnabled = true, PublicRoot = new Uri("https://aliases.example/") };
        }

        private static string Alias(XRegistryResponse response)
        {
            Assert.That(response.IsSuccess, Is.True, response.Error?.Detail);
            return new Uri(response.Metadata.GetProperty("shortself").GetString()!).AbsolutePath;
        }
    }
}
