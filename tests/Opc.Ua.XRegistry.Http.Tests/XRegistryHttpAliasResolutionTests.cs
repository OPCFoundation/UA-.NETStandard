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
using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Http.Tests
{
    [TestFixture]
    public sealed class XRegistryHttpAliasResolutionTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task DirectResolutionPreservesTheCompleteRequestWithoutDispatchingItAsync(bool alias)
        {
            using RecordingHttpHandler handler = Handler();
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot,
                HttpTestData.Qualified with { ShortLinkPrefix = "/_s" });
            var request = new XRegistryRequest(
                XRegistryAction.Replace, alias ? "/_s/1" : HttpTestData.ResourcePath)
            {
                Context = new XRegistryCallContext("alias-caller"),
                Document = ByteString.From(new byte[] { 1, 0, 255 }),
                View = XRegistryView.Metadata,
                OperationId = "resolve-only",
                Parameters = [new XRegistryParameter("binary", null)]
            };
            XRegistryAddressResolution resolution = await endpoint.ResolveAddressAsync(request).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(resolution.Rejection, Is.Null);
                Assert.That(resolution.Request.Path, Is.EqualTo(HttpTestData.ResourcePath));
                Assert.That(resolution.Request.AddressPath, Is.EqualTo(alias ? "/_s/1" : null));
                Assert.That(resolution.Request.Context, Is.EqualTo(request.Context));
                Assert.That(resolution.Request.Document, Is.EqualTo(request.Document));
                Assert.That(resolution.Request.OperationId, Is.EqualTo("resolve-only"));
                Assert.That(resolution.Request.View, Is.EqualTo(XRegistryView.Metadata));
                Assert.That(resolution.Request.Parameters, Is.EqualTo(request.Parameters));
                Assert.That(handler.Requests, Has.Count.EqualTo(alias ? 4 : 0));
                Assert.That(handler.Requests.All(value => value.Method == "GET"), Is.True);
            });
        }

        [TestCase("{}")]
        [TestCase("{\"xid\":\"/_s/2\"}")]
        [TestCase("{\"xid\":17}")]
        public void MalformedAliasMetadataCannotInventACanonicalAddress(string json)
        {
            using RecordingHttpHandler handler = Handler(json);
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot,
                HttpTestData.Qualified with { ShortLinkPrefix = "/_s" });
            Assert.ThrowsAsync<InvalidDataException>(async () => await endpoint.ResolveAddressAsync(
                new XRegistryRequest(XRegistryAction.Read, "/_s/1")).ConfigureAwait(false));
            Assert.That(handler.Requests, Has.Count.EqualTo(4));
        }

        [Test]
        public async Task RetiredAliasResolutionPreservesTheUpstreamRejectionAsync()
        {
            using RecordingHttpHandler handler = Handler(
                """{"type":"about:blank","title":"not_found","detail":"retired"}""", 404);
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot,
                HttpTestData.Qualified with { ShortLinkPrefix = "/_s" });
            XRegistryAddressResolution resolution = await endpoint.ResolveAddressAsync(
                new XRegistryRequest(XRegistryAction.Read, "/_s/1")).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(resolution.Rejection?.StatusCode, Is.EqualTo(404));
                Assert.That(resolution.Rejection?.Error?.Detail, Is.EqualTo("retired"));
                Assert.That(resolution.Request.Path, Is.EqualTo("/_s/1"));
                Assert.That(resolution.Request.AddressPath, Is.Null);
            });
        }

        [TestCase("{\"xid\":\"/schemagroups/g/records/r\"}")]
        [TestCase("{\"xid\":17}")]
        public void RetargetedSuccessfulAliasResponseCannotBeAcceptedAsTheResolvedEntity(string response)
        {
            using RecordingHttpHandler handler = Handler(actual: response);
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot,
                HttpTestData.Qualified with { ShortLinkPrefix = "/_s" });
            Assert.ThrowsAsync<InvalidDataException>(async () => await endpoint.ExecuteAsync(
                new XRegistryRequest(XRegistryAction.Read, "/_s/1") { View = XRegistryView.Metadata })
                .ConfigureAwait(false));
            Assert.That(handler.Requests.All(value => value.Method == "GET"), Is.True);
        }

        [TestCase("/a/b")]
        [TestCase("/s?query")]
        [TestCase("/s$details")]
        public void InvalidAliasMountIsRejectedWithoutHttpDispatch(string prefix)
        {
            using RecordingHttpHandler handler = Handler();
            using var client = new HttpClient(handler);
            Assert.Throws<ArgumentException>(() => new XRegistryHttpEndpoint(
                client, HttpTestData.RegistryRoot, HttpTestData.Qualified with { ShortLinkPrefix = prefix }));
            Assert.That(handler.Requests, Is.Empty);
        }

        [Test]
        public void AliasMountCannotShadowAModelCollection()
        {
            using RecordingHttpHandler handler = Handler();
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot,
                HttpTestData.Qualified with { ShortLinkPrefix = "/schemagroups" });
            Assert.ThrowsAsync<InvalidDataException>(async () =>
                await endpoint.InspectAsync(XRegistryCallContext.Anonymous).ConfigureAwait(false));
            Assert.That(handler.Requests, Has.Count.EqualTo(3));
        }

        private static RecordingHttpHandler Handler(
            string probe = """{"xid":"/schemagroups/g/schemas/r"}""", int status = 200,
            string actual = """{"xid":"/schemagroups/g/schemas/r"}""")
        {
            return new RecordingHttpHandler(request =>
            {
                if (request.RequestUri!.AbsolutePath == "/registry/capabilities")
                {
                    return HttpTestData.JsonResponse(HttpTestData.Capabilities.Replace(
                        "\"inline\"", "\"doc\",\"inline\"", StringComparison.Ordinal));
                }
                if (request.RequestUri.AbsolutePath.StartsWith("/registry/_s/", StringComparison.Ordinal))
                {
                    return request.RequestUri.Query == "?doc"
                        ? HttpTestData.JsonResponse(probe, status) : HttpTestData.JsonResponse(actual);
                }
                return HttpTestData.InspectionResponse(request);
            });
        }
    }
}
