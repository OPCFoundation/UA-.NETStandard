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

using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Opc.Ua.Bindings;
using Opc.Ua.Bindings.WebApi;
using Opc.Ua.Bindings.WebApi.Authentication;
using TUnit.Core.Interfaces;

namespace Opc.Ua.Aot.Tests
{
    /// <summary>
    /// AOT fixture that boots a minimal Kestrel host on
    /// <c>http://127.0.0.1:0</c> with the OPC UA WebApi
    /// Minimal-API endpoints
    /// (<c>MapWebApiEndpoints()</c>) backed by a stub
    /// <see cref="IWebApiServer"/>. Used by
    /// <see cref="WebApiAotTests"/> to verify the binding works under
    /// NativeAOT with both anonymous and Basic-auth flows.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Plain <c>http://</c> (no TLS) sidesteps the certificate
    /// infrastructure that the AOT test harness deliberately does not
    /// stand up — the goal is to exercise the routing / decode /
    /// dispatch / encode loop end-to-end without dragging the whole
    /// HttpsTransportListener pipeline in.
    /// </para>
    /// <para>
    /// The auth registrations exercise
    /// <see cref="OpcUaWebApiAuthenticationBuilderExtensions.AddWebApiBasicAuth"/>
    /// (delivering a validated <see cref="ClaimsPrincipal"/> to
    /// <c>HttpContext.User</c>) so the AOT publish covers the
    /// <c>BasicAuthenticationHandler</c> code path that previously
    /// only ran on the desktop runtime.
    /// </para>
    /// </remarks>
    public sealed class WebApiAotFixture : IAsyncInitializer, IAsyncDisposable
    {
        public StubWebApiServer Server { get; private set; } = default!;
        public HttpClient HttpClient { get; private set; } = default!;
        public Uri BaseAddress { get; private set; } = default!;
        public string ExpectedBasicUser { get; } = "alice";
        public string ExpectedBasicPassword { get; } = "wonderland";
        public string AuthHeaderForExpectedBasicUser { get; private set; } = default!;

        private IHost m_host = default!;

        public async Task InitializeAsync()
        {
            ITelemetryContext telemetry = DefaultTelemetry.Create(
                b => b.SetMinimumLevel(LogLevel.Warning));
            Server = new StubWebApiServer(
                ServiceMessageContext.CreateEmpty(telemetry));

            IHostBuilder hostBuilder = new HostBuilder()
                .ConfigureWebHost(webHost =>
                {
                    webHost.UseKestrel(opts =>
                    {
                        opts.Listen(IPAddress.Loopback, 0);
                    });
                    webHost.ConfigureServices(services =>
                    {
                        services.AddSingleton<IWebApiServer>(Server);
                        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
                        services.AddRouting();
                        services.AddAuthentication()
                            .AddScheme<BasicAuthenticationOptions, BasicAuthenticationHandler>(
                                WebApiAuthSchemes.Basic,
                                options =>
                                {
                                    options.ValidateCredentials = (u, p) =>
                                        u == ExpectedBasicUser && p == ExpectedBasicPassword
                                            ? Task.FromResult<ClaimsPrincipal?>(
                                                BuildPrincipal(u))
                                            : Task.FromResult<ClaimsPrincipal?>(null);
                                });
                    });
                    webHost.Configure(app =>
                    {
                        app.UseRouting();
                        app.UseAuthentication();
                        app.UseEndpoints(e => e.MapWebApiEndpoints());
                    });
                });

            m_host = hostBuilder.Build();
            await m_host.StartAsync().ConfigureAwait(false);

            IServer server = m_host.Services.GetRequiredService<IServer>();
            string baseAddress = server.Features
                .Get<IServerAddressesFeature>()!
                .Addresses
                .First();
            BaseAddress = new Uri(baseAddress);
            HttpClient = new HttpClient { BaseAddress = BaseAddress };
            AuthHeaderForExpectedBasicUser = "Basic " +
                Convert.ToBase64String(
                    System.Text.Encoding.UTF8.GetBytes(
                        $"{ExpectedBasicUser}:{ExpectedBasicPassword}"));
        }

        public async ValueTask DisposeAsync()
        {
            HttpClient?.Dispose();
            if (m_host != null)
            {
                await m_host.StopAsync().ConfigureAwait(false);
                m_host.Dispose();
            }
        }

        private static ClaimsPrincipal BuildPrincipal(string user)
        {
            var identity = new ClaimsIdentity(
                new[]
                {
                    new Claim(ClaimTypes.Name, user)
                },
                authenticationType: WebApiAuthSchemes.Basic,
                nameType: ClaimTypes.Name,
                roleType: ClaimTypes.Role);
            return new ClaimsPrincipal(identity);
        }
    }
}
