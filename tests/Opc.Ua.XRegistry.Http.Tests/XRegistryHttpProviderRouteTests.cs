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

#if XREGISTRY_HTTP_MODERN
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.XRegistry.Protocol;
using Opc.Ua.XRegistry.Server.Protocol;

namespace Opc.Ua.XRegistry.Http.Tests
{
    [TestFixture]
    public sealed class XRegistryHttpProviderRouteTests
    {
        [TestCaseSource(nameof(Routes))]
        public async Task ProviderRoutesHaveIndependentLiteralWireAndPersistenceOutcomesAsync(
            string method, string path, string? input, int status, string field, string expected)
        {
            using var endpoint = new XRegistryTransactionalEndpoint(new XRegistryTransactionalOptions
            {
                RegistryId = "matrix",
                Model = HttpTestData.Json(k_model),
                PublicRoot = HttpHostTestData.PublicRoot,
                DiscoveryRegistries = [HttpHostTestData.PublicRoot]
            }, new InMemoryXRegistryTransactionStore());
            XRegistryResponse seeded = await endpoint.ExecuteAsync(new XRegistryRequest(XRegistryAction.Merge, "/")
            {
                Context = s_writer,
                Metadata = HttpTestData.Json(/*lang=json,strict*/ """
                    {"name":"registry","schemagroups":{"g":{"name":"group","schemas":{"r":{
                      "versions":{"v1":{"name":"first","schema":"one"},"v2":{"name":"second","schema":"two"}},
                      "meta":{"defaultversionid":"v1","defaultversionsticky":true}
                    }}}}}
                    """)
            }).ConfigureAwait(false);
            Assert.That(seeded.StatusCode, Is.EqualTo(200), seeded.Error?.Detail);
            string? generation = (await endpoint.InspectAsync(s_writer).ConfigureAwait(false)).Generation;
            await using HttpRouteTestHost host = await HttpRouteTestHost.StartAsync(endpoint,
                HttpHostTestData.OpenOptions with
                {
                    CreateContextAsync = (_, _) => new ValueTask<XRegistryCallContext>(s_writer)
                }).ConfigureAwait(false);
            using var request = new HttpRequestMessage(new HttpMethod(method),
                new Uri("/registry" + path, UriKind.Relative));
            if (input is not null)
            {
                request.Content = new StringContent(input, Encoding.UTF8, "application/json");
            }
            using HttpResponseMessage response = await host.Client.SendAsync(request).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            Assert.That((int)response.StatusCode, Is.EqualTo(status), body);
            if (status == 204)
            {
                Assert.That(body, Is.Empty);
                XRegistryResponse deleted = await endpoint.ExecuteAsync(
                    new XRegistryRequest(XRegistryAction.Read, expected)).ConfigureAwait(false);
                Assert.That(deleted.StatusCode, Is.EqualTo(404));
                return;
            }
            Assert.That(response.Content.Headers.ContentType!.MediaType, Is.EqualTo("application/json"));
            using var json = JsonDocument.Parse(body);
            AssertValue(json.RootElement, field, expected);
            if (status >= 400)
            {
                XRegistryResponse unchanged = await endpoint.ExecuteAsync(
                    new XRegistryRequest(XRegistryAction.Read, "/")).ConfigureAwait(false);
                Assert.That(unchanged.Generation, Is.EqualTo(generation));
                return;
            }
            if (method == "POST" && path is "/" or "/schemagroups/g")
            {
                Assert.That(json.RootElement.TryGetProperty("name", out _), Is.False,
                    "Owner POST returns only processed collections.");
                XRegistryResponse owner = await endpoint.ExecuteAsync(
                    new XRegistryRequest(XRegistryAction.Read, path)).ConfigureAwait(false);
                AssertValue(owner.Metadata, "name", path == "/" ? "\"registry\"" : "\"group\"");
            }
            else if (method is "PUT" or "PATCH")
            {
                string target = path.EndsWith("$details", StringComparison.Ordinal) ? path[..^8] : path;
                XRegistryResponse stored = await endpoint.ExecuteAsync(
                    new XRegistryRequest(XRegistryAction.Read, target) { View = XRegistryView.Metadata })
                    .ConfigureAwait(false);
                Assert.That(stored.StatusCode, Is.EqualTo(200), stored.Error?.Detail);
                AssertValue(stored.Metadata, field, expected);
            }
            else if (method == "POST" && path == k_resource + "$details")
            {
                XRegistryResponse meta = await endpoint.ExecuteAsync(
                    new XRegistryRequest(XRegistryAction.Read, k_resource + "/meta")).ConfigureAwait(false);
                AssertValue(meta.Metadata, "defaultversionid", "\"v1\"");
                XRegistryResponse created = await endpoint.ExecuteAsync(
                    new XRegistryRequest(XRegistryAction.Read, k_resource + "/versions/v3"))
                    .ConfigureAwait(false);
                AssertValue(created.Metadata, "name", "\"changed\"");
                Assert.That(Encoding.UTF8.GetString(created.Document.Span), Is.EqualTo("third"));
            }
        }

