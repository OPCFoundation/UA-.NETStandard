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

#if NET8_0_OR_GREATER
using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using NUnit.Framework;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Http.Tests
{
    [TestFixture]
    [Category("XRegistryHttp")]
    public sealed class XRegistryHttpHostBoundaryTests
    {
        [TestCase("PATCH", HttpTestData.ResourcePath, "raw", null, null, 405, "details_required")]
        [TestCase("PUT", "/schemagroups", "{}", null, null, 405, "action_not_supported")]
        [TestCase("PUT", "/schemagroups/g/schemas", "{}", null, null, 405, "action_not_supported")]
        [TestCase("PUT", "/schemagroups/g/schemas/r/versions", "{}", null, null, 405, "action_not_supported")]
        [TestCase("PUT", HttpTestData.ResourcePath + "$details", "{}", "xRegistry-name", "x", 400,
            "extra_xregistry_header")]
        [TestCase("PUT", HttpTestData.RecordPath, "{}", "xRegistry-name", "x", 400, "extra_xregistry_header")]
        [TestCase("PUT", HttpTestData.ResourcePath, "x", "xRegistry-contenttype", "text/plain", 400,
            "extra_xregistry_header")]
        [TestCase("PUT", HttpTestData.ResourcePath, "x", "xRegistry-schema", "inline", 400, "extra_xregistry_header")]
        [TestCase("PUT", HttpTestData.ResourcePath, "x", "xRegistry-schemabase64", "eA==", 400,
            "extra_xregistry_header")]
        [TestCase("PUT", HttpTestData.ResourcePath, "x", "xRegistry-schemaurl", "https://other.example/doc", 400,
            "invalid_attribute")]
        public async Task HostRejectsInvalidWritesBeforeExecute(
            string method, string path, string body, string? header, string? value, int status, string code)
        {
            var backend = new FakeRegistryEndpoint { Response = new XRegistryResponse(204) };
            await using HttpRouteTestHost host = await HttpRouteTestHost.StartAsync(
                backend, HttpHostTestData.OpenOptions).ConfigureAwait(false);
            using var request = new HttpRequestMessage(new HttpMethod(method), "/registry" + path)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            if (header is not null)
            {
                request.Headers.TryAddWithoutValidation(header, value);
            }

            using HttpResponseMessage response = await host.Client.SendAsync(request).ConfigureAwait(false);
            JsonElement problem = HttpTestData.Json(await response.Content.ReadAsStringAsync().ConfigureAwait(false));

            Assert.That((int)response.StatusCode, Is.EqualTo(status));
            Assert.That(problem.GetProperty("type").GetString(), Does.EndWith("#" + code));
            Assert.That(problem.GetProperty("subject").GetString(),
                Is.EqualTo(path.Replace("$details", string.Empty, StringComparison.Ordinal)));
            Assert.That(backend.Inspections, Has.Count.EqualTo(1));
            Assert.That(backend.Requests, Is.Empty);
            if (status == 405)
            {
                Assert.That(HttpHostTestData.Header(response, "Allow"), Does.Contain("OPTIONS"));
            }
        }

        [TestCase("", 400)]
        [TestCase("{}", 204)]
        [TestCase("null", 204)]
        public async Task HostMissingMetadataBodyIsDistinctFromEmptyObjectAndJsonNull(string body, int status)
        {
            var backend = new FakeRegistryEndpoint { Response = new XRegistryResponse(204) };
            await using HttpRouteTestHost host = await HttpRouteTestHost.StartAsync(
                backend, HttpHostTestData.OpenOptions).ConfigureAwait(false);
            using var content = new StringContent(body, Encoding.UTF8, "application/json");

            using HttpResponseMessage response = await host.Client.PutAsync(HttpHostTestData.ModelUri, content)
                .ConfigureAwait(false);

            Assert.That((int)response.StatusCode, Is.EqualTo(status));
            Assert.That(backend.Inspections, Has.Count.EqualTo(1));
            if (status == 204)
            {
                Assert.That(backend.Requests, Has.Count.EqualTo(1));
                Assert.That(backend.Requests[0].Metadata.GetRawText(), Is.EqualTo(body));
                Assert.That(backend.Requests[0].Document.IsNull, Is.True);
            }
            else
            {
                JsonElement problem = HttpTestData.Json(await response.Content.ReadAsStringAsync().ConfigureAwait(
                    false));
                Assert.That(problem.GetProperty("type").GetString(), Does.EndWith("#missing_body"));
                Assert.That(backend.Requests, Is.Empty);
            }
        }

        [TestCase("name", "%")]
        [TestCase("name", "%GG")]
        [TestCase("name", "%C0%A0")]
        [TestCase("name", "%ff")]
        [TestCase("name", "\"unterminated")]
        [TestCase("active", "TRUE")]
        [TestCase("epoch", "-1")]
        [TestCase("complex", "{}")]
        [TestCase("list", "[]")]
        [TestCase("labels", "{}")]
        public async Task HostMalformedDocumentHeadersReturnPinnedHeaderErrorBeforeExecute(string name, string value)
        {
            var backend = new FakeRegistryEndpoint { Response = new XRegistryResponse(204) };
            await using HttpRouteTestHost host = await HttpRouteTestHost.StartAsync(
                backend, HttpHostTestData.OpenOptions).ConfigureAwait(false);
            using var request = new HttpRequestMessage(HttpMethod.Put, "/registry" + HttpTestData.ResourcePath)
            {
                Content = new ByteArrayContent([0x41])
            };
            request.Headers.TryAddWithoutValidation("xRegistry-" + name, value);

            using HttpResponseMessage response = await host.Client.SendAsync(request).ConfigureAwait(false);
            JsonElement problem = HttpTestData.Json(await response.Content.ReadAsStringAsync().ConfigureAwait(false));

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(problem.GetProperty("type").GetString(),
                Is.EqualTo("https://github.com/xregistry/spec/blob/main/core/http.md#header_error"));
            Assert.That(backend.Requests, Is.Empty);
            Assert.That(backend.Inspections, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task HostRejectsDuplicateDocumentAttributeHeadersBeforeExecute()
        {
            var backend = new FakeRegistryEndpoint { Response = new XRegistryResponse(204) };
            await using HttpRouteTestHost host = await HttpRouteTestHost.StartAsync(
                backend, HttpHostTestData.OpenOptions).ConfigureAwait(false);
            using var request = new HttpRequestMessage(HttpMethod.Put, "/registry" + HttpTestData.ResourcePath)
            {
                Content = new ByteArrayContent([])
            };
            string[] repeatedValues = ["a", "b"];
            request.Headers.TryAddWithoutValidation("xRegistry-name", repeatedValues);

            using HttpResponseMessage response = await host.Client.SendAsync(request).ConfigureAwait(false);
            JsonElement problem = HttpTestData.Json(await response.Content.ReadAsStringAsync().ConfigureAwait(false));

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(problem.GetProperty("type").GetString(), Does.EndWith("#header_error"));
            Assert.That(backend.Requests, Is.Empty);
        }

        [Test]
        public async Task HostDecodesLegacyQuotedAndLowercasePercentEscapesExactlyOnce()
        {
            var backend = new FakeRegistryEndpoint { Response = new XRegistryResponse(204) };
            await using HttpRouteTestHost host = await HttpRouteTestHost.StartAsync(
                backend, HttpHostTestData.OpenOptions).ConfigureAwait(false);
            using var request = new HttpRequestMessage(HttpMethod.Put, "/registry" + HttpTestData.ResourcePath)
            {
                Content = new ByteArrayContent([])
            };
            request.Headers.TryAddWithoutValidation("xRegistry-name", "\"a \\\"quote\\\" %e2%82%ac %2520 %01\"");
            request.Headers.TryAddWithoutValidation("xRegistry-labels", "null");

            using HttpResponseMessage response = await host.Client.SendAsync(request).ConfigureAwait(false);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(backend.Requests, Has.Count.EqualTo(1));
            Assert.That(backend.Requests[0].Metadata.GetProperty("name").GetString(),
                Is.EqualTo("a \"quote\" € %20 \u0001"));
            Assert.That(backend.Requests[0].Metadata.GetProperty("labels").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(backend.Requests[0].Document.IsNull, Is.False);
            Assert.That(backend.Requests[0].Document.Length, Is.Zero);
        }

        [TestCase(false, 1024)]
        [TestCase(false, 1025)]
        [TestCase(true, 1024)]
        [TestCase(true, 1025)]
        public async Task HostBodyLimitAcceptsBoundaryAndRejectsNextByteBeforeExecute(bool streaming, int size)
        {
            byte[] bytes = new byte[size];
            bytes[0] = 0xff;
            bytes[size - 1] = 0x17;
            var backend = new FakeRegistryEndpoint { Response = new XRegistryResponse(204) };
            XRegistryHttpRouteOptions options = HttpHostTestData.OpenOptions with
            {
                Transport = new XRegistryHttpOptions { MaximumBodyBytes = 1024 }
            };
            await using HttpRouteTestHost host = await HttpRouteTestHost.StartAsync(backend,
                options).ConfigureAwait(false);
            using var request = new HttpRequestMessage(HttpMethod.Put, "/registry" + HttpTestData.ResourcePath)
            {
                Content = streaming ? new StreamingHttpContent(bytes) : new ByteArrayContent(bytes)
            };
            if (streaming)
            {
                request.Headers.TransferEncodingChunked = true;
            }

            using HttpResponseMessage response = await host.Client.SendAsync(request).ConfigureAwait(false);

            Assert.That((int)response.StatusCode, Is.EqualTo(size == 1024 ? 204 : 413));
            Assert.That(backend.Inspections, Has.Count.EqualTo(1));
            Assert.That(backend.Requests, Has.Count.EqualTo(size == 1024 ? 1 : 0));
            if (size == 1024)
            {
                Assert.That(backend.Requests[0].Document.Span.ToArray(), Is.EqualTo(bytes));
            }
            else
            {
                JsonElement problem = HttpTestData.Json(await response.Content.ReadAsStringAsync().ConfigureAwait(
                    false));
                Assert.That(problem.GetProperty("detail").GetString(),
                    Is.EqualTo("The HTTP body exceeds its configured limit."));
            }
        }

        [TestCase("gzip", 1024)]
        [TestCase("gzip", 1025)]
        [TestCase("deflate", 1024)]
        [TestCase("deflate", 1025)]
        public async Task HostDecompressionEnforcesDecodedBodyBoundary(string encoding, int size)
        {
            byte[] raw = Encoding.UTF8.GetBytes(new string(' ', size - 7) + /*lang=json,strict*/ """{"n":7}""");
            byte[] compressed = XRegistryHttpBoundaryTests.Compress(raw, encoding);
            var backend = new FakeRegistryEndpoint { Response = new XRegistryResponse(204) };
            XRegistryHttpRouteOptions options = HttpHostTestData.OpenOptions with
            {
                Transport = new XRegistryHttpOptions { MaximumBodyBytes = 1024 }
            };
            await using HttpRouteTestHost host = await HttpRouteTestHost.StartAsync(backend,
                options).ConfigureAwait(false);
            using var content = new ByteArrayContent(compressed);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            content.Headers.ContentEncoding.Add(encoding);

            using HttpResponseMessage response = await host.Client.PutAsync(HttpHostTestData.ModelUri, content)
                .ConfigureAwait(false);

            Assert.That((int)response.StatusCode, Is.EqualTo(size == 1024 ? 204 : 413));
            Assert.That(backend.Requests, Has.Count.EqualTo(size == 1024 ? 1 : 0));
            if (size == 1024)
            {
                Assert.That(backend.Requests[0].Metadata.GetProperty("n").GetInt32(), Is.EqualTo(7));
                Assert.That(backend.Requests[0].Document.IsNull, Is.True);
            }
        }

        [TestCase("gzip", false, 0)]
        [TestCase("gzip", false, -1)]
        [TestCase("gzip", true, 0)]
        [TestCase("gzip", true, -1)]
        [TestCase("deflate", false, 0)]
        [TestCase("deflate", false, -1)]
        [TestCase("deflate", true, 0)]
        [TestCase("deflate", true, -1)]
        public async Task HostDecompressionAlsoBoundsEncodedBody(string encoding, bool streaming, int adjustment)
        {
            byte[] compressed =
                XRegistryHttpBoundaryTests.Compress(Encoding.UTF8.GetBytes(/*lang=json,strict*/ """{"n":7}"""),
                encoding);
            var backend = new FakeRegistryEndpoint { Response = new XRegistryResponse(204) };
            XRegistryHttpRouteOptions options = HttpHostTestData.OpenOptions with
            {
                Transport = new XRegistryHttpOptions { MaximumBodyBytes = compressed.Length + adjustment }
            };
            await using HttpRouteTestHost host = await HttpRouteTestHost.StartAsync(backend,
                options).ConfigureAwait(false);
            using HttpContent content = streaming ? new StreamingHttpContent(compressed) : new ByteArrayContent(
                compressed);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            content.Headers.ContentEncoding.Add(encoding);

            using HttpResponseMessage response = await host.Client.PutAsync(HttpHostTestData.ModelUri, content)
                .ConfigureAwait(false);

            Assert.That((int)response.StatusCode, Is.EqualTo(adjustment == 0 ? 204 : 413));
            Assert.That(backend.Requests, Has.Count.EqualTo(adjustment == 0 ? 1 : 0));
            if (adjustment == 0)
            {
                Assert.That(backend.Requests[0].Metadata.GetProperty("n").GetInt32(), Is.EqualTo(7));
            }
        }

        [TestCase("br")]
        [TestCase("unknown")]
        [TestCase("identity, identity, identity")]
        public async Task HostRejectsUnsupportedContentEncodingWith415BeforeExecute(string encoding)
        {
            var backend = new FakeRegistryEndpoint { Response = new XRegistryResponse(204) };
            await using HttpRouteTestHost host = await HttpRouteTestHost.StartAsync(
                backend, HttpHostTestData.OpenOptions).ConfigureAwait(false);
            using var content = new StringContent("{}", Encoding.UTF8, "application/json");
            content.Headers.TryAddWithoutValidation("Content-Encoding", encoding);

            using HttpResponseMessage response = await host.Client.PutAsync(HttpHostTestData.ModelUri, content)
                .ConfigureAwait(false);
            JsonElement problem = HttpTestData.Json(await response.Content.ReadAsStringAsync().ConfigureAwait(false));

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.UnsupportedMediaType));
            Assert.That(problem.GetProperty("detail").GetString(), Does.Contain("encoding"));
            Assert.That(backend.Inspections, Has.Count.EqualTo(1));
            Assert.That(backend.Requests, Is.Empty);
        }

        [TestCase("application/json", /*lang=json,strict*/ """{"n":7}""", 204)]
        [TestCase("application/problem+json", /*lang=json,strict*/ """{"n":7}""", 204)]
        [TestCase("application/json; charset=\"utf-8\"", /*lang=json,strict*/ """{"n":7}""", 204)]
        [TestCase("text/plain", "{}", 415)]
        [TestCase("application/json; charset=utf-16", "{}", 415)]
        [TestCase("application/json", /*lang=json,strict*/ """{"n":1,"n":2}""", 400)]
        [TestCase("application/json", "{broken", 400)]
        public async Task HostMetadataContentTypeAndJsonAreValidatedBeforeExecute(string contentType, string body,
            int status)
        {
            var backend = new FakeRegistryEndpoint { Response = new XRegistryResponse(204) };
            await using HttpRouteTestHost host = await HttpRouteTestHost.StartAsync(
                backend, HttpHostTestData.OpenOptions).ConfigureAwait(false);
            using var content = new ByteArrayContent(Encoding.UTF8.GetBytes(body));
            content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);

            using HttpResponseMessage response = await host.Client.PutAsync(HttpHostTestData.ModelUri, content)
                .ConfigureAwait(false);

            Assert.That((int)response.StatusCode, Is.EqualTo(status));
            Assert.That(backend.Requests, Has.Count.EqualTo(status == 204 ? 1 : 0));
            if (status == 204)
            {
                Assert.That(backend.Requests[0].Metadata.GetProperty("n").GetInt32(), Is.EqualTo(7));
            }
        }

        [TestCase(/*lang=json,strict*/ """{"a":{"n":7}}""", 204)]
        [TestCase(/*lang=json,strict*/ """{"a":{"b":{"n":7}}}""", 400)]
        public async Task HostJsonDepthHasExactBoundaryBeforeExecute(string body, int status)
        {
            var backend = new FakeRegistryEndpoint { Response = new XRegistryResponse(204) };
            XRegistryHttpRouteOptions options = HttpHostTestData.OpenOptions with
            {
                Transport = new XRegistryHttpOptions { MaximumJsonDepth = 2 }
            };
            await using HttpRouteTestHost host = await HttpRouteTestHost.StartAsync(backend,
                options).ConfigureAwait(false);
            using var content = new StringContent(body, Encoding.UTF8, "application/json");

            using HttpResponseMessage response = await host.Client.PutAsync(HttpHostTestData.ModelUri, content)
                .ConfigureAwait(false);

            Assert.That((int)response.StatusCode, Is.EqualTo(status));
            Assert.That(backend.Requests, Has.Count.EqualTo(status == 204 ? 1 : 0));
            if (status == 204)
            {
                Assert.That(backend.Requests[0].Metadata.GetProperty("a").GetProperty("n").GetInt32(), Is.EqualTo(7));
            }
        }

        [TestCase(3)]
        [TestCase(4)]
        public async Task HostHeaderCountHasExactBoundaryBeforeInspection(int count)
        {
            var backend = new FakeRegistryEndpoint { Response = new XRegistryResponse(204) };
            XRegistryHttpRouteOptions options = HttpHostTestData.OpenOptions with
            {
                Transport = new XRegistryHttpOptions { MaximumHeaders = 3 }
            };
            await using HttpRouteTestHost host = await HttpRouteTestHost.StartAsync(backend, options,
                prepare: context =>
            {
                context.Request.Headers.Clear();
                for (int index = 0; index < count; index++)
                {
                    context.Request.Headers.Append("X-Pad" + index, "a");
                }
            }).ConfigureAwait(false);

            using HttpResponseMessage response = await host.Client.GetAsync(HttpHostTestData.ModelUri).ConfigureAwait(
                false);

            Assert.That((int)response.StatusCode, Is.EqualTo(count == 3 ? 204 : 431));
            Assert.That(backend.Inspections, Has.Count.EqualTo(count == 3 ? 1 : 0));
            Assert.That(backend.Requests, Has.Count.EqualTo(count == 3 ? 1 : 0));
        }

        [TestCase(100)]
        [TestCase(101)]
        public async Task HostHeaderBytesHaveExactBoundaryBeforeInspection(int valueLength)
        {
            var backend = new FakeRegistryEndpoint { Response = new XRegistryResponse(204) };
            XRegistryHttpRouteOptions options = HttpHostTestData.OpenOptions with
            {
                Transport = new XRegistryHttpOptions { MaximumHeaderBytes = 109 }
            };
            await using HttpRouteTestHost host = await HttpRouteTestHost.StartAsync(backend, options,
                prepare: context =>
            {
                context.Request.Headers.Clear();
                context.Request.Headers.Append("X-Pad", new string('a', valueLength));
            }).ConfigureAwait(false);

            using HttpResponseMessage response = await host.Client.GetAsync(HttpHostTestData.ModelUri).ConfigureAwait(
                false);

            Assert.That((int)response.StatusCode, Is.EqualTo(valueLength == 100 ? 204 : 431));
            Assert.That(backend.Inspections, Has.Count.EqualTo(valueLength == 100 ? 1 : 0));
            Assert.That(backend.Requests, Has.Count.EqualTo(valueLength == 100 ? 1 : 0));
        }

        [TestCase(0)]
        [TestCase(1)]
        public async Task HostUriLengthHasExactBoundaryBeforeInspection(int extra)
        {
            string target = "/registry/model?future=" + new string('a', 40);
            var backend = new FakeRegistryEndpoint { Response = new XRegistryResponse(204) };
            XRegistryHttpRouteOptions options = HttpHostTestData.OpenOptions with
            {
                Transport = new XRegistryHttpOptions { MaximumUriLength = target.Length }
            };
            await using HttpRouteTestHost host = await HttpRouteTestHost.StartAsync(backend, options,
                prepare: context =>
                context.Features.Get<IHttpRequestFeature>()!.RawTarget = target + new string('b', extra))
                .ConfigureAwait(false);

            using HttpResponseMessage response = await host.Client.GetAsync(HttpHostTestData.ModelUri).ConfigureAwait(
                false);

            Assert.That((int)response.StatusCode, Is.EqualTo(extra == 0 ? 204 : 414));
            Assert.That(backend.Inspections, Has.Count.EqualTo(extra == 0 ? 1 : 0));
            Assert.That(backend.Requests, Has.Count.EqualTo(extra == 0 ? 1 : 0));
            if (extra == 0)
            {
                Assert.That(backend.Requests[0].Parameters[0], Is.EqualTo(new XRegistryParameter("future",
                    new string('a', 40))));
            }
        }

        [TestCase("?a=1&b=2", 204)]
        [TestCase("?a=1&b=2&c=3", 400)]
        [TestCase("?x=%GG", 400)]
        [TestCase("?x=%FF", 400)]
        [TestCase("?=x", 400)]
        public async Task HostRejectsInvalidOrExcessiveQueryBeforeInspection(string query, int status)
        {
            var backend = new FakeRegistryEndpoint { Response = new XRegistryResponse(204) };
            XRegistryHttpRouteOptions options = HttpHostTestData.OpenOptions with
            {
                Transport = new XRegistryHttpOptions { MaximumParameters = 2 }
            };
            await using HttpRouteTestHost host = await HttpRouteTestHost.StartAsync(backend, options,
                prepare: context =>
                context.Features.Get<IHttpRequestFeature>()!.RawTarget = "/registry/model" + query).ConfigureAwait(
                    false);

            using HttpResponseMessage response = await host.Client.GetAsync(HttpHostTestData.ModelUri).ConfigureAwait(
                false);

            Assert.That((int)response.StatusCode, Is.EqualTo(status));
            Assert.That(backend.Inspections, Has.Count.EqualTo(status == 204 ? 1 : 0));
            Assert.That(backend.Requests, Has.Count.EqualTo(status == 204 ? 1 : 0));
            if (status == 204)
            {
                XRegistryParameter[] expected = [new("a", "1"), new("b", "2")];
                Assert.That(backend.Requests[0].Parameters.ToArray(), Is.EqualTo(expected));
            }
        }

        [TestCase("r$details", "r", XRegistryView.Metadata)]
        [TestCase("r%24details", "r%24details", XRegistryView.Default)]
        [TestCase("r%2524details$details", "r%2524details", XRegistryView.Metadata)]
        public async Task HostDistinguishesRawDetailsFromEncodedLiteralDollar(
            string rawId, string pathId, XRegistryView view)
        {
            var backend = new FakeRegistryEndpoint { Response = new XRegistryResponse(204) };
            await using HttpRouteTestHost host = await HttpRouteTestHost.StartAsync(
                backend, HttpHostTestData.OpenOptions, prepare: context =>
                    context.Features.Get<IHttpRequestFeature>()!.RawTarget =
                        "/registry/schemagroups/g/schemas/" + rawId).ConfigureAwait(false);

            using HttpResponseMessage response = await host.Client.GetAsync(
                new Uri("/registry/schemagroups/g/schemas/r", UriKind.Relative)).ConfigureAwait(false);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(backend.Requests, Has.Count.EqualTo(1));
            Assert.That(backend.Requests[0].Path, Is.EqualTo("/schemagroups/g/schemas/" + pathId));
            Assert.That(backend.Requests[0].View, Is.EqualTo(view));
        }

        [TestCase("TRACE", 405, 0)]
        [TestCase("GET", 400, 1)]
        [TestCase("OPTIONS", 400, 1)]
        public async Task HostRejectsUnsupportedVerbOrReadBodyWithoutExecution(string method, int status,
            int inspections)
        {
            var backend = new FakeRegistryEndpoint();
            await using HttpRouteTestHost host = await HttpRouteTestHost.StartAsync(
                backend, HttpHostTestData.OpenOptions).ConfigureAwait(false);
            using var request = new HttpRequestMessage(new HttpMethod(method), HttpHostTestData.ModelUri)
            {
                Content = new ByteArrayContent([0x41])
            };

            using HttpResponseMessage response = await host.Client.SendAsync(request).ConfigureAwait(false);

            Assert.That((int)response.StatusCode, Is.EqualTo(status));
            Assert.That(backend.Inspections, Has.Count.EqualTo(inspections));
            Assert.That(backend.Requests, Is.Empty);
            if (status == 405)
            {
                Assert.That(HttpHostTestData.Header(response, "Allow"), Is.EqualTo("OPTIONS"));
            }
        }

        [Test]
        public async Task HostRejectsDetailsOnGroupBeforeExecution()
        {
            var backend = new FakeRegistryEndpoint();
            await using HttpRouteTestHost host = await HttpRouteTestHost.StartAsync(
                backend, HttpHostTestData.OpenOptions).ConfigureAwait(false);

            using HttpResponseMessage response = await host.Client.GetAsync(
                new Uri("/registry/schemagroups/g$details", UriKind.Relative)).ConfigureAwait(false);
            JsonElement problem = HttpTestData.Json(await response.Content.ReadAsStringAsync().ConfigureAwait(false));

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(problem.GetProperty("type").GetString(), Does.EndWith("#bad_details"));
            Assert.That(backend.Inspections, Has.Count.EqualTo(1));
            Assert.That(backend.Requests, Is.Empty);
        }

        [TestCase("/")]
        [TestCase("")]
        public async Task LiteralRootMountForwardsRegistryRelativePath(string mount)
        {
            var backend = new FakeRegistryEndpoint();
            XRegistryHttpRouteOptions options = new(new Uri("https://public.example/"))
            {
                RequireAuthenticatedUser = false
            };
            await using HttpRouteTestHost host = await HttpRouteTestHost.StartAsync(
                backend, options, pattern: mount).ConfigureAwait(false);

            using HttpResponseMessage response = await host.Client.GetAsync(new Uri("/model", UriKind.Relative))
                .ConfigureAwait(false);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(backend.Requests[0].Path, Is.EqualTo("/model"));
            Assert.That(HttpHostTestData.Header(response, "Link"),
                Is.EqualTo("<https://public.example/>;rel=xregistry-root"));
        }

        [TestCase("metadata")]
        [TestCase("header")]
        [TestCase("link")]
        [TestCase("path")]
        [TestCase("relation")]
        [TestCase("body")]
        [TestCase("status")]
        public async Task HostSerializesBeforeSendingAndReportsUnknownMutationOutcome(string defect)
        {
            var backend = new FakeRegistryEndpoint();
            XRegistryResponse result = defect switch
            {
                "metadata" => new XRegistryResponse(200)
                {
                    Metadata =
                    HttpTestData.Json(/*lang=json,strict*/ """{"self":17}""")
                },
                "header" => new XRegistryResponse(200)
                {
                    Document = new ByteString("A"u8.ToArray()),
                    Metadata = HttpTestData.Json(/*lang=json,strict*/ """{"bad name":"SUCCESS-MUST-NOT-LEAK"}""")
                },
                "link" => new XRegistryResponse(201)
                {
                    Metadata = HttpTestData.Json(/*lang=json,strict*/ """{"success":"SUCCESS-MUST-NOT-LEAK"}"""),
                    Location = "https://foreign.example/outside"
                },
                "path" => new XRegistryResponse(201) { Location = "/schemagroups/../outside" },
                "relation" => new XRegistryResponse(200) { Links = [new("invalid relation", "/")] },
                "body" => new XRegistryResponse(200)
                {
                    Document = new ByteString(Encoding.UTF8.GetBytes("SUCCESS-MUST-NOT-LEAK"))
                },
                _ => new XRegistryResponse(204) { Document = new ByteString("A"u8.ToArray()) }
            };
            int committed = 0;
            backend.ExecuteCallback = (_, _) =>
            {
                committed++;
                return new ValueTask<XRegistryResponse>(result);
            };
            XRegistryHttpRouteOptions options = HttpHostTestData.OpenOptions with
            {
                Transport = new XRegistryHttpOptions { MaximumBodyBytes = defect == "body" ? 16 : 4096 }
            };
            await using HttpRouteTestHost host = await HttpRouteTestHost.StartAsync(backend,
                options).ConfigureAwait(false);
            using var content = new StringContent("{}", Encoding.UTF8, "application/json");

            using HttpResponseMessage response = await host.Client.PutAsync(
                new Uri("/registry/schemagroups/g", UriKind.Relative), content).ConfigureAwait(false);
            byte[] bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            string body = Encoding.UTF8.GetString(bytes);
            JsonElement problem = HttpTestData.Json(body);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadGateway));
            Assert.That(problem.GetProperty("detail").GetString(), Does.Contain("mutation outcome may be unknown"));
            Assert.That(body, Does.Not.Contain("SUCCESS-MUST-NOT-LEAK"));
            Assert.That(response.Content.Headers.ContentLength, Is.EqualTo(bytes.Length));
            Assert.That(response.Headers.Location, Is.Null);
            Assert.That(committed, Is.EqualTo(1), "The HTTP transport cannot roll back an already committed provider.");
            Assert.That(backend.Requests, Has.Count.EqualTo(1));
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task HostCannotSubstituteDocumentsAndMetadataWhenTheProviderReturnsTheWrongView(bool rawView)
        {
            var backend = new FakeRegistryEndpoint
            {
                Response = rawView
                    ? new XRegistryResponse(200)
                    {
                        Metadata =
                        HttpTestData.Json(/*lang=json,strict*/ """{"name":"not-a-document"}""")
                    }
                    : new XRegistryResponse(200) { Document = new ByteString(new byte[] { 0xff, 0x41 }) }
            };
            await using HttpRouteTestHost host = await HttpRouteTestHost.StartAsync(
                backend, HttpHostTestData.OpenOptions).ConfigureAwait(false);
            string path = "/registry" + (rawView ? HttpTestData.ResourcePath : HttpTestData.RecordPath);

            using HttpResponseMessage response = await host.Client.GetAsync(new Uri(path, UriKind.Relative))
                .ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadGateway));
            Assert.That(HttpTestData.Json(body).GetProperty("detail").GetString(),
                Does.Contain("response preparation failed"));
            Assert.That(body, Does.Not.Contain("not-a-document"));
            Assert.That(backend.Requests, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task HostSupportsTestHostWithoutAnExplicitRawTarget()
        {
            var backend = new FakeRegistryEndpoint();
            await using HttpRouteTestHost host = await HttpRouteTestHost.StartAsync(
                backend, HttpHostTestData.OpenOptions,
                prepare: static context => context.Features.Get<IHttpRequestFeature>()!.RawTarget = string.Empty)
                .ConfigureAwait(false);

            using HttpResponseMessage response = await host.Client.GetAsync(HttpHostTestData.ModelUri)
                .ConfigureAwait(false);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(backend.Requests, Has.Count.EqualTo(1));
            Assert.That(backend.Requests[0].Path, Is.EqualTo("/model"));
            Assert.That(await response.Content.ReadAsStringAsync().ConfigureAwait(false),
                Is.EqualTo(/*lang=json,strict*/ """{"from":"provider","n":7}"""));
        }

        [TestCase("If-Match", "\"0\"")]
        [TestCase("If-None-Match", "*")]
        [TestCase("If-Unmodified-Since", "Wed, 09 Sep 2026 10:00:00 GMT")]
        [TestCase("If-Modified-Since", "Wed, 09 Sep 2026 10:00:00 GMT")]
        [TestCase("If-Range", "\"0\"")]
        public async Task HostCannotDropUnsupportedHttpValidatorPreconditions(string name, string value)
        {
            var backend = new FakeRegistryEndpoint { Response = new XRegistryResponse(204) };
            await using HttpRouteTestHost host = await HttpRouteTestHost.StartAsync(
                backend, HttpHostTestData.OpenOptions).ConfigureAwait(false);
            using var request = new HttpRequestMessage(HttpMethod.Put, "/registry/schemagroups/g")
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
            request.Headers.TryAddWithoutValidation(name, value);

            using HttpResponseMessage response = await host.Client.SendAsync(request).ConfigureAwait(false);
            JsonElement problem = HttpTestData.Json(await response.Content.ReadAsStringAsync().ConfigureAwait(false));

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(problem.GetProperty("detail").GetString(), Does.Contain("epoch preconditions"));
            Assert.That(backend.Requests, Is.Empty,
                "An unsupported condition must never become an unconditional write.");
        }

        [TestCase("/schemagroups", "PUT")]
        [TestCase(HttpTestData.ResourcePath, "PATCH")]
        public async Task HostOptionsCannotAdvertiseMethodsRejectedByTheWireProfile(string path, string rejectedMethod)
        {
            var backend = new FakeRegistryEndpoint
            {
                Response = new XRegistryResponse(200)
                {
                    AllowedActions = [XRegistryAction.Read, XRegistryAction.Replace,
                        XRegistryAction.Merge, XRegistryAction.Create, XRegistryAction.Describe]
                }
            };
            await using HttpRouteTestHost host = await HttpRouteTestHost.StartAsync(
                backend, HttpHostTestData.OpenOptions).ConfigureAwait(false);
            using var request = new HttpRequestMessage(HttpMethod.Options, "/registry" + path);

            using HttpResponseMessage response = await host.Client.SendAsync(request).ConfigureAwait(false);
            string allow = HttpHostTestData.Header(response, "Allow");

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(allow.Split(", ", StringSplitOptions.None), Does.Not.Contain(rejectedMethod));
            Assert.That(allow, Does.Contain("OPTIONS"));
            Assert.That(HttpHostTestData.Header(response, "Access-Control-Allow-Methods"), Is.EqualTo(allow));
            Assert.That(backend.Requests, Has.Count.EqualTo(1));
            Assert.That(backend.Requests[0].Action, Is.EqualTo(XRegistryAction.Describe));
        }

        [Test]
        public async Task HostPreservesBackendInspectionRejectionWithoutCallingExecute()
        {
            var backend = new FakeRegistryEndpoint
            {
                InspectCallback = (_, _) => throw new XRegistryHttpException("inspection denied",
                    new XRegistryResponse(403)
                    {
                        Metadata = HttpTestData.Json(HttpTestData.Problem),
                        Error = new XRegistryError("mismatched_epoch", "summary")
                    })
            };
            await using HttpRouteTestHost host = await HttpRouteTestHost.StartAsync(
                backend, HttpHostTestData.OpenOptions).ConfigureAwait(false);

            using HttpResponseMessage response = await host.Client.GetAsync(HttpHostTestData.ModelUri).ConfigureAwait(
                false);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            HttpTestData.AssertProblem(HttpTestData.Json(
                await response.Content.ReadAsStringAsync().ConfigureAwait(false)));
            Assert.That(backend.Inspections, Has.Count.EqualTo(1));
            Assert.That(backend.Requests, Is.Empty);
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public async Task HostDistinguishesTransportFailureFromInspectionRejection(bool duringExecute, bool derived)
        {
            var backend = new FakeRegistryEndpoint();
            HttpRequestException error = derived
                ? new XRegistryHttpException("private-wire-detail")
                : new HttpRequestException("private-wire-detail");
            if (duringExecute)
            {
                backend.ExecuteCallback = (_, _) => throw error;
            }
            else
            {
                backend.InspectCallback = (_, _) => throw error;
            }
            await using HttpRouteTestHost host = await HttpRouteTestHost.StartAsync(
                backend, HttpHostTestData.OpenOptions).ConfigureAwait(false);

            using HttpResponseMessage response = await host.Client.GetAsync(HttpHostTestData.ModelUri).ConfigureAwait(
                false);
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            JsonElement problem = HttpTestData.Json(body);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadGateway));
            Assert.That(problem.GetProperty("detail").GetString(),
                Does.Contain("endpoint or response preparation failed"));
            Assert.That(body, Does.Not.Contain("private-wire-detail"));
            Assert.That(backend.Inspections, Has.Count.EqualTo(1));
            Assert.That(backend.Requests, Has.Count.EqualTo(duringExecute ? 1 : 0));
        }

        [TestCase("null")]
        [TestCase("[]")]
        public async Task HostInvalidInspectionModelProducesCompleteBadGatewayWithoutExecute(string model)
        {
            var backend = new FakeRegistryEndpoint();
            backend.Description = backend.Description with { Model = HttpTestData.Json(model) };
            await using HttpRouteTestHost host = await HttpRouteTestHost.StartAsync(
                backend, HttpHostTestData.OpenOptions).ConfigureAwait(false);

            using HttpResponseMessage response = await host.Client.GetAsync(HttpHostTestData.ModelUri).ConfigureAwait(
                false);
            JsonElement problem = HttpTestData.Json(await response.Content.ReadAsStringAsync().ConfigureAwait(false));

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadGateway));
            Assert.That(problem.GetProperty("detail").GetString(),
                Does.Contain("endpoint or response preparation failed"));
            Assert.That(backend.Inspections, Has.Count.EqualTo(1));
            Assert.That(backend.Requests, Is.Empty);
        }

        [Test]
        public async Task HostOperationTimeoutReturnsComplete504WithoutRetry()
        {
            var backend = new FakeRegistryEndpoint();
            CancellationToken observed = default;
            backend.ExecuteCallback = async (_, token) =>
            {
                observed = token;
                var completion = new TaskCompletionSource<XRegistryResponse>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                using CancellationTokenRegistration registration = token.Register(() => completion.TrySetCanceled());
                return await completion.Task.ConfigureAwait(false);
            };
            XRegistryHttpRouteOptions options = HttpHostTestData.OpenOptions with
            {
                Transport = new XRegistryHttpOptions { RequestTimeout = TimeSpan.FromSeconds(1) }
            };
            await using HttpRouteTestHost host = await HttpRouteTestHost.StartAsync(backend,
                options).ConfigureAwait(false);
            using var content = new StringContent("{}", Encoding.UTF8, "application/json");

            using HttpResponseMessage response = await host.Client.PutAsync(HttpHostTestData.ModelUri, content)
                .ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            JsonElement problem = HttpTestData.Json(body);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.GatewayTimeout));
            Assert.That(problem.GetProperty("detail").GetString(),
                Does.Contain("outcome may be unknown; do not retry blindly"));
            Assert.That(observed.IsCancellationRequested, Is.True);
            Assert.That(backend.InspectionTokens[0], Is.EqualTo(observed),
                "Inspection and execution must share the complete-operation deadline.");
            Assert.That(backend.ExecutionTokens[0], Is.EqualTo(observed));
            Assert.That(backend.Requests, Has.Count.EqualTo(1));
            Assert.That(response.Content.Headers.ContentLength, Is.EqualTo(Encoding.UTF8.GetByteCount(body)));
        }

        [TestCase("/registry/{id}")]
        [TestCase("/registry?query")]
        [TestCase("/registry#fragment")]
        public async Task HostRejectsNonliteralMounts(string pattern)
        {
            var backend = new FakeRegistryEndpoint();

            await Assert.ThatAsync(async () =>
            {
                await using HttpRouteTestHost host = await HttpRouteTestHost.StartAsync(
                    backend, HttpHostTestData.OpenOptions, pattern: pattern).ConfigureAwait(false);
            }, Throws.ArgumentException).ConfigureAwait(false);

            Assert.That(backend.Inspections, Is.Empty);
            Assert.That(backend.Requests, Is.Empty);
        }

        [Test]
        public async Task HostRouteOptionsRejectNullRequiredValues()
        {
            var backend = new FakeRegistryEndpoint();
            Assert.That(() => new XRegistryHttpRouteOptions(null!),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("publicRoot"));
            Assert.That(() => XRegistryHttpEndpointRouteBuilderExtensions.MapXRegistry(
                null!, "/registry", backend, HttpHostTestData.OpenOptions),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("routes"));

            await Assert.ThatAsync(async () =>
            {
                await using HttpRouteTestHost host = await HttpRouteTestHost.StartAsync(
                    backend, HttpHostTestData.OpenOptions with { Transport = null! }).ConfigureAwait(false);
            }, Throws.ArgumentNullException.With.Property("ParamName").EqualTo("Transport")).ConfigureAwait(false);

            Assert.That(backend.Inspections, Is.Empty);
            Assert.That(backend.Requests, Is.Empty);
        }

        [Test]
        public async Task HostCallerCancellationReachesActiveExecutionWithoutRetry()
        {
            var backend = new FakeRegistryEndpoint();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var aborted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            backend.ExecuteCallback = async (_, token) =>
            {
                var completion = new TaskCompletionSource<XRegistryResponse>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                using CancellationTokenRegistration registration = token.Register(() =>
                {
                    aborted.TrySetResult(true);
                    completion.TrySetCanceled();
                });
                entered.TrySetResult(true);
                return await completion.Task.ConfigureAwait(false);
            };
            await using HttpRouteTestHost host = await HttpRouteTestHost.StartAsync(
                backend, HttpHostTestData.OpenOptions).ConfigureAwait(false);
            using var caller = new CancellationTokenSource();
            Task execution = SendRequestAsync();
            await entered.Task.ConfigureAwait(false);

            caller.Cancel();
            await aborted.Task.ConfigureAwait(false);
            bool cancelled = false;
            try
            {
                await execution.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }

            Assert.That(cancelled, Is.True);
            Assert.That(backend.Inspections, Has.Count.EqualTo(1));
            Assert.That(backend.Requests, Has.Count.EqualTo(1));
            Assert.That(backend.ExecutionTokens[0].IsCancellationRequested, Is.True);
            Assert.That(backend.InspectionTokens[0], Is.EqualTo(backend.ExecutionTokens[0]));

            async Task SendRequestAsync()
            {
                using var request = new HttpRequestMessage(HttpMethod.Put, HttpHostTestData.ModelUri)
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json")
                };
                using HttpResponseMessage response = await host.Client.SendAsync(request,
                    caller.Token).ConfigureAwait(false);
            }
        }
    }
}
#endif
