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

using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.XRegistry.Protocol;
using Opc.Ua.XRegistry.Server.Protocol;
using static Opc.Ua.XRegistry.Tests.ProtocolProvider.XRegistryProviderCoverage;

namespace Opc.Ua.XRegistry.Tests.ProtocolProvider
{
    [TestFixture]
    public sealed class XRegistryReferenceTests
    {
        [Test]
        public async Task ImportedReferencesProjectAllVersionsAtTheSourceIdentityWithoutCopyingStorageAsync()
        {
            var state = new InMemoryXRegistryTransactionStore();
            using (var endpoint = new XRegistryTransactionalEndpoint(Options(k_model), state))
            {
                await SeedAsync(endpoint).ConfigureAwait(false);
                XRegistryResponse created = await endpoint.ExecuteAsync(Request(XRegistryAction.Replace, k_alias,
                    /*lang=json,strict*/ """{"meta":{"xref":"/library/g/schemas/r"}}""")).ConfigureAwait(false);
                Assert.That(created.StatusCode, Is.EqualTo(201), created.Error?.Detail);
            }
            using var reopened = new XRegistryTransactionalEndpoint(Options(k_model), state);
            XRegistryResponse resource = await reopened.ExecuteAsync(Request(XRegistryAction.Read, k_alias) with
            {
                Parameters = [new("inline", "meta,versions.schema")]
            }).ConfigureAwait(false);
            XRegistryResponse bytes =
                await reopened.ExecuteAsync(Request(XRegistryAction.Read, k_alias + "/versions/v1")
                with
                { View = XRegistryView.Default }).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(resource.StatusCode, Is.EqualTo(200), resource.Error?.Detail);
                Assert.That(resource.Metadata.GetProperty("schemaid").GetString(), Is.EqualTo("alias"));
                Assert.That(
                    resource.Metadata.GetProperty("meta").GetProperty("xref").GetString(), Is.EqualTo(k_target));
                Assert.That(resource.Metadata.GetProperty("meta").GetProperty("readonly").GetBoolean(), Is.True);
                Assert.That(resource.Metadata.GetProperty("versionscount").GetInt32(), Is.EqualTo(2));
                Assert.That(
                    resource.Metadata.GetProperty("versions").GetProperty("v1").GetProperty("schemaid").GetString(),
                    Is.EqualTo("alias"));
                Assert.That(Encoding.UTF8.GetString(bytes.Document.ToArray()), Is.EqualTo("""{"value":1}"""));
            });
            XRegistryResponse rejected =
                await reopened.ExecuteAsync(Request(XRegistryAction.Merge, k_alias + "/versions/v1",
                /*lang=json,strict*/ """{"name":"not-writable"}""")).ConfigureAwait(false);
            Assert.That(rejected.Error?.Code, Is.EqualTo("readonly"));
            Assert.That((await reopened.ExecuteAsync(Request(XRegistryAction.Delete, k_alias)).ConfigureAwait(false))
                .StatusCode,
                Is.EqualTo(204));
            Assert.That((await reopened.ExecuteAsync(Request(XRegistryAction.Read, k_target)).ConfigureAwait(false))
                .Metadata.GetProperty("versionscount").GetInt32(), Is.EqualTo(2));
        }

        [Test]
        public async Task ConversionBackToNormalDoesNotResurrectFormerMetadataOrDocumentsAsync()
        {
            using XRegistryTransactionalEndpoint endpoint = Create(k_model);
            await SeedAsync(endpoint).ConfigureAwait(false);
            XRegistryResponse normal = await endpoint.ExecuteAsync(Request(XRegistryAction.Replace, k_alias,
                /*lang=json,strict*/ """
                {"versionid":"old","schema":{"secret":"old-document"},
                 "meta":{"createdat":"2001-01-01T00:00:00Z","name":"forgotten"}}
                """)).ConfigureAwait(false);
            Assert.That(normal.StatusCode, Is.EqualTo(201), normal.Error?.Detail);
            XRegistryResponse linked = await endpoint.ExecuteAsync(Request(XRegistryAction.Replace, k_alias + "/meta",
                /*lang=json,strict*/ """{"xref":"/library/g/schemas/r","epoch":0}""")).ConfigureAwait(false);
            Assert.That(linked.StatusCode, Is.EqualTo(200), linked.Error?.Detail);
            XRegistryResponse converted = await endpoint.ExecuteAsync(Request(XRegistryAction.Merge, k_alias + "/meta",
                /*lang=json,strict*/ """{"xref":null}""")).ConfigureAwait(false);
            XRegistryResponse document = await endpoint.ExecuteAsync(Request(XRegistryAction.Read, k_alias) with
            { View = XRegistryView.Default }).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(converted.StatusCode, Is.EqualTo(200), converted.Error?.Detail);
                Assert.That(converted.Metadata.GetProperty("createdat").GetString(),
                    Is.EqualTo("2001-01-01T00:00:00.0000000Z"));
                Assert.That(converted.Metadata.GetProperty("epoch").GetInt32(), Is.GreaterThan(1));
                Assert.That(converted.Metadata.TryGetProperty("xref", out _), Is.False);
                Assert.That(converted.Metadata.TryGetProperty("name", out _), Is.False);
                Assert.That(document.Document.IsEmpty, Is.True);
                Assert.That(document.Metadata.GetProperty("versionid").GetString(), Is.EqualTo("1"));
            });
        }

        [TestCase("/library/missing/schemas/missing")]
        [TestCase("/groups/g/schemas/second")]
        public async Task DanglingAndTransitiveReferencesNeverInventAnEmptyVersionAsync(string target)
        {
            using XRegistryTransactionalEndpoint endpoint = Create(k_model);
            await SeedAsync(endpoint).ConfigureAwait(false);
            XRegistryResponse second =
                await endpoint.ExecuteAsync(Request(XRegistryAction.Replace, "/groups/g/schemas/second",
                /*lang=json,strict*/ """{"meta":{"xref":"/library/g/schemas/r"}}""")).ConfigureAwait(false);
            Assert.That(second.StatusCode, Is.EqualTo(201), second.Error?.Detail);
            XRegistryResponse alias = await endpoint.ExecuteAsync(Request(XRegistryAction.Replace, k_alias,
                "{\"meta\":{\"xref\":\"" + target + "\"}}")).ConfigureAwait(false);
            Assert.That(alias.StatusCode, Is.EqualTo(201), alias.Error?.Detail);
            XRegistryResponse view = await endpoint.ExecuteAsync(Request(XRegistryAction.Read, k_alias) with
            { Parameters = [new("inline", "meta,versions")] }).ConfigureAwait(false);
            XRegistryResponse file = await endpoint.ExecuteAsync(Request(XRegistryAction.Read, k_alias) with
            { View = XRegistryView.Default }).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(view.StatusCode, Is.EqualTo(200));
                Assert.That(view.Metadata.TryGetProperty("versionid", out _), Is.False);
                Assert.That(view.Metadata.TryGetProperty("versions", out _), Is.False);
                Assert.That(view.Metadata.GetProperty("meta").TryGetProperty("epoch", out _), Is.False);
                Assert.That(file.StatusCode, Is.EqualTo(404));
                Assert.That(file.Document.IsNull, Is.True);
            });
        }

        [TestCase("""{"meta":{"xref":"/copies/g/schemas/r"}}""", "malformed_xref")]
        [TestCase("""{"meta":{"xref":"/library/g/schemas/r"},"name":"extra"}""", "extra_xref_attribute")]
        [TestCase("""{"meta":{"xref":"/library/g/schemas/r","labels":{}}}""", "extra_xref_attribute")]
        public async Task StructuralSimilarityAndExtraReferenceFieldsCannotBypassTheModelAsync(string body, string code)
        {
            using XRegistryTransactionalEndpoint endpoint = Create(k_model);
            XRegistryResponse response = await endpoint.ExecuteAsync(Request(XRegistryAction.Replace, k_alias, body))
                .ConfigureAwait(false);
            Assert.That(response.Error?.Code, Is.EqualTo(code), response.Error?.Detail);
            Assert.That((await endpoint.ExecuteAsync(Request(XRegistryAction.Read, "/groups")).ConfigureAwait(false))
                .Metadata.GetRawText(), Is.EqualTo("{}"));
        }

        [Test]
        public async Task IgnoreReadonlySkipsAnAliasOnlyInsideACollectionRequestAsync()
        {
            using XRegistryTransactionalEndpoint endpoint = Create(k_model);
            await SeedAsync(endpoint).ConfigureAwait(false);
            _ = await endpoint.ExecuteAsync(Request(XRegistryAction.Replace, k_alias,
                /*lang=json,strict*/ """{"meta":{"xref":"/library/g/schemas/r"}}""")).ConfigureAwait(false);
            XRegistryResponse skipped = await endpoint.ExecuteAsync(Request(XRegistryAction.Merge, "/groups/g/schemas",
                /*lang=json,strict*/ """{"alias":{"name":"must-not-apply"}}""") with
            { Parameters = [new("ignore", "readonly")] }).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(skipped.StatusCode, Is.EqualTo(200), skipped.Error?.Detail);
                Assert.That(skipped.Metadata.GetRawText(), Is.EqualTo("{}"));
            });
            XRegistryResponse rejected = await endpoint.ExecuteAsync(Request(XRegistryAction.Merge, k_alias, "{}") with
            { Parameters = [new("ignore", "readonly")] }).ConfigureAwait(false);
            Assert.That(rejected.Error?.Code, Is.EqualTo("bad_flag"));
        }

        private static async Task SeedAsync(XRegistryTransactionalEndpoint endpoint)
        {
            XRegistryResponse response = await endpoint.ExecuteAsync(Request(XRegistryAction.Replace, k_target,
                /*lang=json,strict*/ """
                {"versions":{"v1":{"schema":{"value":1}},"v2":{"schema":{"value":2}}}}
                """)).ConfigureAwait(false);
            Assert.That(response.StatusCode, Is.EqualTo(201), response.Error?.Detail);
        }

        private const string k_target = "/library/g/schemas/r";
        private const string k_alias = "/groups/g/schemas/alias";

        private const string k_model = /*lang=json,strict*/ """
            {"groups":{
              "library":{"singular":"library","resources":{"schemas":{"singular":"schema"}}},
              "groups":{"singular":"group","ximportresources":["/library/schemas"]},
              "copies":{"singular":"copy","resources":{"schemas":{"singular":"schema"}}}
            }}
            """;
    }
}
