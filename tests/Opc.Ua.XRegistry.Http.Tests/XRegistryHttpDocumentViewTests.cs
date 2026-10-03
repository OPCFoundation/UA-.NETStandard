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
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.XRegistry.Protocol;
#if XREGISTRY_HTTP_MODERN
using Opc.Ua.XRegistry.Server.Protocol;
#endif

namespace Opc.Ua.XRegistry.Http.Tests
{
    [TestFixture]
    public sealed class XRegistryHttpDocumentViewTests
    {
        [Test]
        public async Task PagingCountAndExpirationSurviveHttpAndNativeEnvelopeRoundtripsAsync()
        {
            using var handler = new RecordingHttpHandler(_ =>
            {
                HttpResponseMessage response = HttpTestData.JsonResponse("{}");
                response.Headers.TryAddWithoutValidation("Link",
                    "<https://registry.example/registry/model?cursor=next>;rel=next;count=18446744073709551615");
                response.Content.Headers.Expires = new DateTimeOffset(2026, 9, 12, 18, 0, 0, TimeSpan.Zero);
                return response;
            });
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot);
            XRegistryResponse response =
                await endpoint.ExecuteAsync(new XRegistryRequest(XRegistryAction.Read, "/model"))
                .ConfigureAwait(false);
            var codec = new XRegistryProtocolCodec();
            XRegistryResponse decoded = codec.DecodeResponse(codec.EncodeResponse(response));
            Assert.Multiple(() =>
            {
                Assert.That(decoded.Links[0].Count, Is.EqualTo(ulong.MaxValue));
                Assert.That(decoded.Links[0].Target, Is.EqualTo("/model?cursor=next"));
                Assert.That(decoded.Expires, Is.EqualTo(new DateTimeOffset(2026, 9, 12, 18, 0, 0, TimeSpan.Zero)));
            });
        }

        [TestCase("\"count\":-1")]
        [TestCase("\"count\":\"2\"")]
        [TestCase("\"count\":18446744073709551616")]
        public void MalformedNativePagingMetadataIsRejected(string count)
        {
            string body = "{\"format\":1,\"status\":200,\"links\":[{\"relation\":\"next\",\"target\":\"/model\"," +
                count +
                "}]}";
            Assert.Throws<JsonException>(() =>
                new XRegistryProtocolCodec().DecodeResponse(ByteString.From(Encoding.UTF8.GetBytes(body))));
        }

        [TestCase("-1")]
        [TestCase("18446744073709551616")]
        [TestCase("2;count=3")]
        public void MalformedHttpPaginationCountIsRejected(string count)
        {
            using var handler = new RecordingHttpHandler(_ =>
            {
                HttpResponseMessage response = HttpTestData.JsonResponse("{}");
                response.Headers.TryAddWithoutValidation(
                    "Link", "</registry/model?cursor=next>;rel=next;count=" + count);
                return response;
            });
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot);
            Assert.ThrowsAsync<InvalidDataException>(async () =>
                await endpoint.ExecuteAsync(new XRegistryRequest(XRegistryAction.Read, "/model")).ConfigureAwait(
                    false));
        }

        [TestCase("0")]
        [TestCase("-1")]
        [TestCase("2099-01-01")]
        public async Task InvalidHttpExpirationIsInterpretedAsAlreadyExpiredAsync(string value)
        {
            using var handler = new RecordingHttpHandler(_ =>
            {
                HttpResponseMessage response = HttpTestData.JsonResponse("{}");
                response.Content.Headers.TryAddWithoutValidation("Expires", value);
                return response;
            });
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot);
            XRegistryResponse response =
                await endpoint.ExecuteAsync(new XRegistryRequest(XRegistryAction.Read, "/model"))
                .ConfigureAwait(false);
            Assert.That(response.Expires, Is.LessThanOrEqualTo(DateTimeOffset.FromUnixTimeSeconds(0)));
        }

        [Test]
        public async Task DocReadsMetadataAndPreservesInternalPointersWithoutFollowingThemAsync()
        {
            using var handler = new RecordingHttpHandler(message =>
                message.RequestUri!.AbsolutePath == "/registry/model"
                    ? HttpTestData.JsonResponse(HttpTestData.Model)
                    : HttpTestData.JsonResponse(/*lang=json,strict*/ """
                        {"schemaid":"r","self":"#/","xid":"/schemagroups/g/schemas/r","metaurl":"#/meta",
                         "meta":{"schemaid":"r","self":"#/meta","epoch":1,
                           "defaultversionurl":
                             "https://registry.example/registry/schemagroups/g/schemas/r/versions/v1$details"}}
                        """));
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot);
            XRegistryResponse response = await endpoint.ExecuteAsync(
                new XRegistryRequest(XRegistryAction.Read, HttpTestData.ResourcePath)
                { Parameters = [new XRegistryParameter("doc", null)] }).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(response.Document.IsNull, Is.True);
                Assert.That(response.Metadata.GetProperty("self").GetString(), Is.EqualTo("#/"));
                Assert.That(response.Metadata.GetProperty("metaurl").GetString(), Is.EqualTo("#/meta"));
                Assert.That(response.Metadata.GetProperty("meta").GetProperty("defaultversionurl").GetString(),
                    Is.EqualTo("/schemagroups/g/schemas/r/versions/v1$details"));
                Assert.That(handler.Requests, Has.Count.EqualTo(2));
            });
        }

