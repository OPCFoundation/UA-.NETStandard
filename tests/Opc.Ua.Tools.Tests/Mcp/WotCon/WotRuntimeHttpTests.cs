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
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

#if NET10_0
using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Mcp;
using Opc.Ua.Tools.Tests.McpRuntime;
using Opc.Ua.WotCon.Bindings;
using Opc.Ua.WotCon.Bindings.Tests.Support;

namespace Opc.Ua.Tools.Tests.McpCompanion
{
    /// <summary>
    /// Exercises the existing HTTP executor over the repository's real loopback HTTP server fixture.
    /// </summary>
    [TestFixture]
    [Category("Integration")]
    public sealed class WotRuntimeHttpTests
    {
        [Test]
        public async Task LocalServerRoundTripsTypedReadAndWriteAsync()
        {
            var requests = new ConcurrentQueue<TestHttpRequest>();
            int device = 12;
            using var server = new TestHttpServer(request =>
            {
                requests.Enqueue(request);
                if (request.Method == "PUT")
                {
                    using var body = JsonDocument.Parse(request.Body);
                    Volatile.Write(ref device, body.RootElement.GetInt32());
                }
                return TestHttpResponse.Json(200, Volatile.Read(ref device).ToString(CultureInfo.InvariantCulture));
            });
            await using var fixture = new WotRuntimeTestContext(options => options.NetworkRules =
            [
                WotRuntimeTestContext.Rule(McpCapability.WotRuntime, ["GET"], ["/value"], port: server.Port),
                WotRuntimeTestContext.Rule(McpCapability.WotWrite, ["PUT"], ["/value"], port: server.Port)
            ], realExecutors: true);
            string document = Document(server.Port);
            string handle;
            using (fixture.Enter())
            {
                handle = await fixture.OpenAsync(operations: ["readproperty", "writeproperty"], document: document)
                    .ConfigureAwait(false);
            }
            using (fixture.Enter())
            {
                JsonObject read = WotRuntimeTestContext.Payload(
                    await fixture.Tools.ReadPropertyAsync(handle).ConfigureAwait(false));
                Assert.That(ReadInteger(read), Is.EqualTo(12));
                _ = WotRuntimeTestContext.Payload(await fixture.Tools.WritePropertyAsync(handle,
                    WotRuntimeTestContext.Value(BuiltInType.Int32, "27")).ConfigureAwait(false));
                read = WotRuntimeTestContext.Payload(
                    await fixture.Tools.ReadPropertyAsync(handle).ConfigureAwait(false));
                Assert.That(ReadInteger(read), Is.EqualTo(27));
                _ = WotRuntimeTestContext.Payload(await fixture.Tools.CloseAsync(handle).ConfigureAwait(false));
            }

            Assert.That(requests.Select(request => request.Method), Is.EqualTo(s_expectedMethods));
            Assert.That(requests.All(request => request.Path == "/value"), Is.True);
            Assert.That(requests.All(request => request.Headers["Host"] == $"device.example:{server.Port}"), Is.True);
            Assert.That(requests.All(request => !request.Headers.ContainsKey("Cookie")), Is.True);
            Assert.That(device, Is.EqualTo(27));
            fixture.Dns.Verify(value => value.ResolveAsync("device.example", It.IsAny<CancellationToken>()),
                Times.Exactly(3));
        }

        [Test]
        public async Task RedirectNeverReachesAnotherPathEvenOnTheSameApprovedHostAsync()
        {
            var paths = new ConcurrentQueue<string>();
            using var server = new TestHttpServer(request =>
            {
                paths.Enqueue(request.Path);
                return request.Path == "/value"
                    ? TestHttpResponse.Redirect("/other") : TestHttpResponse.Json(200, "99");
            });
            await using var fixture = new WotRuntimeTestContext(options => options.NetworkRules =
                [
                    WotRuntimeTestContext.Rule(
                        McpCapability.WotRuntime, ["GET"], ["/value", "/other"], port: server.Port)
                ],
                realExecutors: true);
            using IDisposable caller = fixture.Enter();
            string handle = await fixture.OpenAsync(document: Document(server.Port)).ConfigureAwait(false);
            _ = WotRuntimeTestContext.Payload(
                await fixture.Tools.ReadPropertyAsync(handle).ConfigureAwait(false), error: true);

            Assert.That(paths, Is.EqualTo(s_expectedRedirectPaths));
        }

        [Test]
        public async Task CloseDisposesOwnedHttpClientExactlyOnceAsync()
        {
            var channel = new Mock<IWotBindingChannel>(MockBehavior.Strict);
            channel.Setup(value => value.DisposeAsync()).Returns(ValueTask.CompletedTask);
            var handler = new DisposalHandler();
            var client = new HttpClient(handler);
            var connection = new WotRuntimeConnection(channel.Object, client);

            await connection.DisposeAsync().ConfigureAwait(false);
            await connection.DisposeAsync().ConfigureAwait(false);

            Assert.That(handler.DisposeCalls, Is.EqualTo(1));
            channel.Verify(value => value.DisposeAsync(), Times.Once);
            Assert.That(() => client.GetAsync(s_disposalProbe),
                Throws.InstanceOf<ObjectDisposedException>());
        }

        /// <summary>
        /// Retains a caller-supplied DNS name while the injected resolver connects only to the local test listener.
        /// </summary>
        private static string Document(int port)
        {
            return WotRuntimeTestContext.PropertyDocument.Replace(
                "http://device.example/value", $"http://device.example:{port}/value", StringComparison.Ordinal);
        }

        /// <summary>
        /// Reads the actual JSON codec's Int64 result through the UA decoder, not display-text coercion.
        /// </summary>
        private static long ReadInteger(JsonObject result)
        {
            using var decoder = new JsonDecoder(result.ToJsonString(),
                ServiceMessageContext.CreateEmpty(McpRuntimeTestContext.CreateTelemetry()));
            DataValue value = decoder.ReadDataValue("dataValue");
            Assert.That(value.WrappedValue.TryGetValue(out long integer), Is.True);
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
            return integer;
        }

        private static readonly string[] s_expectedMethods = ["GET", "PUT", "GET"];
        private static readonly string[] s_expectedRedirectPaths = ["/value"];
        private static readonly Uri s_disposalProbe = new("http://mcp.invalid");

        /// <summary>
        /// Counts disposal without introducing any network activity.
        /// </summary>
        private sealed class DisposalHandler : HttpMessageHandler
        {
            /// <summary>
            /// Gets the number of disposal calls observed by the owned handler.
            /// </summary>
            internal int DisposeCalls { get; private set; }

            /// <inheritdoc/>
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            }

            /// <inheritdoc/>
            protected override void Dispose(bool disposing)
            {
                DisposeCalls++;
                base.Dispose(disposing);
            }
        }
    }
}
#endif