        private static IEnumerable<TestCaseData> Routes()
        {
            yield return Case("GET", "/", null, 200, "name", "\"registry\"");
            yield return Case("GET", "/schemagroups", null, 200, "g.name", "\"group\"");
            yield return Case("GET", "/schemagroups/g", null, 200, "name", "\"group\"");
            yield return Case("GET", "/schemagroups/g/schemas", null, 200, "r.name", "\"first\"");
            yield return Case("GET", k_resource + "$details", null, 200, "name", "\"first\"");
            yield return Case("GET", k_resource + "/meta", null, 200, "defaultversionid", "\"v1\"");
            yield return Case("GET", k_resource + "/versions", null, 200, "v2.name", "\"second\"");
            yield return Case("GET", k_resource + "/versions/v1$details", null, 200, "name", "\"first\"");
            yield return Case("GET", "/capabilities", null, 200, "specversions", "[\"1.0-rc4\"]");
            yield return Case("GET", "/capabilitiesoffered", null, 200, "specversions", "[\"1.0-rc4\"]");
            yield return Case("GET", "/model", null, 200, "groups.schemagroups.singular", "\"schemagroup\"");
            yield return Case("GET", "/modelsource", null, 200, "groups.schemagroups.singular", "\"schemagroup\"");
            yield return Case("GET", "/export", null, 200, "registryid", "\"matrix\"");
            yield return Case("GET", "/.xregistry", null, 200, "registries", "[\"https://public.example/registry/\"]");
            foreach (string method in s_updateMethods)
            {
                yield return Case(method, "/", "{\"name\":\"changed\"}", 200, "name", "\"changed\"");
                yield return Case(method, "/schemagroups/g", "{\"name\":\"changed\"}", 200, "name", "\"changed\"");
                yield return Case(method, k_resource + "$details", "{\"name\":\"changed\",\"schema\":\"fresh\"}",
                    200, "name", "\"changed\"");
                yield return Case(method, k_resource + "/versions/v1$details",
                    "{\"name\":\"changed\",\"schema\":\"fresh\"}", 200, "name", "\"changed\"");
                yield return Case(method, k_resource + "/meta",
                    "{\"defaultversionid\":\"v2\",\"defaultversionsticky\":true}",
                    200, "defaultversionid", "\"v2\"");
                yield return Case(method, "/capabilities",
                    "{\"mutable\":[\"entities\",\"modelsource\",\"capabilities\"]}",
                    405, "detail", "\"This registry aspect is read-only.\"");
            }
            foreach (string method in s_collectionMethods)
            {
                yield return Case(method, "/schemagroups", "{\"n\":{\"name\":\"changed\"}}",
                    200, "n.name", "\"changed\"");
                yield return Case(method, "/schemagroups/g/schemas",
                    "{\"n\":{\"name\":\"changed\",\"schema\":\"third\"}}", 200, "n.name", "\"changed\"");
                yield return Case(method, k_resource + "/versions",
                    "{\"v3\":{\"name\":\"changed\",\"schema\":\"third\"}}", 200, "v3.name", "\"changed\"");
            }
            yield return Case("POST", "/", "{\"schemagroups\":{\"n\":{\"name\":\"changed\"}}}",
                200, "schemagroups.n.name", "\"changed\"");
            yield return Case(
                "POST", "/schemagroups/g", "{\"schemas\":{\"n\":{\"name\":\"changed\",\"schema\":\"third\"}}}",
                200, "schemas.n.name", "\"changed\"");
            yield return Case("POST", k_resource + "$details",
                "{\"versionid\":\"v3\",\"name\":\"changed\",\"contenttype\":\"text/plain\",\"schema\":\"third\"}",
                200, "name", "\"changed\"");
            yield return Case("POST", "/schemagroups/g/schemas/new$details",
                "{\"versionid\":\"v1\",\"name\":\"changed\",\"schema\":\"new\"}", 200, "name", "\"changed\"");
            yield return Case("PUT", "/modelsource", k_model, 200, "groups.schemagroups.singular", "\"schemagroup\"");
            yield return Case("DELETE", "/schemagroups", "{\"g\":{}}", 204, string.Empty, "/schemagroups/g");
            yield return Case("DELETE", "/schemagroups/g", null, 204, string.Empty, "/schemagroups/g");
            yield return Case("DELETE", "/schemagroups/g/schemas", "{\"r\":{}}", 204, string.Empty, k_resource);
            yield return Case("DELETE", k_resource, null, 204, string.Empty, k_resource);
            yield return Case("DELETE", k_resource + "/versions", "{\"v2\":{}}", 204,
                string.Empty, k_resource + "/versions/v2");
            yield return Case("DELETE", k_resource + "/versions/v2", null, 204,
                string.Empty, k_resource + "/versions/v2");
        }

        private static TestCaseData Case(
            string method, string path, string? input, int status, string field, string expected)
        {
            return new TestCaseData(method, path, input, status, field, expected)
                .SetName("ProviderHttp" + method + path);
        }

        private static void AssertValue(JsonElement value, string path, string expected)
        {
            foreach (string segment in path.Split('.'))
            {
                value = value.GetProperty(segment);
            }
            Assert.That(value.GetRawText(), Is.EqualTo(expected), path);
        }

        private const string k_resource = "/schemagroups/g/schemas/r";

        private const string k_model = /*lang=json,strict*/ """
            {"groups":{"schemagroups":{"singular":"schemagroup","resources":{"schemas":{"singular":"schema"}}}}}
            """;

        private static readonly XRegistryCallContext s_writer =
            new("writer") { IsAuthenticated = true, Roles = ["xregistry.write"] };

        private static readonly string[] s_updateMethods = ["PUT", "PATCH"];
        private static readonly string[] s_collectionMethods = ["POST", "PATCH"];
    }
}
#endif
