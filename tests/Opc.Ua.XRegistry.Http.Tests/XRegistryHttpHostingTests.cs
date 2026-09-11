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
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Http.Tests
{
    [TestFixture]
    [Category("XRegistryHttp")]
    public sealed class XRegistryHttpHostingTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task HostRequiresTrustedInboundAuthenticationDespiteSpoofedHeadersAndOperatorProfile(bool spoof)
        {
            var backend = new FakeRegistryEndpoint();
            backend.Description = backend.Description with { Profile = "native-operator-credentials" };
            await using HttpRouteTestHost host = await HttpRouteTestHost.StartAsync(backend).ConfigureAwait(false);
            using var request = new HttpRequestMessage(HttpMethod.Get, "/registry/model");
            if (spoof)
            {
                request.Headers.TryAddWithoutValidation("xRegistry-subject", "operator");
                request.Headers.TryAddWithoutValidation("xRegistry-roles", "admin");
                request.Headers.TryAddWithoutValidation("X-Forwarded-User", "operator");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer",
                    "not-an-authenticated-identity");
            }

            using HttpResponseMessage response = await host.Client.SendAsync(request).ConfigureAwait(false);
            JsonElement problem = HttpTestData.Json(await response.Content.ReadAsStringAsync().ConfigureAwait(false));

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(problem.GetProperty("detail").GetString(), Is.EqualTo("Inbound authentication is required."));
            Assert.That(backend.Inspections, Is.Empty);
            Assert.That(backend.Requests, Is.Empty);
        }

        [TestCase(ClaimTypes.NameIdentifier)]
        [TestCase("sub")]
        [TestCase(ClaimTypes.Name)]
        public async Task HostMapsPrincipalIdentityThroughInspectAndExecute(string subjectClaim)
        {
            var backend = new FakeRegistryEndpoint();
            var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(subjectClaim, "alice"),
                new Claim(ClaimTypes.Role, "reader"),
                new Claim(ClaimTypes.Role, "editor"),
                new Claim("sid", "session-17")
            ], "verified-test-auth"));
            await using HttpRouteTestHost host = await HttpRouteTestHost.StartAsync(
                backend, principal: principal).ConfigureAwait(false);

            using HttpResponseMessage response = await host.Client.GetAsync(HttpHostTestData.ModelUri).ConfigureAwait(
                false);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(await response.Content.ReadAsStringAsync().ConfigureAwait(false),
                Is.EqualTo(/*lang=json,strict*/ """{"from":"provider","n":7}"""));
            Assert.That(backend.Inspections, Has.Count.EqualTo(1));
            Assert.That(backend.Requests, Has.Count.EqualTo(1));
            XRegistryCallContext context = backend.Inspections[0];
            Assert.That(context.Subject, Is.EqualTo("alice"));
            Assert.That(context.Authority, Is.EqualTo("verified-test-auth"));
            Assert.That(context.IsAuthenticated, Is.True);
            string[] roles = ["reader", "editor"];
            Assert.That(context.Roles.ToArray(), Is.EqualTo(roles));
            Assert.That(context.SessionId, Is.EqualTo("session-17"));
            Assert.That(backend.Requests[0].Context, Is.SameAs(context));
        }

        [TestCase(true, "stable-id")]
        [TestCase(false, "sub-id")]
        public async Task HostPrincipalSubjectPrefersNameIdentifierThenSubOverDisplayName(bool includeIdentifier,
            string expected)
        {
            var backend = new FakeRegistryEndpoint();
            List<Claim> claims =
            [
                new Claim("sub", "sub-id"),
                new Claim(ClaimTypes.Name, "display-name")
            ];
            if (includeIdentifier)
            {
                claims.Add(new Claim(ClaimTypes.NameIdentifier, "stable-id"));
            }
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "test-auth"));
            await using HttpRouteTestHost host = await HttpRouteTestHost.StartAsync(
                backend, principal: principal).ConfigureAwait(false);

            using HttpResponseMessage response = await host.Client.GetAsync(HttpHostTestData.ModelUri).ConfigureAwait(
                false);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(backend.Inspections[0].Subject, Is.EqualTo(expected));
            Assert.That(backend.Requests[0].Context.Subject, Is.EqualTo(expected));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task HostRejectsUnauthenticatedClaimsOrAuthenticatedIdentityWithoutSubject(bool authenticated)
        {
            var backend = new FakeRegistryEndpoint();
            var principal = new ClaimsPrincipal(authenticated
                ? new ClaimsIdentity([], "test-auth")
                : new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "spoof")]));
            await using HttpRouteTestHost host = await HttpRouteTestHost.StartAsync(
                backend, principal: principal).ConfigureAwait(false);

            using HttpResponseMessage response = await host.Client.GetAsync(HttpHostTestData.ModelUri).ConfigureAwait(
                false);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(backend.Inspections, Is.Empty);
            Assert.That(backend.Requests, Is.Empty);
        }

        [Test]
        public async Task HostDefaultAuthorizationDeniesWritesEvenForAnAuthenticatedAdmin()
        {
            var backend = new FakeRegistryEndpoint();
            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "alice"), new Claim(ClaimTypes.Role, "admin")], "test-auth"));
            await using HttpRouteTestHost host = await HttpRouteTestHost.StartAsync(
                backend, principal: principal).ConfigureAwait(false);
            using var content = new StringContent("{}");

            using HttpResponseMessage response = await host.Client.PutAsync(HttpHostTestData.ModelUri,
                content).ConfigureAwait(false);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(backend.Inspections, Is.Empty);
            Assert.That(backend.Requests, Is.Empty);
        }

        [Test]
        public async Task HostAuthorizesCanonicalTargetAndContextBeforeInspectOrPayloadReading()
        {
            var backend = new FakeRegistryEndpoint { Response = new XRegistryResponse(204) };
            var authorized = new List<XRegistryRequest>();
            var inspectionCounts = new List<int>();
            var caller = new XRegistryCallContext("mapped-caller")
            {
                IsAuthenticated = true,
                Authority = "trusted-mapper",
                Roles = ["writer"],
                SessionId = "session-mapped"
            };
            var options = new XRegistryHttpRouteOptions(HttpHostTestData.PublicRoot)
            {
                CreateContextAsync = (_, token) =>
                {
                    Assert.That(token.CanBeCanceled, Is.True);
                    return new ValueTask<XRegistryCallContext>(caller);
                },
                AuthorizeAsync = (_, request, token) =>
                {
                    Assert.That(token.CanBeCanceled, Is.True);
                    authorized.Add(request);
                    inspectionCounts.Add(backend.Inspections.Count);
                    return new ValueTask<bool>(true);
                }
            };
            await using HttpRouteTestHost host = await HttpRouteTestHost.StartAsync(backend,
                options).ConfigureAwait(false);
            using var message = new HttpRequestMessage(HttpMethod.Put,
                "/registry/schemagroups/cafe%cc%81/schemas/r$details?epoch=0&inline&inline=&future=%2520")
            {
                Content = new StringContent(/*lang=json,strict*/ """{"epoch":0,"name":"written"}""", Encoding.UTF8,
                    "application/json")
            };

            using HttpResponseMessage response = await host.Client.SendAsync(message).ConfigureAwait(false);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(authorized, Has.Count.EqualTo(1));
            Assert.That(inspectionCounts, Has.Count.EqualTo(1));
            Assert.That(inspectionCounts[0], Is.Zero);
            XRegistryRequest candidate = authorized[0];
            Assert.That(candidate.Action, Is.EqualTo(XRegistryAction.Replace));
            Assert.That(candidate.Path, Is.EqualTo("/schemagroups/cafe%CC%81/schemas/r"));
            Assert.That(candidate.View, Is.EqualTo(XRegistryView.Metadata));
            XRegistryParameter[] parameters =
                [new("epoch", "0"), new("inline", null), new("inline", string.Empty), new("future", "%20")];
            Assert.That(candidate.Parameters.ToArray(), Is.EqualTo(parameters));
            Assert.That(candidate.Metadata.ValueKind, Is.EqualTo(JsonValueKind.Undefined));
            Assert.That(candidate.Document.IsNull, Is.True);
            Assert.That(candidate.Context, Is.SameAs(caller));
            Assert.That(backend.Inspections[0], Is.SameAs(caller));
            Assert.That(backend.Requests, Has.Count.EqualTo(1));
            Assert.That(backend.Requests[0].Context, Is.SameAs(caller));
            Assert.That(backend.Requests[0].Metadata.GetProperty("name").GetString(), Is.EqualTo("written"));
            Assert.That(backend.Requests[0].Metadata.GetProperty("epoch").GetInt32(), Is.Zero);
        }

        [Test]
        public async Task HostDeniesBeforeEvenInspectionOrPayloadRead()
        {
            var backend = new FakeRegistryEndpoint();
            using var body = new CancellationReadStream();
            XRegistryHttpRouteOptions options = HttpHostTestData.OpenOptions with
            {
                AuthorizeAsync = static (_, _, _) => new ValueTask<bool>(false)
            };
            await using HttpRouteTestHost host = await HttpRouteTestHost.StartAsync(
                backend, options, prepare: context => context.Request.Body = body).ConfigureAwait(false);
            using var request = new HttpRequestMessage(HttpMethod.Put, "/registry/model");

            using HttpResponseMessage response = await host.Client.SendAsync(request).ConfigureAwait(false);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(body.ReadCalls, Is.Zero);
            Assert.That(backend.Inspections, Is.Empty);
            Assert.That(backend.Requests, Is.Empty);
        }

        [TestCase(false, true, true, "PUT", false)]
        [TestCase(true, false, true, "PUT", false)]
        [TestCase(true, true, false, "PUT", false)]
        [TestCase(false, true, true, "DELETE", false)]
        [TestCase(true, false, true, "DELETE", false)]
        [TestCase(true, true, false, "DELETE", true)]
        [TestCase(true, true, true, "PUT", true)]
        public async Task HostPreflightsEachMutationGuaranteeWithoutRequiringDeleteTouch(
            bool atomic, bool conditional, bool touch, string method, bool accepted)
        {
            var backend = new FakeRegistryEndpoint { Response = new XRegistryResponse(204) };
            backend.Description = backend.Description with
            {
                SupportsAtomicMutations = atomic,
                SupportsConditionalMutations = conditional,
                SupportsWriteTouch = touch
            };
            await using HttpRouteTestHost host = await HttpRouteTestHost.StartAsync(
                backend, HttpHostTestData.OpenOptions).ConfigureAwait(false);
            using var request = new HttpRequestMessage(new HttpMethod(method), "/registry/schemagroups/g")
            {
                Content = method == "PUT" ? new StringContent("{}", Encoding.UTF8, "application/json") : null
            };

            using HttpResponseMessage response = await host.Client.SendAsync(request).ConfigureAwait(false);

            Assert.That(response.StatusCode,
                Is.EqualTo(accepted ? HttpStatusCode.NoContent : HttpStatusCode.MethodNotAllowed));
            Assert.That(backend.Inspections, Has.Count.EqualTo(1));
            Assert.That(backend.Requests, Has.Count.EqualTo(accepted ? 1 : 0));
            if (!accepted)
            {
                JsonElement problem = HttpTestData.Json(await response.Content.ReadAsStringAsync().ConfigureAwait(
                    false));
                Assert.That(problem.GetProperty("type").GetString(), Does.EndWith("#action_not_supported"));
                Assert.That(HttpHostTestData.Header(response, "Allow"), Is.EqualTo("GET, HEAD, OPTIONS"));
            }
        }

        [TestCase("GET", "/", XRegistryAction.Read)]
        [TestCase("GET", "/model", XRegistryAction.Read)]
        [TestCase("GET", "/modelsource", XRegistryAction.Read)]
        [TestCase("GET", "/capabilities", XRegistryAction.Read)]
        [TestCase("GET", "/capabilitiesoffered", XRegistryAction.Read)]
        [TestCase("GET", "/export", XRegistryAction.Read)]
        [TestCase("GET", "/.xregistry", XRegistryAction.Read)]
        [TestCase("GET", "/schemagroups", XRegistryAction.Read)]
        [TestCase("PUT", "/model", XRegistryAction.Replace)]
        [TestCase("PUT", "/capabilities", XRegistryAction.Replace)]
        [TestCase("PUT", "/schemagroups/g", XRegistryAction.Replace)]
        [TestCase("PATCH", "/schemagroups", XRegistryAction.Merge)]
        [TestCase("POST", "/schemagroups", XRegistryAction.Create)]
        [TestCase("DELETE", "/schemagroups/g", XRegistryAction.Delete)]
        public async Task HostForwardsMethodsTargetsOrderedFlagsAndRealResponses(
            string method, string path, XRegistryAction action)
        {
            var backend = new FakeRegistryEndpoint();
            await using HttpRouteTestHost host = await HttpRouteTestHost.StartAsync(
                backend, HttpHostTestData.OpenOptions).ConfigureAwait(false);
            using var request = new HttpRequestMessage(new HttpMethod(method),
                "/registry" + path + "?future=a%2Bb&inline&inline=&epoch=0&epoch=18446744073709551616");
            if (action is XRegistryAction.Replace or XRegistryAction.Merge or XRegistryAction.Create)
            {
                request.Content = new StringContent(/*lang=json,strict*/ """{"in":1,"deleted":null}""", Encoding.UTF8,
                    "application/json");
            }

            using HttpResponseMessage response = await host.Client.SendAsync(request).ConfigureAwait(false);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(await response.Content.ReadAsStringAsync().ConfigureAwait(false),
                Is.EqualTo(/*lang=json,strict*/ """{"from":"provider","n":7}"""));
            Assert.That(HttpHostTestData.Header(response, "Link"),
                Is.EqualTo("<https://public.example/registry/>;rel=xregistry-root"));
            Assert.That(backend.Inspections, Has.Count.EqualTo(1));
            Assert.That(backend.Requests, Has.Count.EqualTo(1));
            XRegistryRequest forwarded = backend.Requests[0];
            Assert.That(forwarded.Action, Is.EqualTo(action));
            Assert.That(forwarded.Path, Is.EqualTo(path));
            XRegistryParameter[] parameters =
            [
                new("future", "a+b"), new("inline", null), new("inline", string.Empty),
                new("epoch", "0"), new("epoch", "18446744073709551616")
            ];
            Assert.That(forwarded.Parameters.ToArray(), Is.EqualTo(parameters));
            Assert.That(forwarded.Document.IsNull, Is.True);
            if (request.Content is not null)
            {
                Assert.That(forwarded.Metadata.GetProperty("in").GetInt32(), Is.EqualTo(1));
                Assert.That(forwarded.Metadata.GetProperty("deleted").ValueKind, Is.EqualTo(JsonValueKind.Null));
            }
        }

        [TestCase("json", "application/json")]
        [TestCase("binary", null)]
        [TestCase("empty", null)]
        public async Task HostParsesByteExactRawWritesAndPatchMetadataHeaders(string fixture, string? contentType)
        {
            byte[] bytes = fixture switch
            {
                "json" => Encoding.UTF8.GetBytes(
                    /*lang=json,strict*/ """{ "self":"https://foreign.example/json", "n":1.00 }"""),
                "binary" => [0xff, 0x80, 0, 0x0d, 0x0a],
                _ => []
            };
            var backend = new FakeRegistryEndpoint { Response = new XRegistryResponse(204) };
            await using HttpRouteTestHost host = await HttpRouteTestHost.StartAsync(
                backend, HttpHostTestData.OpenOptions).ConfigureAwait(false);
            using var request = new HttpRequestMessage(HttpMethod.Put, "/registry" + HttpTestData.ResourcePath)
            {
                Content = new ByteArrayContent(bytes)
            };
            if (contentType is not null)
            {
                request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            }
            request.Headers.TryAddWithoutValidation("xRegistry-labels.a.b", "fresh%20value");
            request.Headers.TryAddWithoutValidation("xRegistry-labels.keep", "yes");
            request.Headers.TryAddWithoutValidation("xRegistry-score", "-3");
            request.Headers.TryAddWithoutValidation("xRegistry-scores.x", "0");
            request.Headers.TryAddWithoutValidation("xRegistry-active", "true");
            request.Headers.TryAddWithoutValidation("xRegistry-deleted", "null");

            using HttpResponseMessage response = await host.Client.SendAsync(request).ConfigureAwait(false);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false), Is.Empty);
            Assert.That(backend.Requests, Has.Count.EqualTo(1));
            XRegistryRequest forwarded = backend.Requests[0];
            Assert.That(forwarded.Document.IsNull, Is.False);
            Assert.That(forwarded.Document.Span.ToArray(), Is.EqualTo(bytes));
            Assert.That(forwarded.ContentType, Is.EqualTo(contentType));
            Assert.That(forwarded.Metadata.GetProperty("labels").GetRawText(),
                Is.EqualTo(/*lang=json,strict*/ """{"a.b":"fresh value","keep":"yes"}"""));
            Assert.That(forwarded.Metadata.GetProperty("score").GetInt32(), Is.EqualTo(-3));
            Assert.That(forwarded.Metadata.GetProperty("scores").GetProperty("x").GetInt32(), Is.Zero);
            Assert.That(forwarded.Metadata.GetProperty("active").GetBoolean(), Is.True);
            Assert.That(forwarded.Metadata.GetProperty("deleted").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(forwarded.Metadata.TryGetProperty("name", out _), Is.False,
                "Missing headers do not clear attributes.");
            JsonElement type = forwarded.Metadata.GetProperty("contenttype");
            Assert.That(type.ValueKind, Is.EqualTo(contentType is null ? JsonValueKind.Null : JsonValueKind.String));
            if (contentType is not null)
            {
                Assert.That(type.GetString(), Is.EqualTo("application/json"));
            }
        }

        [TestCase(HttpTestData.RecordPath, false)]
        [TestCase(HttpTestData.RecordPath, true)]
        [TestCase(HttpTestData.ResourcePath, true)]
        public async Task HostMetadataWritesPreserveTypedNestedJsonAndLiteralNull(
            string path, bool details)
        {
            const string json =
                /*lang=json,strict*/
                """{"epoch":18446744073709551616,"name":"null","nested":{"a":[0,false,2.5,null]}}""";
            var backend = new FakeRegistryEndpoint { Response = new XRegistryResponse(204) };
            await using HttpRouteTestHost host = await HttpRouteTestHost.StartAsync(
                backend, HttpHostTestData.OpenOptions).ConfigureAwait(false);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            using HttpResponseMessage response = await host.Client.PutAsync(
                new Uri("/registry" + path + (details ? "$details" : string.Empty), UriKind.Relative), content)
                .ConfigureAwait(false);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(backend.Requests, Has.Count.EqualTo(1));
            Assert.That(backend.Requests[0].Metadata.GetRawText(), Is.EqualTo(json));
            Assert.That(backend.Requests[0].Metadata.GetProperty("epoch").GetRawText(),
                Is.EqualTo("18446744073709551616"));
            Assert.That(backend.Requests[0].Metadata.GetProperty("name").GetString(), Is.EqualTo("null"));
            Assert.That(backend.Requests[0].Metadata.GetProperty("nested").GetProperty("a")[3].ValueKind,
                Is.EqualTo(JsonValueKind.Null));
            Assert.That(backend.Requests[0].Document.IsNull, Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task HostExternalDocumentReferenceWithEmptyBodyStaysAbsentWithoutFetch(bool emptyDocument)
        {
            var backend = new FakeRegistryEndpoint { Response = new XRegistryResponse(204) };
            await using HttpRouteTestHost host = await HttpRouteTestHost.StartAsync(
                backend, HttpHostTestData.OpenOptions).ConfigureAwait(false);
            using var request = new HttpRequestMessage(HttpMethod.Put, "/registry" + HttpTestData.ResourcePath)
            {
                Content = emptyDocument ? new ByteArrayContent([]) : null
            };
            request.Headers.TryAddWithoutValidation("xRegistry-schemaurl", "https://external.example/document");

            using HttpResponseMessage response = await host.Client.SendAsync(request).ConfigureAwait(false);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(backend.Inspections, Has.Count.EqualTo(1));
            Assert.That(backend.Requests, Has.Count.EqualTo(1));
            Assert.That(backend.Requests[0].Document.IsNull, Is.True);
            Assert.That(backend.Requests[0].Metadata.GetProperty("schemaurl").GetString(),
                Is.EqualTo("https://external.example/document"));
            Assert.That(backend.Requests[0].Metadata.GetProperty("contenttype").ValueKind,
                Is.EqualTo(JsonValueKind.Null));
        }

        [Test]
        public async Task HostDocumentReadKeepsJsonBytesAndHeaderSelfWithoutDetails()
        {
            const string raw = /*lang=json,strict*/ """{ "self":"https://foreign.example/document", "n":1.00 }""";
            var backend = new FakeRegistryEndpoint
            {
                Response = new XRegistryResponse(200)
                {
                    Metadata = HttpTestData.Json(
                        /*lang=json,strict*/
                        """
                        {"self":"/schemagroups/g/schemas/r","name":"Euro €","schema":"inline","complex":{"n":7}}
                        """),
                    Document = new ByteString(Encoding.UTF8.GetBytes(raw)),
                    ContentType = "application/json",
                    CorrelationId = "op €"
                }
            };
            await using HttpRouteTestHost host = await HttpRouteTestHost.StartAsync(
                backend, HttpHostTestData.OpenOptions).ConfigureAwait(false);

            using HttpResponseMessage response = await host.Client.GetAsync(
                new Uri("/registry" + HttpTestData.ResourcePath, UriKind.Relative))
                .ConfigureAwait(false);

            Assert.That(await response.Content.ReadAsStringAsync().ConfigureAwait(false), Is.EqualTo(raw));
            Assert.That(response.Content.Headers.ContentType!.ToString(), Is.EqualTo("application/json"));
            Assert.That(HttpHostTestData.Header(response, "xRegistry-self"),
                Is.EqualTo("https://public.example/registry/schemagroups/g/schemas/r"));
            Assert.That(HttpHostTestData.Header(response, "xRegistry-name"), Is.EqualTo("Euro%20%E2%82%AC"));
            Assert.That(HttpHostTestData.Header(response, "xRegistry-xregcorrelationid"), Is.EqualTo("op%20%E2%82%AC"));
            Assert.That(response.Headers.Contains("xRegistry-schema"), Is.False);
            Assert.That(response.Headers.Contains("xRegistry-complex"), Is.False);
            Assert.That(backend.Requests[0].View, Is.EqualTo(XRegistryView.Default));
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task HostRewritesSelfOnlyForDocumentBackedMetadata(bool hasDocument)
        {
            string path = hasDocument ? HttpTestData.ResourcePath : HttpTestData.RecordPath;
            var backend = new FakeRegistryEndpoint
            {
                Response = new XRegistryResponse(200)
                {
                    Metadata = HttpTestData.Json("{\"self\":\"" +
                        path +
                        "\",\"extension\":\"https://foreign.example/opaque\",\"nested\":{\"self\":\"untouched\"}}")
                }
            };
            await using HttpRouteTestHost host = await HttpRouteTestHost.StartAsync(
                backend, HttpHostTestData.OpenOptions).ConfigureAwait(false);

            using HttpResponseMessage response = await host.Client.GetAsync(
                new Uri("/registry" + path + "$details", UriKind.Relative))
                .ConfigureAwait(false);
            JsonElement body = HttpTestData.Json(await response.Content.ReadAsStringAsync().ConfigureAwait(false));

            Assert.That(body.GetProperty("self").GetString(),
                Is.EqualTo("https://public.example/registry" + path + (hasDocument ? "$details" : string.Empty)));
            Assert.That(body.GetProperty("extension").GetString(), Is.EqualTo("https://foreign.example/opaque"));
            Assert.That(body.GetProperty("nested").GetProperty("self").GetString(), Is.EqualTo("untouched"));
            Assert.That(backend.Requests, Has.Count.EqualTo(1));
            Assert.That(backend.Requests[0].View, Is.EqualTo(XRegistryView.Metadata));
        }

        [Test]
        public async Task HostRewritesNestedKnownNavigationButNotEmbeddedDocumentsOrExtensions()
        {
            const string metadata =
                "{\"self\":\"/\",\"schemagroupsurl\":\"/schemagroups\"," +
                "\"extension\":{\"self\":\"https://foreign.example/opaque\"},\"schemagroups\":{\"g\":{" +
                "\"self\":\"/schemagroups/g\",\"schemasurl\":\"/schemagroups/g/schemas\",\"schemas\":{\"r\":{" +
                "\"self\":\"/schemagroups/g/schemas/r\",\"metaurl\":\"/schemagroups/g/schemas/r/meta\"," +
                "\"versionsurl\":\"/schemagroups/g/schemas/r/versions\"," +
                "\"defaultversionurl\":\"/schemagroups/g/schemas/r/versions/v2\"," +
                "\"schema\":{\"self\":\"https://foreign.example/json\"}," +
                "\"meta\":{\"self\":\"/schemagroups/g/schemas/r/meta\"}," +
                "\"versions\":{\"v2\":{\"self\":\"/schemagroups/g/schemas/r/versions/v2\"}}}}," +
                "\"records\":{\"r\":{\"self\":\"/schemagroups/g/records/r\"}}}}}";
            var backend = new FakeRegistryEndpoint
            {
                Response = new XRegistryResponse(200) { Metadata = HttpTestData.Json(metadata) }
            };
            await using HttpRouteTestHost host = await HttpRouteTestHost.StartAsync(
                backend, HttpHostTestData.OpenOptions).ConfigureAwait(false);

            using HttpResponseMessage response = await host.Client.GetAsync(
                new Uri("/registry/export", UriKind.Relative)).ConfigureAwait(false);
            JsonElement root = HttpTestData.Json(await response.Content.ReadAsStringAsync().ConfigureAwait(false));

            Assert.That(root.GetProperty("self").GetString(), Is.EqualTo("https://public.example/registry/"));
            Assert.That(root.GetProperty("schemagroupsurl").GetString(),
                Is.EqualTo("https://public.example/registry/schemagroups"));
            Assert.That(root.GetProperty("extension").GetProperty("self").GetString(),
                Is.EqualTo("https://foreign.example/opaque"));
            JsonElement group = root.GetProperty("schemagroups").GetProperty("g");
            Assert.That(group.GetProperty("self").GetString(),
                Is.EqualTo("https://public.example/registry/schemagroups/g"));
            Assert.That(group.GetProperty("schemasurl").GetString(),
                Is.EqualTo("https://public.example/registry/schemagroups/g/schemas"));
            JsonElement resource = group.GetProperty("schemas").GetProperty("r");
            Assert.That(resource.GetProperty("self").GetString(),
                Is.EqualTo("https://public.example/registry/schemagroups/g/schemas/r$details"));
            Assert.That(resource.GetProperty("metaurl").GetString(),
                Is.EqualTo("https://public.example/registry/schemagroups/g/schemas/r/meta"));
            Assert.That(resource.GetProperty("versionsurl").GetString(),
                Is.EqualTo("https://public.example/registry/schemagroups/g/schemas/r/versions"));
            Assert.That(resource.GetProperty("defaultversionurl").GetString(),
                Is.EqualTo("https://public.example/registry/schemagroups/g/schemas/r/versions/v2"));
            Assert.That(resource.GetProperty("meta").GetProperty("self").GetString(),
                Is.EqualTo("https://public.example/registry/schemagroups/g/schemas/r/meta"));
            Assert.That(resource.GetProperty("versions").GetProperty("v2").GetProperty("self").GetString(),
                Is.EqualTo("https://public.example/registry/schemagroups/g/schemas/r/versions/v2$details"));
            Assert.That(resource.GetProperty("schema").GetProperty("self").GetString(),
                Is.EqualTo("https://foreign.example/json"));
            Assert.That(group.GetProperty("records").GetProperty("r").GetProperty("self").GetString(),
                Is.EqualTo("https://public.example/registry/schemagroups/g/records/r"));
            Assert.That(backend.Requests[0].Path, Is.EqualTo("/export"));
        }

        [TestCase(200)]
        [TestCase(201)]
        [TestCase(303)]
        public async Task HostWritesExactStatusLocationContentLocationAndCorrelation(int status)
        {
            var backend = new FakeRegistryEndpoint
            {
                Response = new XRegistryResponse(status)
                {
                    Metadata = HttpTestData.Json(/*lang=json,strict*/ """{"n":7}"""),
                    Location = "/schemagroups/g",
                    ContentLocation = "/schemagroups/g/schemas/r/versions/v2",
                    CorrelationId = "op €",
                    Links = [new XRegistryLink("next", "/model?inline=a%2Cb")]
                }
            };
            await using HttpRouteTestHost host = await HttpRouteTestHost.StartAsync(
                backend, HttpHostTestData.OpenOptions).ConfigureAwait(false);

            using HttpResponseMessage response = await host.Client.GetAsync(HttpHostTestData.ModelUri).ConfigureAwait(
                false);

            Assert.That((int)response.StatusCode, Is.EqualTo(status));
            Assert.That(HttpHostTestData.Header(response, "Location"),
                Is.EqualTo("https://public.example/registry/schemagroups/g"));
            Assert.That(HttpHostTestData.Header(response, "Content-Location"),
                Is.EqualTo("https://public.example/registry/schemagroups/g/schemas/r/versions/v2"));
            Assert.That(HttpHostTestData.Header(response, "xRegistry-xregcorrelationid"), Is.EqualTo("op%20%E2%82%AC"));
            Assert.That(HttpHostTestData.Header(response, "Link"),
                Is.EqualTo("<https://public.example/registry/>;rel=xregistry-root, " +
                    "<https://public.example/registry/model?inline=a%2Cb>;rel=\"next\""));
            Assert.That(await response.Content.ReadAsStringAsync().ConfigureAwait(false),
                Is.EqualTo(/*lang=json,strict*/ """{"n":7}"""));
            Assert.That(backend.Requests, Has.Count.EqualTo(1), "A redirect is emitted, never followed.");
        }

        [Test]
        public async Task HostPreservesCompleteProblemDetailsWithoutReconstruction()
        {
            var backend = new FakeRegistryEndpoint
            {
                Response = new XRegistryResponse(400)
                {
                    Metadata = HttpTestData.Json(HttpTestData.Problem),
                    Error = new XRegistryError("mismatched_epoch", "summary must not replace original fields")
                }
            };
            await using HttpRouteTestHost host = await HttpRouteTestHost.StartAsync(
                backend, HttpHostTestData.OpenOptions).ConfigureAwait(false);

            using HttpResponseMessage response = await host.Client.GetAsync(HttpHostTestData.ModelUri).ConfigureAwait(
                false);
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(body, Is.EqualTo(HttpTestData.Problem));
            HttpTestData.AssertProblem(HttpTestData.Json(body));
            Assert.That(response.Content.Headers.ContentType!.ToString(),
                Is.EqualTo("application/json; charset=utf-8"));
            Assert.That(response.Content.Headers.ContentLength, Is.EqualTo(Encoding.UTF8.GetByteCount(body)));
            Assert.That(backend.Requests, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task HeadUsesReadAndRetainsContentLengthWithoutBody()
        {
            var backend = new FakeRegistryEndpoint();
            await using HttpRouteTestHost host = await HttpRouteTestHost.StartAsync(
                backend, HttpHostTestData.OpenOptions).ConfigureAwait(false);
            using var request = new HttpRequestMessage(HttpMethod.Head, "/registry/model");

            using HttpResponseMessage response = await host.Client.SendAsync(request).ConfigureAwait(false);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false), Is.Empty);
            Assert.That(response.Content.Headers.ContentLength, Is.EqualTo(25));
            Assert.That(response.Content.Headers.ContentType!.ToString(),
                Is.EqualTo("application/json; charset=utf-8"));
            Assert.That(backend.Requests, Has.Count.EqualTo(1));
            Assert.That(backend.Requests[0].Action, Is.EqualTo(XRegistryAction.Read));
        }

        [TestCase(true, "GET, HEAD, DELETE, OPTIONS")]
        [TestCase(false, "GET, HEAD, OPTIONS")]
        public async Task OptionsFiltersWritesAndMatchesCorsAllowIncludingOptions(bool atomic, string expected)
        {
            var backend = new FakeRegistryEndpoint
            {
                Response = new XRegistryResponse(200)
                {
                    AllowedActions =
                    [
                        XRegistryAction.Read, XRegistryAction.Replace, XRegistryAction.Merge,
                        XRegistryAction.Create, XRegistryAction.Delete, XRegistryAction.Describe, XRegistryAction.Read
                    ]
                }
            };
            backend.Description = backend.Description with
            {
                SupportsAtomicMutations = atomic,
                SupportsWriteTouch = false
            };
            XRegistryHttpRouteOptions options = HttpHostTestData.OpenOptions with
            {
                AuthorizeAsync = static (_, request, _) => new ValueTask<bool>(
                    request.Action is XRegistryAction.Read or XRegistryAction.Delete or XRegistryAction.Describe)
            };
            await using HttpRouteTestHost host = await HttpRouteTestHost.StartAsync(backend,
                options).ConfigureAwait(false);
            using var request = new HttpRequestMessage(HttpMethod.Options, "/registry/schemagroups/g");

            using HttpResponseMessage response = await host.Client.SendAsync(request).ConfigureAwait(false);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(HttpHostTestData.Header(response, "Allow"), Is.EqualTo(expected));
            Assert.That(HttpHostTestData.Header(response, "Access-Control-Allow-Methods"), Is.EqualTo(expected));
            Assert.That(await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false), Is.Empty);
            Assert.That(backend.Requests, Has.Count.EqualTo(1));
            Assert.That(backend.Requests[0].Action, Is.EqualTo(XRegistryAction.Describe));
            Assert.That(backend.Requests[0].IsMutation, Is.False);
            Assert.That(backend.Requests[0].Document.IsNull, Is.True);
        }

        [TestCase(false, "OPTIONS")]
        [TestCase(true, "GET, HEAD, PUT, PATCH, POST, DELETE, OPTIONS")]
        public async Task OptionsAdvertisesEmptyAndFullyAuthorizedActionSets(bool allActions, string expected)
        {
            var backend = new FakeRegistryEndpoint
            {
                Response = new XRegistryResponse(200)
                {
                    AllowedActions = allActions
                        ? [XRegistryAction.Read, XRegistryAction.Replace, XRegistryAction.Merge,
                            XRegistryAction.Create, XRegistryAction.Delete]
                        : []
                }
            };
            await using HttpRouteTestHost host = await HttpRouteTestHost.StartAsync(
                backend, HttpHostTestData.OpenOptions).ConfigureAwait(false);
            using var request = new HttpRequestMessage(HttpMethod.Options, HttpHostTestData.ModelUri);

            using HttpResponseMessage response = await host.Client.SendAsync(request).ConfigureAwait(false);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(HttpHostTestData.Header(response, "Allow"), Is.EqualTo(expected));
            Assert.That(HttpHostTestData.Header(response, "Access-Control-Allow-Methods"), Is.EqualTo(expected));
            Assert.That(backend.Requests, Has.Count.EqualTo(1));
            Assert.That(backend.Requests[0].Action, Is.EqualTo(XRegistryAction.Describe));
            Assert.That(backend.Requests[0].Metadata.ValueKind, Is.EqualTo(JsonValueKind.Undefined));
        }

        [Test]
        public async Task HostDocumentRedirectEmitsHeadersWithoutAnInventedDocumentOrExternalFetch()
        {
            var backend = new FakeRegistryEndpoint
            {
                Response = new XRegistryResponse(303)
                {
                    Metadata = HttpTestData.Json(
                        "{\"self\":\"/schemagroups/g/schemas/r\"," +
                        "\"schemaurl\":\"https://external.example/document\",\"epoch\":0}"),
                    Location = "/schemagroups/g/schemas/other",
                    ContentType = "application/json"
                }
            };
            await using HttpRouteTestHost host = await HttpRouteTestHost.StartAsync(
                backend, HttpHostTestData.OpenOptions).ConfigureAwait(false);

            using HttpResponseMessage response = await host.Client.GetAsync(
                new Uri("/registry" + HttpTestData.ResourcePath, UriKind.Relative)).ConfigureAwait(false);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.SeeOther));
            Assert.That(await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false), Is.Empty);
            Assert.That(response.Content.Headers.ContentLength, Is.Zero);
            Assert.That(HttpHostTestData.Header(response, "xRegistry-self"),
                Is.EqualTo("https://public.example/registry/schemagroups/g/schemas/r"));
            Assert.That(HttpHostTestData.Header(response, "xRegistry-schemaurl"),
                Is.EqualTo("https://external.example/document"));
            Assert.That(HttpHostTestData.Header(response, "xRegistry-epoch"), Is.EqualTo("0"));
            Assert.That(response.Headers.Location!.AbsoluteUri,
                Is.EqualTo("https://public.example/registry/schemagroups/g/schemas/other"));
            Assert.That(backend.Requests, Has.Count.EqualTo(1));
        }
    }
}
#endif
