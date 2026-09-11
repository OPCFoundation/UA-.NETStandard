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
    public sealed class XRegistryHttpEndpointTests
    {
        [Test]
        public async Task InspectRetrievesRootModelAndCapabilitiesInOrder()
        {
            using var handler = new RecordingHttpHandler(HttpTestData.InspectionResponse);
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot, HttpTestData.Qualified);

            XRegistryEndpointDescription result = await endpoint.InspectAsync(XRegistryCallContext.Anonymous)
                .ConfigureAwait(false);

            string[] expectedUris =
            [
                "https://registry.example/registry/",
                "https://registry.example/registry/model",
                "https://registry.example/registry/capabilities"
            ];
            Assert.That(handler.Requests.Select(request => request.Uri), Is.EqualTo(expectedUris));
            Assert.That(handler.Requests.All(request => request.Method == "GET" && !request.HasContent), Is.True);
            Assert.That(result.RegistryId, Is.EqualTo("golden-registry"));
            Assert.That(result.Profile, Is.EqualTo("http-1.0-rc4-qualified"));
            Assert.That(result.Model.GetRawText(), Is.EqualTo(HttpTestData.Model));
            Assert.That(result.Capabilities.GetRawText(), Is.EqualTo(HttpTestData.Capabilities));
            JsonElement extension = result.Model.GetProperty("extension");
            Assert.That(extension.GetProperty("number").GetDecimal(), Is.EqualTo(1.25m));
            Assert.That(extension.GetProperty("boolean").GetBoolean(), Is.True);
            Assert.That(extension.GetProperty("array").GetRawText(), Is.EqualTo("[0,false,null]"));
            Assert.That(extension.GetProperty("epoch").GetRawText(), Is.EqualTo("18446744073709551616"));
            Assert.That(result.SupportsAtomicMutations, Is.True);
            Assert.That(result.SupportsConditionalMutations, Is.True);
            Assert.That(result.SupportsWriteTouch, Is.True);
            Assert.That(result.SupportsOperationReplay, Is.False);
        }

        [TestCase(false, "1.0-rc4", "1.0-rc4", "entities", false)]
        [TestCase(true, "1.0-rc4", "1.0-rc4", "entities", true)]
        [TestCase(true, "1.0-rc4", "1.0-rc4", "modelsource", true)]
        [TestCase(true, "1.0-rc4", "1.0-rc4", "model", false)]
        [TestCase(true, "1.0-rc4", "1.0-rc4", "capabilities", true)]
        [TestCase(true, "1.0-rc4", "1.0-rc4", "", false)]
        [TestCase(true, "1.0-rc4", "1.0-rc4", "future", false)]
        [TestCase(true, "1.0-rc5", "1.0-rc4", "entities", false)]
        [TestCase(true, "1.0-rc4", "1.0-rc5", "entities", false)]
        [TestCase(true, "", "1.0-rc4", "entities", false)]
        public async Task InspectRequiresExplicitPinnedMutableQualification(
            bool qualified, string rootVersion, string advertisedVersion, string mutable, bool expected)
        {
            using var handler = new RecordingHttpHandler(message => message.RequestUri!.AbsolutePath switch
            {
                "/registry/" => HttpTestData.JsonResponse(
                    "{\"registryid\":\"r\",\"specversion\":\"" + rootVersion + "\"}"),
                "/registry/model" => HttpTestData.JsonResponse("{}"),
                _ => HttpTestData.JsonResponse("{\"specversions\":[\"" +
                    advertisedVersion +
                    "\"],\"available\":{\"" +
                    mutable +
                    "\":{\"mutable\":true}},\"mutable\":[]}")
            });
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot,
                new XRegistryHttpOptions { IsQualifiedBinding = qualified });

            XRegistryEndpointDescription result = await endpoint.InspectAsync(XRegistryCallContext.Anonymous)
                .ConfigureAwait(false);

            Assert.That(result.Profile, Is.EqualTo(expected ? "http-1.0-rc4-qualified" : "http-unqualified"));
            Assert.That(result.SupportsAtomicMutations, Is.EqualTo(expected));
            Assert.That(result.SupportsConditionalMutations, Is.EqualTo(expected));
            Assert.That(result.SupportsWriteTouch, Is.EqualTo(expected));
            Assert.That(result.SupportsOperationReplay, Is.False);
            Assert.That(handler.Requests, Has.Count.EqualTo(3));
        }

        [TestCase("/registry/", 1, 400)]
        [TestCase("/registry/model", 2, 403)]
        [TestCase("/registry/capabilities", 3, 503)]
        public async Task InspectPreservesCompleteBackendProblemAtEveryStage(string failedPath, int calls, int status)
        {
            using var handler = new RecordingHttpHandler(message => message.RequestUri!.AbsolutePath == failedPath
                ? HttpTestData.JsonResponse(HttpTestData.Problem, status)
                : HttpTestData.InspectionResponse(message));
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot);

            XRegistryHttpException failure = await HttpTestData.CatchAsync<XRegistryHttpException>(
                async () => await endpoint.InspectAsync(XRegistryCallContext.Anonymous).ConfigureAwait(false))
                .ConfigureAwait(false);

            Assert.That(failure.Response!.StatusCode, Is.EqualTo(status));
            Assert.That(failure.Response.Metadata.GetRawText(), Is.EqualTo(HttpTestData.Problem));
            HttpTestData.AssertProblem(failure.Response.Metadata);
            Assert.That(failure.Response.Error!.Code, Is.EqualTo("mismatched_epoch"));
            Assert.That(failure.Response.Error.Subject, Is.EqualTo("/schemagroups/g"));
            Assert.That(failure.Response.Document.IsNull, Is.True);
            Assert.That(handler.Requests, Has.Count.EqualTo(calls));
        }

        [TestCase("/registry/", 1)]
        [TestCase("/registry/model", 2)]
        [TestCase("/registry/capabilities", 3)]
        public async Task InspectPreservesNonJsonErrorBytes(string failedPath, int calls)
        {
            byte[] raw = [0xff, 0x00, 0x3c, 0x3e, 0x80];
            using var handler = new RecordingHttpHandler(message => message.RequestUri!.AbsolutePath == failedPath
                ? HttpTestData.BytesResponse(raw, "text/html", 502) : HttpTestData.InspectionResponse(message));
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot);

            XRegistryHttpException failure = await HttpTestData.CatchAsync<XRegistryHttpException>(
                async () => await endpoint.InspectAsync(XRegistryCallContext.Anonymous).ConfigureAwait(false))
                .ConfigureAwait(false);

            Assert.That(failure.Response!.StatusCode, Is.EqualTo(502));
            Assert.That(failure.Response.Document.Span.ToArray(), Is.EqualTo(new byte[] { 0xff, 0, 0x3c, 0x3e, 0x80 }));
            Assert.That(failure.Response.Metadata.ValueKind, Is.EqualTo(JsonValueKind.Undefined));
            Assert.That(failure.Response.Error, Is.Null);
            Assert.That(failure.Response.ContentType, Is.EqualTo("text/html"));
            Assert.That(handler.Requests, Has.Count.EqualTo(calls));
        }

        [TestCase("/registry/", 1)]
        [TestCase("/registry/model", 2)]
        [TestCase("/registry/capabilities", 3)]
        public async Task InspectPropagatesTransportFailure(string failedPath, int calls)
        {
            var expected = new HttpRequestException("wire disconnected");
            using var handler = new RecordingHttpHandler((message, _) => message.RequestUri!.AbsolutePath == failedPath
                ? Task.FromException<HttpResponseMessage>(expected)
                : Task.FromResult(HttpTestData.InspectionResponse(message)));
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot);

            HttpRequestException actual = await HttpTestData.CatchAsync<HttpRequestException>(
                async () => await endpoint.InspectAsync(XRegistryCallContext.Anonymous).ConfigureAwait(false))
                .ConfigureAwait(false);

            Assert.That(actual, Is.SameAs(expected));
            Assert.That(handler.Requests, Has.Count.EqualTo(calls));
        }

        [TestCase("{}")]
        [TestCase(/*lang=json,strict*/ """{"registryid":"r"}""")]
        [TestCase(/*lang=json,strict*/ """{"registryid":"","specversion":"1.0-rc4"}""")]
        [TestCase(/*lang=json,strict*/ """{"registryid":5,"specversion":"1.0-rc4"}""")]
        [TestCase(/*lang=json,strict*/ """{"registryid":"r","specversion":4}""")]
        [TestCase("[]")]
        public async Task InspectionRequiresActualRootIdentityAndVersion(string rawRoot)
        {
            using var handler = new RecordingHttpHandler(_ => HttpTestData.JsonResponse(rawRoot));
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot);

            await Assert.ThatAsync(async () => await endpoint.InspectAsync(XRegistryCallContext.Anonymous)
                .ConfigureAwait(false), Throws.TypeOf<InvalidDataException>()).ConfigureAwait(false);
            Assert.That(handler.Requests, Has.Count.EqualTo(1));
        }

        [TestCase("/registry/model", "null")]
        [TestCase("/registry/model", "[]")]
        [TestCase("/registry/capabilities", "false")]
        [TestCase("/registry/capabilities", "[]")]
        public async Task InspectionRequiresObjectModelAndCapabilities(string path, string body)
        {
            using var handler = new RecordingHttpHandler(message => message.RequestUri!.AbsolutePath == path
                ? HttpTestData.JsonResponse(body) : HttpTestData.InspectionResponse(message));
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot);

            await Assert.ThatAsync(async () => await endpoint.InspectAsync(XRegistryCallContext.Anonymous)
                .ConfigureAwait(false), Throws.TypeOf<InvalidDataException>()).ConfigureAwait(false);
            Assert.That(handler.Requests, Has.Count.EqualTo(3));
        }

        [TestCase("https://registry.example/r", false, "https://registry.example/r/")]
        [TestCase("http://localhost:1234/r", true, "http://localhost:1234/r/")]
        [TestCase("http://127.0.0.2/r", true, "http://127.0.0.2/r/")]
        [TestCase("http://[::1]/r", true, "http://[::1]/r/")]
        [TestCase("https://registry.example/cafe%cc%81", false, "https://registry.example/cafe%CC%81/")]
        public void ConstructorAcceptsOnlySecureOrExplicitLoopbackRoots(string root, bool loopback, string canonical)
        {
            using var handler = new RecordingHttpHandler(HttpTestData.InspectionResponse);
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, new Uri(root),
                new XRegistryHttpOptions { AllowLoopbackHttp = loopback });

            Assert.That(endpoint.RegistryRoot.AbsoluteUri, Is.EqualTo(canonical));
            Assert.That(handler.Requests, Is.Empty);
        }

        [TestCase("http://localhost/r", false)]
        [TestCase("http://127.0.0.1/r", false)]
        [TestCase("http://registry.example/r", true)]
        [TestCase("http://localhost.example/r", true)]
        [TestCase("http://192.168.0.1/r", true)]
        [TestCase("https://user:secret@registry.example/r", false)]
        [TestCase("https://registry.example/r?q=x", false)]
        [TestCase("https://registry.example/r#x", false)]
        [TestCase("ftp://registry.example/r", false)]
        [TestCase("/relative", false)]
        public void ConstructorRejectsUnsafeRoots(string root, bool loopback)
        {
            using var handler = new RecordingHttpHandler(HttpTestData.InspectionResponse);
            using var client = new HttpClient(handler);

            Assert.That(() => new XRegistryHttpEndpoint(client, new Uri(root, UriKind.RelativeOrAbsolute),
                new XRegistryHttpOptions { AllowLoopbackHttp = loopback }), Throws.ArgumentException);
            Assert.That(handler.Requests, Is.Empty);
        }

        [Test]
        public async Task ExecutePreservesOrderedFlagsAndCanonicalDetailsPath()
        {
            using var handler = new RecordingHttpHandler(message => message.RequestUri!.AbsolutePath ==
                "/registry/model"
                ? HttpTestData.JsonResponse(HttpTestData.Model)
                : HttpTestData.JsonResponse(
                    /*lang=json,strict*/ """{"epoch":18446744073709551616,"value":[true,null,1.5]}"""));
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot);
            var request = new XRegistryRequest(XRegistryAction.Read, "/schemagroups/cafe%cc%81/schemas/100%25")
            {
                View = XRegistryView.Metadata,
                Parameters =
                [
                    new("epoch", "0"), new("epoch", "18446744073709551616"), new("epoch", "null"),
                    new("inline", null), new("inline", string.Empty), new("inline", "a,b"),
                    new("filter", "x=1,y=true"), new("filter", "name=a b"),
                    new("ignore", null), new("ignore", string.Empty), new("ignore", "*"),
                    new("sort", "name=desc"), new("doc", null), new("binary", null),
                    new("collections", null), new("specversion", "1.0-rc4"),
                    new("setdefaultversionid", "v2"), new("future", "a+b&c")
                ]
            };

            XRegistryResponse response = await endpoint.ExecuteAsync(request).ConfigureAwait(false);

            Assert.That(handler.Requests, Has.Count.EqualTo(2));
            Assert.That(handler.Requests[0].Uri, Is.EqualTo("https://registry.example/registry/model"));
            Assert.That(handler.Requests[1].Method, Is.EqualTo("GET"));
            Assert.That(handler.Requests[1].Uri, Is.EqualTo(
                "https://registry.example/registry/schemagroups/cafe%CC%81/schemas/100%25$details" +
                "?epoch=0&epoch=18446744073709551616&epoch=null&inline&inline=&inline=a%2Cb" +
                "&filter=x%3D1%2Cy%3Dtrue&filter=name%3Da%20b&ignore&ignore=&ignore=%2A" +
                "&sort=name%3Ddesc&doc&binary&collections&specversion=1.0-rc4&setdefaultversionid=v2&future=a%2Bb%26c")
                    );
            Assert.That(handler.Requests[1].HasContent, Is.False);
            Assert.That(response.Metadata.GetProperty("epoch").GetRawText(), Is.EqualTo("18446744073709551616"));
            Assert.That(response.Metadata.GetProperty("value").GetRawText(), Is.EqualTo("[true,null,1.5]"));
        }

        [TestCase("/schemagroups/g/schemas/r%24details", XRegistryView.Default, "r%24details")]
        [TestCase("/schemagroups/g/schemas/r%2524details", XRegistryView.Metadata, "r%2524details$details")]
        public async Task EncodedDollarIdentityIsNotTheRawDetailsSuffix(
            string path, XRegistryView view, string suffix)
        {
            using var handler = new RecordingHttpHandler(message => message.RequestUri!.AbsolutePath ==
                "/registry/model"
                ? HttpTestData.JsonResponse(HttpTestData.Model)
                : view == XRegistryView.Metadata
                    ? HttpTestData.JsonResponse(/*lang=json,strict*/ """{"schemaid":"literal"}""")
                    : HttpTestData.BytesResponse([0x21], "application/octet-stream"));
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot);

            XRegistryResponse response = await endpoint.ExecuteAsync(
                new XRegistryRequest(XRegistryAction.Read, path) { View = view }).ConfigureAwait(false);

            Assert.That(handler.Requests[1].Uri,
                Is.EqualTo("https://registry.example/registry/schemagroups/g/schemas/" + suffix));
            Assert.That(response.StatusCode, Is.EqualTo(200));
            Assert.That(response.Document.IsNull, Is.EqualTo(view == XRegistryView.Metadata));
        }

        [TestCase("/")]
        [TestCase("/model")]
        [TestCase("/modelsource")]
        [TestCase("/capabilities")]
        [TestCase("/capabilitiesoffered")]
        [TestCase("/export")]
        [TestCase("/.xregistry")]
        public async Task KnownRoutesForwardWithoutInventedResources(string path)
        {
            using var handler = new RecordingHttpHandler(_ => HttpTestData.JsonResponse(
                                     /*lang=json,strict*/
                                     """{"actual":"backend","n":7}"""));
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot);

            XRegistryResponse result = await endpoint.ExecuteAsync(new XRegistryRequest(XRegistryAction.Read, path))
                .ConfigureAwait(false);

            Assert.That(handler.Requests, Has.Count.EqualTo(path is "/" or "/export" ? 2 : 1));
            Assert.That(handler.Requests[0].Uri, Is.EqualTo("https://registry.example/registry" + path));
            Assert.That(result.Metadata.GetRawText(),
                Is.EqualTo(/*lang=json,strict*/ """{"actual":"backend","n":7}"""));
            Assert.That(result.Document.IsNull, Is.True);
        }

        [TestCase("/")]
        [TestCase("/export")]
        public async Task RootAndExportUseTheModelToCanonicalizeEveryInlineNavigationPath(string path)
        {
            const string body = /*lang=json,strict*/ """
                {
                  "self":"https://registry.example/registry/",
                  "schemagroupsurl":"https://registry.example/registry/schemagroups",
                  "schemagroups":{
                    "g":{
                      "self":"https://registry.example/registry/schemagroups/g",
                      "schemas":{
                        "r":{
                          "self":"https://registry.example/registry/schemagroups/g/schemas/r$details",
                          "schemaurl":"https://external.example/document",
                          "schema":{"self":"https://external.example/embedded"}
                        }
                      }
                    }
                  }
                }
                """;
            using var handler = new RecordingHttpHandler(
                message => message.RequestUri!.AbsolutePath == "/registry/model"
                ? HttpTestData.JsonResponse(HttpTestData.Model) : HttpTestData.JsonResponse(body));
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot);

            XRegistryResponse response = await endpoint.ExecuteAsync(new XRegistryRequest(XRegistryAction.Read, path)
            {
                Parameters = [new("inline", "*")]
            }).ConfigureAwait(false);

            Assert.That(handler.Requests, Has.Count.EqualTo(2));
            Assert.That(handler.Requests[0].Uri,
                Is.EqualTo("https://registry.example/registry" + path + "?inline=%2A"));
            Assert.That(handler.Requests[1].Uri, Is.EqualTo("https://registry.example/registry/model"));
            Assert.That(response.Metadata.GetProperty("self").GetString(), Is.EqualTo("/"));
            Assert.That(response.Metadata.GetProperty("schemagroupsurl").GetString(), Is.EqualTo("/schemagroups"));
            JsonElement group = response.Metadata.GetProperty("schemagroups").GetProperty("g");
            Assert.That(group.GetProperty("self").GetString(), Is.EqualTo("/schemagroups/g"));
            JsonElement resource = group.GetProperty("schemas").GetProperty("r");
            Assert.That(resource.GetProperty("self").GetString(), Is.EqualTo(HttpTestData.ResourcePath));
            Assert.That(resource.GetProperty("schemaurl").GetString(), Is.EqualTo("https://external.example/document"));
            Assert.That(resource.GetProperty("schema").GetProperty("self").GetString(),
                Is.EqualTo("https://external.example/embedded"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task RootInspectionCannotHideForeignInlineLinksOrReplaceAnAuthoritativeRejection(bool rejected)
        {
            using var handler = new RecordingHttpHandler(
                message => message.RequestUri!.AbsolutePath == "/registry/model"
                ? HttpTestData.JsonResponse(HttpTestData.Model)
                : HttpTestData.JsonResponse(rejected ? HttpTestData.Problem :
                                         /*lang=json,strict*/
                                         """{"schemagroups":{"g":{"self":"https://foreign.example/group"}}}""",
                                             rejected ? 403 : 200));
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot);

            if (rejected)
            {
                XRegistryResponse response = await endpoint.ExecuteAsync(
                    new XRegistryRequest(XRegistryAction.Read, "/"))
                    .ConfigureAwait(false);

                Assert.That(response.StatusCode, Is.EqualTo(403));
                HttpTestData.AssertProblem(response.Metadata);
                Assert.That(handler.Requests, Has.Count.EqualTo(1),
                    "A rejection needs no model-dependent link decoding.");
            }
            else
            {
                await Assert.ThatAsync(async () => await endpoint.ExecuteAsync(
                    new XRegistryRequest(XRegistryAction.Read, "/")).ConfigureAwait(false),
                    Throws.TypeOf<InvalidDataException>()).ConfigureAwait(false);

                Assert.That(handler.Requests, Has.Count.EqualTo(2));
            }
        }

        [TestCase(XRegistryAction.Replace, "PUT")]
        [TestCase(XRegistryAction.Merge, "PATCH")]
        [TestCase(XRegistryAction.Create, "POST")]
        [TestCase(XRegistryAction.Delete, "DELETE")]
        public async Task ActionsMapToExactWireMethods(XRegistryAction action, string method)
        {
            using var handler = new RecordingHttpHandler(message => message.Method == HttpMethod.Get
                ? HttpTestData.InspectionResponse(message) : new HttpResponseMessage(HttpStatusCode.NoContent));
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot, HttpTestData.Qualified);
            var request = new XRegistryRequest(action, "/schemagroups/g")
            {
                Metadata = action ==
                    XRegistryAction.Delete ? default : HttpTestData.Json(/*lang=json,strict*/ """{"epoch":0}""")
            };

            XRegistryResponse response = await endpoint.ExecuteAsync(request).ConfigureAwait(false);

            Assert.That(handler.Requests, Has.Count.EqualTo(4));
            Assert.That(handler.Requests[3].Method, Is.EqualTo(method));
            Assert.That(handler.Requests[3].Uri, Is.EqualTo("https://registry.example/registry/schemagroups/g"));
            Assert.That(handler.Requests[3].Body, Is.EqualTo(action == XRegistryAction.Delete
                ? [] : Encoding.UTF8.GetBytes(/*lang=json,strict*/ """{"epoch":0}""")));
            Assert.That(response.StatusCode, Is.EqualTo(204));
            Assert.That(response.Document.IsNull, Is.True);
        }

        [Test]
        public async Task QualifiedMutationSendsOneCompleteNestedRequest()
        {
            const string body =
                """{"epoch":0,"schemagroups":{"g":{"schemas":{"r":{"meta":{"epoch":18446744073709551616},""" +
                "\"schema\":{\"type\":\"object\"},\"labels\":{\"env\":\"test\"}}}}}}";
            using var handler = new RecordingHttpHandler(message => message.Method == HttpMethod.Get
                ? HttpTestData.InspectionResponse(message)
                : HttpTestData.JsonResponse(/*lang=json,strict*/ """{"epoch":1,"processed":1}""", 201));
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot, HttpTestData.Qualified);

            XRegistryResponse result = await endpoint.ExecuteAsync(new XRegistryRequest(XRegistryAction.Merge, "/")
            {
                Metadata = HttpTestData.Json(body)
            }).ConfigureAwait(false);

            string[] expectedMethods = ["GET", "GET", "GET", "PATCH"];
            Assert.That(handler.Requests.Select(item => item.Method), Is.EqualTo(expectedMethods));
            Assert.That(handler.Requests[3].Uri, Is.EqualTo("https://registry.example/registry/"));
            Assert.That(Encoding.UTF8.GetString(handler.Requests[3].Body), Is.EqualTo(body));
            Assert.That(handler.Requests[3].Header("Content-Type"), Is.EqualTo("application/json; charset=utf-8"));
            Assert.That(result.StatusCode, Is.EqualTo(201));
            Assert.That(result.Metadata.GetProperty("processed").GetInt32(), Is.EqualTo(1));
        }

        [TestCase(false, null, XRegistryAction.Replace)]
        [TestCase(false, null, XRegistryAction.Merge)]
        [TestCase(false, null, XRegistryAction.Create)]
        [TestCase(false, null, XRegistryAction.Delete)]
        [TestCase(true, "operation-17", XRegistryAction.Replace)]
        [TestCase(true, "", XRegistryAction.Read)]
        public async Task ReplayAndUnqualifiedMutationsRejectBeforeSending(
            bool qualified, string? operationId, XRegistryAction action)
        {
            using var handler = new RecordingHttpHandler(HttpTestData.InspectionResponse);
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot,
                new XRegistryHttpOptions { IsQualifiedBinding = qualified });

            XRegistryResponse response = await endpoint.ExecuteAsync(new XRegistryRequest(action, "/")
            {
                OperationId = operationId
            }).ConfigureAwait(false);

            Assert.That(response.StatusCode, Is.EqualTo(405));
            Assert.That(response.Error!.Code, Is.EqualTo("action_not_supported"));
            Assert.That(response.Error.Subject, Is.EqualTo("/"));
            Assert.That(response.AllowedActions.ToArray(), Is.EqualTo([ XRegistryAction.Read,
                XRegistryAction.Describe ]));
            Assert.That(handler.Requests, Is.Empty);
        }

        [Test]
        public async Task LostMutationResponseNeverRetries()
        {
            var lost = new HttpRequestException("response lost after commit");
            using var handler = new RecordingHttpHandler((message, _) => message.Method == HttpMethod.Get
                ? Task.FromResult(HttpTestData.InspectionResponse(message))
                : Task.FromException<HttpResponseMessage>(lost));
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot, HttpTestData.Qualified);

            HttpRequestException error = await HttpTestData.CatchAsync<HttpRequestException>(
                async () => await endpoint.ExecuteAsync(new XRegistryRequest(XRegistryAction.Replace, "/")
                {
                    Metadata = HttpTestData.Json(/*lang=json,strict*/ """{"epoch":0}""")
                }).ConfigureAwait(false)).ConfigureAwait(false);

            Assert.That(error, Is.SameAs(lost));
            string[] expectedMethods = ["GET", "GET", "GET", "PUT"];
            Assert.That(handler.Requests.Select(item => item.Method), Is.EqualTo(expectedMethods));
            Assert.That(Encoding.UTF8.GetString(handler.Requests[3].Body),
                Is.EqualTo(/*lang=json,strict*/ """{"epoch":0}"""));
        }

        [TestCase("Location", "https://foreign.example/registry/r")]
        [TestCase("Location", "https://registry.example/outside/r")]
        [TestCase("Location", "https://user:secret@registry.example/registry/r")]
        [TestCase("Content-Location", "https://foreign.example/registry/r")]
        [TestCase("Content-Location", "/registry-other/r")]
        [TestCase("Content-Location", "https://user@registry.example/registry/r")]
        [TestCase("Link", "https://foreign.example/registry/r")]
        [TestCase("Link", "/outside/r")]
        [TestCase("Link", "https://user@registry.example/registry/r")]
        [TestCase("Location", "/registry/../outside/r")]
        [TestCase("Link", "/registry/%2e%2e/outside")]
        [TestCase("Location", "//foreign.example/registry/r")]
        public async Task NavigationHeadersRejectOriginRootAndCredentialEscapes(string name, string target)
        {
            using var handler = new RecordingHttpHandler(_ =>
            {
                HttpResponseMessage response = HttpTestData.JsonResponse("{}");
                string value = name == "Link" ? "<" + target + ">;rel=next" : target;
                if (!response.Headers.TryAddWithoutValidation(name, value))
                {
                    response.Content.Headers.TryAddWithoutValidation(name, value);
                }
                return response;
            });
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot);

            Exception error = await HttpTestData.CatchAsync<Exception>(
                async () => await endpoint.ExecuteAsync(new XRegistryRequest(XRegistryAction.Read, "/model"))
                    .ConfigureAwait(false)).ConfigureAwait(false);

            Assert.That(error, Is.InstanceOf<InvalidDataException>().Or.InstanceOf<ArgumentException>());
            Assert.That(handler.Requests, Has.Count.EqualTo(1), "No navigation link may be followed.");
        }

        [Test]
        public async Task ChangedFinalRequestUriIsRejected()
        {
            using var handler = new RecordingHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://registry.example/registry/redirected"),
                Content = new StringContent("{}")
            });
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot);

            await Assert.ThatAsync(async () => await endpoint.ExecuteAsync(new XRegistryRequest(XRegistryAction.Read,
                "/model"))
                .ConfigureAwait(false), Throws.TypeOf<InvalidDataException>().With.Message.Contains("redirect"))
                .ConfigureAwait(false);
            Assert.That(handler.Requests, Has.Count.EqualTo(1));
        }

        [TestCase(200)]
        [TestCase(201)]
        [TestCase(303)]
        public async Task ResponsePreservesStatusLocationsCorrelationAndLinks(int status)
        {
            using var handler = new RecordingHttpHandler(_ =>
            {
                HttpResponseMessage response = HttpTestData.JsonResponse(/*lang=json,strict*/ """{"value":17}""",
                    status);
                response.Headers.Location = new Uri("https://registry.example/registry/schemagroups/g");
                response.Content.Headers.ContentLocation = new Uri("/registry/schemagroups/g/schemas/r/versions/v2",
                    UriKind.Relative);
                response.Headers.TryAddWithoutValidation("xRegistry-xregcorrelationid", "op%20%E2%82%AC%2520");
                response.Headers.TryAddWithoutValidation("Link",
                    "</registry/>;rel=xregistry-root, </registry/model?inline=a,b>;rel=\"next alternate\"");
                return response;
            });
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot);

            XRegistryResponse result = await endpoint.ExecuteAsync(new XRegistryRequest(XRegistryAction.Read, "/model"))
                .ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(status));
            Assert.That(result.Location, Is.EqualTo("/schemagroups/g"));
            Assert.That(result.ContentLocation, Is.EqualTo("/schemagroups/g/schemas/r/versions/v2"));
            Assert.That(result.CorrelationId, Is.EqualTo("op €%20"));
            string[] expectedRelations = ["xregistry-root", "next", "alternate"];
            Assert.That(result.Links.ToArray()!.Select(link => link.Relation), Is.EqualTo(expectedRelations));
            Assert.That(result.Links[1].Target, Is.EqualTo("/model?inline=a%2Cb"));
            Assert.That(result.Metadata.GetProperty("value").GetInt32(), Is.EqualTo(17));
            Assert.That(handler.Requests, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task ActionNotSupportedRetainsCompleteProblemAndAllowedMethods()
        {
            const string problem =
                """{"type":"https://github.com/xregistry/spec/blob/main/core/spec.md#action_not_supported",""" +
                "\"title\":\"No writes\",\"detail\":\"read only\",\"subject\":\"/model\"," +
                "\"args\":{\"verb\":\"PATCH\"},\"extension\":7}";
            using var handler = new RecordingHttpHandler(_ =>
            {
                HttpResponseMessage response = HttpTestData.JsonResponse(problem, 405);
                response.Content.Headers.TryAddWithoutValidation("Allow", "GET, HEAD, OPTIONS, UNKNOWN");
                return response;
            });
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot);

            XRegistryResponse result = await endpoint.ExecuteAsync(new XRegistryRequest(XRegistryAction.Read, "/model"))
                .ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(405));
            Assert.That(result.Error!.Code, Is.EqualTo("action_not_supported"));
            Assert.That(result.Error.Detail, Is.EqualTo("read only"));
            Assert.That(result.Metadata.GetRawText(), Is.EqualTo(problem));
            Assert.That(result.AllowedActions.ToArray(), Is.EqualTo([ XRegistryAction.Read,
                XRegistryAction.Describe ]));
            Assert.That(handler.Requests, Has.Count.EqualTo(1));
        }

        [TestCase("Host")]
        [TestCase("xRegistry-epoch")]
        [TestCase("XREGISTRY-subject")]
        public void DefaultHeadersCannotInjectHostOrAttributes(string name)
        {
            using var handler = new RecordingHttpHandler(HttpTestData.InspectionResponse);
            using var client = new HttpClient(handler);
            client.DefaultRequestHeaders.TryAddWithoutValidation(name, name == "Host" ? "foreign.example" : "0");

            Assert.That(() => new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot), Throws.ArgumentException);
            Assert.That(handler.Requests, Is.Empty);
        }

        [TestCase("Host", "foreign.example")]
        [TestCase("xRegistry-epoch", "0")]
        public async Task DefaultHeaderChangesAfterConstructionAreRejectedBeforeSend(string name, string value)
        {
            using var handler = new RecordingHttpHandler(HttpTestData.InspectionResponse);
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot);
            client.DefaultRequestHeaders.TryAddWithoutValidation(name, value);

            await Assert.ThatAsync(async () => await endpoint.ExecuteAsync(new XRegistryRequest(XRegistryAction.Read,
                "/model"))
                .ConfigureAwait(false), Throws.ArgumentException).ConfigureAwait(false);
            Assert.That(handler.Requests, Is.Empty);
        }

        [TestCase("/model", 1)]
        [TestCase("/schemagroups/g", 2)]
        public async Task ClientDescribeSendsOptionsWithoutMutationOrPayload(string path, int calls)
        {
            using var handler = new RecordingHttpHandler(message =>
            {
                if (message.Method == HttpMethod.Get)
                {
                    return HttpTestData.InspectionResponse(message);
                }
                var response = new HttpResponseMessage(HttpStatusCode.NoContent)
                {
                    Content = new ByteArrayContent([])
                };
                response.Content.Headers.TryAddWithoutValidation("Allow", "GET, HEAD, OPTIONS");
                return response;
            });
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot);

            XRegistryResponse result = await endpoint.ExecuteAsync(new XRegistryRequest(XRegistryAction.Describe, path))
                .ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(204));
            Assert.That(result.Document.IsNull, Is.True);
            Assert.That(result.Metadata.ValueKind, Is.EqualTo(JsonValueKind.Undefined));
            Assert.That(result.AllowedActions.ToArray(), Is.EqualTo([ XRegistryAction.Read,
                XRegistryAction.Describe ]));
            Assert.That(handler.Requests, Has.Count.EqualTo(calls));
            Assert.That(handler.Requests[calls - 1].Method, Is.EqualTo("OPTIONS"));
            Assert.That(handler.Requests[calls - 1].Uri, Is.EqualTo("https://registry.example/registry" + path));
            Assert.That(handler.Requests[calls - 1].HasContent, Is.False);
        }

        [TestCase("1.0-rc4", "{}")]
        [TestCase("1.0-rc4", /*lang=json,strict*/ """{"specversions":["1.0-rc4"],"mutable":[]}""")]
        [TestCase("1.0-rc4", /*lang=json,strict*/ """{"specversions":["future"],"mutable":["entities"]}""")]
        [TestCase("future", /*lang=json,strict*/ """{"specversions":["1.0-rc4"],"mutable":["entities"]}""")]
        public async Task QualifiedSettingCannotBypassUnqualifiedInspection(string version, string capabilities)
        {
            using var handler = new RecordingHttpHandler(message => message.RequestUri!.AbsolutePath switch
            {
                "/registry/" => HttpTestData.JsonResponse("{\"registryid\":\"r\",\"specversion\":\"" + version + "\"}"),
                "/registry/model" => HttpTestData.JsonResponse(HttpTestData.Model),
                _ => HttpTestData.JsonResponse(capabilities)
            });
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot, HttpTestData.Qualified);

            XRegistryResponse result = await endpoint.ExecuteAsync(new XRegistryRequest(XRegistryAction.Replace,
                "/schemagroups/g")
            {
                Metadata = HttpTestData.Json(/*lang=json,strict*/ """{"epoch":0}""")
            }).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(405));
            Assert.That(result.Error!.Code, Is.EqualTo("action_not_supported"));
            Assert.That(result.Error.Subject, Is.EqualTo("/schemagroups/g"));
            Assert.That(handler.Requests, Has.Count.EqualTo(3));
            Assert.That(handler.Requests.Select(request => request.Method), Is.All.EqualTo("GET"));
        }

        [TestCase("/registry/", 1)]
        [TestCase("/registry/model", 2)]
        [TestCase("/registry/capabilities", 3)]
        public async Task InspectRejectsBodylessSuccessRatherThanInventingMetadata(string failedPath, int calls)
        {
            using var handler = new RecordingHttpHandler(message => message.RequestUri!.AbsolutePath == failedPath
                ? new HttpResponseMessage(HttpStatusCode.NoContent) : HttpTestData.InspectionResponse(message));
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot);

            await Assert.ThatAsync(async () => await endpoint.InspectAsync(XRegistryCallContext.Anonymous)
                .ConfigureAwait(false), Throws.TypeOf<InvalidDataException>().With.Message.Contains("missing"))
                .ConfigureAwait(false);
            Assert.That(handler.Requests, Has.Count.EqualTo(calls));
        }

        [TestCase(200)]
        [TestCase(201)]
        public async Task SuccessfulMetadataReadRequiresAnActualJsonBody(int status)
        {
            using var handler = new RecordingHttpHandler(_ => new HttpResponseMessage((HttpStatusCode)status));
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot);

            await Assert.ThatAsync(async () => await endpoint.ExecuteAsync(new XRegistryRequest(XRegistryAction.Read,
                "/model"))
                .ConfigureAwait(false), Throws.TypeOf<InvalidDataException>().With.Message.Contains("missing"))
                .ConfigureAwait(false);
            Assert.That(handler.Requests, Has.Count.EqualTo(1));
        }

        [TestCase(/*lang=json,strict*/ """{"title":"opaque gateway","extension":[true,null]}""")]
        [TestCase("null")]
        [TestCase("[1,null]")]
        public async Task NonProblemJsonErrorRetainsOriginalBodyWithoutInventingAnError(string raw)
        {
            using var handler = new RecordingHttpHandler(_ => HttpTestData.JsonResponse(raw, 403));
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot);

            XRegistryResponse result = await endpoint.ExecuteAsync(new XRegistryRequest(XRegistryAction.Read, "/model"))
                .ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(403));
            Assert.That(result.Metadata.GetRawText(), Is.EqualTo(raw));
            Assert.That(result.Error, Is.Null);
            Assert.That(result.Document.IsNull, Is.True);
            Assert.That(handler.Requests, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task MetadataNavigationBecomesRelativeWhileExtensionAndDocumentJsonStayUntouched()
        {
            const string raw =
                "{\"self\":\"https://registry.example/registry/schemagroups/g/schemas/r$details\"," +
                "\"metaurl\":\"/registry/schemagroups/g/schemas/r/meta\"," +
                "\"versionsurl\":\"/registry/schemagroups/g/schemas/r/versions\"," +
                "\"defaultversionurl\":\"/registry/schemagroups/g/schemas/r/versions/v2\"," +
                "\"schema\":{\"self\":\"https://foreign.example/json\"},\"note\":\"https://foreign.example/opaque\"}";
            using var handler = new RecordingHttpHandler(message => message.RequestUri!.AbsolutePath ==
                "/registry/model"
                ? HttpTestData.JsonResponse(HttpTestData.Model) : HttpTestData.JsonResponse(raw));
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot);

            XRegistryResponse result = await endpoint.ExecuteAsync(
                new XRegistryRequest(XRegistryAction.Read, HttpTestData.ResourcePath) { View = XRegistryView.Metadata })
                .ConfigureAwait(false);

            Assert.That(result.Metadata.GetProperty("self").GetString(), Is.EqualTo(HttpTestData.ResourcePath));
            Assert.That(result.Metadata.GetProperty("metaurl").GetString(),
                Is.EqualTo(HttpTestData.ResourcePath + "/meta"));
            Assert.That(result.Metadata.GetProperty("versionsurl").GetString(),
                Is.EqualTo(HttpTestData.ResourcePath + "/versions"));
            Assert.That(result.Metadata.GetProperty("defaultversionurl").GetString(),
                Is.EqualTo(HttpTestData.ResourcePath + "/versions/v2"));
            Assert.That(result.Metadata.GetProperty("schema").GetProperty("self").GetString(),
                Is.EqualTo("https://foreign.example/json"));
            Assert.That(result.Metadata.GetProperty("note").GetString(), Is.EqualTo("https://foreign.example/opaque"));
            Assert.That(result.Document.IsNull, Is.True);
            Assert.That(handler.Requests, Has.Count.EqualTo(2));
        }

        [TestCase("self")]
        [TestCase("metaurl")]
        [TestCase("versionsurl")]
        [TestCase("defaultversionurl")]
        public async Task MetadataNavigationRejectsForeignTargetsWithoutFollowing(string name)
        {
            using var handler = new RecordingHttpHandler(message => message.RequestUri!.AbsolutePath ==
                "/registry/model"
                ? HttpTestData.JsonResponse(HttpTestData.Model)
                : HttpTestData.JsonResponse("{\"" + name + "\":\"https://foreign.example/registry/r\"}"));
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot);

            await Assert.ThatAsync(async () => await endpoint.ExecuteAsync(
                new XRegistryRequest(XRegistryAction.Read, HttpTestData.ResourcePath) { View = XRegistryView.Metadata })
                .ConfigureAwait(false), Throws.TypeOf<InvalidDataException>().With.Message.Contains("origin"))
                .ConfigureAwait(false);
            Assert.That(handler.Requests, Has.Count.EqualTo(2));
        }

        [TestCase("</registry/model;rel=next")]
        [TestCase("</registry/model>;rel=\"next")]
        [TestCase("registry/model;rel=next")]
        [TestCase("</registry/model>;rel=\"\"")]
        public async Task MalformedNavigationHeaderFailsInsteadOfBeingIgnored(string link)
        {
            using var handler = new RecordingHttpHandler(_ =>
            {
                HttpResponseMessage response = HttpTestData.JsonResponse("{}");
                response.Headers.TryAddWithoutValidation("Link", link);
                return response;
            });
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot);

            await Assert.ThatAsync(async () => await endpoint.ExecuteAsync(new XRegistryRequest(XRegistryAction.Read,
                "/model"))
                .ConfigureAwait(false), Throws.TypeOf<InvalidDataException>()).ConfigureAwait(false);
            Assert.That(handler.Requests, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task DuplicateSingletonResponseHeaderIsRejected()
        {
            using var handler = new RecordingHttpHandler(_ =>
            {
                HttpResponseMessage response = HttpTestData.JsonResponse("{}");
                string[] values = ["op-a", "op-b"];
                response.Headers.TryAddWithoutValidation("xRegistry-xregcorrelationid", values);
                return response;
            });
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot);

            await Assert.ThatAsync(async () => await endpoint.ExecuteAsync(new XRegistryRequest(XRegistryAction.Read,
                "/model"))
                .ConfigureAwait(false), Throws.InstanceOf<IOException>().With.Message.Contains("singleton"))
                .ConfigureAwait(false);
            Assert.That(handler.Requests, Has.Count.EqualTo(1));
        }

        [TestCase("/unknown/entity")]
        [TestCase("/schemagroups/g/unknown/r")]
        [TestCase("/schemagroups/g/schemas/r/other")]
        public async Task UnknownModelPathsAreForwardedAsMetadataNotInventedDocuments(string path)
        {
            using var handler = new RecordingHttpHandler(message => message.RequestUri!.AbsolutePath ==
                "/registry/model"
                ? HttpTestData.JsonResponse(HttpTestData.Model)
                : HttpTestData.JsonResponse(/*lang=json,strict*/ """{"actual":"provider","future":[1,null]}"""));
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot);

            XRegistryResponse result = await endpoint.ExecuteAsync(new XRegistryRequest(XRegistryAction.Read, path))
                .ConfigureAwait(false);

            Assert.That(handler.Requests, Has.Count.EqualTo(2));
            Assert.That(handler.Requests[1].Uri, Is.EqualTo("https://registry.example/registry" + path));
            Assert.That(result.Metadata.GetRawText(),
                Is.EqualTo(/*lang=json,strict*/ """{"actual":"provider","future":[1,null]}"""));
            Assert.That(result.Document.IsNull, Is.True);
        }
    }
}