#if XREGISTRY_HTTP_MODERN
        [Test]
        public async Task OpaqueDocumentViewContinuationKeepsLocalPointersThroughHttpHostingAsync()
        {
            using var provider = new XRegistryTransactionalEndpoint(new XRegistryTransactionalOptions
            {
                Model = HttpTestData.Json(
                    /*lang=json,strict*/ """{"groups":{"groups":{"singular":"group","resources":{}}}}"""),
                PublicRoot = HttpHostTestData.PublicRoot,
                PageSize = 1
            }, new InMemoryXRegistryTransactionStore());
            XRegistryResponse seeded = await provider.ExecuteAsync(new XRegistryRequest(XRegistryAction.Merge, "/")
            {
                Context = new XRegistryCallContext("writer") { IsAuthenticated = true, Roles = ["xregistry.write"] },
                Metadata = HttpTestData.Json(
                    /*lang=json,strict*/ """{"groups":{"a":{"name":"first"},"b":{"name":"second"}}}""")
            }).ConfigureAwait(false);
            Assert.That(seeded.StatusCode, Is.EqualTo(200), seeded.Error?.Detail);
            await using HttpRouteTestHost host = await HttpRouteTestHost.StartAsync(
                provider, HttpHostTestData.OpenOptions)
                .ConfigureAwait(false);
            using HttpResponseMessage first = await host.Client.GetAsync(
                new Uri("/registry/groups?doc", UriKind.Relative)).ConfigureAwait(false);
            string link = first.Headers.GetValues("Link").Single(
                value => value.Contains("rel=\"next\"", StringComparison.Ordinal));
            var target = new Uri(link[1..link.IndexOf('>', StringComparison.Ordinal)]);
            Assert.That(target.Query, Does.Not.Contain("doc"));
            using HttpResponseMessage second = await host.Client.GetAsync(target).ConfigureAwait(false);
            string body = await second.Content.ReadAsStringAsync().ConfigureAwait(false);
            Assert.That(second.StatusCode, Is.EqualTo(System.Net.HttpStatusCode.OK), body);
            using JsonDocument json = JsonDocument.Parse(body);
            Assert.Multiple(() =>
            {
                Assert.That(json.RootElement.GetProperty("b").GetProperty("name").GetString(), Is.EqualTo("second"));
                Assert.That(json.RootElement.GetProperty("b").GetProperty("self").GetString(), Is.EqualTo("#/b"));
            });
        }

        [Test]
        public async Task OptionalHttpFacilitiesNeverInventValidatorsOrPartialContentAsync()
        {
            var backend = new FakeRegistryEndpoint
            {
                Response = new XRegistryResponse(200)
                {
                    Metadata = HttpTestData.Json("{}"),
                    Document = ByteString.From([0x41, 0x42, 0x43]),
                    ContentType = "text/plain"
                }
            };
            await using HttpRouteTestHost host =
                await HttpRouteTestHost.StartAsync(backend, HttpHostTestData.OpenOptions)
                .ConfigureAwait(false);
            using var request = new HttpRequestMessage(HttpMethod.Get,
                new Uri("/registry" + HttpTestData.ResourcePath, UriKind.Relative));
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 0);
            using HttpResponseMessage response = await host.Client.SendAsync(request).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(System.Net.HttpStatusCode.OK));
                Assert.That(response.Headers.ETag, Is.Null);
                Assert.That(response.Content.Headers.ContentRange, Is.Null);
                Assert.That(response.Content.Headers.ContentLength, Is.EqualTo(3));
                Assert.That(response.Content.Headers.ContentEncoding, Is.Empty);
            });
            Assert.That(await response.Content.ReadAsStringAsync().ConfigureAwait(false), Is.EqualTo("ABC"));
        }

        [Test]
        public async Task HostedDocResponseUsesJsonRatherThanDomainHeadersAsync()
        {
            var backend = new FakeRegistryEndpoint
            {
                Response = new XRegistryResponse(200)
                {
                    Metadata = HttpTestData.Json(/*lang=json,strict*/ """
                        {"schemaid":"r","self":"#/","xid":"/schemagroups/g/schemas/r",
                         "versionsurl":"/schemagroups/g/schemas/r/versions"}
                        """),
                    Expires = new DateTimeOffset(2026, 9, 12, 18, 0, 0, TimeSpan.Zero),
                    Links = [new XRegistryLink("next", "/schemagroups/g/schemas?cursor=opaque") { Count = 42 }]
                }
            };
            await using HttpRouteTestHost host =
                await HttpRouteTestHost.StartAsync(backend, HttpHostTestData.OpenOptions)
                .ConfigureAwait(false);
            using HttpResponseMessage response = await host.Client.GetAsync(
                new Uri("/registry" + HttpTestData.ResourcePath + "?doc", UriKind.Relative)).ConfigureAwait(false);
            using JsonDocument body = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync().ConfigureAwait(false));
            Assert.Multiple(() =>
            {
                Assert.That(response.Content.Headers.ContentType!.MediaType, Is.EqualTo("application/json"));
                Assert.That(response.Headers.Contains("xRegistry-schemaid"), Is.False);
                Assert.That(body.RootElement.GetProperty("self").GetString(), Is.EqualTo("#/"));
                Assert.That(body.RootElement.GetProperty("versionsurl").GetString(),
                    Is.EqualTo("https://public.example/registry/schemagroups/g/schemas/r/versions"));
                Assert.That(response.Headers.GetValues("Link"),
                    Does.Contain(
                        "<https://public.example/registry/schemagroups/g/schemas?cursor=opaque>" +
                        ";rel=\"next\";count=42"));
                Assert.That(response.Content.Headers.Expires,
                    Is.EqualTo(new DateTimeOffset(2026, 9, 12, 18, 0, 0, TimeSpan.Zero)));
            });
        }
#endif
    }
}
