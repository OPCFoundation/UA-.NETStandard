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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.XRegistry.Bridge.Sync;
using Opc.Ua.XRegistry.Protocol;
using Opc.Ua.XRegistry.Server.Protocol;
using static Opc.Ua.XRegistry.Bridge.Tests.Sync.XRegistrySyncFixture;

namespace Opc.Ua.XRegistry.Bridge.Tests.Sync
{
    [TestFixture]
    public sealed class XRegistryResourceSynchronizationTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task ExternalDocumentVersionsSynchronizeWithoutFetchingAndCanReturnToLocalBytesAsync(bool reverse)
        {
            await using var fixture = new XRegistrySyncFixture();
            const string model = /*lang=json,strict*/ """
                {"groups":{"groups":{"singular":"group","resources":{"schemas":{"singular":"schema"}}}}}
                """;
            using XRegistryTransactionalEndpoint native = Provider("native", model, fixture.Clock);
            using XRegistryTransactionalEndpoint http = Provider("http", model, fixture.Clock);
            XRegistryTransactionalEndpoint source = reverse ? http : native;
            XRegistryTransactionalEndpoint destination = reverse ? native : http;
            var engine = new XRegistrySynchronizer(
                native, http, fixture.Store, fixture.Options, fixture.Telemetry, fixture.Clock);
            _ = await engine.RunOnceAsync().ConfigureAwait(false);
            XRegistryResponse created = await source.ExecuteAsync(Request(XRegistryAction.Replace,
                k_resource + "/versions/v1",
                /*lang=json,strict*/ """{"schemaurl":"https://documents.example/first"}"""))
                .ConfigureAwait(false);
            Assert.That(created.StatusCode, Is.EqualTo(201), created.Error?.Detail);
            XRegistrySyncReport copied = await engine.RunOnceAsync().ConfigureAwait(false);
            Assert.That(copied.Status, Is.EqualTo(XRegistrySyncStatus.Succeeded), Details(copied));
            XRegistryResponse remote = await destination.ExecuteAsync(Request(XRegistryAction.Read,
                k_resource + "/versions/v1") with
            { View = XRegistryView.Default }).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(remote.StatusCode, Is.EqualTo(303));
                Assert.That(remote.Location, Is.EqualTo("https://documents.example/first"));
                Assert.That(remote.Document.IsNull, Is.True);
            });
            XRegistryResponse replaced = await source.ExecuteAsync(Request(XRegistryAction.Replace,
                k_resource + "/versions/v1",
                /*lang=json,strict*/ """{"schemaurl":"https://documents.example/second"}"""))
                .ConfigureAwait(false);
            Assert.That(replaced.StatusCode, Is.EqualTo(200), replaced.Error?.Detail);
            XRegistrySyncReport updated = await engine.RunOnceAsync().ConfigureAwait(false);
            Assert.That(updated.Status, Is.EqualTo(XRegistrySyncStatus.Succeeded), Details(updated));
            XRegistryResponse local = await source.ExecuteAsync(Request(XRegistryAction.Replace,
                k_resource + "/versions/v1", /*lang=json,strict*/ """{"schemabase64":"AQID"}""")).ConfigureAwait(false);
            Assert.That(local.StatusCode, Is.EqualTo(200), local.Error?.Detail);
            XRegistrySyncReport materialized = await engine.RunOnceAsync().ConfigureAwait(false);
            Assert.That(materialized.Status, Is.EqualTo(XRegistrySyncStatus.Succeeded), Details(materialized));
            XRegistryResponse final = await destination.ExecuteAsync(Request(XRegistryAction.Read,
                k_resource + "/versions/v1") with
            { View = XRegistryView.Default }).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(final.StatusCode, Is.EqualTo(200));
                Assert.That(final.Document, Is.EqualTo(ByteString.From(new byte[] { 1, 2, 3 })));
                Assert.That(final.Metadata.TryGetProperty("schemaurl", out _), Is.False);
            });
            Assert.That((await engine.RunOnceAsync().ConfigureAwait(false)).Applied, Is.Zero);
        }

        [TestCase("createdat")]
        [TestCase("modifiedat")]
        [TestCase("semver")]
        [TestCase("manual")]
        public async Task CompleteResourceClosuresPreserveOrderingAndMatchingAttributesAsync(string mode)
        {
            await using var fixture = new XRegistrySyncFixture();
            string model = """
                {"groups":{"groups":{"singular":"group","resources":{"schemas":{
                  "singular":"schema","versionmode":"MODE","maxversions":3,
                  "attributes":{"shared":{"type":"string","matchversions":true}}
                }}}}}
                """.Replace("MODE", mode, StringComparison.Ordinal);
            using XRegistryTransactionalEndpoint source = Provider("source", model, fixture.Clock);
            using XRegistryTransactionalEndpoint target = Provider("target", model, fixture.Clock);
            await SeedAsync(source, "old").ConfigureAwait(false);
            await SeedAsync(target, "old").ConfigureAwait(false);
            var engine = new XRegistrySynchronizer(
                source, target, fixture.Store, fixture.Options, fixture.Telemetry, fixture.Clock);
            Assert.That(
                (await engine.RunOnceAsync().ConfigureAwait(false)).Status, Is.EqualTo(XRegistrySyncStatus.Succeeded));
            fixture.Clock.Advance(TimeSpan.FromMinutes(1));
            await SeedAsync(source, "new").ConfigureAwait(false);
            XRegistrySyncReport copied = await engine.RunOnceAsync().ConfigureAwait(false);
            XRegistryResponse first =
                await target.ExecuteAsync(Request(XRegistryAction.Read, k_resource + "/versions/1.0.0"))
                .ConfigureAwait(false);
            XRegistryResponse second =
                await target.ExecuteAsync(Request(XRegistryAction.Read, k_resource + "/versions/2.0.0"))
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(copied.Status, Is.EqualTo(XRegistrySyncStatus.Succeeded), Details(copied));
                Assert.That(copied.Applied, Is.EqualTo(1));
                Assert.That(first.Metadata.GetProperty("shared").GetString(), Is.EqualTo("new"));
                Assert.That(second.Metadata.GetProperty("shared").GetString(), Is.EqualTo("new"));
            });
            XRegistrySyncReport repeated = await engine.RunOnceAsync().ConfigureAwait(false);
            Assert.That(repeated.Applied, Is.Zero, Details(repeated));
        }

        [Test]
        public async Task AChangedSiblingAfterPreparationAbortsTheWholeClosureAsync()
        {
            await using var fixture = new XRegistrySyncFixture();
            const string model = /*lang=json,strict*/ """
                {"groups":{"groups":{"singular":"group","resources":{"schemas":{
                  "singular":"schema","versionmode":"semver","attributes":{"shared":{"type":"string"}}
                }}}}}
                """;
            using XRegistryTransactionalEndpoint source = Provider("source", model, fixture.Clock);
            using XRegistryTransactionalEndpoint target = Provider("target", model, fixture.Clock);
            await SeedAsync(source, "old").ConfigureAwait(false);
            await SeedAsync(target, "old").ConfigureAwait(false);
            var prepared = new RacingEndpoint(target);
            var engine = new XRegistrySynchronizer(
                source, prepared, fixture.Store, fixture.Options, fixture.Telemetry, fixture.Clock);
            _ = await engine.RunOnceAsync().ConfigureAwait(false);
            _ = await source.ExecuteAsync(Request(XRegistryAction.Merge, k_resource + "/versions/1.0.0",
                /*lang=json,strict*/ """{"shared":"new"}""")).ConfigureAwait(false);
            prepared.AfterPrepare = async () =>
            {
                _ = await target.ExecuteAsync(Request(XRegistryAction.Merge, k_resource + "/versions/2.0.0",
                    /*lang=json,strict*/ """{"name":"concurrent-sibling"}""")).ConfigureAwait(false);
            };
            XRegistrySyncReport report = await engine.RunOnceAsync().ConfigureAwait(false);
            XRegistryResponse unchanged =
                await target.ExecuteAsync(Request(XRegistryAction.Read, k_resource + "/versions/1.0.0"))
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(report.Applied, Is.Zero, Details(report));
                Assert.That(report.Conflicts, Is.GreaterThan(0));
                Assert.That(unchanged.Metadata.GetProperty("shared").GetString(), Is.EqualTo("old"));
            });
        }

        private static async Task SeedAsync(XRegistryTransactionalEndpoint endpoint, string shared)
        {
            XRegistryResponse response = await endpoint.ExecuteAsync(Request(XRegistryAction.Replace, k_resource,
                """
                {"versions":{"1.0.0":{"shared":"VALUE","schemabase64":"YQ=="},
                             "2.0.0":{"shared":"VALUE","schemabase64":"Yg=="}}}
                """.Replace("VALUE", shared, StringComparison.Ordinal))).ConfigureAwait(false);
            Assert.That(response.StatusCode, Is.AnyOf(200, 201), response.Error?.Detail);
        }

        private static XRegistryTransactionalEndpoint Provider(string id, string model, TimeProvider clock)
        {
            return new(new XRegistryTransactionalOptions { RegistryId = id, Model = Json(model) },
                new InMemoryXRegistryTransactionStore(), clock);
        }

        private static XRegistryRequest Request(XRegistryAction action, string path, string? json = null)
        {
            return new(action, path)
            {
                Context = Writer,
                View = XRegistryView.Metadata,
                Metadata = json is null ? default : Json(json)
            };
        }

        private sealed class RacingEndpoint(XRegistryTransactionalEndpoint endpoint) : IXRegistryPreparedEndpoint
        {
            public Func<Task>? AfterPrepare { get; set; }

            public ValueTask<XRegistryEndpointDescription> InspectAsync(XRegistryCallContext context,
                CancellationToken cancellationToken = default)
            {
                return endpoint.InspectAsync(context, cancellationToken);
            }

            public ValueTask<XRegistryResponse> ExecuteAsync(XRegistryRequest request,
                CancellationToken cancellationToken = default)
            {
                return endpoint.ExecuteAsync(request, cancellationToken);
            }

            public async ValueTask<IXRegistryPreparedOperation> PrepareAsync(XRegistryRequest request,
                CancellationToken cancellationToken = default)
            {
                IXRegistryPreparedOperation operation =
                    await endpoint.PrepareAsync(request, cancellationToken).ConfigureAwait(false);
                Func<Task>? callback = AfterPrepare;
                AfterPrepare = null;
                if (callback is not null)
                {
                    await callback().ConfigureAwait(false);
                }
                return operation;
            }
        }

        private const string k_resource = "/groups/g/schemas/r";
    }
}
