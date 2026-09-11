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
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.XRegistry.Protocol;
using Opc.Ua.XRegistry.Server.Protocol;

namespace Opc.Ua.XRegistry.Tests.ProtocolProvider
{
    [TestFixture]
    public sealed class XRegistryTransactionalVersionsCoverageTests
    {
        [Test]
        public async Task ResourceCreateAddsAVersionWhileReplaceTargetsOnlyThePriorDefaultAsync()
        {
            using var endpoint = XRegistryProviderCoverage.Create();
            XRegistryResponse first = await endpoint.ExecuteAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Replace, k_resource, """{"versionid":"1","name":"initial"}""")).ConfigureAwait(false);
            XRegistryResponse second = await endpoint.ExecuteAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Create, k_resource, """{"versionid":"2","name":"second"}""")).ConfigureAwait(false);
            XRegistryResponse mismatched = await endpoint.ExecuteAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Replace, k_resource, """{"versionid":"1","name":"wrong-target"}"""))
                .ConfigureAwait(false);
            XRegistryResponse replaced = await endpoint.ExecuteAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Replace, k_resource, """{"name":"updated-default"}""")).ConfigureAwait(false);
            XRegistryResponse original = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Read, k_resource + "/versions/1"))
                .ConfigureAwait(false);
            XRegistryResponse meta = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Read, k_resource + "/meta")).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(first.StatusCode, Is.EqualTo(201));
                Assert.That(second.StatusCode, Is.EqualTo(200));
                Assert.That(second.Metadata.GetProperty("versionid").GetString(), Is.EqualTo("2"));
                Assert.That(mismatched.Error?.Code, Is.EqualTo("mismatched_id"));
                Assert.That(original.Metadata.GetProperty("name").GetString(), Is.EqualTo("initial"));
                Assert.That(original.Metadata.GetProperty("epoch").GetInt32(), Is.Zero);
                Assert.That(replaced.Metadata.GetProperty("name").GetString(), Is.EqualTo("updated-default"));
                Assert.That(replaced.Metadata.GetProperty("versionid").GetString(), Is.EqualTo("2"));
                Assert.That(replaced.Metadata.GetProperty("epoch").GetInt32(), Is.EqualTo(1));
                Assert.That(replaced.Metadata.GetProperty("versionscount").GetInt32(), Is.EqualTo(2));
                Assert.That(meta.Metadata.GetProperty("defaultversionid").GetString(), Is.EqualTo("2"));
                Assert.That(meta.Metadata.GetProperty("epoch").GetInt32(), Is.EqualTo(1));
            });
        }

        [Test]
        public async Task ServerAssignedVersionsSkipOccupiedIdsAndRejectExplicitNewIdsAsync()
        {
            using var endpoint = CreateWithResource("""{"singular":"schema","setversionid":false}""");
            XRegistryResponse first = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Replace, k_resource, "{}")).ConfigureAwait(false);
            XRegistryResponse second = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Create, k_resource, "{}")).ConfigureAwait(false);
            XRegistryResponse rejected = await endpoint.ExecuteAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Replace, k_resource + "/versions/manual", "{}")).ConfigureAwait(false);
            XRegistryResponse read = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Read, k_resource)).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(first.Metadata.GetProperty("versionid").GetString(), Is.EqualTo("1"));
                Assert.That(second.Metadata.GetProperty("versionid").GetString(), Is.EqualTo("2"));
                Assert.That(second.Metadata.GetProperty("ancestorid").GetString(), Is.EqualTo("1"));
                Assert.That(rejected.Error?.Code, Is.EqualTo("versionid_not_allowed"));
                Assert.That(read.Metadata.GetProperty("versionscount").GetInt32(), Is.EqualTo(2));
                Assert.That(read.Metadata.GetProperty("versionid").GetString(), Is.EqualTo("2"));
            });
        }

        [Test]
        public async Task MetaOnlyCreationAssignsAnEmptyFirstDocumentVersionAsync()
        {
            using var endpoint = XRegistryProviderCoverage.Create();
            XRegistryResponse meta = await endpoint.ExecuteAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Replace, k_resource + "/meta", """{"name":"resource metadata"}"""))
                .ConfigureAwait(false);
            XRegistryResponse document = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Read, k_resource + "/versions/1") with
                {
                    View = XRegistryView.Default
                }).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(meta.StatusCode, Is.EqualTo(201));
                Assert.That(meta.Metadata.GetProperty("schemaid").GetString(), Is.EqualTo("r"));
                Assert.That(meta.Metadata.GetProperty("name").GetString(), Is.EqualTo("resource metadata"));
                Assert.That(meta.Metadata.GetProperty("defaultversionid").GetString(), Is.EqualTo("1"));
                Assert.That(meta.Metadata.GetProperty("defaultversionsticky").GetBoolean(), Is.False);
                Assert.That(document.StatusCode, Is.EqualTo(200));
                Assert.That(document.Document.IsNull, Is.False);
                Assert.That(document.Document.Length, Is.Zero);
                Assert.That(document.Metadata.GetProperty("ancestorid").GetString(), Is.EqualTo("1"));
            });
        }

        [TestCase("""{"schema":{"kind":"record"}}""", """{"kind":"record"}""")]
        [TestCase("""{"schema":"quoted"}""", "\"quoted\"")]
        [TestCase("""{"schema":123}""", "123")]
        [TestCase("""{"schema":null}""", "")]
        [TestCase("""{"schemabase64":null}""", "")]
        public async Task InlineDocumentFormsPreserveIndependentUtf8BytesAsync(string input, string expected)
        {
            using var endpoint = XRegistryProviderCoverage.Create();
            XRegistryResponse created = await endpoint.ExecuteAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Replace, k_resource + "/versions/v1", input) with
                {
                    View = XRegistryView.Default
                }).ConfigureAwait(false);
            XRegistryResponse document = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Read, k_resource) with
                {
                    View = XRegistryView.Default
                }).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(created.StatusCode, Is.EqualTo(201));
                Assert.That(created.Location,
                    Is.EqualTo("https://registry.example/registry/groups/g/schemas/r/versions/v1"));
                Assert.That(created.ContentLocation,
                    Is.EqualTo("https://registry.example/registry/groups/g/schemas/r/versions/v1"));
                Assert.That(created.ContentType, Is.EqualTo("application/json"));
                Assert.That(created.Document.ToArray(), Is.EqualTo(Encoding.UTF8.GetBytes(expected)));
                Assert.That(document.Document.IsNull, Is.False);
                Assert.That(document.Document.ToArray(), Is.EqualTo(Encoding.UTF8.GetBytes(expected)));
                Assert.That(document.ContentType, Is.EqualTo("application/json"));
                Assert.That(document.Metadata.GetProperty("versionid").GetString(), Is.EqualTo("v1"));
            });
        }

        [TestCase("""{"schema":{},"schemabase64":"AQID"}""", "one_resource", 400)]
        [TestCase("""{"schemabase64":"invalid!"}""", "bad_request", 400)]
        [TestCase("""{"schemaurl":"https://example.test/document"}""", "action_not_supported", 405)]
        [TestCase("""{"schemaid":"other"}""", "mismatched_id", 400)]
        [TestCase("""{"versionid":"other"}""", "mismatched_id", 400)]
        public async Task InvalidDocumentFormsAndAddressIdsRollBackImplicitParentsAsync(
            string input, string code, int status)
        {
            using var endpoint = XRegistryProviderCoverage.Create();
            XRegistryResponse rejected = await endpoint.ExecuteAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Replace, k_resource + "/versions/v1", input)).ConfigureAwait(false);
            await XRegistryProviderCoverage.AssertPristineAsync(endpoint, rejected, code, status)
                .ConfigureAwait(false);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task DocumentlessTypeRejectsInlineAndRawBodiesWithoutCreatingParentsAsync(bool raw)
        {
            using var endpoint = CreateWithResource("""{"singular":"schema","hasdocument":false}""");
            XRegistryRequest request = XRegistryProviderCoverage.Request(
                XRegistryAction.Replace, k_resource, raw ? "{}" : """{"schema":null}""");
            if (raw)
            {
                request = request with { Document = ByteString.Empty };
            }
            XRegistryResponse rejected = await endpoint.ExecuteAsync(request).ConfigureAwait(false);
            await XRegistryProviderCoverage.AssertPristineAsync(endpoint, rejected, "one_resource")
                .ConfigureAwait(false);
            XRegistryResponse metadata = await endpoint.ExecuteAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Replace, k_resource, """{"versionid":"metadata-only"}""") with
                {
                    View = XRegistryView.Default
                }).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(metadata.StatusCode, Is.EqualTo(201));
                Assert.That(metadata.Document.IsNull, Is.True);
                Assert.That(metadata.Metadata.GetProperty("versionid").GetString(), Is.EqualTo("metadata-only"));
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task DocumentByteLimitAcceptsBoundaryAndRejectsTheAdjacentByteWithoutTouchingAsync(bool raw)
        {
            using var endpoint = new XRegistryTransactionalEndpoint(
                XRegistryProviderCoverage.Options() with { MaxDocumentBytes = 3 },
                new InMemoryXRegistryTransactionStore(), new XRegistryProviderCoverageTimeProvider());
            XRegistryResponse created = await endpoint.ExecuteAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Replace, k_resource + "/versions/v1", """{"schemabase64":"AAEC"}"""))
                .ConfigureAwait(false);
            XRegistryRequest oversized = XRegistryProviderCoverage.Request(
                XRegistryAction.Merge, k_resource + "/versions/v1",
                raw ? "{}" : """{"schemabase64":"AAECAw=="}""");
            if (raw)
            {
                oversized = oversized with { Document = ByteString.From(new byte[] { 0, 1, 2, 3 }) };
            }
            XRegistryResponse rejected = await endpoint.ExecuteAsync(oversized).ConfigureAwait(false);
            XRegistryResponse actual = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Read, k_resource) with
                {
                    View = XRegistryView.Default
                }).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(created.StatusCode, Is.EqualTo(201));
                Assert.That(rejected.StatusCode, Is.EqualTo(413));
                Assert.That(rejected.Error?.Code, Is.EqualTo("bad_request"));
                Assert.That(actual.Document.ToArray(), Is.EqualTo(new byte[] { 0, 1, 2 }));
                Assert.That(actual.Metadata.GetProperty("epoch").GetInt32(), Is.Zero);
                Assert.That(actual.Metadata.GetProperty("versionscount").GetInt32(), Is.EqualTo(1));
            });
        }

        [Test]
        public async Task RawDocumentWriteUsesCallerContentTypeAndPreservesUnspecifiedMetadataAsync()
        {
            using var endpoint = XRegistryProviderCoverage.Create();
            XRegistryResponse created = await endpoint.ExecuteAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Replace, k_resource + "/versions/v1",
                """{"name":"retained","schemabase64":"AQ==","contenttype":"application/old"}"""))
                .ConfigureAwait(false);
            XRegistryResponse changed = await endpoint.ExecuteAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Replace, k_resource + "/versions/v1", """{"contenttype":"application/metadata"}""") with
                {
                    Document = ByteString.From(new byte[] { 0, 255, 1, 2 }),
                    ContentType = "application/octet-stream",
                    View = XRegistryView.Default
                }).ConfigureAwait(false);
            XRegistryResponse meta = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Read, k_resource + "/meta")).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(created.StatusCode, Is.EqualTo(201));
                Assert.That(changed.StatusCode, Is.EqualTo(200));
                Assert.That(changed.Document.ToArray(), Is.EqualTo(new byte[] { 0, 255, 1, 2 }));
                Assert.That(changed.ContentType, Is.EqualTo("application/octet-stream"));
                Assert.That(changed.Metadata.GetProperty("contenttype").GetString(),
                    Is.EqualTo("application/octet-stream"));
                Assert.That(changed.Metadata.GetProperty("name").GetString(), Is.EqualTo("retained"));
                Assert.That(changed.Metadata.GetProperty("epoch").GetInt32(), Is.EqualTo(1));
                Assert.That(meta.Metadata.GetProperty("epoch").GetInt32(), Is.Zero);
            });
        }

        [Test]
        public async Task OmittedDocumentPatchPreservesBytesWhileNullFormClearsThemAsync()
        {
            using var endpoint = XRegistryProviderCoverage.Create();
            XRegistryResponse created = await endpoint.ExecuteAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Replace, k_resource + "/versions/v1",
                """{"schemabase64":"AQID","contenttype":"application/custom"}""")).ConfigureAwait(false);
            XRegistryResponse patch = await endpoint.ExecuteAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Merge, k_resource + "/versions/v1", """{"name":"metadata only"}"""))
                .ConfigureAwait(false);
            XRegistryResponse kept = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Read, k_resource) with
                {
                    View = XRegistryView.Default
                }).ConfigureAwait(false);
            XRegistryResponse cleared = await endpoint.ExecuteAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Merge, k_resource + "/versions/v1", """{"schema":null}""")).ConfigureAwait(false);
            XRegistryResponse empty = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Read, k_resource) with
                {
                    View = XRegistryView.Default
                }).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(created.StatusCode, Is.EqualTo(201));
                Assert.That(patch.StatusCode, Is.EqualTo(200));
                Assert.That(kept.Document.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));
                Assert.That(kept.ContentType, Is.EqualTo("application/custom"));
                Assert.That(cleared.StatusCode, Is.EqualTo(200));
                Assert.That(empty.Document.IsNull, Is.False);
                Assert.That(empty.Document.Length, Is.Zero);
                Assert.That(empty.ContentType, Is.EqualTo("application/custom"));
                Assert.That(empty.Metadata.GetProperty("name").GetString(), Is.EqualTo("metadata only"));
                Assert.That(empty.Metadata.GetProperty("epoch").GetInt32(), Is.EqualTo(2));
            });
        }

        [TestCase("manual", "a", "b", "b")]
        [TestCase("createdat", "b", "a", "a")]
        [TestCase("modifiedat", "a", "b", "b")]
        [TestCase("CREATEDAT", "b", "a", "a")]
        public async Task OrderingModeSelectsIndependentDefaultAndAncestryAsync(
            string mode, string selected, string firstAncestor, string secondAncestor)
        {
            using var endpoint = CreateWithResource(
                $$"""{"singular":"schema","hasdocument":false,"versionmode":"{{mode}}"}""");
            XRegistryResponse created = await endpoint.ExecuteAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Replace, k_resource,
                """
                {"versions":{
                "a":{"createdat":"2026-01-01T00:00:00Z","modifiedat":"2026-01-03T00:00:00Z","ancestorid":"b"},
                "b":{"createdat":"2026-01-02T00:00:00Z","modifiedat":"2026-01-01T00:00:00Z","ancestorid":"b"}}}
                """)).ConfigureAwait(false);
            XRegistryResponse versions = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Read, k_resource + "/versions"))
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(created.StatusCode, Is.EqualTo(201));
                Assert.That(created.Metadata.GetProperty("versionid").GetString(), Is.EqualTo(selected));
                Assert.That(versions.Metadata.GetProperty("a").GetProperty("ancestorid").GetString(),
                    Is.EqualTo(firstAncestor));
                Assert.That(versions.Metadata.GetProperty("b").GetProperty("ancestorid").GetString(),
                    Is.EqualTo(secondAncestor));
                Assert.That(versions.Metadata.GetProperty("a").GetProperty("createdat").GetString(),
                    Is.EqualTo("2026-01-01T00:00:00.0000000Z"));
            });
        }

        [TestCase("createdat", "b")]
        [TestCase("modifiedat", "a")]
        public async Task UpdatingModifiedAtMovesDefaultOnlyForModifiedOrderingAsync(string mode, string expected)
        {
            using var endpoint = CreateWithResource(
                $$"""{"singular":"schema","hasdocument":false,"versionmode":"{{mode}}"}""");
            XRegistryResponse created = await endpoint.ExecuteAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Replace, k_resource,
                """
                {"versions":{
                "a":{"createdat":"2026-01-01T00:00:00Z","modifiedat":"2026-01-01T00:00:00Z"},
                "b":{"createdat":"2026-01-02T00:00:00Z","modifiedat":"2026-01-02T00:00:00Z"}}}
                """)).ConfigureAwait(false);
            XRegistryResponse changed = await endpoint.ExecuteAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Merge, k_resource + "/versions/a", """{"modifiedat":"2026-01-03T00:00:00Z"}"""))
                .ConfigureAwait(false);
            XRegistryResponse resource = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Read, k_resource)).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(created.Metadata.GetProperty("versionid").GetString(), Is.EqualTo("b"));
                Assert.That(changed.StatusCode, Is.EqualTo(200));
                Assert.That(changed.Metadata.GetProperty("epoch").GetInt32(), Is.EqualTo(1));
                Assert.That(changed.Metadata.GetProperty("createdat").GetString(),
                    Is.EqualTo("2026-01-01T00:00:00.0000000Z"));
                Assert.That(resource.Metadata.GetProperty("versionid").GetString(), Is.EqualTo(expected));
                Assert.That(resource.Metadata.GetProperty("versionscount").GetInt32(), Is.EqualTo(2));
            });
        }

        [Test]
        public async Task SingleVersionRetentionPrunesOldVersionAndRerootsTheSurvivorAsync()
        {
            using var endpoint = CreateWithResource("""{"singular":"schema","hasdocument":false,"maxversions":1}""");
            XRegistryResponse first = await endpoint.ExecuteAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Replace, k_resource + "/versions/a", "{}")).ConfigureAwait(false);
            XRegistryResponse next = await endpoint.ExecuteAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Replace, k_resource + "/versions/b", "{}")).ConfigureAwait(false);
            XRegistryResponse old = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Read, k_resource + "/versions/a"))
                .ConfigureAwait(false);
            XRegistryResponse resource = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Read, k_resource)).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(first.StatusCode, Is.EqualTo(201));
                Assert.That(next.StatusCode, Is.EqualTo(201), next.Error?.Code + ": " + next.Error?.Detail);
                Assert.That(old.StatusCode, Is.EqualTo(404));
                Assert.That(resource.Metadata.GetProperty("versionid").GetString(), Is.EqualTo("b"));
                Assert.That(resource.Metadata.GetProperty("ancestorid").GetString(), Is.EqualTo("b"));
                Assert.That(resource.Metadata.GetProperty("versionscount").GetInt32(), Is.EqualTo(1));
            });
        }

        [Test]
        public async Task SingleVersionRetentionRejectsAStickyDefaultWithoutChangingMetaAsync()
        {
            using var endpoint = CreateWithResource("""{"singular":"schema","hasdocument":false,"maxversions":1}""");
            XRegistryResponse created = await endpoint.ExecuteAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Replace, k_resource + "/versions/a", "{}")).ConfigureAwait(false);
            XRegistryResponse rejected = await endpoint.ExecuteAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Merge, k_resource + "/meta", """{"defaultversionid":"a"}""")).ConfigureAwait(false);
            XRegistryResponse meta = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Read, k_resource + "/meta")).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(created.StatusCode, Is.EqualTo(201));
                Assert.That(rejected.Error?.Code, Is.EqualTo("setdefaultversionsticky_false"));
                Assert.That(meta.Metadata.GetProperty("defaultversionid").GetString(), Is.EqualTo("a"));
                Assert.That(meta.Metadata.GetProperty("defaultversionsticky").GetBoolean(), Is.False);
                Assert.That(meta.Metadata.GetProperty("epoch").GetInt32(), Is.Zero);
            });
        }

        [TestCase("""{"versions":{"a":{"ancestorid":"missing"}}}""", "invalid_ancestor")]
        [TestCase("""{"versions":{"a":{"ancestorid":"b"},"b":{"ancestorid":"a"}}}""", "invalid_ancestor")]
        [TestCase("""{"versions":{"a":{"ancestorid":"a"},"b":{"ancestorid":"b"}}}""", "multiple_roots")]
        public async Task InvalidManualAncestryRejectsTheEntireResourceAsync(string input, string code)
        {
            using var endpoint = CreateWithResource(
                """{"singular":"schema","hasdocument":false,"singleversionroot":true}""");
            XRegistryResponse rejected = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Replace, k_resource, input)).ConfigureAwait(false);
            await XRegistryProviderCoverage.AssertPristineAsync(endpoint, rejected, code).ConfigureAwait(false);
        }

        [TestCase("""{"defaultversionid":"missing"}""", "invalid_defaultversionid", 400)]
        [TestCase("""{"defaultversionsticky":"true"}""", "invalid_attribute", 400)]
        [TestCase("""{"xref":"/groups/other/schemas/r"}""", "action_not_supported", 405)]
        [TestCase("""{"compatibility":"strict"}""", "action_not_supported", 405)]
        public async Task InvalidMetaSelectionAndUnsupportedFeaturesCannotCreateAResourceAsync(
            string input, string code, int status)
        {
            using var endpoint = XRegistryProviderCoverage.Create();
            XRegistryResponse rejected = await endpoint.ExecuteAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Replace, k_resource + "/meta", input)).ConfigureAwait(false);
            await XRegistryProviderCoverage.AssertPristineAsync(endpoint, rejected, code, status)
                .ConfigureAwait(false);
        }

        [TestCase("""{"settings":{"format":"xml"}}""")]
        [TestCase("{}")]
        public async Task NestedMatchVersionsRejectsDifferencesAndMissingValuesWithoutChangingExistingVersionsAsync(
            string invalid)
        {
            using var endpoint = CreateWithResource("""
                {"singular":"schema","hasdocument":false,"attributes":{
                "settings":{"type":"object","attributes":{"format":{"type":"string","matchversions":true}}},
                "local":{"type":"string"}}}
                """);
            XRegistryResponse created = await endpoint.ExecuteAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Replace, k_resource,
                """
                {"versions":{"a":{"settings":{"format":"json"},"local":"first"},
                "b":{"settings":{"format":"json"},"local":"second"}}}
                """)).ConfigureAwait(false);
            XRegistryResponse rejected = await endpoint.ExecuteAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Replace, k_resource + "/versions/c", invalid)).ConfigureAwait(false);
            XRegistryResponse resource = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Read, k_resource)).ConfigureAwait(false);
            XRegistryResponse missing = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Read, k_resource + "/versions/c"))
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(created.StatusCode, Is.EqualTo(201));
                Assert.That(rejected.Error?.Code, Is.EqualTo("mismatched_version_attribute"));
                Assert.That(missing.StatusCode, Is.EqualTo(404));
                Assert.That(resource.Metadata.GetProperty("versionid").GetString(), Is.EqualTo("b"));
                Assert.That(resource.Metadata.GetProperty("versionscount").GetInt32(), Is.EqualTo(2));
                Assert.That(resource.Metadata.GetProperty("local").GetString(), Is.EqualTo("second"));
                Assert.That(resource.Metadata.GetProperty("settings").GetProperty("format").GetString(),
                    Is.EqualTo("json"));
                Assert.That(resource.Metadata.GetProperty("epoch").GetInt32(), Is.Zero);
            });
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task ModelAndInstanceGroupConstraintsEnforceEnumsAndEqualityAtomicallyAsync(
            bool instanceConstraint, bool invalidOwner)
        {
            JsonObject model = JsonNode.Parse("""
                {"groups":{"groups":{"singular":"group","attributes":{"tenant":{"type":"string"}},
                "resources":{"schemas":{"singular":"schema","hasdocument":false,"attributes":{
                "owner":{"type":"string"},"settings":{"type":"object","attributes":{"format":{"type":"string"}}}}}}}}}
                """)!.AsObject();
            JsonObject constraints = JsonNode.Parse("""
                {"schemas":{"owner":{"equals":"tenant"},"settings.format":{"enum":["json"]}}}
                """)!.AsObject();
            var groupInput = new JsonObject { ["tenant"] = "tenant-a" };
            if (instanceConstraint)
            {
                groupInput["constraints"] = constraints;
            }
            else
            {
                model["groups"]!["groups"]!["constraints"] = constraints;
            }
            using var endpoint = XRegistryProviderCoverage.Create(model.ToJsonString());
            XRegistryResponse group = await endpoint.ExecuteAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Replace, "/groups/g", groupInput.ToJsonString())).ConfigureAwait(false);
            XRegistryResponse created = await endpoint.ExecuteAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Replace, k_resource + "/versions/a",
                """{"owner":"tenant-a","settings":{"format":"json"}}""")).ConfigureAwait(false);
            string invalid = invalidOwner
                ? """{"owner":"different","settings":{"format":"json"}}"""
                : """{"owner":"tenant-a","settings":{"format":"xml"}}""";
            XRegistryResponse rejected = await endpoint.ExecuteAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Replace, k_resource + "/versions/b", invalid)).ConfigureAwait(false);
            XRegistryResponse resource = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Read, k_resource)).ConfigureAwait(false);
            XRegistryResponse missing = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Read, k_resource + "/versions/b"))
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(group.StatusCode, Is.EqualTo(201));
                Assert.That(created.StatusCode, Is.EqualTo(201));
                Assert.That(rejected.Error?.Code, Is.EqualTo("invalid_attribute"));
                Assert.That(missing.StatusCode, Is.EqualTo(404));
                Assert.That(resource.Metadata.GetProperty("versionscount").GetInt32(), Is.EqualTo(1));
                Assert.That(resource.Metadata.GetProperty("owner").GetString(), Is.EqualTo("tenant-a"));
                Assert.That(resource.Metadata.GetProperty("settings").GetProperty("format").GetString(),
                    Is.EqualTo("json"));
                Assert.That(resource.Metadata.GetProperty("epoch").GetInt32(), Is.Zero);
            });
        }

        private static XRegistryTransactionalEndpoint CreateWithResource(string definition)
        {
            return XRegistryProviderCoverage.Create(
                """{"groups":{"groups":{"singular":"group","resources":{"schemas":""" + definition + "}}}}");
        }

        private const string k_resource = "/groups/g/schemas/r";
    }
}
