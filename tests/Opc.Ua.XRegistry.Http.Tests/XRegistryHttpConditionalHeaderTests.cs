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
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.XRegistry.Protocol;
#if XREGISTRY_HTTP_MODERN
using Opc.Ua.XRegistry.Server.Protocol;
#endif

namespace Opc.Ua.XRegistry.Http.Tests
{
    [TestFixture]
    public sealed class XRegistryHttpConditionalHeaderTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task ConditionalScalarMapAndNestedHeadersAreTypedRegardlessOfOrderAsync(bool selectorFirst)
        {
            using var handler = new RecordingHttpHandler(message =>
            {
                if (message.RequestUri!.AbsolutePath == "/registry/model")
                {
                    return HttpTestData.JsonResponse(k_model);
                }
                HttpResponseMessage response = HttpTestData.BytesResponse([0x41], "text/plain");
                if (selectorFirst)
                {
                    response.Headers.TryAddWithoutValidation("xRegistry-kind", "MEASURE");
                }
                response.Headers.TryAddWithoutValidation("xRegistry-score", "17");
                response.Headers.TryAddWithoutValidation("xRegistry-scores.large", "18446744073709551616");
                response.Headers.TryAddWithoutValidation("xRegistry-level", "detail");
                response.Headers.TryAddWithoutValidation("xRegistry-rank", "2.5");
                if (!selectorFirst)
                {
                    response.Headers.TryAddWithoutValidation("xRegistry-kind", "MEASURE");
                }
                return response;
            });
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot);
            XRegistryResponse response = await endpoint.ExecuteAsync(
                new XRegistryRequest(XRegistryAction.Read, HttpTestData.ResourcePath)).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(response.Metadata.GetProperty("score").GetInt32(), Is.EqualTo(17));
                Assert.That(response.Metadata.GetProperty("scores").GetProperty("large").GetRawText(),
                    Is.EqualTo("18446744073709551616"));
                Assert.That(response.Metadata.GetProperty("rank").GetDecimal(), Is.EqualTo(2.5m));
                Assert.That(response.Document.Span.ToArray(), Is.EqualTo(new byte[] { 0x41 }));
            });
        }

        [Test]
        public async Task ConditionalNumericWritesUseDeclaredTypesBeforeDispatchAsync()
        {
            using var handler = new RecordingHttpHandler(message =>
                message.Method == HttpMethod.Get
                    ? message.RequestUri!.AbsolutePath == "/registry/model"
                        ? HttpTestData.JsonResponse(k_model) : HttpTestData.InspectionResponse(message)
                    : HttpTestData.BytesResponse([], status: 204));
            using var client = new HttpClient(handler);
            var endpoint = new XRegistryHttpEndpoint(client, HttpTestData.RegistryRoot, HttpTestData.Qualified);
            XRegistryResponse response = await endpoint.ExecuteAsync(
                new XRegistryRequest(XRegistryAction.Replace, HttpTestData.ResourcePath)
                {
                    Metadata = HttpTestData.Json(/*lang=json,strict*/ """
                        {"kind":"measure","score":17,"scores":{"large":18446744073709551616},
                         "level":"detail","rank":2.5}
                        """),
                    ContentType = "text/plain",
                    Document = ByteString.From([0x41])
                }).ConfigureAwait(false);
            CapturedHttpRequest write = handler.Requests.Single(request => request.Method == "PUT");
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(204));
                Assert.That(write.Header("xRegistry-score"), Is.EqualTo("17"));
                Assert.That(write.Header("xRegistry-scores.large"), Is.EqualTo("18446744073709551616"));
                Assert.That(write.Header("xRegistry-rank"), Is.EqualTo("2.5"));
                Assert.That(write.Body, Is.EqualTo(new byte[] { 0x41 }));
            });
        }

#if XREGISTRY_HTTP_MODERN
        [Test]
        public async Task RawConditionalWriteCommitsTypedValuesAndReturnsExactDocumentAsync()
        {
            using var endpoint = new XRegistryTransactionalEndpoint(new XRegistryTransactionalOptions
            {
                Model = HttpTestData.Json(k_model),
                PublicRoot = HttpHostTestData.PublicRoot
            }, new InMemoryXRegistryTransactionStore());
            var caller = new XRegistryCallContext("writer") { IsAuthenticated = true, Roles = ["xregistry.write"] };
            await using HttpRouteTestHost host = await HttpRouteTestHost.StartAsync(endpoint,
                HttpHostTestData.OpenOptions with
                {
                    CreateContextAsync = (_, _) => new ValueTask<XRegistryCallContext>(caller)
                }).ConfigureAwait(false);
            using var request = new HttpRequestMessage(HttpMethod.Put,
                new Uri("/registry" + HttpTestData.ResourcePath, UriKind.Relative))
            {
                Content = new StringContent("payload", Encoding.UTF8, "text/plain")
            };
            request.Headers.TryAddWithoutValidation("xRegistry-score", "17");
            request.Headers.TryAddWithoutValidation("xRegistry-kind", "MEASURE");
            request.Headers.TryAddWithoutValidation("xRegistry-scores.large", "18446744073709551616");
            using HttpResponseMessage response = await host.Client.SendAsync(request).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created), body);
            XRegistryResponse stored = await endpoint.ExecuteAsync(
                new XRegistryRequest(XRegistryAction.Read, HttpTestData.ResourcePath)
                { View = XRegistryView.Metadata }).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(body, Is.EqualTo("payload"));
                Assert.That(response.Headers.GetValues("xRegistry-score").Single(), Is.EqualTo("17"));
                Assert.That(stored.Metadata.GetProperty("score").GetInt32(), Is.EqualTo(17));
                Assert.That(stored.Metadata.GetProperty("scores").GetProperty("large").GetRawText(),
                    Is.EqualTo("18446744073709551616"));
            });
        }
#endif

        private const string k_model = /*lang=json,strict*/ """
            {"groups":{"schemagroups":{"singular":"schemagroup","resources":{"schemas":{
              "singular":"schema","attributes":{"kind":{"type":"string","ifvalues":{
                "measure":{"siblingattributes":{
                  "score":{"type":"integer"},"scores":{"type":"map","item":{"type":"uinteger"}},
                  "level":{"type":"string","ifvalues":{"detail":{"siblingattributes":{"rank":{"type":"decimal"}}}}}
                }}
              }}}}
            }}}}
            """;
    }
}
