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
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Http.Tests
{
    [TestFixture]
    [Category("XRegistryHttp")]
    public sealed class XRegistryHttpDocumentTests
    {
        [TestCase("json", "application/json")]
        [TestCase("binary", "application/octet-stream")]
        [TestCase("binary", null)]
        [TestCase("empty", "application/json")]
        public async Task DocumentViewPreservesRawBytesRegardlessOfContentType(string fixture, string? contentType)
        {
            byte[] bytes = fixture switch
            {
                "json" => Encoding.UTF8.GetBytes(
                    /*lang=json,strict*/ """{ "self" : "https://foreign.example/document", "n":1.00 }"""),
                "binary" => [0xff, 0x80, 0, 0x0d, 0x0a, 0xc0],
                _ => []
            };
            using var handler = new RecordingHttpHandler(message => message.RequestUri!.AbsolutePath ==
                "/registry/model"
                ? HttpTestData.JsonResponse(HttpTestData.Model)
                : HttpTestData.BytesResponse(bytes, contentType));
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot);

            XRegistryResponse response = await endpoint.ExecuteAsync(
                new XRegistryRequest(XRegistryAction.Read, HttpTestData.ResourcePath)).ConfigureAwait(false);

            Assert.That(response.Document.IsNull, Is.False);
            Assert.That(response.Document.Span.ToArray(), Is.EqualTo(bytes));
            Assert.That(response.ContentType, Is.EqualTo(contentType));
            Assert.That(response.Metadata.TryGetProperty("self", out _), Is.False,
                "JSON bytes are not registry metadata.");
            Assert.That(handler.Requests, Has.Count.EqualTo(2));
            Assert.That(handler.Requests[1].Uri,
                Is.EqualTo("https://registry.example/registry/schemagroups/g/schemas/r"));
        }

        [TestCase(200, false)]
        [TestCase(204, true)]
        public async Task EmptyDocumentIsPresentExceptForBodylessStatus(int status, bool absent)
        {
            using var handler = new RecordingHttpHandler(message => message.RequestUri!.AbsolutePath ==
                "/registry/model"
                ? HttpTestData.JsonResponse(HttpTestData.Model)
                : HttpTestData.BytesResponse([], status: status));
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot);

            XRegistryResponse response = await endpoint.ExecuteAsync(
                new XRegistryRequest(XRegistryAction.Read, HttpTestData.ResourcePath)).ConfigureAwait(false);

            Assert.That(response.StatusCode, Is.EqualTo(status));
            Assert.That(response.Document.IsNull, Is.EqualTo(absent));
            Assert.That(response.Document.Length, Is.Zero);
            Assert.That(handler.Requests, Has.Count.EqualTo(2));
        }

        [TestCase(HttpTestData.ResourcePath, XRegistryView.Metadata)]
        [TestCase(HttpTestData.RecordPath, XRegistryView.Default)]
        [TestCase(HttpTestData.RecordPath, XRegistryView.Metadata)]
        [TestCase("/schemagroups/g/schemas/r/meta", XRegistryView.Default)]
        public async Task DocumentAndMetadataViewsFollowHasDocumentModel(string path, XRegistryView view)
        {
            const string raw =
                /*lang=json,strict*/
                """{"epoch":18446744073709551616,"name":"null","nested":{"items":[true,2.5,null]}}""";
            using var handler = new RecordingHttpHandler(message => message.RequestUri!.AbsolutePath ==
                "/registry/model"
                ? HttpTestData.JsonResponse(HttpTestData.Model) : HttpTestData.JsonResponse(raw));
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot);

            XRegistryResponse response = await endpoint.ExecuteAsync(
                new XRegistryRequest(XRegistryAction.Read, path) { View = view }).ConfigureAwait(false);

            Assert.That(response.Document.IsNull, Is.True);
            Assert.That(response.Metadata.GetRawText(), Is.EqualTo(raw));
            Assert.That(response.Metadata.GetProperty("epoch").GetRawText(), Is.EqualTo("18446744073709551616"));
            Assert.That(response.Metadata.GetProperty("name").GetString(), Is.EqualTo("null"));
            Assert.That(response.Metadata.GetProperty("nested").GetProperty("items")[0].GetBoolean(), Is.True);
            Assert.That(response.Metadata.GetProperty("nested").GetProperty("items")[1].GetDecimal(), Is.EqualTo(2.5m));
            Assert.That(response.Metadata.GetProperty("nested").GetProperty("items")[2].ValueKind,
                Is.EqualTo(JsonValueKind.Null));
            Assert.That(handler.Requests[1].Uri,
                Is.EqualTo("https://registry.example/registry" +
                    path +
                    (view == XRegistryView.Metadata ? "$details" : string.Empty)));
        }

        [TestCase("/schemagroups/g/defaults/r")]
        [TestCase("/schemagroups/g/schemas/r/versions/v2")]
        public async Task ModelDefaultHasDocumentAndVersionRouteUseRawContent(string path)
        {
            using var handler = new RecordingHttpHandler(message => message.RequestUri!.AbsolutePath ==
                "/registry/model"
                ? HttpTestData.JsonResponse(HttpTestData.Model) : HttpTestData.BytesResponse([0xfe, 0x10]));
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot);

            XRegistryResponse response = await endpoint.ExecuteAsync(new XRegistryRequest(XRegistryAction.Read, path))
                .ConfigureAwait(false);

            Assert.That(response.Document.Span.ToArray(), Is.EqualTo(new byte[] { 0xfe, 0x10 }));
            Assert.That(handler.Requests[1].Uri, Is.EqualTo("https://registry.example/registry" + path));
            Assert.That(response.Metadata.EnumerateObject().ToArray(), Is.Empty);
        }

        [Test]
        public async Task DocumentWriteEncodesPatchHeadersExactly()
        {
            const string metadata =
                "{\"epoch\":18446744073709551616,\"name\":\"a \\\"b\\\"%€\\u0001%20😀\"," +
                "\"labels\":{\"a.b\":\"x y\",\"keep\":\"v\"},\"score\":-17,\"ratio\":1.25,\"active\":false," +
                "\"removed\":null,\"contenttype\":\"application/json\"}";
            using var handler = new RecordingHttpHandler(message => message.Method == HttpMethod.Get
                ? HttpTestData.InspectionResponse(message) : new HttpResponseMessage(HttpStatusCode.NoContent));
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot, HttpTestData.Qualified);

            XRegistryResponse response = await endpoint.ExecuteAsync(
                new XRegistryRequest(XRegistryAction.Replace, HttpTestData.ResourcePath)
                {
                    Metadata = HttpTestData.Json(metadata),
                    Document = new ByteString(new byte[] { 0xff, 0, 0x7b, 0x7d }),
                    ContentType = "application/json"
                }).ConfigureAwait(false);

            Assert.That(handler.Requests, Has.Count.EqualTo(4));
            CapturedHttpRequest sent = handler.Requests[3];
            Assert.That(sent.Method, Is.EqualTo("PUT"));
            Assert.That(sent.Body, Is.EqualTo(new byte[] { 0xff, 0, 0x7b, 0x7d }));
            Assert.That(sent.Header("Content-Type"), Is.EqualTo("application/json"));
            Assert.That(sent.Header("xRegistry-epoch"), Is.EqualTo("18446744073709551616"));
            Assert.That(sent.Header("xRegistry-name"), Is.EqualTo("a%20%22b%22%25%E2%82%AC%01%2520%F0%9F%98%80"));
            Assert.That(sent.Header("xRegistry-labels.a.b"), Is.EqualTo("x%20y"));
            Assert.That(sent.Header("xRegistry-labels.keep"), Is.EqualTo("v"));
            Assert.That(sent.Header("xRegistry-score"), Is.EqualTo("-17"));
            Assert.That(sent.Header("xRegistry-ratio"), Is.EqualTo("1.25"));
            Assert.That(sent.Header("xRegistry-active"), Is.EqualTo("false"));
            Assert.That(sent.Header("xRegistry-removed"), Is.EqualTo("null"));
            Assert.That(sent.Headers.ContainsKey("xRegistry-contenttype"), Is.False);
            Assert.That(sent.Headers.ContainsKey("xRegistry-description"), Is.False, "Absent attributes stay absent.");
            Assert.That(response.StatusCode, Is.EqualTo(204));
        }

        [Test]
        public async Task DocumentResponseDecodesTypedMapAndLegacyEscapesOnce()
        {
            using var handler = new RecordingHttpHandler(message =>
            {
                if (message.RequestUri!.AbsolutePath == "/registry/model")
                {
                    return HttpTestData.JsonResponse(HttpTestData.Model);
                }
                HttpResponseMessage response = HttpTestData.BytesResponse([0x3b], "text/plain");
                response.Headers.TryAddWithoutValidation("xRegistry-epoch", "18446744073709551616");
                response.Headers.TryAddWithoutValidation("xRegistry-score", "-17");
                response.Headers.TryAddWithoutValidation("xRegistry-ratio", "1.25");
                response.Headers.TryAddWithoutValidation("xRegistry-active", "true");
                response.Headers.TryAddWithoutValidation("xRegistry-scores.major", "-2");
                response.Headers.TryAddWithoutValidation("xRegistry-checks.pass", "false");
                response.Headers.TryAddWithoutValidation("xRegistry-labels.a.b", "x%20y");
                response.Headers.TryAddWithoutValidation("xRegistry-name",
                    "a%20%22b%22%25%e2%82%ac%01%2520%f0%9f%98%80");
                response.Headers.TryAddWithoutValidation("xRegistry-description",
                    "\"old \\\"quote\\\" \\\\ %e2%82%ac %2520\"");
                response.Headers.TryAddWithoutValidation("xRegistry-deleted", "null");
                return response;
            });
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot);

            XRegistryResponse response = await endpoint.ExecuteAsync(
                new XRegistryRequest(XRegistryAction.Read, HttpTestData.ResourcePath)).ConfigureAwait(false);

            JsonElement metadata = response.Metadata;
            Assert.That(metadata.GetProperty("epoch").GetRawText(), Is.EqualTo("18446744073709551616"));
            Assert.That(metadata.GetProperty("score").GetInt32(), Is.EqualTo(-17));
            Assert.That(metadata.GetProperty("ratio").GetDecimal(), Is.EqualTo(1.25m));
            Assert.That(metadata.GetProperty("active").GetBoolean(), Is.True);
            Assert.That(metadata.GetProperty("scores").GetProperty("major").GetInt32(), Is.EqualTo(-2));
            Assert.That(metadata.GetProperty("checks").GetProperty("pass").GetBoolean(), Is.False);
            Assert.That(metadata.GetProperty("labels").GetProperty("a.b").GetString(), Is.EqualTo("x y"));
            Assert.That(metadata.GetProperty("name").GetString(), Is.EqualTo("a \"b\"%€\u0001%20😀"));
            Assert.That(metadata.GetProperty("description").GetString(), Is.EqualTo("old \"quote\" \\ € %20"));
            Assert.That(metadata.GetProperty("deleted").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(metadata.GetProperty("contenttype").GetString(), Is.EqualTo("text/plain"));
            Assert.That(response.Document.Span.ToArray(), Is.EqualTo(";"u8.ToArray()));
            Assert.That(handler.Requests, Has.Count.EqualTo(2));
        }

        [TestCase("name", "%")]
        [TestCase("name", "%GG")]
        [TestCase("name", "%C0%A0")]
        [TestCase("name", "%ff")]
        [TestCase("name", "\"unterminated")]
        [TestCase("name", "unescaped\"quote")]
        [TestCase("name", "\"incomplete\\\"")]
        [TestCase("epoch", "-1")]
        [TestCase("epoch", "1.0")]
        [TestCase("epoch", "true")]
        [TestCase("active", "TRUE")]
        [TestCase("active", "1")]
        [TestCase("complex", "{}")]
        [TestCase("list", "[]")]
        [TestCase("labels", "{}")]
        [TestCase("name.key", "v")]
        [TestCase("contenttype", "text/plain")]
        [TestCase("schema", "inline")]
        [TestCase("schemabase64", "eA==")]
        public async Task DocumentHeadersRejectMalformedOrComplexValues(string name, string value)
        {
            using var handler = new RecordingHttpHandler(message =>
            {
                if (message.RequestUri!.AbsolutePath == "/registry/model")
                {
                    return HttpTestData.JsonResponse(HttpTestData.Model);
                }
                HttpResponseMessage response = HttpTestData.BytesResponse([0x55]);
                response.Headers.TryAddWithoutValidation("xRegistry-" + name, value);
                return response;
            });
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot);

            Exception error = await HttpTestData.CatchAsync<Exception>(
                async () => await endpoint.ExecuteAsync(new XRegistryRequest(XRegistryAction.Read,
                    HttpTestData.ResourcePath))
                    .ConfigureAwait(false)).ConfigureAwait(false);

            Assert.That(error,
                Is.InstanceOf<IOException>().Or.TypeOf<InvalidDataException>().Or.InstanceOf<JsonException>());
            Assert.That(handler.Requests, Has.Count.EqualTo(2));
        }

        [TestCase("name", "name")]
        [TestCase("name", "NAME")]
        [TestCase("labels", "labels.a")]
        [TestCase("labels.a", "labels")]
        public async Task DocumentHeadersRejectDuplicateAndScalarMapCollisions(string first, string second)
        {
            using var handler = new RecordingHttpHandler(message =>
            {
                if (message.RequestUri!.AbsolutePath == "/registry/model")
                {
                    return HttpTestData.JsonResponse(HttpTestData.Model);
                }
                HttpResponseMessage response = HttpTestData.BytesResponse([0x55]);
                response.Headers.TryAddWithoutValidation("xRegistry-" + first, first == "labels" ? "null" : "a");
                response.Headers.TryAddWithoutValidation("xRegistry-" + second, second == "labels" ? "null" : "b");
                return response;
            });
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot);

            await Assert.ThatAsync(
                async () => await endpoint.ExecuteAsync(new XRegistryRequest(XRegistryAction.Read,
                    HttpTestData.ResourcePath))
                    .ConfigureAwait(false), Throws.InstanceOf<IOException>()).ConfigureAwait(false);
            Assert.That(handler.Requests, Has.Count.EqualTo(2));
        }

        [TestCase(/*lang=json,strict*/ """{"name":"null"}""")]
        [TestCase(/*lang=json,strict*/ """{"complex":{"child":1}}""")]
        [TestCase(/*lang=json,strict*/ """{"list":[1]}""")]
        [TestCase(/*lang=json,strict*/ """{"labels":{"nested":{"child":1}}}""")]
        [TestCase(/*lang=json,strict*/ """{"schema":"inline"}""")]
        [TestCase(/*lang=json,strict*/ """{"schemabase64":"eA=="}""")]
        [TestCase(/*lang=json,strict*/ """{"bad name":"value"}""")]
        [TestCase("[]")]
        public async Task DocumentWriteRejectsUnrepresentableHeaderMetadata(string metadata)
        {
            using var handler = new RecordingHttpHandler(HttpTestData.InspectionResponse);
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot, HttpTestData.Qualified);

            await Assert.ThatAsync(
                async () => await endpoint.ExecuteAsync(new XRegistryRequest(XRegistryAction.Replace,
                    HttpTestData.ResourcePath)
                {
                    Metadata = HttpTestData.Json(metadata),
                    Document = ByteString.Empty
                }).ConfigureAwait(false), Throws.InstanceOf<IOException>()).ConfigureAwait(false);
            Assert.That(handler.Requests, Has.Count.EqualTo(3), "No unrepresentable mutation may be sent.");
        }

        [TestCase(/*lang=json,strict*/ """{"epoch":"0"}""")]
        [TestCase(/*lang=json,strict*/ """{"epoch":1.25}""")]
        [TestCase(/*lang=json,strict*/ """{"epoch":-1}""")]
        [TestCase(/*lang=json,strict*/ """{"active":"true"}""")]
        [TestCase(/*lang=json,strict*/ """{"name":17}""")]
        [TestCase(/*lang=json,strict*/ """{"undeclared":17}""")]
        [TestCase(/*lang=json,strict*/ """{"scores":{"key":"17"}}""")]
        [TestCase(/*lang=json,strict*/ """{"labels.a":"value"}""")]
        [TestCase(/*lang=json,strict*/ """{"labels":{"KEY":"first","key":"second"}}""")]
        [TestCase(/*lang=json,strict*/ """{"name":"first","NAME":"second"}""")]
        public async Task DocumentHeaderEncodingCannotCoerceJsonTypesOrAttributeIdentities(string metadata)
        {
            using var handler = new RecordingHttpHandler(HttpTestData.InspectionResponse);
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot, HttpTestData.Qualified);

            await Assert.ThatAsync(async () => await endpoint.ExecuteAsync(
                new XRegistryRequest(XRegistryAction.Replace, HttpTestData.ResourcePath)
                {
                    Metadata = HttpTestData.Json(metadata),
                    Document = ByteString.Empty
                }).ConfigureAwait(false), Throws.InstanceOf<IOException>()).ConfigureAwait(false);

            Assert.That(handler.Requests, Has.Count.EqualTo(3), "Representability must be checked before any PUT.");
            Assert.That(handler.Requests.All(request => request.Method == "GET"), Is.True);
        }

        [TestCase(/*lang=json,strict*/ """{"contenttype":"text/plain"}""", null)]
        [TestCase(/*lang=json,strict*/ """{"contenttype":"text/plain"}""", "application/json")]
        [TestCase(/*lang=json,strict*/ """{"contenttype":null}""", "text/plain")]
        [TestCase(/*lang=json,strict*/ """{"contenttype":17}""", null)]
        public async Task ConflictingDocumentContentTypesCannotSilentlyChangeTheRequest(
            string metadata,
            string? contentType)
        {
            using var handler = new RecordingHttpHandler(HttpTestData.InspectionResponse);
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot, HttpTestData.Qualified);

            await Assert.ThatAsync(async () => await endpoint.ExecuteAsync(
                new XRegistryRequest(XRegistryAction.Replace, HttpTestData.ResourcePath)
                {
                    Metadata = HttpTestData.Json(metadata),
                    ContentType = contentType,
                    Document = ByteString.Empty
                }).ConfigureAwait(false), Throws.InstanceOf<IOException>()).ConfigureAwait(false);

            Assert.That(handler.Requests, Has.Count.EqualTo(3));
        }

        [TestCase(204)]
        [TestCase(303)]
        [TestCase(304)]
        public async Task BodylessDocumentStatusesCannotReturnSuccessShapedContent(int status)
        {
            using var handler = new RecordingHttpHandler(message => message.RequestUri!.AbsolutePath ==
                "/registry/model"
                ? HttpTestData.JsonResponse(HttpTestData.Model)
                : HttpTestData.BytesResponse([0x41], status: status));
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot);

            await Assert.ThatAsync(async () => await endpoint.ExecuteAsync(
                new XRegistryRequest(XRegistryAction.Read, HttpTestData.ResourcePath)).ConfigureAwait(false),
                Throws.TypeOf<InvalidDataException>()).ConfigureAwait(false);

            Assert.That(handler.Requests, Has.Count.EqualTo(2));
        }

        [TestCase("{}")]
        [TestCase("null")]
        [TestCase(/*lang=json,strict*/ """{"name":"null","removed":null}""")]
        public async Task MetadataWritesPreserveEmptyObjectJsonNullAndLiteralNullString(string metadata)
        {
            using var handler = new RecordingHttpHandler(message => message.Method == HttpMethod.Get
                ? HttpTestData.InspectionResponse(message) : new HttpResponseMessage(HttpStatusCode.NoContent));
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot, HttpTestData.Qualified);

            XRegistryResponse result = await endpoint.ExecuteAsync(
                new XRegistryRequest(XRegistryAction.Merge, HttpTestData.ResourcePath)
                {
                    View = XRegistryView.Metadata,
                    Metadata = HttpTestData.Json(metadata)
                }).ConfigureAwait(false);

            Assert.That(handler.Requests, Has.Count.EqualTo(4));
            Assert.That(handler.Requests[3].Method, Is.EqualTo("PATCH"));
            Assert.That(handler.Requests[3].Uri,
                Is.EqualTo("https://registry.example/registry/schemagroups/g/schemas/r$details"));
            Assert.That(Encoding.UTF8.GetString(handler.Requests[3].Body), Is.EqualTo(metadata));
            Assert.That(handler.Requests[3].Header("Content-Type"), Is.EqualTo("application/json; charset=utf-8"));
            Assert.That(handler.Requests[3].Headers.Keys.Any(key => key.StartsWith("xRegistry-",
                StringComparison.OrdinalIgnoreCase)),
                Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(204));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task DocumentWriteAcceptsPresentEmptyBytesOrExternalReferenceWithoutFetching(bool external)
        {
            using var handler = new RecordingHttpHandler(message => message.Method == HttpMethod.Get
                ? HttpTestData.InspectionResponse(message) : new HttpResponseMessage(HttpStatusCode.NoContent));
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot, HttpTestData.Qualified);

            XRegistryResponse response = await endpoint.ExecuteAsync(
                new XRegistryRequest(XRegistryAction.Replace, HttpTestData.ResourcePath)
                {
                    Document = external ? default : ByteString.Empty,
                    Metadata = external ? HttpTestData.Json(
                                             /*lang=json,strict*/
                                             """{"schemaurl":"https://external.example/document"}""") : default
                }).ConfigureAwait(false);

            Assert.That(response.StatusCode, Is.EqualTo(204));
            Assert.That(handler.Requests, Has.Count.EqualTo(4));
            Assert.That(handler.Requests[3].HasContent, Is.True);
            Assert.That(handler.Requests[3].Body, Is.Empty);
            Assert.That(handler.Requests[3].Headers.ContainsKey("Content-Type"), Is.False);
            Assert.That(handler.Requests[3].Headers.ContainsKey("xRegistry-schemaurl"), Is.EqualTo(external));
            if (external)
            {
                Assert.That(handler.Requests[3].Header("xRegistry-schemaurl"),
                    Is.EqualTo("https://external.example/document"));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task DocumentWriteRejectsAbsentBytesOrExternalReferenceWithNonemptyBody(bool external)
        {
            using var handler = new RecordingHttpHandler(HttpTestData.InspectionResponse);
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot, HttpTestData.Qualified);

            await Assert.ThatAsync(
                async () => await endpoint.ExecuteAsync(new XRegistryRequest(XRegistryAction.Replace,
                    HttpTestData.ResourcePath)
                {
                    Document = external ? new ByteString(new byte[] { 0x01 }) : default,
                    Metadata = external ? HttpTestData.Json(
                                             /*lang=json,strict*/
                                             """{"schemaurl":"https://external.example/document"}""") : default
                }).ConfigureAwait(false), Throws.InstanceOf<IOException>()).ConfigureAwait(false);
            Assert.That(handler.Requests, Has.Count.EqualTo(3));
        }

        [TestCase(XRegistryAction.Merge, HttpTestData.ResourcePath, "PATCH")]
        [TestCase(XRegistryAction.Replace, "/schemagroups", "collection")]
        [TestCase(XRegistryAction.Replace, "/schemagroups/g/schemas", "collection")]
        [TestCase(XRegistryAction.Replace, "/schemagroups/g/schemas/r/versions", "collection")]
        public async Task CollectionPutAndRawDocumentPatchAreRejected(XRegistryAction action, string path,
            string reason)
        {
            using var handler = new RecordingHttpHandler(HttpTestData.InspectionResponse);
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot, HttpTestData.Qualified);

            await Assert.ThatAsync(async () => await endpoint.ExecuteAsync(new XRegistryRequest(action, path)
            {
                Metadata = HttpTestData.Json("{}"),
                Document = action == XRegistryAction.Merge ? ByteString.Empty : default
            }).ConfigureAwait(false),
                Throws.InstanceOf<IOException>().With.Message.Contains(reason)).ConfigureAwait(false);
            Assert.That(handler.Requests, Has.Count.EqualTo(3));
        }

        [Test]
        public async Task MissingMetadataWriteBodyRejectsWithoutMutation()
        {
            using var handler = new RecordingHttpHandler(HttpTestData.InspectionResponse);
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot, HttpTestData.Qualified);

            await Assert.ThatAsync(async () => await endpoint.ExecuteAsync(
                new XRegistryRequest(XRegistryAction.Replace, HttpTestData.RecordPath)).ConfigureAwait(false),
                Throws.InstanceOf<IOException>().With.Message.Contains("body")).ConfigureAwait(false);
            Assert.That(handler.Requests, Has.Count.EqualTo(3));
        }

        [Test]
        public async Task EmptyDocumentHeaderMapCannotSilentlyBecomeAnAbsentAttribute()
        {
            using var handler = new RecordingHttpHandler(message => message.Method == HttpMethod.Get
                ? HttpTestData.InspectionResponse(message) : new HttpResponseMessage(HttpStatusCode.NoContent));
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot, HttpTestData.Qualified);

            await Assert.ThatAsync(async () => await endpoint.ExecuteAsync(
                new XRegistryRequest(XRegistryAction.Replace, HttpTestData.ResourcePath)
                {
                    Metadata = HttpTestData.Json(/*lang=json,strict*/ """{"labels":{}}"""),
                    Document = ByteString.Empty
                }).ConfigureAwait(false), Throws.InstanceOf<IOException>()).ConfigureAwait(false);

            Assert.That(handler.Requests, Has.Count.EqualTo(3),
                "An empty replacement map cannot be expressed by sending no attribute headers (which means unchanged)."
                    );
        }

        [TestCase(303)]
        [TestCase(304)]
        public async Task DocumentRedirectHasAbsentBytesAndPreservesMetadataWithoutFollowing(int status)
        {
            using var handler = new RecordingHttpHandler(message =>
            {
                if (message.RequestUri!.AbsolutePath == "/registry/model")
                {
                    return HttpTestData.JsonResponse(HttpTestData.Model);
                }
                HttpResponseMessage response = HttpTestData.BytesResponse([], status: status);
                response.Headers.Location = new Uri("https://registry.example/registry/schemagroups/g/schemas/other");
                response.Headers.TryAddWithoutValidation("xRegistry-epoch", "0");
                response.Headers.TryAddWithoutValidation("xRegistry-schemaurl", "https://external.example/document");
                return response;
            });
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot);

            XRegistryResponse result = await endpoint.ExecuteAsync(
                new XRegistryRequest(XRegistryAction.Read, HttpTestData.ResourcePath)).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(status));
            Assert.That(result.Document.IsNull, Is.True);
            Assert.That(result.Metadata.GetProperty("epoch").GetInt32(), Is.Zero);
            Assert.That(result.Metadata.GetProperty("schemaurl").GetString(),
                Is.EqualTo("https://external.example/document"));
            Assert.That(result.Location, Is.EqualTo("/schemagroups/g/schemas/other"));
            Assert.That(handler.Requests, Has.Count.EqualTo(2));
        }

        [TestCase(/*lang=json,strict*/ """{"groups":{"schemagroups":17}}""")]
        [TestCase(/*lang=json,strict*/ """{"groups":{"schemagroups":{"resources":{"schemas":17}}}}""")]
        [TestCase(/*lang=json,strict*/ """{"groups":{"schemagroups":{"resources":{"schemas":{}}}}}""")]
        [TestCase(/*lang=json,strict*/ """{"groups":{"schemagroups":{"resources":{"schemas":{"singular":""}}}}}""")]
        [TestCase(/*lang=json,strict*/ """{"groups":{"schemagroups":{"resources":{"schemas":{"singular":17}}}}}""")]
        [TestCase(
            /*lang=json,strict*/
            """
            {"groups":{"schemagroups":{"resources":{"schemas":{"singular":"schema","hasdocument":"false"}}}}}
            """)]
        public async Task MalformedEffectiveModelCannotInventAResourceRepresentation(string model)
        {
            using var handler = new RecordingHttpHandler(message =>
            {
                Assert.That(message.RequestUri!.AbsolutePath, Is.EqualTo("/registry/model"));
                return HttpTestData.JsonResponse(model);
            });
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot);

            await Assert.ThatAsync(async () => await endpoint.ExecuteAsync(
                new XRegistryRequest(XRegistryAction.Read, HttpTestData.ResourcePath)).ConfigureAwait(false),
                Throws.InstanceOf<JsonException>()).ConfigureAwait(false);

            Assert.That(handler.Requests, Has.Count.EqualTo(1));
        }

        [TestCase("attributes")]
        [TestCase("resourceattributes")]
        public async Task ModelAttributeContainerDeterminesNumericHeaderType(string container)
        {
            string model = "{\"groups\":{\"schemagroups\":{\"resources\":{\"schemas\":{\"singular\":\"schema\",\"" +
                container +
                "\":{\"priority\":{\"type\":\"integer\"}}}}}}}";
            using var handler = new RecordingHttpHandler(message =>
            {
                if (message.RequestUri!.AbsolutePath == "/registry/model")
                {
                    return HttpTestData.JsonResponse(model);
                }
                HttpResponseMessage response = HttpTestData.BytesResponse([0x41]);
                response.Headers.TryAddWithoutValidation("xRegistry-priority", "-7");
                return response;
            });
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot);

            XRegistryResponse response = await endpoint.ExecuteAsync(
                new XRegistryRequest(XRegistryAction.Read, HttpTestData.ResourcePath)).ConfigureAwait(false);

            Assert.That(response.Metadata.GetProperty("priority").GetInt32(), Is.EqualTo(-7));
            Assert.That(response.Document.Span.ToArray(), Is.EqualTo("A"u8.ToArray()));
            Assert.That(handler.Requests, Has.Count.EqualTo(2));
        }
    }
}
