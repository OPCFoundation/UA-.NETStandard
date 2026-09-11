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
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using Opc.Ua.XRegistry.Bridge.Native;
using Opc.Ua.XRegistry.Bridge.Sync;
using Opc.Ua.XRegistry.Http;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Bridge.Tests.Native
{
    public sealed partial class XRegistryNativeIntegrationTests
    {
        [Test]
        public async Task HttpGatewayWritesThroughRealNativePreparedEndpointAndRebasesLinksAsync()
        {
            using IHost host = new HostBuilder().ConfigureWebHost(web => web.UseTestServer()
                .ConfigureServices(services => services.AddRouting())
                .Configure(app => app.UseRouting().UseEndpoints(routes => routes.MapXRegistry("/registry", m_native,
                    new XRegistryHttpRouteOptions(new Uri("https://frontend.example/registry/"))
                    {
                        CreateContextAsync = (_, _) => new ValueTask<XRegistryCallContext>(s_writer),
                        AuthorizeAsync = (_, _, _) => new ValueTask<bool>(true)
                    })))).Build();
            await host.StartAsync().ConfigureAwait(false);
            try
            {
                using HttpClient client = host.GetTestClient();
                using var group = new HttpRequestMessage(HttpMethod.Put, "/registry/schemagroups/wired")
                {
                    Content = new StringContent(/*lang=json,strict*/ """{"name":"through-http"}""", Encoding.UTF8,
                        "application/json")
                };
                using HttpResponseMessage created = await client.SendAsync(group).ConfigureAwait(false);
                Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.Created),
                    await created.Content.ReadAsStringAsync().ConfigureAwait(false));
                Assert.That(created.Headers.Location?.AbsoluteUri,
                    Is.EqualTo("https://frontend.example/registry/schemagroups/wired"));
                using var invalid = new HttpRequestMessage(HttpMethod.Patch, "/registry")
                {
                    Content = new StringContent(
                        /*lang=json,strict*/ """{"name":"uncommitted","schemagroups":{"partial":{},"bad":null}}""",
                        Encoding.UTF8, "application/json")
                };
                using HttpResponseMessage rejected = await client.SendAsync(invalid).ConfigureAwait(false);
                XRegistryResponse partial = await m_forwarder.Inner.ExecuteAsync(
                    Request(XRegistryAction.Read, "/schemagroups/partial")).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(rejected.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
                    Assert.That(partial.StatusCode, Is.EqualTo(404));
                });
                using var resource = new HttpRequestMessage(HttpMethod.Put,
                    "/registry/schemagroups/wired/schemas/r$details")
                {
                    Content = new StringContent(
                        /*lang=json,strict*/
                        """{"versionid":"v1","schemabase64":"AP8A","contenttype":"application/octet-stream"}""",
                        Encoding.UTF8, "application/json")
                };
                using HttpResponseMessage registered = await client.SendAsync(resource).ConfigureAwait(false);
                Assert.That(registered.StatusCode, Is.EqualTo(HttpStatusCode.Created),
                    await registered.Content.ReadAsStringAsync().ConfigureAwait(false));
                using HttpResponseMessage document = await client.GetAsync(
                    new Uri("/registry/schemagroups/wired/schemas/r", UriKind.Relative))
                    .ConfigureAwait(false);
                Assert.That(await document.Content.ReadAsByteArrayAsync().ConfigureAwait(false),
                    Is.EqualTo(new byte[] { 0, 255, 0 }));
                using var touch = new HttpRequestMessage(HttpMethod.Put, "/registry/schemagroups/wired/schemas/r")
                {
                    Content = new ByteArrayContent([0, 255, 0])
                };
                touch.Headers.Add("xRegistry-epoch", "0");
                touch.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                using HttpResponseMessage touched = await client.SendAsync(touch).ConfigureAwait(false);
                Assert.That(touched.StatusCode, Is.EqualTo(HttpStatusCode.OK),
                    await touched.Content.ReadAsStringAsync().ConfigureAwait(false));
                XRegistryResponse version = await m_forwarder.Inner.ExecuteAsync(
                    Request(XRegistryAction.Read, "/schemagroups/wired/schemas/r/versions/v1")).ConfigureAwait(false);
                XRegistryResponse meta = await m_forwarder.Inner.ExecuteAsync(
                    Request(XRegistryAction.Read, "/schemagroups/wired/schemas/r/meta")).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(version.Metadata.GetProperty("epoch").GetInt32(), Is.EqualTo(1));
                    Assert.That(meta.Metadata.GetProperty("epoch").GetInt32(), Is.Zero);
                });
            }
            finally
            {
                await host.StopAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task NativeGatewayWritesThroughIndependentHttpFixtureAndCleanCloseDoesNotTouchAsync()
        {
            using var handler = new IndependentRegistryHttpHandler();
            using var http = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(http, new Uri("https://independent.example/api/"),
                new XRegistryHttpOptions { IsQualifiedBinding = true });
            XRegistryBridgeNativeOptions options = m_options with
            {
                NamespaceUri = "urn:native-http-test:" + Guid.NewGuid().ToString("N")
            };
            var factory = new CapturingFactory(new XRegistryBridgeNodeManagerFactory(endpoint, options));
            await m_server.NodeManagerLifecycle.AddAsync(factory, callerContext: null).ConfigureAwait(false);
            await m_session.FetchNamespaceTablesAsync().ConfigureAwait(false);
            NodeId group = await FindChildEntityAsync(factory.Manager!.RegistryNodeId,
                "/groups/g").ConfigureAwait(false);
            NodeId resource = await FindChildEntityAsync(group, "/groups/g/schemas/r").ConfigureAwait(false);
            var file = new ResourceTypeClient(m_session, resource, m_telemetry);
            uint clean = await file.OpenAsync(2).ConfigureAwait(false);
            await file.CloseAsync(clean).ConfigureAwait(false);
            Assert.That(handler.Writes, Is.Zero);
            uint write = await file.OpenAsync(6).ConfigureAwait(false);
            await file.WriteAsync(write, ByteString.From(new byte[] { 0, 255, 5 })).ConfigureAwait(false);
            await file.CloseAsync(write).ConfigureAwait(false);
            uint read = await file.OpenAsync(1).ConfigureAwait(false);
            ByteString bytes = await file.ReadAsync(read, 32).ConfigureAwait(false);
            await file.CloseAsync(read).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(handler.Writes, Is.EqualTo(1));
                Assert.That(handler.ExpectedEpoch, Is.EqualTo("0"));
                Assert.That(handler.Document, Is.EqualTo(new byte[] { 0, 255, 5 }));
                Assert.That(bytes.ToArray(), Is.EqualTo(handler.Document));
            });
            handler.Unavailable = true;
            Assert.ThrowsAsync<ServiceResultException>(async () => await file.OpenAsync(1).ConfigureAwait(false));
        }

        [Test]
        public async Task HttpHeaderFailureAbortsTheActualRemoteNativePreparedMutationAsync()
        {
            await SeedAsync("/schemagroups/g/schemas/r",
                /*lang=json,strict*/ """{"versionid":"v1","schema":"unchanged"}""")
                .ConfigureAwait(false);
            using IHost host = new HostBuilder().ConfigureWebHost(web => web.UseTestServer()
                .ConfigureServices(services => services.AddRouting())
                .Configure(app => app.UseRouting().UseEndpoints(routes => routes.MapXRegistry("/registry", m_native,
                    new XRegistryHttpRouteOptions(new Uri("https://frontend.example/registry/"))
                    {
                        CreateContextAsync = (_, _) => new ValueTask<XRegistryCallContext>(s_writer),
                        AuthorizeAsync = (_, _, _) => new ValueTask<bool>(true),
                        Transport = new XRegistryHttpOptions { MaximumHeaders = 5 }
                    })))).Build();
            await host.StartAsync().ConfigureAwait(false);
            try
            {
                using HttpClient client = host.GetTestClient();
                using var request = new HttpRequestMessage(HttpMethod.Put, "/registry/schemagroups/g/schemas/r")
                {
                    Content = new ByteArrayContent([9, 8, 7])
                };
                request.Headers.Add("xRegistry-epoch", "0");
                using HttpResponseMessage response = await client.SendAsync(request).ConfigureAwait(false);
                XRegistryResponse actual = await m_forwarder.Inner.ExecuteAsync(
                    Request(XRegistryAction.Read, "/schemagroups/g/schemas/r/versions/v1") with
                    {
                        View = XRegistryView.Default
                    }).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadGateway));
                    Assert.That(actual.Metadata.GetProperty("epoch").GetInt32(), Is.Zero);
                    Assert.That(Utf8(actual.Document), Is.EqualTo("unchanged"));
                    Assert.That(m_forwarder.Mutations, Is.Empty);
                });
            }
            finally
            {
                await host.StopAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task SynchronizationConvergesBothDirectionsAcrossRealNativeAndIndependentHttpAsync()
        {
            await ResetEndpointAsync(IndependentRegistryHttpHandler.ModelJson).ConfigureAwait(false);
            XRegistryResponse seeded = await m_forwarder.Inner.ExecuteAsync(
                Request(XRegistryAction.Replace, "/groups/g/schemas/r",
                    /*lang=json,strict*/ """{"versionid":"v1"}""") with
                {
                    Document = ByteString.From(new byte[] { 1, 2, 3 }),
                    ContentType = "application/octet-stream"
                }).ConfigureAwait(false);
            Assert.That(seeded.StatusCode, Is.EqualTo(201));
            await m_manager.RefreshAsync().ConfigureAwait(false);
            using var handler = new IndependentRegistryHttpHandler();
            using var client = new HttpClient(handler);
            var http = new XRegistryHttpEndpoint(client, new Uri("https://independent.example/api/"),
                new XRegistryHttpOptions { IsQualifiedBinding = true });
            var state = new MemoryXRegistrySyncStateStore();
            await using ConfiguredAsyncDisposable stateLifetime = state.ConfigureAwait(false);
            var options = new XRegistrySyncOptions("wire-sync", "native-registry", "https://independent.example/api/")
            {
                OpcUaContext = s_writer,
                HttpContext = s_writer
            };
            var engine = new XRegistrySynchronizer(m_native, http, state, options, m_telemetry);
            XRegistrySyncReport baseline = await engine.RunOnceAsync().ConfigureAwait(false);
            Assert.That(baseline.Status, Is.EqualTo(XRegistrySyncStatus.Succeeded), SyncDetails(baseline));
            await m_native.ExecuteAsync(Request(XRegistryAction.Merge, "/",
                /*lang=json,strict*/ """{"name":"from-native"}"""))
                .ConfigureAwait(false);
            XRegistrySyncReport toHttp = await engine.RunOnceAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(toHttp.Status, Is.EqualTo(XRegistrySyncStatus.Succeeded), SyncDetails(toHttp));
                Assert.That(handler.RootName, Is.EqualTo("from-native"));
                Assert.That(toHttp.Applied, Is.EqualTo(1));
            });
            handler.SetRootName("from-http");
            XRegistrySyncReport toNative = await engine.RunOnceAsync().ConfigureAwait(false);
            XRegistryResponse root = await m_native.ExecuteAsync(Request(XRegistryAction.Read,
                "/")).ConfigureAwait(false);
            XRegistrySyncReport stable = await engine.RunOnceAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(toNative.Status, Is.EqualTo(XRegistrySyncStatus.Succeeded), SyncDetails(toNative));
                Assert.That(root.Metadata.GetProperty("name").GetString(), Is.EqualTo("from-http"));
                Assert.That(toNative.Applied, Is.EqualTo(1));
                Assert.That(stable.Applied + stable.Deleted, Is.Zero);
            });
        }

        private static string SyncDetails(XRegistrySyncReport report)
        {
            return string.Join(Environment.NewLine, report.Records.ToList().Select(record =>
                record.Path + ": " + record.Kind + ": " + record.Detail));
        }

        private sealed class IndependentRegistryHttpHandler : HttpMessageHandler
        {
            public byte[] Document { get; private set; } = [1, 2, 3];

            public int Writes { get; private set; }

            public string? ExpectedEpoch { get; private set; }

            public bool Unavailable { get; set; }

            public string? RootName { get; private set; }

            public int RootEpoch { get; private set; }

            public void SetRootName(string value)
            {
                RootName = value;
                RootEpoch++;
            }

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Unavailable)
                {
                    return Json(new JsonObject { ["type"] = "unavailable" }, HttpStatusCode.ServiceUnavailable);
                }
                string path = request.RequestUri!.AbsolutePath.TrimEnd('/');
                if (request.Method == HttpMethod.Put && path == "/api")
                {
                    using var metadata = JsonDocument.Parse(
                        await request.Content!.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
                    if (metadata.RootElement.GetProperty("epoch").GetInt32() != RootEpoch)
                    {
                        return Json(new JsonObject { ["type"] = "mismatched_epoch" }, HttpStatusCode.BadRequest);
                    }
                    SetRootName(metadata.RootElement.GetProperty("name").GetString()!);
                    return Json(Root());
                }
                if (request.Method == HttpMethod.Put && path == "/api/groups/g/schemas/r/versions/v1")
                {
                    ExpectedEpoch = request.Headers.GetValues("xRegistry-epoch").Single();
                    if (ExpectedEpoch != Writes.ToString(CultureInfo.InvariantCulture))
                    {
                        return Json(new JsonObject { ["type"] = "mismatched_epoch" }, HttpStatusCode.BadRequest);
                    }
                    Document = await request.Content!.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                    Writes++;
                    return ContentResponse();
                }
                Assert.That(request.Method, Is.EqualTo(HttpMethod.Get));
                if (path is "/api/groups/g/schemas/r" or "/api/groups/g/schemas/r/versions/v1")
                {
                    return ContentResponse();
                }
                JsonObject body = path switch
                {
                    "/api" => Root(),
                    "/api/model" => JsonNode.Parse(ModelJson)!.AsObject(),
                    "/api/capabilities" => JsonNode.Parse("""
                        {"specversions":["1.0-rc4"],"available":{"entities":{"mutable":true},
                        "model":{"mutable":false},"capabilities":{"mutable":false}},"mutable":[]}
                        """)!.AsObject(),
                    "/api/groups" => new JsonObject { ["g"] = Group() },
                    "/api/groups/g" => Group(),
                    "/api/groups/g/schemas" => new JsonObject { ["r"] = Version() },
                    "/api/groups/g/schemas/r$details" => Version(),
                    "/api/groups/g/schemas/r/versions/v1$details" => Version(),
                    "/api/groups/g/schemas/r/versions" => new JsonObject { ["v1"] = Version() },
                    "/api/groups/g/schemas/r/meta" => new JsonObject
                    {
                        ["schemaid"] = "r",
                        ["epoch"] = 0,
                        ["defaultversionid"] = "v1",
                        ["createdat"] = k_created,
                        ["modifiedat"] = k_created,
                        ["defaultversionsticky"] = false
                    },
                    _ => throw new AssertionException("Unexpected independent HTTP path: " + path)
                };
                return Json(body);
            }

            private JsonObject Root()
            {
                var root = new JsonObject
                {
                    ["registryid"] = "independent",
                    ["specversion"] = "1.0-rc4",
                    ["epoch"] = RootEpoch,
                    ["self"] = "https://independent.example/api",
                    ["xid"] = "/"
                };
                if (RootName is not null)
                {
                    root["name"] = RootName;
                }
                return root;
            }

            private HttpResponseMessage ContentResponse()
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Document) };
                response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                response.Headers.Add("xRegistry-schemaid", "r");
                response.Headers.Add("xRegistry-versionid", "v1");
                response.Headers.Add("xRegistry-epoch", Writes.ToString(CultureInfo.InvariantCulture));
                response.Headers.Add("xRegistry-createdat", k_created);
                return response;
            }

            private JsonObject Version()
            {
                return new JsonObject
                {
                    ["schemaid"] = "r",
                    ["versionid"] = "v1",
                    ["epoch"] = Writes,
                    ["contenttype"] = "application/octet-stream",
                    ["createdat"] = k_created,
                    ["modifiedat"] = k_created,
                    ["ancestorid"] = "v1",
                    ["isdefault"] = true
                };
            }

            private static JsonObject Group()
            {
                return new JsonObject
                {
                    ["groupid"] = "g",
                    ["epoch"] = 0,
                    ["createdat"] = k_created,
                    ["modifiedat"] = k_created
                };
            }

            private static HttpResponseMessage Json(JsonObject body, HttpStatusCode status = HttpStatusCode.OK)
            {
                return new HttpResponseMessage(status)
                {
                    Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
                };
            }

            private const string k_created = "2026-01-01T00:00:00Z";

            public const string ModelJson = """
                {"groups":{"groups":{"singular":"group","resources":{"schemas":{
                "singular":"schema","hasdocument":true}}}}}
                """;
        }
    }
}
#endif
