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
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Opc.Ua.Bindings.WebApi;
using Opc.Ua.Schema;
using Opc.Ua.Schema.OpenApi;

namespace Opc.Ua.Bindings.Https.WebApi.Tests
{
    /// <summary>
    /// Tests of the OpenAPI document route of the REST binding: off by
    /// default, opt-in through <see cref="WebApiTransportOptions"/>, scoped
    /// by the service set and protected like the service routes.
    /// </summary>
    [TestFixture]
    [Category("WebApiOpenApiDocument")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class WebApiOpenApiDocumentEndpointTests
    {
        private const string kDocumentPath = "/openapi.json";

        private static Uri DocumentUri(string path)
        {
            return new Uri(path, UriKind.Relative);
        }

        [Test]
        public async Task DocumentIsNotServedByDefaultAsync()
        {
            await using TestHost host = await TestHost.StartAsync(new WebApiTransportOptions())
                .ConfigureAwait(false);

            using HttpResponseMessage response = await host.Client.GetAsync(DocumentUri(kDocumentPath))
                .ConfigureAwait(false);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(host.GetEndpoint(kDocumentPath), Is.Null);
        }

        [Test]
        public async Task DocumentIsServedAtTheConfiguredPathAsync()
        {
            await using TestHost host = await TestHost.StartAsync(
                new WebApiTransportOptions { OpenApiDocumentPath = "/spec/opcua.json" }).ConfigureAwait(false);

            using HttpResponseMessage response = await host.Client.GetAsync(DocumentUri("/spec/opcua.json"))
                .ConfigureAwait(false);
            using HttpResponseMessage atDefaultPath = await host.Client.GetAsync(DocumentUri(kDocumentPath))
                .ConfigureAwait(false);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/json"));
            Assert.That(atDefaultPath.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        }

        [Test]
        public async Task DocumentWithoutSchemasDescribesAllServicesAsync()
        {
            await using TestHost host = await TestHost.StartAsync(
                new WebApiTransportOptions { OpenApiDocumentPath = kDocumentPath }).ConfigureAwait(false);

            JsonObject document = await host.GetDocumentAsync().ConfigureAwait(false);

            Assert.That(document["paths"]!.AsObject(), Has.Count.EqualTo(WebApiServiceRoutes.Count));
            Assert.That(document.ContainsKey("components"), Is.False);
            Assert.That(document.ContainsKey("servers"), Is.False);
        }

        [Test]
        public async Task DocumentWithSchemasContainsTheComponentSchemasAsync()
        {
            await using TestHost host = await TestHost.StartAsync(new WebApiTransportOptions
            {
                OpenApiDocumentPath = kDocumentPath,
                OpenApiIncludeSchemas = true
            }).ConfigureAwait(false);

            JsonObject document = await host.GetDocumentAsync().ConfigureAwait(false);

            JsonObject schemas = document["components"]!["schemas"]!.AsObject();
            Assert.That(schemas.ContainsKey("ReadRequest"), Is.True);
            Assert.That(schemas.ContainsKey("DataValue"), Is.True);
        }

        [Test]
        public async Task SessionlessServiceSetMapsAndDescribesOnlyTheSessionlessServicesAsync()
        {
            await using TestHost host = await TestHost.StartAsync(new WebApiTransportOptions
            {
                ServiceSet = WebApiServiceSet.Sessionless,
                OpenApiDocumentPath = kDocumentPath
            }).ConfigureAwait(false);

            JsonObject document = await host.GetDocumentAsync().ConfigureAwait(false);

            string[] sessionless = [.. WebApiServiceRoutes.GetRoutes(WebApiServiceSet.Sessionless).ToArray()!.Select(r => r.Path)];
            Assert.That(document["paths"]!.AsObject().Select(p => p.Key), Is.EquivalentTo(sessionless));
            string[] mappedPostRoutes =
            [
                .. host.Endpoints
                    .Where(e => e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains("POST") == true)
                    .Select(e => e.RoutePattern.RawText!)
            ];
            Assert.That(mappedPostRoutes, Is.EquivalentTo(sessionless));
            Assert.That(host.GetEndpoint("/findservers"), Is.Null);
            Assert.That(host.GetEndpoint("/createsession"), Is.Null);
        }

        [Test]
        public async Task PathBaseIsListedAsTheServerUrlAsync()
        {
            await using TestHost host = await TestHost.StartAsync(
                new WebApiTransportOptions { OpenApiDocumentPath = kDocumentPath },
                pathBase: "/opcua").ConfigureAwait(false);

            JsonObject document = await host.GetDocumentAsync("/opcua" + kDocumentPath).ConfigureAwait(false);

            Assert.That((string?)document["servers"]![0]!["url"], Is.EqualTo("/opcua/"));
        }

        [Test]
        public async Task RepeatedRequestsGetTheSameDocumentAsync()
        {
            await using TestHost host = await TestHost.StartAsync(new WebApiTransportOptions
            {
                OpenApiDocumentPath = kDocumentPath,
                OpenApiIncludeSchemas = true
            }).ConfigureAwait(false);

            string first = await host.Client.GetStringAsync(DocumentUri(kDocumentPath)).ConfigureAwait(false);
            string second = await host.Client.GetStringAsync(DocumentUri(kDocumentPath)).ConfigureAwait(false);

            Assert.That(second, Is.EqualTo(first));
        }

        [Test]
        public async Task PostToTheDocumentPathIsNotAllowedAsync()
        {
            await using TestHost host = await TestHost.StartAsync(
                new WebApiTransportOptions { OpenApiDocumentPath = kDocumentPath }).ConfigureAwait(false);

            using HttpResponseMessage response = await host.Client.PostAsync(DocumentUri(kDocumentPath), new StringContent("{}"))
                .ConfigureAwait(false);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.MethodNotAllowed));
        }

        [Test]
        public async Task RegisteredGeneratorBuildsTheDocumentAsync()
        {
            // A generator that does not know the service types cannot build a
            // document with schemas: the failure shows the registered one ran.
            var generator = new WebApiOpenApiGenerator(new DataTypeDefinitionRegistry());
            await using TestHost host = await TestHost.StartAsync(
                new WebApiTransportOptions { OpenApiDocumentPath = kDocumentPath, OpenApiIncludeSchemas = true },
                generator: generator).ConfigureAwait(false);

            await Assert.ThatAsync(
                () => host.Client.GetAsync(DocumentUri(kDocumentPath)),
                Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
        }

        [Test]
        public async Task DocumentRequiresAuthenticationLikeTheServiceRoutesAsync()
        {
            await using TestHost host = await TestHost.StartAsync(
                new WebApiTransportOptions { OpenApiDocumentPath = kDocumentPath },
                authenticate: true).ConfigureAwait(false);

            using HttpResponseMessage anonymous = await host.Client.GetAsync(DocumentUri(kDocumentPath)).ConfigureAwait(false);
            using var authenticatedRequest = new HttpRequestMessage(HttpMethod.Get, DocumentUri(kDocumentPath));
            authenticatedRequest.Headers.Add(TestAuthenticationHandler.UserHeader, "reader");
            using HttpResponseMessage authenticated = await host.Client.SendAsync(authenticatedRequest)
                .ConfigureAwait(false);
            using var serviceRequest = new HttpRequestMessage(HttpMethod.Post, DocumentUri("/read"))
            {
                Content = new StringContent("{}")
            };
            using HttpResponseMessage service = await host.Client.SendAsync(serviceRequest).ConfigureAwait(false);

            Assert.That(anonymous.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(authenticated.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(service.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        }

        [Test]
        public async Task DocumentRouteCarriesTheAuthorizationMetadataOfTheServiceRoutesAsync()
        {
            await using TestHost host = await TestHost.StartAsync(
                new WebApiTransportOptions { OpenApiDocumentPath = kDocumentPath },
                authenticate: true).ConfigureAwait(false);

            RouteEndpoint document = host.GetEndpoint(kDocumentPath)!;
            RouteEndpoint read = host.GetEndpoint("/read")!;
            RouteEndpoint discovery = host.GetEndpoint("/findservers")!;

            Assert.That(document.Metadata.GetMetadata<IAuthorizeData>(), Is.Not.Null);
            Assert.That(document.Metadata.GetMetadata<IAllowAnonymous>(), Is.Null);
            Assert.That(read.Metadata.GetMetadata<IAuthorizeData>(), Is.Not.Null);
            Assert.That(discovery.Metadata.GetMetadata<IAllowAnonymous>(), Is.Not.Null);
        }

        [Test]
        public async Task DocumentRouteIsOpenWhenNoAuthenticationSchemeIsRegisteredAsync()
        {
            await using TestHost host = await TestHost.StartAsync(
                new WebApiTransportOptions { OpenApiDocumentPath = kDocumentPath }).ConfigureAwait(false);

            Assert.That(host.GetEndpoint(kDocumentPath)!.Metadata.GetMetadata<IAuthorizeData>(), Is.Null);
        }

        [Test]
        public void MapWebApiEndpointsRejectsNullArguments()
        {
            var endpoints = new UnusedEndpointRouteBuilder();

            Assert.That(
                () => WebApiEndpointRouteBuilderExtensions.MapWebApiEndpoints(
                    null!,
                    new WebApiTransportOptions()),
                Throws.TypeOf<ArgumentNullException>());
            Assert.That(
                () => endpoints.MapWebApiEndpoints(null!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void MapWebApiEndpointsRejectsAnUndefinedServiceSet()
        {
            var endpoints = new UnusedEndpointRouteBuilder();

            Assert.That(
                () => endpoints.MapWebApiEndpoints(new WebApiTransportOptions { ServiceSet = (WebApiServiceSet)42 }),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        /// <summary>
        /// An endpoint route builder the argument checks never reach.
        /// </summary>
        private sealed class UnusedEndpointRouteBuilder : IEndpointRouteBuilder
        {
            public IServiceProvider ServiceProvider => throw new NotSupportedException();

            public ICollection<EndpointDataSource> DataSources => throw new NotSupportedException();

            public IApplicationBuilder CreateApplicationBuilder()
            {
                throw new NotSupportedException();
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Performance",
            "CA1812:Avoid uninstantiated internal classes",
            Justification = "Instantiated by the authentication builder; TODO: remove if CA1812 tracks DI.")]
        private sealed class TestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
        {
            public const string SchemeName = "Test";
            public const string UserHeader = "X-Test-User";

            public TestAuthenticationHandler(
                IOptionsMonitor<AuthenticationSchemeOptions> options,
                ILoggerFactory logger,
                UrlEncoder encoder)
                : base(options, logger, encoder)
            {
            }

            protected override Task<AuthenticateResult> HandleAuthenticateAsync()
            {
                if (!Request.Headers.TryGetValue(UserHeader, out Microsoft.Extensions.Primitives.StringValues user))
                {
                    return Task.FromResult(AuthenticateResult.NoResult());
                }
                var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, user.ToString())], SchemeName);
                var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
                return Task.FromResult(AuthenticateResult.Success(ticket));
            }
        }

        /// <summary>
        /// Runs the REST pipeline of the startup contributor on a test
        /// server.
        /// </summary>
        private sealed class TestHost : IAsyncDisposable
        {
            private readonly HttpsTransportListener m_listener;
            private readonly IHost m_host;

            private TestHost(HttpsTransportListener listener, IHost host)
            {
                m_listener = listener;
                m_host = host;
                Client = host.GetTestClient();
            }

            public HttpClient Client { get; }

            public IEnumerable<RouteEndpoint> Endpoints => m_host.Services
                .GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>();

            public static async Task<TestHost> StartAsync(
                WebApiTransportOptions options,
                bool authenticate = false,
                string? pathBase = null,
                WebApiOpenApiGenerator? generator = null)
            {
                var telemetry = new TestTelemetryContext();
                var listener = new HttpsTransportListener(Utils.UriSchemeHttps, telemetry);
                var server = new WebApiServer(ServiceMessageContext.CreateEmpty(telemetry), "openapi-test");
                var contributor = new WebApiHttpsStartupContributor(server, options, generator);

                IHost host = await new HostBuilder()
                    .ConfigureWebHost(webHost => webHost
                        .UseTestServer()
                        .ConfigureServices(services =>
                        {
                            services.AddLogging();
                            if (authenticate)
                            {
                                services.AddAuthentication(TestAuthenticationHandler.SchemeName)
                                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                                        TestAuthenticationHandler.SchemeName,
                                        _ => { });
                            }
                            contributor.ConfigureServices(services, listener);
                        })
                        .Configure(app =>
                        {
                            if (pathBase != null)
                            {
                                app.UsePathBase(pathBase);
                            }
                            contributor.Configure(app, listener);
                        }))
                    .StartAsync()
                    .ConfigureAwait(false);
                return new TestHost(listener, host);
            }

            public RouteEndpoint? GetEndpoint(string path)
            {
                return Endpoints.FirstOrDefault(e => e.RoutePattern.RawText == path);
            }

            public async Task<JsonObject> GetDocumentAsync(string path = kDocumentPath)
            {
                string text = await Client.GetStringAsync(DocumentUri(path)).ConfigureAwait(false);
                return JsonNode.Parse(text)!.AsObject();
            }

            public async ValueTask DisposeAsync()
            {
                Client.Dispose();
                await m_host.StopAsync().ConfigureAwait(false);
                m_host.Dispose();
                await m_listener.DisposeAsync().ConfigureAwait(false);
            }
        }

        private sealed class TestTelemetryContext : TelemetryContextBase
        {
            public TestTelemetryContext()
                : base(NullLoggerFactory.Instance)
            {
            }
        }
    }
}

#endif // NET8_0_OR_GREATER
