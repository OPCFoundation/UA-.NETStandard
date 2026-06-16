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

#nullable enable

using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;

namespace Opc.Ua.Bindings.WebApi.Tests
{
    /// <summary>
    /// Regression tests for the WebApi route metadata fix
    /// (alert <c>sec-7-require-authorization</c>). Pins the
    /// per-route authorization metadata semantics — the contributor
    /// applies <c>RequireAuthorization()</c> on the route group when
    /// auth schemes are registered, and the discovery routes
    /// (<c>/findservers</c>, <c>/getendpoints</c>) carry
    /// <c>AllowAnonymous</c> metadata so they remain reachable
    /// without a credential.
    /// </summary>
    [TestFixture]
    [Category("WebApiAuthorization")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class WebApiRouteAuthorizationMetadataTests
    {
        [Test]
        public void DiscoveryRoutesCarryAllowAnonymousMetadata()
        {
            using TestServer testServer = BuildTestServer(applyRequireAuth: true);
            EndpointDataSource dataSource = testServer.Services
                .GetRequiredService<EndpointDataSource>();

            RouteEndpoint findServers = FindRoute(dataSource, "/findservers");
            RouteEndpoint getEndpoints = FindRoute(dataSource, "/getendpoints");

            Assert.That(findServers.Metadata.GetMetadata<IAllowAnonymous>(), Is.Not.Null,
                "/findservers must carry IAllowAnonymous metadata so discovery is " +
                "callable without authentication.");
            Assert.That(getEndpoints.Metadata.GetMetadata<IAllowAnonymous>(), Is.Not.Null,
                "/getendpoints must carry IAllowAnonymous metadata so discovery is " +
                "callable without authentication.");
        }

        [Test]
        public void BusinessRoutesCarryAuthorizationMetadataWhenGroupRequiresAuth()
        {
            using TestServer testServer = BuildTestServer(applyRequireAuth: true);
            EndpointDataSource dataSource = testServer.Services
                .GetRequiredService<EndpointDataSource>();

            string[] businessRoutes =
            {
                "/read", "/write", "/historyread", "/historyupdate", "/call",
                "/browse", "/browsenext", "/translate", "/registernodes",
                "/unregisternodes", "/createsession", "/activatesession",
                "/closesession", "/cancel", "/createmonitoreditems",
                "/modifymonitoreditems", "/setmonitoringmode", "/settriggering",
                "/deletemonitoreditems", "/createsubscription",
                "/modifysubscription", "/setpublishingmode", "/publish",
                "/republish", "/transfersubscriptions", "/deletesubscriptions"
            };

            foreach (string route in businessRoutes)
            {
                RouteEndpoint endpoint = FindRoute(dataSource, route);
                Assert.That(endpoint.Metadata.GetMetadata<IAuthorizeData>(), Is.Not.Null,
                    $"{route} must carry IAuthorizeData metadata when RequireAuthorization() " +
                    "is applied to the group.");
                Assert.That(endpoint.Metadata.GetMetadata<IAllowAnonymous>(), Is.Null,
                    $"{route} must NOT carry IAllowAnonymous metadata — only discovery " +
                    "routes are anonymous-exempt.");
            }
        }

        [Test]
        public void NoRouteRequiresAuthorizationWhenGroupHasNoAuth()
        {
            using TestServer testServer = BuildTestServer(applyRequireAuth: false);
            EndpointDataSource dataSource = testServer.Services
                .GetRequiredService<EndpointDataSource>();

            // When the contributor doesn't call RequireAuthorization()
            // (no auth schemes registered), every route should be free
            // of IAuthorizeData metadata so the historical anonymous
            // flow is preserved.
            foreach (RouteEndpoint endpoint in dataSource.Endpoints.OfType<RouteEndpoint>())
            {
                Assert.That(endpoint.Metadata.GetMetadata<IAuthorizeData>(), Is.Null,
                    $"{endpoint.RoutePattern.RawText} must NOT carry IAuthorizeData when " +
                    "no auth scheme is registered.");
            }
        }

        private static TestServer BuildTestServer(bool applyRequireAuth)
        {
            IHostBuilder hostBuilder = new HostBuilder()
                .ConfigureWebHost(webHost =>
                {
                    webHost.UseTestServer();
                    webHost.ConfigureServices(services =>
                    {
                        services.AddRouting();
                        services.AddAuthorization();
                    });
                    webHost.Configure(app =>
                    {
                        app.UseRouting();
                        if (applyRequireAuth)
                        {
                            app.UseAuthorization();
                        }
                        app.UseEndpoints(endpoints =>
                        {
                            IEndpointConventionBuilder group = endpoints.MapWebApiEndpoints();
                            if (applyRequireAuth)
                            {
                                group.RequireAuthorization();
                            }
                        });
                    });
                });
            IHost host = hostBuilder.Start();
            return host.GetTestServer();
        }

        private static RouteEndpoint FindRoute(EndpointDataSource dataSource, string path)
        {
            return dataSource.Endpoints
                .OfType<RouteEndpoint>()
                .First(e => e.RoutePattern.RawText == path);
        }
    }
}
