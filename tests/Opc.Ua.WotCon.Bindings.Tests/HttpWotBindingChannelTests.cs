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
using System.Collections.Concurrent;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.WotCon.Bindings.Http;
using Opc.Ua.WotCon.Bindings.Planners;
using Opc.Ua.WotCon.Bindings.Tests.Support;

namespace Opc.Ua.WotCon.Bindings.Tests
{
    /// <summary>
    /// Additional channel-level tests for <c>HttpWotBindingChannel</c>:
    /// non-2xx responses mapped to OPC UA status codes, body-size enforcement,
    /// codec decode failure, write semantics, action invocation with empty
    /// body, and polling subscription creation.
    /// </summary>
    [TestFixture]
    public sealed class HttpWotBindingChannelTests
    {
        private static WotProtocolBinderRegistry Registry(
            HttpWotBindingOptions? options = null,
            WotBindingBounds? bounds = null)
        {
            return new WotProtocolBinderRegistry(
                [new HttpBindingPlanner()],
                [new HttpWotBindingExecutor(options ?? new HttpWotBindingOptions
                {
                    ClientFactory = () => new HttpClient(),
                    CallerClientHandlesRedirectSafety = true
                })],
                bounds: bounds,
                endpointPolicy: new WotEndpointPolicy { AllowLoopback = true });
        }

        private static WotBindingPlan Plan(WotProtocolBinderRegistry registry, string td)
        {
            return registry.Prepare(WotBindingPlanRequest.FromDocument(
                "xid", WoTDocumentKindEnum.ThingDescription, Encoding.UTF8.GetBytes(td)));
        }

        private static string PropertyTd(string baseUrl, string contentType = "application/json")
        {
            return "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\",\"title\":\"t\"," +
                "\"properties\":{\"p\":{\"type\":\"number\",\"forms\":[{\"href\":\"" +
                baseUrl + "/prop\",\"contentType\":\"" + contentType + "\"}]}}}";
        }

        private static string ActionTd(string baseUrl, string contentType = "application/json")
        {
            return "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\",\"title\":\"t\"," +
                "\"actions\":{\"act\":{\"forms\":[{\"href\":\"" +
                baseUrl + "/action\",\"contentType\":\"" + contentType + "\"}]}}}";
        }

        [Test]
        public async Task HttpChannelReadNon2xxStatusReturnsMappedStatusCode()
        {
            using var server = new TestHttpServer((_, _, _) =>
                new TestHttpResponse(404, "text/plain", Encoding.UTF8.GetBytes("not found")));

            WotProtocolBinderRegistry registry = Registry();
            WotBindingPlan plan = Plan(registry, PropertyTd(server.BaseUrl));
            WotCompiledForm read = plan.CompiledForms.First(
                f => f.Operation == WoTBindingCapabilityEnum.ReadProperty);

            IWotBindingChannel channel = await registry.OpenChannelAsync(read).ConfigureAwait(false);
            await using (channel.ConfigureAwait(false))
            {
                WotReadResult result = await channel.ReadAsync().ConfigureAwait(false);
                Assert.That(result.Success, Is.False);
                // HTTP 404 maps to BadNotFound via HttpStatusMapper.
                Assert.That(StatusCode.IsBad(result.Status), Is.True);
            }
        }

        [Test]
        public async Task HttpChannelReadServerErrorStatusReturnsMappedStatusCode()
        {
            using var server = new TestHttpServer((_, _, _) =>
                new TestHttpResponse(500, "text/plain", Encoding.UTF8.GetBytes("error")));

            WotProtocolBinderRegistry registry = Registry();
            WotBindingPlan plan = Plan(registry, PropertyTd(server.BaseUrl));
            WotCompiledForm read = plan.CompiledForms.First(
                f => f.Operation == WoTBindingCapabilityEnum.ReadProperty);

            IWotBindingChannel channel = await registry.OpenChannelAsync(read).ConfigureAwait(false);
            await using (channel.ConfigureAwait(false))
            {
                WotReadResult result = await channel.ReadAsync().ConfigureAwait(false);
                Assert.That(result.Success, Is.False);
                Assert.That(StatusCode.IsBad(result.Status), Is.True);
            }
        }

