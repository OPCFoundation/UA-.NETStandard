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
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Http.Tests
{
    [TestFixture]
    public sealed class XRegistryHttpAliasProfileTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task KnownStableAliasProfileResolvesWithDocAndDispatchesOnlyOnceToTheOriginalAddressAsync(
            bool write)
        {
            using var handler = new RecordingHttpHandler(message => Respond(message, true, HttpTestData.ResourcePath));
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot,
                HttpTestData.Qualified with { ShortLinkPrefix = "/_s" });
            XRegistryResponse response = await endpoint.ExecuteAsync(
                new XRegistryRequest(write ? XRegistryAction.Replace : XRegistryAction.Read, "/_s/1")
                {
                    Document = write ? ByteString.From(new byte[] { 1, 2 }) : default,
                    ContentType = write ? "application/octet-stream" : null
                }).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(200));
                Assert.That(response.Document, Is.EqualTo(ByteString.From(new byte[] { 1, 2 })));
                Assert.That(handler.Requests.Count(request => request.Method == "PUT"), Is.EqualTo(write ? 1 : 0));
                Assert.That(handler.Requests[^1].Uri, Is.EqualTo("https://registry.example/registry/_s/1"));
                Assert.That(handler.Requests.Any(request =>
                    request.Uri.EndsWith("/_s/1?doc", StringComparison.Ordinal)),
                    Is.True);
                Assert.That(handler.Requests.Any(request =>
                    request.Uri.Contains("/schemas/r", StringComparison.Ordinal)),
                    Is.False, "The canonical shape must not redirect execution away from the original alias.");
            });
        }

        [Test]
        public async Task UnsupportedAliasDiscoveryNeverGuessesARawRepresentationOrSendsAWriteAsync()
        {
            using var handler = new RecordingHttpHandler(message => Respond(message, false, HttpTestData.ResourcePath));
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot,
                HttpTestData.Qualified with { ShortLinkPrefix = "/_s" });
            XRegistryResponse rejected = await endpoint.ExecuteAsync(
                new XRegistryRequest(XRegistryAction.Replace, "/_s/1")
                { Document = ByteString.Empty }).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(rejected.StatusCode, Is.EqualTo(405));
                Assert.That(handler.Requests, Has.Count.EqualTo(3));
                Assert.That(handler.Requests.All(request => request.Method == "GET"), Is.True);
            });
        }

        [Test]
        public async Task ChangedCanonicalAliasTargetIsRejectedBeforeMutationDispatchAsync()
        {
            using var handler = new RecordingHttpHandler(message => Respond(message, true, HttpTestData.RecordPath));
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot,
                HttpTestData.Qualified with { ShortLinkPrefix = "/_s" });
            XRegistryResponse response = await endpoint.ExecuteAsync(
                new XRegistryRequest(XRegistryAction.Replace, "/_s/1")
                { Document = ByteString.Empty }.AtResolvedPath(HttpTestData.ResourcePath)).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(409));
                Assert.That(response.Error?.Code, Is.EqualTo("address_changed"));
                Assert.That(handler.Requests.All(request => request.Method == "GET"), Is.True);
            });
        }

        private static HttpResponseMessage Respond(HttpRequestMessage message, bool doc, string path)
        {
            if (message.RequestUri!.AbsolutePath == "/registry/capabilities")
            {
                return HttpTestData.JsonResponse(doc
                    ? HttpTestData.Capabilities.Replace("\"inline\"", "\"doc\",\"inline\"", StringComparison.Ordinal)
                    : HttpTestData.Capabilities);
            }
            if (message.RequestUri.AbsolutePath != "/registry/_s/1")
            {
                return HttpTestData.InspectionResponse(message);
            }
            if (message.RequestUri.Query == "?doc")
            {
                return HttpTestData.JsonResponse("{\"schemaid\":\"r\",\"xid\":\"" + path + "\",\"self\":\"#/\"}");
            }
            HttpResponseMessage response = HttpTestData.BytesResponse([1, 2], "application/octet-stream");
            response.Headers.TryAddWithoutValidation("xRegistry-xid", path);
            return response;
        }
    }
}
