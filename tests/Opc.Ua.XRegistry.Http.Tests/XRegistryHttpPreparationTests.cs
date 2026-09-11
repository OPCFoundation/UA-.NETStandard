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
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.XRegistry.Protocol;
using Opc.Ua.XRegistry.Server.Protocol;

namespace Opc.Ua.XRegistry.Http.Tests
{
    [TestFixture]
    public sealed class XRegistryHttpPreparationTests
    {
        [Test]
        public async Task HostNeverCommitsWhenActualHttpHeadersCannotBeEncodedAsync()
        {
            var endpoint = new FakeRegistryEndpoint
            {
                Response = new XRegistryResponse(200)
                {
                    Document = ByteString.From(new byte[] { 1, 2 }),
                    Metadata = HttpTestData.Json(/*lang=json,strict*/ """
                        {"schemaid":"r","versionid":"v1","epoch":1,"name":"name",
                        "description":"description","contenttype":"application/octet-stream"}
                        """)
                }
            };
            XRegistryHttpRouteOptions options = HttpHostTestData.OpenOptions with
            {
                Transport = new XRegistryHttpOptions { MaximumHeaders = 3 }
            };
            await using HttpRouteTestHost host = await HttpRouteTestHost.StartAsync(endpoint,
                options).ConfigureAwait(false);
            using var request = new HttpRequestMessage(HttpMethod.Put, "/registry/schemagroups/g/schemas/r")
            {
                Content = new ByteArrayContent([1, 2])
            };
            using HttpResponseMessage response = await host.Client.SendAsync(request).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadGateway));
                Assert.That(endpoint.Commits, Is.Zero);
                Assert.That(endpoint.Aborts, Is.EqualTo(1));
            });
        }

        [Test]
        public async Task HostNeverCommitsWhenActualHttpMetadataExceedsResponseLimitAsync()
        {
            var endpoint = new FakeRegistryEndpoint
            {
                Response = new XRegistryResponse(200)
                {
                    Metadata = HttpTestData.Json("{\"name\":\"" + new string('a', 512) + "\"}")
                }
            };
            await using HttpRouteTestHost host = await HttpRouteTestHost.StartAsync(endpoint,
                HttpHostTestData.OpenOptions with
                {
                    Transport = new XRegistryHttpOptions { MaximumBodyBytes = 128 }
                }).ConfigureAwait(false);
            using var request = new HttpRequestMessage(HttpMethod.Patch, "/registry")
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
            using HttpResponseMessage response = await host.Client.SendAsync(request).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadGateway));
                Assert.That(endpoint.Commits, Is.Zero);
                Assert.That(endpoint.Aborts, Is.EqualTo(1));
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task HostRequiresBothMeasuredPreparationAndTheActualInterfaceAsync(bool hasInterface)
        {
            var endpoint = new FakeRegistryEndpoint();
            if (hasInterface)
            {
                endpoint.Description = endpoint.Description with { SupportsPreparedMutations = false };
            }
            await using HttpRouteTestHost host = await HttpRouteTestHost.StartAsync(
                hasInterface ? endpoint : new NonPreparedEndpoint(endpoint), HttpHostTestData.OpenOptions)
                .ConfigureAwait(false);
            using var request = new HttpRequestMessage(HttpMethod.Patch, "/registry")
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
            using HttpResponseMessage response = await host.Client.SendAsync(request).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.MethodNotAllowed));
                Assert.That(endpoint.Requests, Is.Empty);
                Assert.That(endpoint.Commits, Is.Zero);
            });
        }

        [Test]
        public async Task HostReturnsPreparedPreviewEvenWhenCommitChangesTheFakeLiveViewAsync()
        {
            var endpoint = new FakeRegistryEndpoint
            {
                Response = new XRegistryResponse(200)
                {
                    Metadata =
                    HttpTestData.Json(/*lang=json,strict*/ """{"epoch":1,"name":"preview"}""")
                }
            };
            XRegistryResponse preview = endpoint.Response;
            endpoint.CommitCallback = _ =>
            {
                endpoint.Response = new XRegistryResponse(200)
                {
                    Metadata = HttpTestData.Json(/*lang=json,strict*/ """{"epoch":2,"name":"later"}""")
                };
                return new ValueTask<XRegistryResponse>(preview);
            };
            await using HttpRouteTestHost host = await HttpRouteTestHost.StartAsync(endpoint,
                HttpHostTestData.OpenOptions)
                .ConfigureAwait(false);
            using var request = new HttpRequestMessage(HttpMethod.Patch, "/registry")
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
            using HttpResponseMessage response = await host.Client.SendAsync(request).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(HttpTestData.Json(body).GetProperty("name").GetString(), Is.EqualTo("preview"));
                Assert.That(HttpTestData.Json(body).GetProperty("epoch").GetInt32(), Is.EqualTo(1));
                Assert.That(endpoint.Commits, Is.EqualTo(1));
            });
        }

        [Test]
        public async Task ActualProviderAtomicRejectionAndEpochGuardsSurviveHttpHostingAsync()
        {
            using var endpoint = new XRegistryTransactionalEndpoint(new XRegistryTransactionalOptions
            {
                Model = HttpTestData.Json(
                    /*lang=json,strict*/ """{"groups":{"groups":{"singular":"group","resources":{}}}}"""),
                PublicRoot = HttpHostTestData.PublicRoot
            }, new InMemoryXRegistryTransactionStore());
            var caller = new XRegistryCallContext("writer") { IsAuthenticated = true, Roles = ["xregistry.write"] };
            await using HttpRouteTestHost host = await HttpRouteTestHost.StartAsync(endpoint,
                HttpHostTestData.OpenOptions with
                {
                    CreateContextAsync = (_, _) => new ValueTask<XRegistryCallContext>(caller)
                }).ConfigureAwait(false);
            using var malformed = new HttpRequestMessage(HttpMethod.Patch, "/registry")
            {
                Content = new StringContent(
                    /*lang=json,strict*/ """{"epoch":0,"name":"not-committed","groups":{"a":{},"b":null}}""",
                    Encoding.UTF8, "application/json")
            };
            using HttpResponseMessage rejection = await host.Client.SendAsync(malformed).ConfigureAwait(false);
            XRegistryResponse untouched = await endpoint.ExecuteAsync(new XRegistryRequest(XRegistryAction.Read, "/"))
                .ConfigureAwait(false);
            using var valid = new HttpRequestMessage(HttpMethod.Patch, "/registry")
            {
                Content = new StringContent(/*lang=json,strict*/ """{"epoch":0,"name":"committed"}""", Encoding.UTF8,
                    "application/json")
            };
            using HttpResponseMessage accepted = await host.Client.SendAsync(valid).ConfigureAwait(false);
            using var stale = new HttpRequestMessage(HttpMethod.Patch, "/registry")
            {
                Content = new StringContent(/*lang=json,strict*/ """{"epoch":0,"name":"stale"}""", Encoding.UTF8,
                    "application/json")
            };
            using HttpResponseMessage mismatch = await host.Client.SendAsync(stale).ConfigureAwait(false);
            XRegistryResponse result = await endpoint.ExecuteAsync(new XRegistryRequest(XRegistryAction.Read, "/"))
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(rejection.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
                Assert.That(untouched.Metadata.TryGetProperty("name", out _), Is.False);
                Assert.That(untouched.Metadata.GetProperty("groupscount").GetInt32(), Is.Zero);
                Assert.That(accepted.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(mismatch.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
                Assert.That(result.Metadata.GetProperty("name").GetString(), Is.EqualTo("committed"));
                Assert.That(result.Metadata.GetProperty("epoch").GetInt32(), Is.EqualTo(1));
            });
        }

        private sealed class NonPreparedEndpoint(FakeRegistryEndpoint inner) : IXRegistryEndpoint
        {
            public ValueTask<XRegistryEndpointDescription> InspectAsync(
                XRegistryCallContext context, CancellationToken cancellationToken = default)
            {
                return inner.InspectAsync(context, cancellationToken);
            }

            public ValueTask<XRegistryResponse> ExecuteAsync(
                XRegistryRequest request, CancellationToken cancellationToken = default)
            {
                return inner.ExecuteAsync(request, cancellationToken);
            }
        }
    }
}
#endif
