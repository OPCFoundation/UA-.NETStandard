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
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Moq.Protected;
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

        [TestCase(false)]
        [TestCase(true)]
        public async Task HttpChannelReadUsesTheCompiledMethod(bool relativeHref)
        {
            string? receivedMethod = null;
            using var server = new TestHttpServer((method, path, _) =>
            {
                receivedMethod = method;
                return method == "POST" && path == "/read"
                    ? new TestHttpResponse(200, "application/json", Encoding.UTF8.GetBytes("\"ready\""))
                    : new TestHttpResponse(405, "text/plain", []);
            });
            string href = relativeHref ? "/read" : server.BaseUrl + "/read";
            string td = $$"""
                {
                  "@context": [
                    "https://www.w3.org/2022/wot/td/v1.1",
                    { "htv": "http://www.w3.org/2011/http#" }
                  ],
                  "title": "Post-backed read",
                  "base": "{{server.BaseUrl}}/thing/",
                  "properties": {
                    "state": {
                      "type": "string",
                      "forms": [{
                        "href": "{{href}}",
                        "contentType": "application/json",
                        "op": "readproperty",
                        "htv:methodName": "POST"
                      }]
                    }
                  }
                }
                """;
            WotProtocolBinderRegistry registry = Registry();
            WotCompiledForm read = Plan(registry, td).CompiledForms.Single(
                form => form.Operation == WoTBindingCapabilityEnum.ReadProperty);

            await using IWotBindingChannel channel = await registry.OpenChannelAsync(read).ConfigureAwait(false);
            WotReadResult result = await channel.ReadAsync().ConfigureAwait(false);

            Assert.That(receivedMethod, Is.EqualTo("POST"));
            Assert.That(result.Status, Is.EqualTo(StatusCodes.Good));
            Assert.That(result.Value.WrappedValue.TryGetValue(out string? value), Is.True);
            Assert.That(value, Is.EqualTo("ready"));
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
            string? receivedBody = null;
            using var server = new TestHttpServer((method, path, body) =>
            {
                if (method == "POST" && path == "/action")
                {
                    receivedBody = Encoding.UTF8.GetString(body);
                    return new TestHttpResponse(200, "application/json", Encoding.UTF8.GetBytes("99"));
                }
                return new TestHttpResponse(404, "text/plain", []);
            });

            string td = $$"""
                {
                  "@context": "https://www.w3.org/2022/wot/td/v1.1",
                  "title": "Scalar action",
                  "actions": {
                    "act": {
                      "input": { "type": "integer" },
                      "output": { "type": "integer" },
                      "forms": [{
                        "href": "{{server.BaseUrl}}/action",
                        "contentType": "application/json",
                        "op": "invokeaction"
                      }]
                    }
                  }
                }
                """;
            WotProtocolBinderRegistry registry = Registry();
            WotBindingPlan plan = Plan(registry, td);
            WotCompiledForm invoke = plan.CompiledForms.First(
                f => f.Operation == WoTBindingCapabilityEnum.InvokeAction);

            IWotBindingChannel channel = await registry.OpenChannelAsync(invoke).ConfigureAwait(false);
            await using (channel.ConfigureAwait(false))
            {
                WotInvokeResult result = await channel.InvokeAsync(
                    [new Variant(1L)]).ConfigureAwait(false);
                Assert.That(result.Success, Is.True);
                Assert.That(result.Outputs, Has.Count.EqualTo(1));
                Assert.That(result.Outputs[0].WrappedValue.TryGetValue(out long output), Is.True);
                Assert.That(output, Is.EqualTo(99L));
                Assert.That(receivedBody, Is.EqualTo("1"));
            }
        }

        [Test]
        public async Task HttpChannelInvokeSendsEveryNamedInputInFieldOrder()
        {
            int sendCount = 0;
            HttpMethod? receivedMethod = null;
            Uri? receivedUri = null;
            string? receivedBody = null;
            var handler = new Mock<HttpMessageHandler>();
            handler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .Returns<HttpRequestMessage, CancellationToken>(async (request, cancellationToken) =>
                {
                    Interlocked.Increment(ref sendCount);
                    receivedMethod = request.Method;
                    receivedUri = request.RequestUri;
                    receivedBody = request.Content is null
                        ? null
                        : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                });
            using var client = new HttpClient(handler.Object);
            WotProtocolBinderRegistry registry = Registry(new HttpWotBindingOptions
            {
                ClientFactory = () => client,
                CallerClientHandlesRedirectSafety = true
            });
            const string td = """
                {
                  "@context": "https://www.w3.org/2022/wot/td/v1.1",
                  "title": "Ordered limits",
                  "actions": {
                    "setLimits": {
                      "input": {
                        "type": "object",
                        "uav:argumentLayout": "named",
                        "uav:fieldOrder": ["Maximum", "Minimum"],
                        "properties": {
                          "Minimum": { "type": "integer" },
                          "Maximum": { "type": "integer" }
                        },
                        "required": ["Minimum", "Maximum"]
                      },
                      "forms": [{
                        "href": "https://http-payloads.example/limits",
                        "op": "invokeaction",
                        "contentType": "application/json"
                      }]
                    }
                  }
                }
                """;
            WotCompiledForm invoke = Plan(registry, td).CompiledForms.Single(
                form => form.Operation == WoTBindingCapabilityEnum.InvokeAction);
            ArrayOf<Variant> inputs = [new Variant(42L), new Variant(-7L)];

            await using IWotBindingChannel channel = await registry.OpenChannelAsync(invoke).ConfigureAwait(false);
            WotInvokeResult result = await channel.InvokeAsync(inputs.Span.ToArray()).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.Good));
            Assert.That(result.Outputs, Is.Empty);
            Assert.That(sendCount, Is.EqualTo(1), "One native invocation must send exactly one complete request.");
            Assert.That(receivedMethod, Is.EqualTo(HttpMethod.Post));
            Assert.That(receivedUri, Is.EqualTo(new Uri("https://http-payloads.example/limits")));
            Assert.That(receivedBody, Is.Not.Null);
            using JsonDocument body = JsonDocument.Parse(receivedBody!);
            Assert.That(body.RootElement.ValueKind, Is.EqualTo(JsonValueKind.Object));
            string[] expectedOrder = ["Maximum", "Minimum"];
            Assert.That(
                body.RootElement.EnumerateObject().Select(property => property.Name),
                Is.EqualTo(expectedOrder));
            Assert.That(body.RootElement.GetProperty("Maximum").GetInt64(), Is.EqualTo(42L));
            Assert.That(body.RootElement.GetProperty("Minimum").GetInt64(), Is.EqualTo(-7L));
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
            const string payload = """
                {
                  "EventId": "AQID",
                  "EventType": "i=2041",
                  "SourceNode": "i=2253",
                  "SourceName": "boiler-7",
                  "Time": "2026-08-01T12:00:00Z",
                  "ReceiveTime": "2026-08-01T12:00:01Z",
                  "Message": "Over limit",
                  "Severity": 700
                }
                """;
            int sendCount = 0;
            HttpMethod? receivedMethod = null;
            Uri? receivedUri = null;
            var handler = new Mock<HttpMessageHandler>();
            handler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .Returns<HttpRequestMessage, CancellationToken>((request, _) =>
                {
                    Interlocked.Increment(ref sendCount);
                    receivedMethod = request.Method;
                    receivedUri = request.RequestUri;
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(payload, Encoding.UTF8, "application/json")
                    });
                });
            using var client = new HttpClient(handler.Object);
            WotProtocolBinderRegistry registry = Registry(new HttpWotBindingOptions
            {
                ClientFactory = () => client,
                CallerClientHandlesRedirectSafety = true,
                ObserveInterval = TimeSpan.FromHours(1)
            });
            const string td = """
                {
                  "@context": "https://www.w3.org/2022/wot/td/v1.1",
                  "title": "HTTP events",
                  "events": {
                    "alarm": {
                      "data": {
                        "type": "object",
                        "properties": {
                          "EventId": { "type": "string", "contentEncoding": "base64" },
                          "EventType": { "type": "string" },
                          "SourceNode": { "type": "string" },
                          "SourceName": { "type": "string" },
                          "Time": { "type": "string", "format": "date-time" },
                          "ReceiveTime": { "type": "string", "format": "date-time" },
                          "Message": { "type": "string" },
                          "Severity": { "type": "integer", "minimum": 0, "maximum": 65535 }
                        }
                      },
                      "forms": [{
                        "href": "https://http-payloads.example/events",
                        "op": "subscribeevent",
                        "contentType": "application/json"
                      }]
                    }
                  }
                }
                """;
            WotCompiledForm form = Plan(registry, td).CompiledForms.Single(
                compiled => compiled.Operation == WoTBindingCapabilityEnum.SubscribeEvent);
            var received = new TaskCompletionSource<WotNotification>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            await using IWotBindingChannel channel = await registry.OpenChannelAsync(form).ConfigureAwait(false);
            IWotSubscription subscription = await channel.SubscribeEventAsync(
                notification => received.TrySetResult(notification)).ConfigureAwait(false);
            await using (subscription.ConfigureAwait(false))
            {
                WotNotification notification = await received.Task.WaitAsync(TimeSpan.FromSeconds(10))
                    .ConfigureAwait(false);
                var expected = new Dictionary<string, Variant>(StringComparer.Ordinal)
                {
                    ["EventId"] = new Variant(new ByteString(new byte[] { 1, 2, 3 })),
                    ["EventType"] = new Variant(Ua.ObjectTypeIds.BaseEventType),
                    ["SourceNode"] = new Variant(Ua.ObjectIds.Server),
                    ["SourceName"] = new Variant("boiler-7"),
                    ["Time"] = new Variant(new DateTimeUtc(2026, 8, 1, 12, 0, 0)),
                    ["ReceiveTime"] = new Variant(new DateTimeUtc(2026, 8, 1, 12, 0, 1)),
                    ["Message"] = new Variant(new LocalizedText("Over limit")),
                    ["Severity"] = new Variant((ushort)700)
                };

                Assert.That(form.AffordanceKind, Is.EqualTo(WotAffordanceKind.Event));
                Assert.That(subscription.Form, Is.SameAs(form));
                Assert.That(subscription, Is.TypeOf<PollingWotSubscription>());
                Assert.That(notification.Value.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(notification.EventFields.Keys, Is.EquivalentTo(expected.Keys));
                Assert.That(notification.Data.Members.Keys, Is.EquivalentTo(expected.Keys));
                foreach (KeyValuePair<string, Variant> field in expected)
                {
                    Assert.That(notification.Data.TryGetValue([field.Key], out DataValue data), Is.True, field.Key);
                    Assert.That(data.StatusCode, Is.EqualTo(StatusCodes.Good), field.Key);
                    Assert.That(data.WrappedValue, Is.EqualTo(field.Value), field.Key);
                    Assert.That(
                        notification.EventFields[field.Key].StatusCode, Is.EqualTo(StatusCodes.Good), field.Key);
                    Assert.That(notification.EventFields[field.Key].WrappedValue, Is.EqualTo(field.Value), field.Key);
                }
            }

            Assert.That(sendCount, Is.EqualTo(1));
            Assert.That(receivedMethod, Is.EqualTo(HttpMethod.Get));
            Assert.That(receivedUri, Is.EqualTo(new Uri("https://http-payloads.example/events")));
        }
    }
}
