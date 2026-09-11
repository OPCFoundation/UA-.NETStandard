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
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Http.Tests
{
    internal static class HttpHostTestData
    {
        public static Uri PublicRoot { get; } = new("https://public.example/registry/");

        public static Uri ModelUri { get; } = new("/registry/model", UriKind.Relative);

        public static XRegistryHttpRouteOptions OpenOptions { get; } = new(PublicRoot)
        {
            RequireAuthenticatedUser = false,
            AuthorizeAsync = static (_, _, _) => new ValueTask<bool>(true)
        };

        public static string Header(HttpResponseMessage response, string name)
        {
            return string.Join(", ", response.Headers.TryGetValues(name, out IEnumerable<string>? values)
                ? values : response.Content.Headers.GetValues(name));
        }
    }

    internal sealed class FakeRegistryEndpoint : IXRegistryPreparedEndpoint
    {
        public XRegistryEndpointDescription Description { get; set; } = new("independent-provider")
        {
            Profile = "test-provider",
            Model = HttpTestData.Json(HttpTestData.Model),
            Capabilities = HttpTestData.Json(HttpTestData.Capabilities),
            SupportsAtomicMutations = true,
            SupportsConditionalMutations = true,
            SupportsWriteTouch = true,
            SupportsOperationReplay = false,
            SupportsPreparedMutations = true
        };

        public XRegistryResponse Response { get; set; } = new(200)
        {
            Metadata = HttpTestData.Json(/*lang=json,strict*/ """{"from":"provider","n":7}""")
        };

        public List<XRegistryCallContext> Inspections { get; } = [];

        public List<XRegistryRequest> Requests { get; } = [];

        public List<CancellationToken> InspectionTokens { get; } = [];

        public List<CancellationToken> ExecutionTokens { get; } = [];

        public Func<XRegistryCallContext, CancellationToken,
            ValueTask<XRegistryEndpointDescription>>? InspectCallback
        { get; set; }

        public Func<XRegistryRequest, CancellationToken, ValueTask<XRegistryResponse>>? ExecuteCallback { get; set; }

        public Func<CancellationToken, ValueTask<XRegistryResponse>>? CommitCallback { get; set; }

        public int Commits { get; private set; }

        public int Aborts { get; private set; }

        public ValueTask<XRegistryEndpointDescription> InspectAsync(
            XRegistryCallContext context, CancellationToken cancellationToken = default)
        {
            Inspections.Add(context);
            InspectionTokens.Add(cancellationToken);
            return InspectCallback is null
                ? new ValueTask<XRegistryEndpointDescription>(Description)
                : InspectCallback(context, cancellationToken);
        }

        public ValueTask<XRegistryResponse> ExecuteAsync(
            XRegistryRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            ExecutionTokens.Add(cancellationToken);
            return ExecuteCallback is null
                ? new ValueTask<XRegistryResponse>(Response)
                : ExecuteCallback(request, cancellationToken);
        }

        public async ValueTask<IXRegistryPreparedOperation> PrepareAsync(
            XRegistryRequest request, CancellationToken cancellationToken = default)
        {
            XRegistryResponse response = await ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
            return new PreparedOperation(this, response);
        }

        private sealed class PreparedOperation(FakeRegistryEndpoint endpoint, XRegistryResponse response)
            : IXRegistryPreparedOperation
        {
            public XRegistryResponse Response => response;

            public ValueTask<XRegistryResponse> CommitAsync(CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                endpoint.Commits++;
                m_committed = true;
                return endpoint.CommitCallback is null
                    ? new ValueTask<XRegistryResponse>(Response) : endpoint.CommitCallback(cancellationToken);
            }

            public ValueTask DisposeAsync()
            {
                if (!m_committed)
                {
                    endpoint.Aborts++;
                }
                return default;
            }

            private bool m_committed;
        }
    }

    internal sealed class HttpRouteTestHost : IAsyncDisposable
    {
        private HttpRouteTestHost(IHost host)
        {
            m_host = host;
            Client = host.GetTestClient();
        }

        public HttpClient Client { get; }

        public TestServer Server => m_host.GetTestServer();

        public static async Task<HttpRouteTestHost> StartAsync(
            IXRegistryEndpoint endpoint,
            XRegistryHttpRouteOptions? options = null,
            ClaimsPrincipal? principal = null,
            string pattern = "/registry",
            Action<HttpContext>? prepare = null)
        {
            IHost host = new HostBuilder().ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services => services.AddRouting())
                .Configure(app =>
                {
                    app.Use((context, next) =>
                    {
                        // TestServer leaves RawTarget empty; real HTTP servers supply the origin-form target.
                        // Cases that need exact raw escapes override this feature in prepare below.
                        IHttpRequestFeature feature = context.Features.Get<IHttpRequestFeature>()!;
                        if (string.IsNullOrEmpty(feature.RawTarget))
                        {
                            feature.RawTarget = context.Request.PathBase.ToUriComponent() +
                                context.Request.Path.ToUriComponent() +
                                context.Request.QueryString.Value;
                        }
                        if (principal is not null)
                        {
                            context.User = principal;
                        }
                        prepare?.Invoke(context);
                        return next(context);
                    });
                    app.UseRouting();
                    app.UseEndpoints(routes => routes.MapXRegistry(
                        pattern, endpoint, options ?? new XRegistryHttpRouteOptions(HttpHostTestData.PublicRoot)));
                })).Build();
            try
            {
                await host.StartAsync().ConfigureAwait(false);
                return new HttpRouteTestHost(host);
            }
            catch
            {
                host.Dispose();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await m_host.StopAsync().ConfigureAwait(false);
            m_host.Dispose();
        }

        private readonly IHost m_host;
    }
}
#endif
