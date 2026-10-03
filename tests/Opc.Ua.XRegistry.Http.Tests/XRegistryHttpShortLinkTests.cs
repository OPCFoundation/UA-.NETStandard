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

#if XREGISTRY_HTTP_MODERN
using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.XRegistry.Protocol;
using Opc.Ua.XRegistry.Server.Protocol;

namespace Opc.Ua.XRegistry.Http.Tests
{
    [TestFixture]
    public sealed class XRegistryHttpShortLinkTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task AliasDispatchPreservesRawOrMetadataBodiesAndPublishesCanonicalLinksAsync(bool details)
        {
            using XRegistryTransactionalEndpoint provider = CreateProvider();
            string alias = await SeedAsync(provider).ConfigureAwait(false);
            await using HttpRouteTestHost host = await HttpRouteTestHost.StartAsync(provider, RouteOptions())
                .ConfigureAwait(false);
            using var request = new HttpRequestMessage(HttpMethod.Put,
                new Uri(alias + (details ? "$details" : string.Empty), UriKind.Relative))
            {
                Content = new StringContent(details ? """{"name":"changed","schemabase64":"AP8B"}""" : "raw payload",
                    Encoding.UTF8, details ? "application/json" : "text/plain")
            };
            using HttpResponseMessage response = await host.Client.SendAsync(request).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), body);
            Assert.That(response.Headers.Location, Is.Null,
                "Writes dispatch directly; they must never redirect and replay.");
            XRegistryResponse stored = await provider.ExecuteAsync(
                new XRegistryRequest(XRegistryAction.Read, k_version))
                .ConfigureAwait(false);
            if (details)
            {
                using var metadata = JsonDocument.Parse(body);
                Assert.Multiple(() =>
                {
                    Assert.That(stored.Document, Is.EqualTo(ByteString.From(new byte[] { 0, 255, 1 })));
                    Assert.That(metadata.RootElement.GetProperty("self").GetString(),
                        Is.EqualTo("https://public.example/registry" + k_version + "$details"));
                    Assert.That(metadata.RootElement.GetProperty("shortself").GetString(),
                        Is.EqualTo("https://public.example" + alias));
                });
            }
            else
            {
                Assert.That(Encoding.UTF8.GetString(stored.Document.Span), Is.EqualTo("raw payload"));
            }
            using HttpResponseMessage doc = await host.Client.GetAsync(new Uri(alias + "?doc", UriKind.Relative))
                .ConfigureAwait(false);
            using var view = JsonDocument.Parse(await doc.Content.ReadAsStringAsync().ConfigureAwait(false));
            Assert.Multiple(() =>
            {
                Assert.That(doc.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(view.RootElement.GetProperty("self").GetString(), Is.EqualTo("#/"));
                Assert.That(view.RootElement.GetProperty("shortself").GetString(),
                    Is.EqualTo("https://public.example" + alias));
            });
        }

        [Test]
        public async Task AliasCannotBypassCanonicalAuthorizationAsync()
        {
            using XRegistryTransactionalEndpoint provider = CreateProvider();
            string alias = await SeedAsync(provider).ConfigureAwait(false);
            XRegistryResponse before = await provider.ExecuteAsync(
                new XRegistryRequest(XRegistryAction.Read, k_version))
                .ConfigureAwait(false);
            await using HttpRouteTestHost host = await HttpRouteTestHost.StartAsync(provider, RouteOptions() with
            {
                AuthorizeAsync = (_, request, _) =>
                    new ValueTask<bool>(!request.IsMutation || request.Path != k_version)
            }).ConfigureAwait(false);
            using var content = new StringContent("must-not-change", Encoding.UTF8, "text/plain");
            using HttpResponseMessage response = await host.Client.PutAsync(new Uri(alias, UriKind.Relative), content)
                .ConfigureAwait(false);
            XRegistryResponse after = await provider.ExecuteAsync(new XRegistryRequest(XRegistryAction.Read, k_version))
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
                Assert.That(after.Document, Is.EqualTo(before.Document));
                Assert.That(after.Generation, Is.EqualTo(before.Generation));
            });
        }

        [Test]
        public async Task DeletionAfterAliasResolutionCannotOverwriteAReplacementEntityAsync()
        {
            using XRegistryTransactionalEndpoint provider = CreateProvider();
            string alias = await SeedAsync(provider).ConfigureAwait(false);
            bool changed = false;
            await using HttpRouteTestHost host = await HttpRouteTestHost.StartAsync(provider, RouteOptions() with
            {
                AuthorizeAsync = async (context, request, ct) =>
                {
                    if (!changed && request.IsMutation && request.Path == k_version)
                    {
                        changed = true;
                        _ = await provider.ExecuteAsync(new XRegistryRequest(XRegistryAction.Delete, k_version)
                        { Context = s_writer }, ct).ConfigureAwait(false);
                        _ = await provider.ExecuteAsync(new XRegistryRequest(XRegistryAction.Replace, k_version)
                        {
                            Context = s_writer,
                            View = XRegistryView.Metadata,
                            Metadata = HttpTestData.Json("""{"name":"replacement","schemabase64":"AwQ="}""")
                        }, ct).ConfigureAwait(false);
                    }
                    return true;
                }
            }).ConfigureAwait(false);
            using var content = new StringContent("stale bytes", Encoding.UTF8, "text/plain");
            using HttpResponseMessage response = await host.Client.PutAsync(new Uri(alias, UriKind.Relative), content)
                .ConfigureAwait(false);
            XRegistryResponse after = await provider.ExecuteAsync(new XRegistryRequest(XRegistryAction.Read, k_version))
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
                Assert.That(after.Document, Is.EqualTo(ByteString.From(new byte[] { 3, 4 })));
                Assert.That(after.Metadata.GetProperty("name").GetString(), Is.EqualTo("replacement"));
            });
        }

        private static XRegistryHttpRouteOptions RouteOptions()
        {
            return HttpHostTestData.OpenOptions with
            {
                CreateContextAsync = (_, _) => new ValueTask<XRegistryCallContext>(s_writer)
            };
        }

        private static XRegistryTransactionalEndpoint CreateProvider()
        {
            return new XRegistryTransactionalEndpoint(new XRegistryTransactionalOptions
            {
                Model = HttpTestData.Json(
                    """
                    {"groups":{"schemagroups":{"singular":"schemagroup","resources":{"schemas":{"singular":"schema"}}}}}
                    """),
                PublicRoot = HttpHostTestData.PublicRoot,
                ShortLinksEnabled = true
            }, new InMemoryXRegistryTransactionStore());
        }

        private static async Task<string> SeedAsync(XRegistryTransactionalEndpoint provider)
        {
            _ = await provider.InitializeShortLinksAsync(s_writer).ConfigureAwait(false);
            XRegistryResponse created = await provider.ExecuteAsync(
                new XRegistryRequest(XRegistryAction.Replace, k_version)
                {
                    Context = s_writer,
                    View = XRegistryView.Metadata,
                    Metadata = HttpTestData.Json("""{"name":"original","schemabase64":"AQI="}""")
                }).ConfigureAwait(false);
            Assert.That(created.IsSuccess, Is.True, created.Error?.Detail);
            return new Uri(created.Metadata.GetProperty("shortself").GetString()!).AbsolutePath;
        }

        private const string k_version = "/schemagroups/g/schemas/r/versions/v1";

        private static readonly XRegistryCallContext s_writer =
            new("alias-writer") { IsAuthenticated = true, Roles = ["xregistry.write"] };
    }
}
#endif
