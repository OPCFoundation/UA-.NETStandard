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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.XRegistry.Protocol;
#if XREGISTRY_HTTP_MODERN
using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Opc.Ua.XRegistry.Http;
#endif

namespace Opc.Ua.XRegistry.Bridge.Tests.Native
{
    public sealed partial class XRegistryNativeIntegrationTests
    {
        [Test]
        public async Task NativeAliasesRetainOriginalAddressAcrossPreparationAndReplayAsync()
        {
            await ResetEndpointAsync(shortLinks: true).ConfigureAwait(false);
            await m_manager.RefreshAsync().ConfigureAwait(false);
            XRegistryResponse created = await m_native.ExecuteAsync(
                Request(XRegistryAction.Replace, "/schemagroups/aliased", "{}")).ConfigureAwait(false);
            Assert.That(created.StatusCode, Is.EqualTo(201), created.Error?.Detail);
            string alias = new Uri(created.Metadata.GetProperty("shortself").GetString()!).AbsolutePath;
            XRegistryRequest request = Request(XRegistryAction.Merge, alias, """{"name":"native-alias"}""") with
            { OperationId = "native-alias-once" };
            XRegistryAddressResolution resolution = await m_native.ResolveAddressAsync(request).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(resolution.Rejection, Is.Null);
                Assert.That(resolution.Request.Path, Is.EqualTo("/schemagroups/aliased"));
                Assert.That(resolution.Request.AddressPath, Is.EqualTo(alias));
            });
            IXRegistryPreparedOperation preparation = await m_native.PrepareAsync(resolution.Request)
                .ConfigureAwait(false);
            await using (preparation.ConfigureAwait(false))
            {
                Assert.That(preparation.Response.StatusCode, Is.EqualTo(200), preparation.Response.Error?.Detail);
                XRegistryResponse committed = await preparation.CommitAsync().ConfigureAwait(false);
                Assert.That(committed.Metadata.GetProperty("name").GetString(), Is.EqualTo("native-alias"));
            }
            XRegistryResponse replay = await m_native.ExecuteAsync(request).ConfigureAwait(false);
            Assert.That(replay.Metadata.GetProperty("epoch").GetInt32(), Is.EqualTo(1));
            _ = await m_native.ExecuteAsync(Request(XRegistryAction.Delete, alias)).ConfigureAwait(false);
            _ = await m_native.ExecuteAsync(Request(XRegistryAction.Replace, "/schemagroups/aliased",
                """{"name":"replacement"}""")).ConfigureAwait(false);
            XRegistryResponse stale = await m_native.ExecuteAsync(resolution.Request with { OperationId = null })
                .ConfigureAwait(false);
            Assert.That(stale.StatusCode, Is.EqualTo(404));
        }

#if XREGISTRY_HTTP_MODERN
        [Test]
        public async Task HttpGatewayResolvesRemoteNativeAliasesBeforeDecodingRawBodiesAsync()
        {
            await ResetEndpointAsync(shortLinks: true).ConfigureAwait(false);
            await m_manager.RefreshAsync().ConfigureAwait(false);
            XRegistryResponse created = await m_native.ExecuteAsync(Request(XRegistryAction.Replace,
                "/schemagroups/g/schemas/r/versions/v1", """{"schemabase64":"AQI="}""")).ConfigureAwait(false);
            Assert.That(created.StatusCode, Is.EqualTo(201), created.Error?.Detail);
            string alias = new Uri(created.Metadata.GetProperty("shortself").GetString()!).AbsolutePath;
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
                using var content = new StringContent("native-through-alias", Encoding.UTF8, "text/plain");
                using HttpResponseMessage response = await client.PutAsync(
                    new Uri("/registry" + alias, UriKind.Relative), content).ConfigureAwait(false);
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK),
                    await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                Assert.That(response.Headers.Location, Is.Null);
                XRegistryResponse actual = await m_forwarder.Inner.ExecuteAsync(Request(XRegistryAction.Read,
                    "/schemagroups/g/schemas/r/versions/v1") with
                { View = XRegistryView.Default }).ConfigureAwait(false);
                Assert.That(Encoding.UTF8.GetString(actual.Document.Span), Is.EqualTo("native-through-alias"));
            }
            finally
            {
                await host.StopAsync().ConfigureAwait(false);
            }
        }
#endif
    }
}
