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

using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.XRegistry.Protocol;
using Opc.Ua.XRegistry.Server.Protocol;
using static Opc.Ua.XRegistry.Tests.ProtocolProvider.XRegistryProviderCoverage;

namespace Opc.Ua.XRegistry.Tests.ProtocolProvider
{
    [TestFixture]
    public sealed class XRegistryDocumentViewTests
    {
        [Test]
        public async Task DocumentViewProjectsPointersAndOmitsDefaultVersionDuplicationAsync()
        {
            using XRegistryTransactionalEndpoint endpoint = Create();
            XRegistryResponse created = await endpoint.ExecuteAsync(Request(XRegistryAction.Replace, k_resource,
                /*lang=json,strict*/ """{"versions":{"v1":{"schema":{"value":1}},"v2":{"schema":{"value":2}}}}"""))
                .ConfigureAwait(false);
            Assert.That(created.StatusCode, Is.EqualTo(201), created.Error?.Detail);
            XRegistryResponse response = await endpoint.ExecuteAsync(Request(XRegistryAction.Read, "/") with
            { Parameters = [new("doc", null), new("inline", "*")] }).ConfigureAwait(false);
            System.Text.Json.JsonElement resource = response.Metadata.GetProperty("groups").GetProperty("g")
                .GetProperty("schemas").GetProperty("r");
            Assert.Multiple(() =>
            {
                Assert.That(response.Document.IsNull, Is.True);
                Assert.That(response.Metadata.GetProperty("self").GetString(), Is.EqualTo("#/"));
                Assert.That(response.Metadata.GetProperty("groupsurl").GetString(), Is.EqualTo("#/groups"));
                Assert.That(resource.GetProperty("self").GetString(), Is.EqualTo("#/groups/g/schemas/r"));
                Assert.That(resource.TryGetProperty("versionid", out _), Is.False);
                Assert.That(resource.TryGetProperty("schema", out _), Is.False);
                Assert.That(resource.GetProperty("metaurl").GetString(), Is.EqualTo("#/groups/g/schemas/r/meta"));
                Assert.That(resource.GetProperty("meta").GetProperty("defaultversionurl").GetString(),
                    Is.EqualTo("#/groups/g/schemas/r/versions/v2"));
                Assert.That(resource.GetProperty("versions").GetProperty("v1").GetProperty("schema")
                    .GetProperty("value").GetInt32(), Is.EqualTo(1));
            });
        }

        [Test]
        public async Task NonInlinedDocumentTargetsKeepAbsoluteLinksAndNoSyntheticContentAsync()
        {
            using XRegistryTransactionalEndpoint endpoint = Create();
            _ = await endpoint.ExecuteAsync(Request(XRegistryAction.Replace, k_resource,
                /*lang=json,strict*/ """{"versionid":"v1","schema":{"value":1}}""")).ConfigureAwait(false);
            XRegistryResponse response = await endpoint.ExecuteAsync(Request(XRegistryAction.Read, k_resource) with
            { View = XRegistryView.Default, Parameters = [new("doc", null), new("inline", "meta")] }).ConfigureAwait(
                false);
            XRegistryResponse version =
                await endpoint.ExecuteAsync(Request(XRegistryAction.Read, k_resource + "/versions/v1") with
                { View = XRegistryView.Default, Parameters = [new("doc", null)] }).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(200), response.Error?.Detail);
                Assert.That(response.Document.IsNull, Is.True);
                Assert.That(response.Metadata.GetProperty("self").GetString(), Is.EqualTo("#/"));
                Assert.That(
                    response.Metadata.GetProperty("meta").GetProperty("self").GetString(), Is.EqualTo("#/meta"));
                Assert.That(response.Metadata.GetProperty("meta").GetProperty("defaultversionurl").GetString(),
                    Is.EqualTo("https://registry.example/registry/groups/g/schemas/r/versions/v1"));
                Assert.That(version.Document.IsNull, Is.True);
                Assert.That(version.Metadata.TryGetProperty("schema", out _), Is.False);
                Assert.That(version.Metadata.GetProperty("self").GetString(), Is.EqualTo("#/"));
            });
        }

        [Test]
        public async Task CollectionsFlagImplicitlyInlinesNestedVersionsWithoutRootAttributesAsync()
        {
            using XRegistryTransactionalEndpoint endpoint = Create();
            _ = await endpoint.ExecuteAsync(Request(XRegistryAction.Replace, k_resource,
                /*lang=json,strict*/ """{"versionid":"v1","schema":{"value":1}}""")).ConfigureAwait(false);
            XRegistryResponse response = await endpoint.ExecuteAsync(Request(XRegistryAction.Read, "/") with
            { Parameters = [new("collections", null)] }).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(response.Metadata.EnumerateObject().Count(), Is.EqualTo(1));
                Assert.That(response.Metadata.GetProperty("groups").GetProperty("g").GetProperty("schemas")
                    .GetProperty("r").GetProperty("versions").GetProperty("v1").GetProperty("schema").GetProperty(
                        "value")
                    .GetInt32(), Is.EqualTo(1));
            });
        }

        [Test]
        public async Task DocumentAliasContainsOnlyReferenceIdentityAndRejectsDirectVersionDocAccessAsync()
        {
            using XRegistryTransactionalEndpoint endpoint = Create();
            _ = await endpoint.ExecuteAsync(Request(XRegistryAction.Replace, k_resource,
                /*lang=json,strict*/ """{"versionid":"v1","schema":{}}""")).ConfigureAwait(false);
            _ = await endpoint.ExecuteAsync(Request(XRegistryAction.Replace, "/groups/g/schemas/alias",
                /*lang=json,strict*/ """{"meta":{"xref":"/groups/g/schemas/r"}}""")).ConfigureAwait(false);
            XRegistryResponse alias =
                await endpoint.ExecuteAsync(Request(XRegistryAction.Read, "/groups/g/schemas/alias") with
                { Parameters = [new("doc", null), new("inline", "*")] }).ConfigureAwait(false);
            XRegistryResponse versions = await endpoint.ExecuteAsync(
                Request(XRegistryAction.Read, "/groups/g/schemas/alias/versions") with
                { Parameters = [new("doc", null)] }).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(alias.StatusCode, Is.EqualTo(200), alias.Error?.Detail);
                Assert.That(alias.Metadata.TryGetProperty("versions", out _), Is.False);
                Assert.That(alias.Metadata.TryGetProperty("versionid", out _), Is.False);
                Assert.That(alias.Metadata.GetProperty("meta").TryGetProperty("epoch", out _), Is.False);
                Assert.That(alias.Metadata.GetProperty("meta").GetProperty("xref").GetString(), Is.EqualTo(k_resource));
                Assert.That(versions.Error?.Code, Is.EqualTo("cannot_doc_xref"));
            });
        }

        private const string k_resource = "/groups/g/schemas/r";
    }
}