        [Test]
        public async Task HttpChannelReadBodyTooLargeReturnsBadEncodingLimitsExceeded()
        {
            // Respond with a body larger than the configured limit.
            byte[] bigBody = Encoding.UTF8.GetBytes("\"" + new string('x', 20) + "\"");
            using var server = new TestHttpServer((_, _, _) =>
                new TestHttpResponse(200, "application/json", bigBody));

            // Limit MaxPayloadBytes to 10 so the 20-char body exceeds it.
            var bounds = new WotBindingBounds { MaxPayloadBytes = 10 };
            WotProtocolBinderRegistry registry = Registry(bounds: bounds);
            WotBindingPlan plan = Plan(registry, PropertyTd(server.BaseUrl));
            WotCompiledForm read = plan.CompiledForms.First(
                f => f.Operation == WoTBindingCapabilityEnum.ReadProperty);

            IWotBindingChannel channel = await registry.OpenChannelAsync(read).ConfigureAwait(false);
            await using (channel.ConfigureAwait(false))
            {
                WotReadResult result = await channel.ReadAsync().ConfigureAwait(false);
                Assert.That(result.Success, Is.False);
                Assert.That(result.Status, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            }
        }

        [Test]
        public async Task HttpChannelReadBadDecodeReturnsBadDecodingError()
        {
            // Respond with malformed JSON so the JSON codec fails to decode.
            byte[] badJson = Encoding.UTF8.GetBytes("{this is not valid json!}");
            using var server = new TestHttpServer((_, _, _) =>
                new TestHttpResponse(200, "application/json", badJson));

            WotProtocolBinderRegistry registry = Registry();
            WotBindingPlan plan = Plan(registry, PropertyTd(server.BaseUrl));
            WotCompiledForm read = plan.CompiledForms.First(
                f => f.Operation == WoTBindingCapabilityEnum.ReadProperty);

            IWotBindingChannel channel = await registry.OpenChannelAsync(read).ConfigureAwait(false);
            await using (channel.ConfigureAwait(false))
            {
                WotReadResult result = await channel.ReadAsync().ConfigureAwait(false);
                Assert.That(result.Success, Is.False);
                Assert.That(result.Status, Is.EqualTo(StatusCodes.BadDecodingError));
            }
        }

        [Test]
        public async Task HttpChannelWriteReturnsGoodOnSuccess()
        {
            using var server = new TestHttpServer((method, path, _) =>
            {
                if (method == "PUT" && path == "/prop")
                {
                    return new TestHttpResponse(200, "application/json", Encoding.UTF8.GetBytes("OK"));
                }
                return new TestHttpResponse(405, "text/plain", []);
            });

            WotProtocolBinderRegistry registry = Registry();
            WotBindingPlan plan = Plan(registry, PropertyTd(server.BaseUrl));
            WotCompiledForm write = plan.CompiledForms.First(
                f => f.Operation == WoTBindingCapabilityEnum.WriteProperty);

            IWotBindingChannel channel = await registry.OpenChannelAsync(write).ConfigureAwait(false);
            await using (channel.ConfigureAwait(false))
            {
                WotWriteResult result = await channel.WriteAsync(
                    new DataValue(new Variant(42L))).ConfigureAwait(false);
                Assert.That(result.Success, Is.True);
            }
        }

        [Test]
        public async Task HttpChannelWriteNon2xxReturnsMappedStatusCode()
        {
            using var server = new TestHttpServer((_, _, _) =>
                new TestHttpResponse(403, "text/plain", Encoding.UTF8.GetBytes("forbidden")));

            WotProtocolBinderRegistry registry = Registry();
            WotBindingPlan plan = Plan(registry, PropertyTd(server.BaseUrl));
            WotCompiledForm write = plan.CompiledForms.First(
                f => f.Operation == WoTBindingCapabilityEnum.WriteProperty);

            IWotBindingChannel channel = await registry.OpenChannelAsync(write).ConfigureAwait(false);
            await using (channel.ConfigureAwait(false))
            {
                WotWriteResult result = await channel.WriteAsync(
                    new DataValue(new Variant(1L))).ConfigureAwait(false);
                Assert.That(result.Success, Is.False);
                Assert.That(StatusCode.IsBad(result.Status), Is.True);
            }
        }

        [Test]
        public async Task HttpChannelInvokeWithEmptyResponseBodyReturnsGoodWithNoOutput()
        {
            using var server = new TestHttpServer((method, path, _) =>
            {
                if (method == "POST" && path == "/action")
                {
                    // Return 200 with empty body.
                    return new TestHttpResponse(200, "application/json", []);
                }
                return new TestHttpResponse(404, "text/plain", []);
            });

            WotProtocolBinderRegistry registry = Registry();
            WotBindingPlan plan = Plan(registry, ActionTd(server.BaseUrl));
            WotCompiledForm invoke = plan.CompiledForms.First(
                f => f.Operation == WoTBindingCapabilityEnum.InvokeAction);

            IWotBindingChannel channel = await registry.OpenChannelAsync(invoke).ConfigureAwait(false);
            await using (channel.ConfigureAwait(false))
            {
                WotInvokeResult result = await channel.InvokeAsync([]).ConfigureAwait(false);
                Assert.That(result.Success, Is.True);
                Assert.That(result.Outputs, Is.Empty);
            }
        }

        [Test]
        public async Task HttpChannelInvokeWithInputsAndJsonResponseDecodesOutput()
        {
            using var server = new TestHttpServer((method, path, _) =>
            {
                if (method == "POST" && path == "/action")
                {
                    return new TestHttpResponse(200, "application/json", Encoding.UTF8.GetBytes("99"));
                }
                return new TestHttpResponse(404, "text/plain", []);
            });

            WotProtocolBinderRegistry registry = Registry();
            WotBindingPlan plan = Plan(registry, ActionTd(server.BaseUrl));
            WotCompiledForm invoke = plan.CompiledForms.First(
                f => f.Operation == WoTBindingCapabilityEnum.InvokeAction);

            IWotBindingChannel channel = await registry.OpenChannelAsync(invoke).ConfigureAwait(false);
            await using (channel.ConfigureAwait(false))
            {
                WotInvokeResult result = await channel.InvokeAsync(
                    [new Variant(1L)]).ConfigureAwait(false);
                Assert.That(result.Success, Is.True);
                Assert.That(result.Outputs, Has.Count.EqualTo(1));
            }
        }

        [Test]
        public async Task HttpChannelObserveAsyncCreatesPollingSubscription()
        {
            int pollCount = 0;
            using var server = new TestHttpServer((_, _, _) =>
            {
                Interlocked.Increment(ref pollCount);
                return TestHttpResponse.Json(200, pollCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
            });

            WotProtocolBinderRegistry registry = Registry(
                options: new HttpWotBindingOptions
                {
                    ClientFactory = () => new HttpClient(),
                    CallerClientHandlesRedirectSafety = true,
                    ObserveInterval = TimeSpan.FromMilliseconds(100)
                });

            WotBindingPlan plan = Plan(registry, PropertyTd(server.BaseUrl));
            WotCompiledForm read = plan.CompiledForms.First(
                f => f.Operation == WoTBindingCapabilityEnum.ReadProperty);

            IWotBindingChannel channel = await registry.OpenChannelAsync(read).ConfigureAwait(false);
            await using (channel.ConfigureAwait(false))
            {
                var received = new ConcurrentQueue<WotNotification>();
                IWotSubscription sub = await channel.ObserveAsync(n => received.Enqueue(n))
                    .ConfigureAwait(false);
                await using (sub.ConfigureAwait(false))
                {
                    // Wait for at least one notification from the polling loop.
                    bool got = false;
                    for (int i = 0; i < 80 && !got; i++)
                    {
                        if (!received.IsEmpty)
                        {
                            got = true;
                        }
                        await Task.Delay(50).ConfigureAwait(false);
                    }
                    Assert.That(got, Is.True, "ObserveAsync should create a polling subscription that delivers data.");
                }
            }
        }

        [Test]
        public async Task HttpChannelSubscribeEventAsyncDelegatesToObserve()
        {
            using var server = new TestHttpServer((_, _, _) =>
                TestHttpResponse.Json(200, "123"));

            WotProtocolBinderRegistry registry = Registry(
                options: new HttpWotBindingOptions
                {
                    ClientFactory = () => new HttpClient(),
                    CallerClientHandlesRedirectSafety = true,
                    ObserveInterval = TimeSpan.FromMilliseconds(100)
                });

            WotBindingPlan plan = Plan(registry, PropertyTd(server.BaseUrl));
            WotCompiledForm read = plan.CompiledForms.First(
                f => f.Operation == WoTBindingCapabilityEnum.ReadProperty);

            IWotBindingChannel channel = await registry.OpenChannelAsync(read).ConfigureAwait(false);
            await using (channel.ConfigureAwait(false))
            {
                var received = new ConcurrentQueue<WotNotification>();
                IWotSubscription sub = await channel.SubscribeEventAsync(n => received.Enqueue(n))
                    .ConfigureAwait(false);
                await using (sub.ConfigureAwait(false))
                {
                    bool got = false;
                    for (int i = 0; i < 80 && !got; i++)
                    {
                        if (!received.IsEmpty)
                        {
                            got = true;
                        }
                        await Task.Delay(50).ConfigureAwait(false);
                    }
                    Assert.That(got, Is.True,
                        "SubscribeEventAsync should delegate to ObserveAsync and deliver notifications.");
                }
            }
        }
    }
}
