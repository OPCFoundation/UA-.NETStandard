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

using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using Opc.Ua.Server;
using Opc.Ua.XRegistry.Bridge.Native;
using Opc.Ua.XRegistry.Bridge.Sync;
using Opc.Ua.XRegistry.Http;
using Opc.Ua.XRegistry.Protocol;
using Opc.Ua.XRegistry.Server.Protocol;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Aot.Tests
{
    /// <summary>
    /// Executes both gateway directions and durable reconciliation through real loopback transports.
    /// The published NativeAOT process owns its generated credentials, stores and listeners.
    /// </summary>
    public sealed class XRegistryGatewayAotTests
    {
        [Test]
        public async Task NativeGatewayHttpGatewayAndSynchronizationExecuteWithoutReflectionAsync()
        {
            string root = Path.Combine(Path.GetTempPath(), "xregistry-aot-" + Guid.NewGuid().ToString("N"));
            ITelemetryContext telemetry = DefaultTelemetry.Create(builder => builder.SetMinimumLevel(LogLevel.Warning));
            var fixture = new AotServerFixture<ReferenceServer>(context => new ReferenceServer(context), telemetry)
            {
                AutoAccept = true,
                SecurityNone = false
            };
            Directory.CreateDirectory(root);
            try
            {
                await fixture.LoadConfigurationAsync(Path.Combine(root, "server")).ConfigureAwait(false);
                fixture.Config.ServerConfiguration.UserTokenPolicies =
                [
                    new UserTokenPolicy
                    {
                        PolicyId = "xregistry-aot-user",
                        TokenType = UserTokenType.UserName,
                        SecurityPolicyUri = SecurityPolicies.Basic256Sha256
                    }
                ];
                ReferenceServer server = await fixture.StartAsync().ConfigureAwait(false);
                string userName = "xregistry-aot-" + Guid.NewGuid().ToString("N");
                byte[] password = Encoding.UTF8.GetBytes(Guid.NewGuid().ToString("N"));
                await Assert.That(server.UserDatabase.CreateUser(userName, password, [Role.Operator])).IsTrue();
                var writer = new XRegistryCallContext(userName)
                {
                    IsAuthenticated = true,
                    Authority = "aot-fixture",
                    Roles = ["xregistry.write"]
                };
                using var model = JsonDocument.Parse(
                    """
                    {"groups":{"groups":{"singular":"group","attributes":{"cycles":{"type":"integer"}},
                    "resources":{"schemas":{"singular":"schema"}}}}}
                    """);
                using var nativeStore = new FileXRegistryTransactionStore(Path.Combine(root, "native-state"));
                await using var blobs = new FileXRegistryDocumentStore(Path.Combine(root, "documents"));
                using var nativeProvider = new XRegistryTransactionalEndpoint(new XRegistryTransactionalOptions
                {
                    RegistryId = "aot-native",
                    Model = model.RootElement,
                    PublicRoot = new Uri("https://native.example/registry/"),
                    DocumentStore = blobs,
                    ShortLinksEnabled = true
                }, nativeStore);
                _ = await nativeProvider.InitializeShortLinksAsync(writer).ConfigureAwait(false);
                var nativeOptions = new XRegistryBridgeNativeOptions
                {
                    NamespaceUri = "urn:xregistry:aot:native",
                    RootIdentifier = "Native",
                    AttributeMappings =
                    [
                        new("/groups", XRegistryNativeAttributeScope.Group, ["cycles"],
                            [new("urn:xregistry:aot:properties", "Cycles")]) { NativeType = BuiltInType.Int32 }
                    ],
                    ProjectionContext = writer,
                    ContextFactory = context => writer with
                    {
                        SessionId = (context as ISessionSystemContext)?.SessionId.ToString()
                    },
                    AuthorizeCallerAsync = (context, _, ct) =>
                    {
                        ct.ThrowIfCancellationRequested();
                        return new ValueTask<bool>(
                            context is ISessionSystemContext { UserIdentity: { } identity } &&
                            identity.TokenType == UserTokenType.UserName &&
                            identity.DisplayName == userName);
                    },
                    SpoolDirectory = Path.Combine(root, "spool"),
                    MemoryBufferThreshold = 64
                };
                await server.NodeManagerLifecycle.AddAsync(
                    new XRegistryBridgeNodeManagerFactory(nativeProvider, nativeOptions),
                    callerContext: null).ConfigureAwait(false);
                await using var application =
                    new ApplicationInstance(telemetry) { ApplicationName = "XRegistryAotClient" };
                string pki = Path.Combine(root, "client");
                ApplicationConfiguration configuration = await application
                    .Build("urn:xregistry:aot:client", "urn:xregistry:aot:product")
                    .AsClient()
                    .AddSecurityConfiguration(ApplicationConfigurationBuilder.CreateDefaultApplicationCertificates(
                        "CN=XRegistryAotClient, O=OPC Foundation, DC=localhost", CertificateStoreType.Directory, pki),
                            pki)
                    .SetAutoAcceptUntrustedCertificates(true)
                    .CreateAsync().ConfigureAwait(false);
                await Assert.That(
                    await application.CheckApplicationInstanceCertificatesAsync(true).ConfigureAwait(false)).IsTrue();
                var nativeUrl = new Uri($"opc.tcp://localhost:{fixture.Port}/ReferenceServer");
                using DiscoveryClient discovery = await DiscoveryClient.CreateAsync(
                    configuration, nativeUrl, EndpointConfiguration.Create(configuration)).ConfigureAwait(false);
                ArrayOf<EndpointDescription> endpoints =
                    await discovery.GetEndpointsAsync(default).ConfigureAwait(false);
                EndpointDescription encrypted = endpoints.ToList().Single(endpoint =>
                    endpoint.SecurityMode == MessageSecurityMode.SignAndEncrypt &&
                    endpoint.SecurityPolicyUri == SecurityPolicies.Basic256Sha256);
                encrypted.EndpointUrl = new UriBuilder(encrypted.EndpointUrl) { Host = "localhost" }.Uri.AbsoluteUri;
                await using ManagedSession session = await ManagedSession.CreateAsync(
                    configuration, new ConfiguredEndpoint(null, encrypted, EndpointConfiguration.Create(configuration)),
                    new DefaultSessionFactory(telemetry), new UserIdentity(userName, password),
                    telemetry: telemetry, sessionName: "XRegistryNativeAot").ConfigureAwait(false);
                Array.Clear(password, 0, password.Length);
                await session.FetchNamespaceTablesAsync().ConfigureAwait(false);
                var native = new XRegistryOpcUaEndpoint(session,
                    ExpandedNodeId.ToNodeId(nativeOptions.RootAddress, session.NamespaceUris),
                        nativeOptions, telemetry);

                int port = AotServerFixtureSupport.GetNextFreeIPPort();
                var httpRoot = new Uri($"http://127.0.0.1:{port}/registry/");
                var gatewayRoot = new Uri($"http://127.0.0.1:{port}/gateway/");
                using var httpProvider = new XRegistryTransactionalEndpoint(new XRegistryTransactionalOptions
                {
                    RegistryId = "aot-http",
                    Model = model.RootElement,
                    PublicRoot = httpRoot
                }, new InMemoryXRegistryTransactionStore());
                WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
                builder.WebHost.UseKestrel(options => options.Listen(IPAddress.Loopback, port));
                builder.Logging.SetMinimumLevel(LogLevel.Warning);
                WebApplication app = builder.Build();
                await using ConfiguredAsyncDisposable appLifetime = app.ConfigureAwait(false);
                var wireOptions = new XRegistryHttpOptions { AllowLoopbackHttp = true, Telemetry = telemetry };
                app.MapXRegistry("/registry", httpProvider, new XRegistryHttpRouteOptions(httpRoot)
                {
                    Transport = wireOptions,
                    CreateContextAsync = (_, _) => new ValueTask<XRegistryCallContext>(writer),
                    AuthorizeAsync = (_, _, _) => new ValueTask<bool>(true)
                });
                app.MapXRegistry("/gateway", native, new XRegistryHttpRouteOptions(gatewayRoot)
                {
                    Transport = wireOptions,
                    CreateContextAsync = (_, _) => new ValueTask<XRegistryCallContext>(writer),
                    AuthorizeAsync = (_, _, _) => new ValueTask<bool>(true)
                });
                await app.StartAsync().ConfigureAwait(false);
                try
                {
                    using var handler = new HttpClientHandler
                    {
                        AllowAutoRedirect = false,
                        UseCookies = false,
                        CheckCertificateRevocationList = true
                    };
                    using var client = new HttpClient(handler);
                    using var content = new StringContent(
                        """
                        {"name":"from-http","cycles":7,"schemas":{"r":{"versionid":"v1","schema":{"type":"object"}}}}
                        """,
                        Encoding.UTF8, "application/json");
                    using HttpResponseMessage written =
                        await client.PutAsync(new Uri(gatewayRoot, "groups/native"), content)
                        .ConfigureAwait(false);
                    await Assert.That(written.StatusCode).IsEqualTo(HttpStatusCode.Created);
                    XRegistryResponse actual = await native.ExecuteAsync(
                        new XRegistryRequest(XRegistryAction.Read, "/groups/native") { Context = writer })
                            .ConfigureAwait(false);
                    await Assert.That(actual.Metadata.GetProperty("name").GetString()).IsEqualTo("from-http");
                    string alias = new Uri(actual.Metadata.GetProperty("shortself").GetString()!).Segments[^1];
                    using HttpResponseMessage aliasRead = await client.GetAsync(new Uri(gatewayRoot, "_s/" + alias))
                        .ConfigureAwait(false);
                    using var aliasMetadata = JsonDocument.Parse(
                        await aliasRead.Content.ReadAsStringAsync().ConfigureAwait(false));
                    await Assert.That(aliasRead.StatusCode).IsEqualTo(HttpStatusCode.OK);
                    await Assert.That(aliasMetadata.RootElement.GetProperty("cycles").GetInt32()).IsEqualTo(7);

                    var http = new XRegistryHttpEndpoint(client, httpRoot,
                        wireOptions with { IsQualifiedBinding = true });
                    XRegistryBridgeNativeOptions reverseOptions = nativeOptions with
                    {
                        NamespaceUri = "urn:xregistry:aot:http",
                        RootIdentifier = "Http"
                    };
                    await server.NodeManagerLifecycle.AddAsync(
                        new XRegistryBridgeNodeManagerFactory(http, reverseOptions),
                        callerContext: null).ConfigureAwait(false);
                    await session.FetchNamespaceTablesAsync().ConfigureAwait(false);
                    var reverse = new XRegistryOpcUaEndpoint(session,
                        ExpandedNodeId.ToNodeId(reverseOptions.RootAddress, session.NamespaceUris),
                            reverseOptions, telemetry);
                    using var metadata = JsonDocument.Parse("""{"name":"from-opcua"}""");
                    XRegistryResponse reverseWrite = await reverse.ExecuteAsync(new XRegistryRequest(
                        XRegistryAction.Replace, "/groups/http")
                    {
                        Context = writer,
                        View = XRegistryView.Metadata,
                        Metadata = metadata.RootElement
                    }).ConfigureAwait(false);
                    await Assert.That(reverseWrite.StatusCode).IsEqualTo(201);
                    using HttpResponseMessage read =
                        await client.GetAsync(new Uri(httpRoot, "groups/http")).ConfigureAwait(false);
                    using var response =
                        JsonDocument.Parse(await read.Content.ReadAsStringAsync().ConfigureAwait(false));
                    await Assert.That(response.RootElement.GetProperty("name").GetString()).IsEqualTo("from-opcua");

                    await using var state =
                        new FileXRegistrySyncStateStore(LocalFileSystem.Instance, Path.Combine(root, "sync"));
                    var synchronizer = new XRegistrySynchronizer(native, http, state, new XRegistrySyncOptions(
                        "aot-pair", nativeUrl.AbsoluteUri, httpRoot.AbsoluteUri)
                    {
                        OpcUaContext = writer,
                        HttpContext = writer,
                        ConflictPolicy = XRegistrySyncConflictPolicy.PreferOpcUa
                    }, telemetry);
                    XRegistrySyncReport report = await synchronizer.RunOnceAsync().ConfigureAwait(false);
                    await Assert.That(report.Status).IsEqualTo(XRegistrySyncStatus.Succeeded);
                    await Assert.That(report.Applied).IsGreaterThan(0);
                    XRegistrySyncReport repeated = await synchronizer.RunOnceAsync().ConfigureAwait(false);
                    await Assert.That(repeated.Status).IsEqualTo(XRegistrySyncStatus.Succeeded);
                    await Assert.That(repeated.Applied).IsEqualTo(0);
                }
                finally
                {
                    using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    await app.StopAsync(stop.Token).ConfigureAwait(false);
                }
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
